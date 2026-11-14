// Sea sparkle (item 76): the blue flashes of dinoflagellates where a swimmer passes through the open sea at night,
// drawn by SeaSparkle as small billboards on the water. Added on top in HDR, each a soft dot that flashes up and
// fades, twinkling. Faded by _BorealEyeshine, which DayClock sets from how dark it is (0 by day). Vertex colour is
// the spark's colour. TEXCOORD0: uv in xy, age (0-1) in z, a stable random number in w. In Resources for builds.
Shader "Hidden/Boreal/SeaSparkle"
{
    Properties
    {
        _Intensity ("HDR intensity", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Sparkle"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            float _BorealEyeshine;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 centred = (input.uv.xy - 0.5) * 2.0;
                float dot2 = saturate(1.0 - dot(centred, centred));
                float glow = dot2 * dot2 * dot2;
                float age = input.uv.z;
                // A quick flash, then a slower fade, with a twinkle on top.
                float flash = saturate(age * 12.0) * saturate((1.0 - age) * 1.6);
                float twinkle = 0.6 + 0.4 * sin(_Time.y * 17.0 + input.uv.w * 47.0);
                float3 light = input.color.rgb * (_Intensity * glow * flash * twinkle * _BorealEyeshine);
                return float4(light, 0.0);
            }
            ENDHLSL
        }
    }
}
