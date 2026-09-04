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

                Light mainLight = GetMainLight(IN.shadowCoord);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 radiance = mainLight.color * (NdotL * mainLight.shadowAttenuation * mainLight.distanceAttenuation);

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
    }
}
