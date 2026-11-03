// Eyeshine (item 75): the glow of night eyes in the dark, drawn by AnimalBodyView as two small dots on the front of
// the body. Added on top of what is behind it and faded by _BorealEyeshine, which DayClock sets from how dark it is
// (0 by day). Vertex colour: how strongly this animal's eyes shine, from its night vision. It lives in Resources so
// player builds always include it.
Shader "Hidden/Boreal/Eyeshine"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Eyeshine"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _BorealEyeshine;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color.rgb * _BorealEyeshine;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.color, 0.0);
            }
            ENDHLSL
        }
    }
}
