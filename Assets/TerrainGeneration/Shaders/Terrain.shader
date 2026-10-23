Shader "Custom/Terrain"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #define MAX_LAYERS 16
            float4 baseColours[MAX_LAYERS];
            float baseStartHeights[MAX_LAYERS];
            float baseBlends[MAX_LAYERS];
            float minHeight;
            float maxHeight;
            float normalizedWaterLevel;
            float landThreshold;
            int layerCount;

            int enableBog;
            float4 bogTint;
            float moistureScale;
            float2 moistureOffset;
            float bogMoistureThreshold;
            float bogMinHeight;
            float bogMaxHeight;
            float bogMaxSlope;

            // Surface detail (TextureData's Surface Detail settings).
            int enableSurfaceDetail;
            float4 meadowWarm;
            float4 meadowCool;
            float meadowPatchScale;
            float meadowVariation;
            float4 rockTint;
            float rockSlopeStart;
            float rockSlopeEnd;
            float rockBandHeight;
            float bumpStrength;
            float detailDistance;

            int enableErosion;
            float erosionSlopeThreshold;
            float erosionStrength;
            float erosionDarkening;
            float erosionScale;

            // Set globally by WaterSurface while a water surface is drawn over the terrain. When it is 0 the water
            // layers stay painted on, as they are in the editor previews.
            float _TerrainWaterSurface;
            float _TerrainWaterHeight;
            float4 _TerrainLakebedShallow;
            float4 _TerrainLakebedDeep;
            float _TerrainLakebedDeepDepth;
            float _TerrainCaustics;
            float _TerrainWetShore;

            // Set globally by SurvivabilityHeatmap: colours over the habitable square, whose world-space minimum
            // corner is in xy and inverse size in zw. Opacity 0 hides it.
            TEXTURE2D(_SurvivalHeatmap);
            SAMPLER(sampler_SurvivalHeatmap);
            float4 _SurvivalHeatmapRect;
            float _SurvivalHeatmapOpacity;

            // Set globally by SeaBiomeMap: how much of the bed each painted sea biome covers (seagrass, kelp, reef and
            // dead zone in r, g, b and a) over the habitable square, laid out like the heatmap, and the biomes' colours.
            // Strength 0 leaves the plain bed.
            TEXTURE2D(_SeaBedMap);
            SAMPLER(sampler_SeaBedMap);
            float4 _SeaBedMapRect;
            float _SeaBedStrength;
            float4 _SeaBedSeagrass;
            float4 _SeaBedKelp;
            float4 _SeaBedCoral;
            float4 _SeaBedCoralSecond;
            float4 _SeaBedDeadZone;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD3;
            };

            float InverseLerp(float a, float b, float v)
            {
                return saturate((v - a) / max(b - a, 1e-5));
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Integer hash of a lattice cell. Unlike Hash21 it keeps full precision kilometres from the origin, where
            // the fine surface detail is sampled at thousands of cells per axis.
            float HashCell(float2 cell)
            {
                uint2 q = (uint2)(int2(cell) + 32768);
                uint h = (q.x * 1597334677u) ^ (q.y * 3812015801u);
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return h * (1.0 / 4294967296.0);
            }

            float DetailNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = HashCell(i);
                float b = HashCell(i + float2(1, 0));
                float c = HashCell(i + float2(0, 1));
                float d = HashCell(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float DetailFbm(float2 p)
            {
                return 0.5 * DetailNoise(p) + 0.3 * DetailNoise(p * 2.03 + 17.1) + 0.2 * DetailNoise(p * 4.11 + 5.3);
            }

            // Bedrock seen from the side: horizontal ledges bent by noise, with grain inside each ledge. y is height.
            float RockStrata(float2 p)
            {
                float bend = DetailNoise(p / 9.0) * 2.5;
                float layer = frac((p.y + bend) / max(rockBandHeight, 0.1));
                float ledge = smoothstep(0.0, 0.2, layer) * (1.0 - 0.6 * smoothstep(0.6, 1.0, layer));
                return saturate(0.55 * ledge + 0.45 * DetailNoise(p / 1.3));
            }

            // Rock pattern from three sides (triplanar), so cliffs never show a texture stretched down them: ledges on
            // the walls, cracked slabs on top.
            float RockPattern(float3 p, float3 normal)
            {
                float3 weights = pow(abs(normal), 4.0);
                weights /= weights.x + weights.y + weights.z;
                return RockStrata(p.zy) * weights.x + DetailFbm(p.xz / 3.0) * weights.y + RockStrata(p.xy) * weights.z;
            }

            // How much of material b shows over a, given b's share and each material's height: the higher surface wins
            // close to the border, so grass fills the cracks between rocks instead of fading over them.
            float HeightBlend(float share, float heightA, float heightB, float depth)
            {
                float a = (1.0 - share) + heightA;
                float b = share + heightB;
                float top = max(a, b) - depth;
                float weightA = max(a - top, 0.0);
                float weightB = max(b - top, 0.0);
                return weightB / max(weightA + weightB, 1e-5);
            }

            // Bumps the shading normal by a height field using its screen-space derivatives (Mikkelsen 2010), so small
            // surface detail catches the light without a normal map.
            float3 PerturbNormal(float3 normal, float3 position, float height)
            {
                float3 dpdx = ddx(position);
                float3 dpdy = ddy(position);
                float dhdx = ddx(height);
                float dhdy = ddy(height);
                float3 r1 = cross(dpdy, normal);
                float3 r2 = cross(normal, dpdx);
                float det = dot(dpdx, r1);
                float3 surfaceGradient = sign(det) * (dhdx * r1 + dhdy * r2);
                return normalize(abs(det) * normal - surfaceGradient);
            }

            // Distance to the nearest border between drifting cells, 0 on a border.
            float CellBorderDistance(float2 p, float time)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float nearest = 8.0;
                float second = 8.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 toPoint = offset + 0.5 + 0.4 * sin(time + 6.2831853 * Hash22(cell + offset)) - local;
                        float distanceSquared = dot(toPoint, toPoint);
                        second = min(second, max(nearest, distanceSquared));
                        nearest = min(nearest, distanceSquared);
                    }
                }
                return sqrt(second) - sqrt(nearest);
            }

            // Threads of sunlight focused by the waves onto a shallow bed: two drifting cell patterns.
            float Caustics(float2 positionXZ, float time)
            {
                float a = CellBorderDistance(positionXZ * 0.45, time * 0.9);
                float b = CellBorderDistance(positionXZ * 0.62 + 17.3, -time * 0.7);
                return 1.0 - smoothstep(0.0, 0.16, min(a, b));
            }

            // The bed in its sea biome's colours: seagrass in patches on the sand, mottled kelp rock, corals mixing
            // two colours, and dark dead-zone silt. Neighbouring biomes blend through the map's filtering.
            float3 PaintSeaBed(float3 bed, float2 positionXZ)
            {
                float2 uv = (positionXZ - _SeaBedMapRect.xy) * _SeaBedMapRect.zw;
                if (any(uv < 0.0) || any(uv > 1.0)) return bed;

                float4 cover = SAMPLE_TEXTURE2D_LOD(_SeaBedMap, sampler_SeaBedMap, uv, 0);
                float total = saturate(cover.r + cover.g + cover.b + cover.a);
                if (total <= 0.001) return bed;

                float patches = ValueNoise(positionXZ * 0.3);
                float grain = ValueNoise(positionXZ * 1.4 + 31.7);
                float3 seagrass = lerp(bed, _SeaBedSeagrass.rgb, smoothstep(0.3, 0.55, patches));
                float3 kelp = _SeaBedKelp.rgb * (0.75 + 0.5 * grain);
                float3 coral = lerp(_SeaBedCoral.rgb, _SeaBedCoralSecond.rgb, smoothstep(0.35, 0.65, grain))
                             * (0.8 + 0.4 * patches);
                float3 dead = _SeaBedDeadZone.rgb * (0.85 + 0.3 * grain);
                float3 painted = (seagrass * cover.r + kelp * cover.g + coral * cover.b + dead * cover.a) / total;
                return lerp(bed, painted, total * _SeaBedStrength);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);

                float heightPercent = InverseLerp(minHeight, maxHeight, IN.positionWS.y);
                float slope = 1.0 - saturate(normalWS.y);

                float slopeBiasedHeight = saturate(heightPercent + slope * 0.18);

                float3 albedo = baseColours[0].rgb;
                float snow = 0.0;
                for (int i = 1; i < layerCount; i++)
                {
                    float sampleHeight = (i == layerCount - 1) ? heightPercent : slopeBiasedHeight;
                    float drawStrength = InverseLerp(-baseBlends[i] * 0.5 - 1e-4, baseBlends[i] * 0.5, sampleHeight - baseStartHeights[i]);

                    if (i == layerCount - 1)
                    {
                        drawStrength *= saturate(1.0 - slope * 1.6);
                        snow = drawStrength;
                    }

                    albedo = albedo * (1 - drawStrength) + baseColours[i].rgb * drawStrength;
                }

                // Surface detail: varied meadows, rock on ground too steep to walk, and small bumps that catch the light.
                // All of it is noise, so nothing repeats. Fine detail fades out with distance, where it would shimmer.
                float3 shadingNormal = normalWS;
                if (enableSurfaceDetail)
                {
                    float near = 1.0 - smoothstep(detailDistance * 0.4, detailDistance, distance(IN.positionWS, _WorldSpaceCameraPos));
                    float onLand = step(landThreshold, heightPercent);

                    float patches = DetailFbm(IN.positionWS.xz / max(meadowPatchScale, 1.0));
                    float clumps = DetailNoise(IN.positionWS.xz / 6.0);
                    float grain = DetailNoise(IN.positionWS.xz / 0.9);
                    float3 tint = lerp(meadowCool.rgb, meadowWarm.rgb, smoothstep(0.3, 0.7, patches));
                    float variation = meadowVariation * onLand * (1.0 - 0.7 * snow);
                    albedo *= lerp(float3(1, 1, 1), tint, variation);
                    albedo *= 1.0 + variation * ((clumps - 0.5) * 0.3 + (grain - 0.5) * 0.2 * near);

                    float rockShare = smoothstep(rockSlopeStart, rockSlopeEnd, slope) * onLand;
                    float rock = 0.0;
                    float rockPattern = 0.0;
                    if (rockShare > 0.001)
                    {
                        rockPattern = RockPattern(IN.positionWS, normalWS);
                        rock = HeightBlend(rockShare, 0.35 + 0.3 * clumps, rockPattern, 0.15);
                        float3 rockColour = rockTint.rgb * (0.7 + 0.45 * rockPattern) * (0.9 + 0.2 * patches);
                        albedo = lerp(albedo, rockColour, rock);
                    }

                    float bumpHeight = lerp((clumps - 0.5) * 0.12 + (grain - 0.5) * 0.04, rockPattern * 0.4, rock);
                    shadingNormal = PerturbNormal(normalWS, IN.positionWS, bumpHeight * bumpStrength * near);
                }

                // Metres below the water surface (negative above it).
                float waterDepth = _TerrainWaterHeight - IN.positionWS.y;
                if (_TerrainWaterSurface > 0.5)
                {
                    // The surface drawn on top supplies the blue, so beds become sand at the shore and silt deeper down.
                    float underwater = smoothstep(-0.1, 0.2, waterDepth);
                    float3 bed = lerp(_TerrainLakebedShallow.rgb, _TerrainLakebedDeep.rgb,
                                      saturate(waterDepth / max(_TerrainLakebedDeepDepth, 0.01)));
                    if (_SeaBedStrength > 0.0) bed = PaintSeaBed(bed, IN.positionWS.xz);
                    albedo = lerp(albedo, bed, underwater);

                    // Damp, darker ground in a thin band just above the water line.
                    float wet = 1.0 - smoothstep(0.0, max(_TerrainWetShore, 0.001), -waterDepth);
                    albedo *= 1.0 - 0.3 * wet * (1.0 - underwater);
                }

                if (enableErosion)
                {
                    float erosionSlopeFactor = smoothstep(erosionSlopeThreshold, erosionSlopeThreshold + 0.25, slope) * erosionStrength;
                    erosionSlopeFactor *= step(normalizedWaterLevel, heightPercent);
                    if (erosionSlopeFactor > 0.0)
                    {
                        float2 streakCoord = float2((IN.positionWS.x + IN.positionWS.z), IN.positionWS.y * 0.3) / erosionScale;
                        float streak = ValueNoise(streakCoord);
                        albedo *= 1.0 - erosionDarkening * (1.0 - streak) * erosionSlopeFactor;
                    }
                }
                if (enableBog)
                {
                    float moisture = ValueNoise((IN.positionWS.xz + moistureOffset) / moistureScale);

                    float bogHeightFactor = smoothstep(bogMinHeight, bogMinHeight + 0.05, heightPercent);
                    bogHeightFactor *= 1.0 - smoothstep(bogMaxHeight, bogMaxHeight + 0.1, heightPercent);

                    float bogSlopeFactor = 1.0 - smoothstep(bogMaxSlope * 0.6, bogMaxSlope, slope);

                    float bogFactor = smoothstep(bogMoistureThreshold - 0.1, bogMoistureThreshold, moisture);
                    bogFactor *= bogHeightFactor * bogSlopeFactor;
                    bogFactor *= step(landThreshold, heightPercent);

                    albedo = lerp(albedo, bogTint.rgb, bogFactor);
                }

                float variation = ValueNoise(IN.positionWS.xz * 0.07) * 2.0 - 1.0;
                albedo *= 1.0 + variation * 0.06;

                // The survivability heatmap replaces the ground colour before lighting, so hills still read.
                if (_SurvivalHeatmapOpacity > 0.0)
                {
                    float2 heatmapUV = (IN.positionWS.xz - _SurvivalHeatmapRect.xy) * _SurvivalHeatmapRect.zw;
                    if (all(heatmapUV >= 0.0) && all(heatmapUV <= 1.0))
                    {
                        float4 heat = SAMPLE_TEXTURE2D_LOD(_SurvivalHeatmap, sampler_SurvivalHeatmap, heatmapUV, 0);
                        albedo = lerp(albedo, heat.rgb, heat.a * _SurvivalHeatmapOpacity);
                    }
                }

                // Shadow coordinates are found per pixel, because each pixel picks its own shadow cascade. Passing the
                // position lets the shadows fade out towards the shadow distance instead of ending in a hard line.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS), IN.positionWS, half4(1, 1, 1, 1));
                AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionHCS));
                float NdotL = saturate(dot(shadingNormal, mainLight.direction));
                float3 radiance = mainLight.color * (NdotL * mainLight.shadowAttenuation * mainLight.distanceAttenuation);

                // Caustics on shallow beds near the camera; they fade with depth and with distance, where the thin
                // threads would shimmer.
                if (_TerrainWaterSurface > 0.5 && _TerrainCaustics > 0.0 && waterDepth > 0.0)
                {
                    float causticFade = smoothstep(0.0, 0.5, waterDepth) * exp(-waterDepth / 3.0)
                                      * (1.0 - smoothstep(60.0, 160.0, distance(IN.positionWS, _WorldSpaceCameraPos)));
                    if (causticFade > 0.001)
                    {
                        radiance += mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation
                                    * _TerrainCaustics * causticFade * Caustics(IN.positionWS.xz, _Time.y));
                    }
                }

                radiance *= occlusion.directAmbientOcclusion;
                float3 ambient = SampleSH(shadingNormal) * occlusion.indirectAmbientOcclusion;

                float3 colour = albedo * (radiance + ambient);
                colour = MixFog(colour, IN.fogFactor);

                return float4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // The depth passes put the terrain into the camera depth texture (URP fills it from a DepthNormals prepass
        // while SSAO is on). The water surface reads it to find the shore and how deep the water is.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target
            {
                return IN.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            void DepthNormalsFrag(Varyings IN, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(IN.normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
