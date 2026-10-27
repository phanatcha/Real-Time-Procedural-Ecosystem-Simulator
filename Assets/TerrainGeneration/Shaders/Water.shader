Shader "Custom/Water"
{
    // Stylised sea in the spirit of Valheim and Sea of Thieves, drawn at the water level by WaterSurface: long Gerstner
    // swells from a directional wave spectrum (WaveSpectrum builds them from the wind settings below) with the shorter
    // waves from the same spectrum on top as an FFT ocean (OceanFFT), turquoise over the shallows and cold deep blue
    // further out, sky reflection at grazing angles, pale highlights and a soft glow on wave crests, foam where the
    // waves fold over and along the shore. Each tile's vertex colour (red) says how much the waves move it, so
    // rivers and lakes stay calm. The lake and river bed settings are not used
    // here: WaterSurface hands them to the terrain shader, which draws the bed seen through the water.
    Properties
    {
        [Header(Water colour)]
        _ShallowColor ("Shallow colour", Color) = (0.19, 0.53, 0.53, 1)
        _DeepColor ("Deep colour", Color) = (0.035, 0.15, 0.29, 1)
        _DeepColorDepth ("Depth of the deep colour (m)", Float) = 6
        _Clarity ("See-through distance (m)", Float) = 2.5

        [Header(Wave spectrum)]
        _WindDirection ("Wind direction (degrees, 0 is +x, 90 is +z)", Range(0, 360)) = 35
        _WindSpeed ("Wind speed (m/s)", Range(0.5, 25)) = 7
        _Fetch ("Open water upwind (km)", Range(1, 300)) = 40
        _PeakSharpness ("Peak sharpness (JONSWAP gamma)", Range(1, 7)) = 3.3
        _Spread ("Direction spread (0 all with the wind)", Range(0, 2)) = 1
        [IntRange] _WaveCount ("Number of waves", Range(4, 32)) = 24
        _ShortestWave ("Shortest wave (m)", Float) = 1
        _SwellLength ("Gerstner swells down to (m), FFT waves below", Float) = 20
        _WaveHeightScale ("Wave height", Range(0, 3)) = 1
        _Choppiness ("Choppiness", Range(0, 1)) = 0.8
        _SpectrumSeed ("Seed", Float) = 1

        [Header(Waves)]
        _MeshSpacing ("Wave mesh spacing (m)", Float) = 2
        _WaveSpeed ("Wave speed", Float) = 1
        _WaveFade ("Each wave fades out this many of its lengths away", Float) = 40
        _RippleStrength ("Small ripples", Range(0, 1)) = 0.35
        _RippleSize ("Ripple size (m)", Float) = 2.5

        [Header(Light)]
        _ReflectionStrength ("Sky reflection", Range(0, 1)) = 0.85
        _GlintSharpness ("Sun glint sharpness", Range(16, 1024)) = 300
        _GlintStrength ("Sun glint strength", Range(0, 4)) = 1.5
        _ScatterColor ("Light through crests", Color) = (0.22, 0.6, 0.58, 1)
        _ScatterStrength ("Crest glow", Range(0, 2)) = 0.5

        [Header(Wave crest highlights)]
        _CrestColor ("Crest highlight colour", Color) = (0.76, 0.87, 0.92, 1)
        _CrestHighlight ("Crest highlight strength", Range(0, 1)) = 0.45
        _CrestThreshold ("Crest threshold (higher, fewer crests)", Range(0, 1)) = 0.3
        _CrestBreakup ("Crest patchiness", Range(0, 1)) = 0.7

        [Header(Foam)]
        _FoamColor ("Foam colour", Color) = (0.9, 0.94, 0.95, 1)
        _FoamWidth ("Shore foam width (m)", Float) = 0.5
        _FoamSpeed ("Shore foam pulse speed", Float) = 1.2
        _CrestFoam ("Foam on wave crests", Range(0, 1)) = 0.5
        _FoamLife ("Crest foam fades over (s)", Range(0.2, 10)) = 3

        [Header(Lake and river bed seen through the water)]
        _LakebedShallowColor ("Bed colour at the shore", Color) = (0.66, 0.6, 0.44, 1)
        _LakebedDeepColor ("Bed colour in deep water", Color) = (0.22, 0.25, 0.19, 1)
        _LakebedDeepDepth ("Depth of the deep bed colour (m)", Float) = 5
        _CausticsStrength ("Caustics", Range(0, 2)) = 0.6
        _WetShoreHeight ("Wet shore band (m)", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha: the shader decides how much of the bed behind shows through. Culling is off so the
            // surface still draws where a steep crest leans over towards the camera.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Enabled by WaterSurface for cameras without a depth texture (such as the Mobile quality level).
            #pragma multi_compile _ _WATER_NO_DEPTH_TEXTURE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DeepColorDepth;
                float _Clarity;
                float _WindDirection;
                float _WindSpeed;
                float _Fetch;
                float _PeakSharpness;
                float _Spread;
                float _WaveCount;
                float _ShortestWave;
                float _SwellLength;
                float _WaveHeightScale;
                float _Choppiness;
                float _SpectrumSeed;
                float _MeshSpacing;
                float _WaveSpeed;
                float _WaveFade;
                half _RippleStrength;
                float _RippleSize;
                half _ReflectionStrength;
                half _GlintSharpness;
                half _GlintStrength;
                half4 _ScatterColor;
                half _ScatterStrength;
                half4 _CrestColor;
                half _CrestHighlight;
                half _CrestThreshold;
                half _CrestBreakup;
                half4 _FoamColor;
                float _FoamWidth;
                float _FoamSpeed;
                half _CrestFoam;
                float _FoamLife;
                half4 _LakebedShallowColor;
                half4 _LakebedDeepColor;
                float _LakebedDeepDepth;
                half _CausticsStrength;
                float _WetShoreHeight;
            CBUFFER_END

            // Set globally by SurvivabilityHeatmap (the same map Terrain.shader paints on land). While it scores the
            // sea, _SurvivalHeatmapWater is 1 and the map is painted on the water, so it reads at any depth.
            TEXTURE2D(_SurvivalHeatmap);
            SAMPLER(sampler_SurvivalHeatmap);
            float4 _SurvivalHeatmapRect;
            float _SurvivalHeatmapOpacity;
            float _SurvivalHeatmapWater;

            // The spectrum's waves, set globally by WaterSurface from the settings above: direction x, direction z,
            // wavenumber (rad/m) and amplitude (m), each wave's phase, how far crests move sideways, and the sea's
            // significant wave height.
            #define MAX_WAVES 32
            float4 _WaterWaves[MAX_WAVES];
            float _WaterWavePhases[MAX_WAVES];
            float _WaterWaveCount;
            float _WaterWaveChop;
            float _WaterWaveHeight;

            // The FFT ocean's two tiling patches, set globally by WaterSurface. Displacement: xyz how far each point
            // moves, w foam. Slopes: xy the surface's slopes, z the Jacobian minus 1. _WaterDetail: x and y one over
            // each patch's size (m), z 1 while the patches are running.
            TEXTURE2D(_WaterDetailDisplacement0);
            TEXTURE2D(_WaterDetailDisplacement1);
            TEXTURE2D(_WaterDetailSlopes0);
            TEXTURE2D(_WaterDetailSlopes1);
            SAMPLER(sampler_WaterDetailDisplacement0);
            float4 _WaterDetail;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                // xy: where this point rests when the water is still, z: how much the waves move it here, w: how far
                // the waves lifted it.
                float4 wave : TEXCOORD2;
            };

            // Pseudo-random numbers from a world position, without sine so large coordinates stay stable.
            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Smooth 0-1 noise with features about one unit across.
            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash22(cell).x;
                float b = Hash22(cell + float2(1, 0)).x;
                float c = Hash22(cell + float2(0, 1)).x;
                float d = Hash22(cell + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // The spectrum's Gerstner waves (GPU Gems 1, chapter 1) at the point resting at p: points circle as a wave
            // passes, so crests are sharp and troughs wide. Returns how far the surface moves there, with its normal and
            // how much it is squeezed (1 when flat, lower where a crest is about to fold over, which is where foam forms).
            // weight scales every wave; each wave also fades out once it is _WaveFade of its lengths from the camera,
            // where it would shimmer, and waves shorter than shortest are left out (the mesh can't show them).
            float3 Waves(float2 p, float weight, float distanceToCamera, float shortest, out float3 normal, out float squeeze)
            {
                float time = _Time.y * _WaveSpeed;
                float3 offset = 0.0;
                float3 tangent = float3(1.0, 0.0, 0.0);
                float3 binormal = float3(0.0, 0.0, 1.0);
                int count = (int)_WaterWaveCount;
                for (int i = 0; i < count; i++)
                {
                    float4 wave = _WaterWaves[i];
                    float k = wave.z;
                    float waveLength = 6.2831853 / max(k, 0.0001);
                    float fade = (1.0 - smoothstep(0.4 * _WaveFade * waveLength, _WaveFade * waveLength, distanceToCamera))
                               * saturate((waveLength - shortest) / max(0.33 * shortest, 0.0001));
                    float amplitude = wave.w * weight * fade;
                    float ka = k * amplitude;
                    float chop = _WaterWaveChop;
                    float s, c;
                    sincos(k * dot(wave.xy, p) - sqrt(9.81 * k) * time + _WaterWavePhases[i], s, c);
                    offset += float3(wave.x * chop * amplitude * c, amplitude * s, wave.y * chop * amplitude * c);
                    tangent += float3(-wave.x * wave.x * chop * ka * s, wave.x * ka * c, -wave.x * wave.y * chop * ka * s);
                    binormal += float3(-wave.x * wave.y * chop * ka * s, wave.y * ka * c, -wave.y * wave.y * chop * ka * s);
                }

                normal = normalize(cross(binormal, tangent));
                squeeze = tangent.x * binormal.z - tangent.z * binormal.x;
                return offset;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 restWS = TransformObjectToWorld(input.positionOS.xyz);

                // The mesh moves with the waves at least three points long; shorter ones only shade the surface.
                float weight = input.color.r;
                float distanceToCamera = distance(restWS, _WorldSpaceCameraPos);
                float3 normal;
                float squeeze;
                float3 offset = Waves(restWS.xz, weight, distanceToCamera, 3.0 * _MeshSpacing, normal, squeeze);
                // The larger FFT patch only holds waves long enough for the mesh; the smaller one only shades.
                if (_WaterDetail.z > 0.5)
                {
                    offset += SAMPLE_TEXTURE2D_LOD(_WaterDetailDisplacement0, sampler_WaterDetailDisplacement0,
                                                   restWS.xz * _WaterDetail.x, 0).xyz * weight;
                }

                float3 positionWS = restWS + offset;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.wave = float4(restWS.xz, weight, offset.y);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float2 restXZ = input.wave.xy;
                float weight = input.wave.z;

                // The normal is worked out again for every pixel, with every wave, so the surface stays smooth and
                // detailed whatever the mesh.
                float distanceToCamera = distance(positionWS, _WorldSpaceCameraPos);
                float3 normalWS;
                float squeeze;
                Waves(restXZ, weight, distanceToCamera, 0.0, normalWS, squeeze);

                // The FFT's shorter waves on top: their slopes add to the swells', their squeezing to the swells'
                // squeezing, and the foam they left where they broke comes along too.
                half detailSqueeze = 0.0;
                half detailFoam = 0.0;
                if (_WaterDetail.z > 0.5)
                {
                    float2 uv0 = restXZ * _WaterDetail.x;
                    float2 uv1 = restXZ * _WaterDetail.y;
                    float3 detail = (SAMPLE_TEXTURE2D(_WaterDetailSlopes0, sampler_WaterDetailDisplacement0, uv0).xyz +
                                     SAMPLE_TEXTURE2D(_WaterDetailSlopes1, sampler_WaterDetailDisplacement0, uv1).xyz) * weight;
                    detailSqueeze = detail.z;
                    detailFoam = (SAMPLE_TEXTURE2D(_WaterDetailDisplacement0, sampler_WaterDetailDisplacement0, uv0).w +
                                  SAMPLE_TEXTURE2D(_WaterDetailDisplacement1, sampler_WaterDetailDisplacement0, uv1).w) * weight;
                    float2 swellSlope = -normalWS.xz / max(normalWS.y, 0.05);
                    normalWS = normalize(float3(-(swellSlope.x + detail.x), 1.0, -(swellSlope.y + detail.y)));
                }

                // Small ripples drifting across, mostly for rivers and lakes, which the waves leave still. They fade out
                // in the distance where they'd shimmer.
                float rippleFade = 1.0 - smoothstep(60.0, 220.0, distanceToCamera);
                float2 rippleUV = restXZ / max(_RippleSize, 0.1);
                float drift = _Time.y * 0.35;
                float2 slope = float2(ValueNoise(rippleUV + float2(drift, 0.0)) - ValueNoise(rippleUV + float2(drift - 0.3, 0.0)),
                                      ValueNoise(rippleUV + float2(0.0, -drift)) - ValueNoise(rippleUV + float2(0.0, 0.3 - drift)));
                slope += float2(ValueNoise(rippleUV * 2.3 + float2(-drift, 7.1)) - ValueNoise(rippleUV * 2.3 + float2(0.3 - drift, 7.1)),
                                ValueNoise(rippleUV * 2.3 + float2(3.7, drift)) - ValueNoise(rippleUV * 2.3 + float2(3.7, drift - 0.3))) * 0.5;
                normalWS = normalize(normalWS + float3(slope.x, 0.0, slope.y) *
                                     (2.0 * _RippleStrength * rippleFade * lerp(1.0, 0.3, weight)));
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                #if defined(_WATER_NO_DEPTH_TEXTURE)
                    // Without a depth texture the bed can't be found: treat all water as fairly deep, with no shore foam.
                    float waterDepth = _DeepColorDepth;
                    float viewDistanceInWater = _DeepColorDepth;
                    half foam = 0.0;
                #else
                    // Rebuild the position of the bed behind this pixel from the camera depth texture.
                    float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                    #if UNITY_REVERSED_Z
                        float sceneDepth = SampleSceneDepth(screenUV);
                    #else
                        float sceneDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(screenUV));
                    #endif
                    float3 bedWS = ComputeWorldSpacePosition(screenUV, sceneDepth, UNITY_MATRIX_I_VP);
                    float waterDepth = max(positionWS.y - bedWS.y, 0.0);
                    float viewDistanceInWater = distance(positionWS, bedWS);

                    // Shore foam: a crisp band where the water is shallowest that breathes in and out like lapping
                    // waves, plus a fainter line further out that fades as it rolls in.
                    float pulse = 0.5 + 0.5 * sin(_Time.y * _FoamSpeed + dot(positionWS.xz, float2(0.23, 0.17)));
                    float foamEdge = _FoamWidth * lerp(0.6, 1.0, pulse);
                    float depthPerPixel = max(fwidth(waterDepth), 0.0001);
                    half foam = saturate((foamEdge - waterDepth) / depthPerPixel + 0.5);
                    float outerLineDepth = _FoamWidth * lerp(2.4, 1.3, pulse);
                    half outerLine = 1.0 - saturate(abs(waterDepth - outerLineDepth) / (0.08 * _FoamWidth + depthPerPixel));
                    foam = max(foam, outerLine * 0.55 * (1.0 - pulse));
                #endif

                // Crest foam from the Jacobian of the waves' sideways movement, which drops towards 0 where a crest
                // squeezes and folds over: fresh, soft-edged foam where swells and shorter waves together fold, and the
                // FFT's fading streaks from waves that broke a moment ago, gathered on the swell crests the way
                // whitecaps ride on them.
                half breaking = saturate((lerp(0.3, 0.9, _CrestFoam) - (squeeze + detailSqueeze)) * 4.0);
                half swellCrest = saturate((1.0 - squeeze) * 4.0);
                half streaks = saturate(detailFoam) * lerp(0.2, 1.0, swellCrest);
                foam = max(foam, max(breaking, streaks) * step(0.001, _CrestFoam));

                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1.0, 1.0, 1.0, 1.0));
                half3 sunLight = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);

                // Turquoise over the shallows, deep blue further down.
                half depthBlend = 1.0 - exp(-waterDepth / max(_DeepColorDepth, 0.01));
                half3 waterColour = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);
                half3 bodyLight = waterColour * (ambient + sunLight * NdotL);

                // Wave crests: parts of the surface that are both high and sharpened. Height alone would light each long
                // swell from end to end in a stripe; sharpening comes from several of the waves meeting, so together they
                // pick out short, irregular stretches of crest, and slow drifting noise breaks those up further. Far
                // away the short waves fade out, the surface sharpens less and the crests calm down with it.
                half height = saturate(input.wave.w / max(0.5 * _WaterWaveHeight * weight, 0.01));
                half sharpness = saturate((1.0 - squeeze) * 2.0);
                half breakup = lerp(1.0, 2.0 * ValueNoise(restXZ * 0.12 + float2(0.0, _Time.y * 0.05)), _CrestBreakup);
                half crest = saturate(height * sharpness * breakup);

                // Light through the crests: the thin water at the top of a wave glows a soft turquoise, most when
                // looking towards the sun.
                half towardsSun = pow(saturate(dot(-viewDirWS, mainLight.direction)), 4.0);
                bodyLight += _ScatterColor.rgb * sunLight * (crest * (0.15 + towardsSun) * _ScatterStrength);

                // Pale blue-white on the crests, lit like the rest of the water so it never glows on its own and stays
                // well short of the foam's white.
                half crestHighlight = smoothstep(_CrestThreshold, _CrestThreshold + 0.3, crest) * _CrestHighlight;
                half3 crestLight = _CrestColor.rgb * (ambient + sunLight * (0.5 + 0.5 * NdotL));
                bodyLight = lerp(bodyLight, crestLight, crestHighlight);

                // Sky reflection, strongest at grazing angles (Fresnel). Reflections are kept above the horizon.
                half3 reflectDir = reflect(-viewDirWS, normalWS);
                reflectDir.y = abs(reflectDir.y);
                half3 skyColour = GlossyEnvironmentReflection(reflectDir, 0.08, 1.0);
                half fresnel = (0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewDirWS)), 5.0)) * _ReflectionStrength;

                // The sun's reflection on the waves.
                float3 halfDir = normalize(float3(mainLight.direction) + float3(viewDirWS));
                half3 glint = sunLight * (pow(saturate(dot(normalWS, halfDir)), _GlintSharpness) * _GlintStrength);

                // Share of the bed still visible, from how far the eye looks through the water.
                half transmittance = exp(-viewDistanceInWater / max(_Clarity, 0.01));

                // Premultiplied: the blend adds the bed behind the surface scaled by (1 - alpha).
                half3 colour = bodyLight * (1.0 - transmittance) * (1.0 - fresnel) + skyColour * fresnel + glint;
                half alpha = 1.0 - transmittance * (1.0 - fresnel);

                half3 foamColour = _FoamColor.rgb * (ambient + sunLight * (0.4 + 0.6 * NdotL));
                colour = lerp(colour, foamColour, foam);
                alpha = lerp(alpha, 1.0, foam);

                // The survivability heatmap's sea, lit like the foam.
                if (_SurvivalHeatmapWater > 0.5 && _SurvivalHeatmapOpacity > 0.0)
                {
                    float2 heatmapUV = (positionWS.xz - _SurvivalHeatmapRect.xy) * _SurvivalHeatmapRect.zw;
                    if (all(heatmapUV >= 0.0) && all(heatmapUV <= 1.0))
                    {
                        float4 heat = SAMPLE_TEXTURE2D_LOD(_SurvivalHeatmap, sampler_SurvivalHeatmap, heatmapUV, 0);
                        half cover = heat.a * _SurvivalHeatmapOpacity;
                        colour = lerp(colour, heat.rgb * (ambient + sunLight * (0.4 + 0.6 * NdotL)), cover);
                        alpha = lerp(alpha, 1.0, cover);
                    }
                }

                colour = MixFogColor(colour, unity_FogColor.rgb * alpha, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
