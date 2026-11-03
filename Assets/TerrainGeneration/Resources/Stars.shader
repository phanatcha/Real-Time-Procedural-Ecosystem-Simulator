// The night sky's stars, drawn by DayNightLighting as small quads on a dome round the camera. Added on top of the sky,
// behind everything else, and faded by _BorealStarAlpha, which is 0 by day. Vertex colour: rgb the star's brightness,
// alpha its twinkle phase. It lives in Resources so player builds always include it.
Shader "Hidden/Boreal/Stars"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Stars"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _BorealStarAlpha;

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
                half twinkle = 0.75 + 0.25 * sin(_Time.y * 2.3 + input.color.a * 6.2831853);
                output.color = input.color.rgb * (_BorealStarAlpha * twinkle);
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
