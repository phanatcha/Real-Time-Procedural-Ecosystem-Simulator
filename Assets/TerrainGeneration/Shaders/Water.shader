Shader "Custom/Water"
{
    // Low-poly water surface drawn at the water level by WaterSurface. The lake and river bed settings are not used
    // here: WaterSurface hands them to the terrain shader, which draws the bed seen through the water.
    Properties
    {
        [Header(Water colour)]
        _ShallowColor ("Shallow colour", Color) = (0.24, 0.76, 0.72, 1)
        _DeepColor ("Deep colour", Color) = (0.04, 0.19, 0.34, 1)
        _DeepColorDepth ("Depth of the deep colour (m)", Float) = 6
        _Clarity ("See-through distance (m)", Float) = 2.5

        [Header(Waves and facets)]
        _FacetSize ("Facet size (m)", Float) = 3
        _FacetJitter ("Facet irregularity", Range(0, 0.25)) = 0.22
        _WaveHeight ("Wave height (m)", Float) = 0.3
        _WaveLength ("Swell length (m)", Float) = 11
        _WaveSpeed ("Wave speed", Float) = 1
        _WaveFadeDistance ("Waves calm down by (m)", Float) = 450
        _FacetShading ("Facet contrast", Range(0, 1)) = 0.5

        [Header(Light)]
        _ReflectionStrength ("Sky reflection", Range(0, 1)) = 0.85
        _GlintSharpness ("Sun glint sharpness", Range(16, 1024)) = 300
        _GlintStrength ("Sun glint strength", Range(0, 4)) = 1.5

        [Header(Shore foam)]
        _FoamColor ("Foam colour", Color) = (0.93, 0.97, 1, 1)
        _FoamWidth ("Foam width (m)", Float) = 0.5
        _FoamSpeed ("Foam pulse speed", Float) = 1.2

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

            // Premultiplied alpha: the shader decides how much of the bed behind shows through. Culling is off so a
            // triangle folded over by the facet jitter still draws instead of leaving a hole.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
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
                float _FacetSize;
                float _FacetJitter;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveFadeDistance;
                half _FacetShading;
                half _ReflectionStrength;
                half _GlintSharpness;
                half _GlintStrength;
                half4 _FoamColor;
                float _FoamWidth;
                float _FoamSpeed;
                half4 _LakebedShallowColor;
                half4 _LakebedDeepColor;
                float _LakebedDeepDepth;
                half _CausticsStrength;
                float _WetShoreHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            // Pseudo-random numbers from a world position, without sine so large coordinates stay stable. The same
            // position always gives the same numbers, so neighbouring tiles agree about their shared edge.
            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Three crossing swells plus an independent bob for every grid point, roughly in -1..1.
            float WaveShape(float2 gridPoint, float time)
            {
                float k = 6.2831853 / max(_WaveLength, 0.01);
                float swell = sin(dot(gridPoint, float2(0.82, 0.57)) * k + time)
                            + 0.6 * sin(dot(gridPoint, float2(-0.43, 0.9)) * (k * 1.63) + time * 1.31)
                            + 0.35 * sin(dot(gridPoint, float2(0.96, -0.28)) * (k * 2.71) + time * 1.87);
                float2 random = Hash22(gridPoint + 71.3);
                float bob = sin(time * (1.4 + random.x) + random.y * 6.2831853);
                return swell * (0.75 / 1.95) + bob * 0.25;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 gridPoint = positionWS.xz;

                // Irregular triangles: every grid point is nudged sideways by its own fixed random amount.
                positionWS.xz += (Hash22(gridPoint) - 0.5) * (2.0 * _FacetJitter * _FacetSize);

                // Waves calm down far away, where facets would shrink below a pixel and shimmer.
                float distanceToCamera = distance(gridPoint, _WorldSpaceCameraPos.xz);
                float calm = 1.0 - 0.8 * smoothstep(0.35 * _WaveFadeDistance, _WaveFadeDistance, distanceToCamera);
                positionWS.y += WaveShape(gridPoint, _Time.y * _WaveSpeed) * _WaveHeight * calm;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;

                // One flat normal per triangle gives the faceted look; tilting it further makes the facets read.
                float3 faceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
                faceNormal = faceNormal.y < 0.0 ? -faceNormal : faceNormal;
                float tilt = 1.0 + 2.0 * _FacetShading;
                float3 normalWS = normalize(float3(faceNormal.x * tilt, faceNormal.y, faceNormal.z * tilt));
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                #if defined(_WATER_NO_DEPTH_TEXTURE)
                    // Without a depth texture the bed can't be found: treat all water as fairly deep, with no foam.
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

                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1.0, 1.0, 1.0, 1.0));
                half3 sunLight = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);

                // Turquoise over the shallows, deep blue further down; facets facing the sun are brighter.
                half depthBlend = 1.0 - exp(-waterDepth / max(_DeepColorDepth, 0.01));
                half3 waterColour = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);
                half3 bodyLight = waterColour * (ambient + sunLight * NdotL);

                // Sky reflection, strongest at grazing angles (Fresnel). Reflections are kept above the horizon.
                half3 reflectDir = reflect(-viewDirWS, normalWS);
                reflectDir.y = abs(reflectDir.y);
                half3 skyColour = GlossyEnvironmentReflection(reflectDir, 0.08, 1.0);
                half fresnel = (0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewDirWS)), 5.0)) * _ReflectionStrength;

                // Sun glints: whole facets flash as they tilt towards the sun.
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

                colour = MixFogColor(colour, unity_FogColor.rgb * alpha, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
