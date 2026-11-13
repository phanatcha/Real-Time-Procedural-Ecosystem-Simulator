// Wildfire sparks (item 79): small glowing embers drawn by WildfireVisuals as velocity-stretched billboards that rise
// and swirl downwind. Added on top in HDR, flickering, cooling from yellow-orange to dull red as they age. Vertex colour
// is the ember's colour. TEXCOORD0: uv in xy, age (0-1) in z, a stable random number in w. In Resources for builds.
Shader "Hidden/Boreal/WildfireEmber"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Intensity ("HDR intensity", Float) = 7
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Ember"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _Intensity;
            CBUFFER_END

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
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 centred = (input.uv.xy - 0.5) * 2.0;
                float glow = saturate(1.0 - dot(centred, centred));
                glow = glow * glow;
                float age = input.uv.z;
                float flicker = 0.55 + 0.45 * sin(_Time.y * 23.0 + input.uv.w * 61.0);
                float cooling = saturate(age * 1.3);
                float3 colour = lerp(input.color.rgb, float3(0.55, 0.06, 0.01), cooling);
                float fade = saturate((1.0 - age) * 3.0) * saturate(age * 20.0);
                float3 light = colour * (_Intensity * glow * flicker * fade * (1.0 - 0.6 * cooling));
                return float4(MixFogColor(light, half3(0.0, 0.0, 0.0), input.fog), 0.0);
            }
            ENDHLSL
        }
    }
}
