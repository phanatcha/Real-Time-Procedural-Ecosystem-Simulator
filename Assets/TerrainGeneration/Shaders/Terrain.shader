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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
                float4 shadowCoord : TEXCOORD2;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.positionWS);
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
                for (int i = 1; i < layerCount; i++)
                {
                    float sampleHeight = (i == layerCount - 1) ? heightPercent : slopeBiasedHeight;
                    float drawStrength = InverseLerp(-baseBlends[i] * 0.5 - 1e-4, baseBlends[i] * 0.5, sampleHeight - baseStartHeights[i]);

                    if (i == layerCount - 1)
                    {
                        drawStrength *= saturate(1.0 - slope * 1.6);
                    }

                    albedo = albedo * (1 - drawStrength) + baseColours[i].rgb * drawStrength;
                }

                // Metres below the water surface (negative above it).
                float waterDepth = _TerrainWaterHeight - IN.positionWS.y;
                if (_TerrainWaterSurface > 0.5)
                {
                    // The surface drawn on top supplies the blue, so beds become sand at the shore and silt deeper down.
                    float underwater = smoothstep(-0.1, 0.2, waterDepth);
                    float3 bed = lerp(_TerrainLakebedShallow.rgb, _TerrainLakebedDeep.rgb,
                                      saturate(waterDepth / max(_TerrainLakebedDeepDepth, 0.01)));
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

                Light mainLight = GetMainLight(IN.shadowCoord);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
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

                float3 ambient = SampleSH(normalWS);

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
