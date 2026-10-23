// Trees, grass and rocks in a painterly style (after Genshin Impact) that keeps real shadows, sky light and ambient
// occlusion. Stylisation blends the diffuse light from a smooth, realistic falloff (0) to a soft two-tone step with
// cool shadows (1). Vertex colours carry the colour, and their alpha how flexible a part is: flexible parts sway in
// the wind and let sunlight through (see VegetationMeshBuilder).
Shader "Custom/Vegetation"
{
    Properties
    {
        _Stylisation ("Stylisation (0 realistic, 1 painted)", Range(0, 1)) = 0.5
        _ShadowTint ("Shadow tint (multiplier)", Vector) = (0.85, 0.92, 1.06, 0)
        _Translucency ("Light through leaves", Range(0, 2)) = 0.6
        _Rim ("Rim light", Range(0, 1)) = 0.2
        _WindStrength ("Wind strength (m)", Range(0, 1)) = 0.2
        _WindSpeed ("Wind speed", Range(0, 4)) = 1.2
        _WindDirection ("Wind direction (x, z)", Vector) = (0.8, 0, 0.6, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _Stylisation;
        float4 _ShadowTint;
        float _Translucency;
        float _Rim;
        float _WindStrength;
        float _WindSpeed;
        float4 _WindDirection;
    CBUFFER_END

    // Sways flexible parts along the wind: a slow gust plus a faster flutter, out of step from place to place. Every
    // pass applies it, so shadows and depth move with what is drawn.
    float3 ApplyWind(float3 positionWS, float flexibility)
    {
        float phase = dot(positionWS.xz, float2(0.05, 0.07));
        float time = _Time.y * _WindSpeed;
        float sway = sin(time + phase) * 0.7 + sin(time * 2.3 + phase * 1.9) * 0.3;
        float2 direction = normalize(_WindDirection.xz + float2(0.00001, 0.0));
        return positionWS + float3(direction.x, 0.0, direction.y) * (sway * _WindStrength * flexibility);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200
        Cull Off

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD3;
                float4 color : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), IN.color.a);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                if (!frontFace)
                {
                    normalWS = -normalWS;
                }

                float3 albedo = IN.color.rgb;
                float flexibility = IN.color.a;

                // Shadow coordinates are found per pixel, because each pixel picks its own shadow cascade. Passing the
                // position lets the shadows fade out towards the shadow distance instead of ending in a hard line.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS), IN.positionWS, half4(1, 1, 1, 1));
                AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionHCS));
                float shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // Leaves and grass wrap the light a little further round than rock and bark, as they let some through.
                float facing = dot(normalWS, mainLight.direction);
                float wrap = 0.35 * saturate(flexibility * 2.0);
                float realistic = saturate((facing + wrap) / (1.0 + wrap));
                float painted = smoothstep(-0.05, 0.25, facing + wrap * 0.5);
                float lit = lerp(realistic, painted, _Stylisation) * shadow * occlusion.directAmbientOcclusion;

                // Sides away from the sun are lit by the blue sky; the painted look leans into that cool tint.
                float3 ambient = SampleSH(normalWS) * occlusion.indirectAmbientOcclusion;
                float3 shadeTint = lerp(float3(1.0, 1.0, 1.0), _ShadowTint.rgb, _Stylisation * (1.0 - lit));
                float3 colour = albedo * (ambient * shadeTint + mainLight.color * lit);

                // Sunlight glowing through leaves and grass when they are between the camera and the sun.
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float backlight = pow(saturate(dot(-viewDirection, mainLight.direction)), 4.0);
                colour += albedo * mainLight.color * (backlight * flexibility * _Translucency * (0.4 + 0.6 * shadow));

                // A soft rim along silhouettes, lifting them off the background.
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirection)), 3.0) * _Rim;
                colour += albedo * rim * (ambient + mainLight.color * shadow * 0.5);

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
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), IN.color.a);
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

        // These two passes put trees and plants in the camera depth texture, so ambient occlusion and the water's
        // depth effects (shore foam, how far you can see into the water) take them into account. Cull Off above
        // applies here too, so both sides of the foliage are written, as they are drawn.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), IN.color.a));
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), IN.color.a));
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // Back faces get the flipped normal, as in the lit pass, so occlusion treats both sides of a leaf alike.
            void DepthNormalsFrag(Varyings IN, bool frontFace : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 normalWS = frontFace ? IN.normalWS : -IN.normalWS;
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
