// Wildfire smoke and haze (item 79), drawn by WildfireVisuals as soft billboards. Each puff is a ball of churning
// noise, dark brown-grey as it leaves the fire and paler as it rises and thins, lit by the sky and the sun, and glowing
// from the fire below while young (_Glow, raised at night by WildfireVisuals). It fades where it meets the ground and
// trees (soft particles) and into the fog. _Opacity thins it for haze. Vertex colour: tint in rgb, opacity in a.
// TEXCOORD0: uv in xy, age (0-1) in z, a stable random number in w. In Resources so builds include it.
Shader "Hidden/Boreal/WildfireSmoke"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.7
        _Glow ("Glow from the fire", Float) = 0.5
        _SoftDistance ("Soft distance (m)", Float) = 5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Smoke"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _Opacity;
                float _Glow;
                float _SoftDistance;
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
                float4 screen : TEXCOORD1;
                float3 lit : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.color = input.color;
                output.uv = input.uv;
                output.screen = ComputeScreenPos(position.positionCS);
                output.fog = ComputeFogFactor(position.positionCS.z);
                // Sky light from above and the sun, which smoke scatters whichever way it shines.
                Light sun = GetMainLight();
                output.lit = SampleSH(float3(0.0, 1.0, 0.0)) * 0.95 +
                             sun.color * (0.3 + 0.35 * saturate(sun.direction.y + 0.3));
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float age = input.uv.z;
                float seed = input.uv.w;
                float2 centred = (uv - 0.5) * 2.0;
                float ball = saturate(1.0 - length(centred));
                // The puff churns slowly and is eaten away at its edges by the noise.
                float2 churn = uv * 0.85 + float2(seed * 5.37, seed * 2.91) + _Time.y * float2(0.011, -0.017);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, churn).r * 0.7 +
                              SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, churn * 2.6).g * 0.3;
                float density = saturate(ball * 1.7 - (1.0 - noise) * 1.05);
                density *= saturate(age * 6.0) * saturate((1.0 - age) * 1.6);

                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                density *= saturate((sceneDepth - input.screen.w) / max(_SoftDistance, 0.01));

                float3 tint = lerp(float3(0.13, 0.115, 0.1), float3(0.5, 0.5, 0.52), saturate(age * 1.4)) * input.color.rgb;
                float3 colour = tint * input.lit * (0.75 + 0.5 * noise);
                float young = (1.0 - age);
                colour += float3(1.0, 0.4, 0.12) * (_Glow * young * young * young * (0.6 + 0.4 * noise));
                colour = MixFog(colour, input.fog);
                return float4(colour, saturate(density * input.color.a * _Opacity));
            }
            ENDHLSL
        }
    }
}
