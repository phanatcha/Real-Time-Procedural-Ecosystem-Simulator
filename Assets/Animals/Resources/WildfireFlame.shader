// Wildfire flames (item 79), drawn by WildfireVisuals as upright billboards from one shared particle system. Each
// flame is a ragged teardrop torn by scrolling noise, white-yellow at its hot base, orange and red towards its tips,
// in HDR so the bloom picks it up. Premultiplied: mostly light added on top, with the faintest darkening at the tips.
// It fades where it meets the ground (soft particles) and with the fog. TEXCOORD0: uv in xy, the particle's age (0-1)
// in z and a stable random number in w. Vertex alpha is how strongly the cell burns. In Resources so builds include it.
Shader "Hidden/Boreal/WildfireFlame"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Intensity ("HDR intensity", Float) = 4.5
        _SoftDistance ("Soft distance (m)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Flame"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _Intensity;
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
                float fog : TEXCOORD2;
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
                return output;
            }

            // Deep red at the coolest, through orange and yellow to near white at the hottest, brighter as it heats.
            float3 Blackbody(float heat)
            {
                float3 colour = lerp(float3(0.45, 0.04, 0.005), float3(1.0, 0.3, 0.03), saturate(heat * 2.2));
                colour = lerp(colour, float3(1.0, 0.6, 0.15), saturate(heat * 2.2 - 1.0));
                colour = lerp(colour, float3(1.0, 0.88, 0.6), saturate(heat * 3.0 - 2.4));
                return colour * (0.25 + 1.4 * heat * heat);
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float age = input.uv.z;
                float seed = input.uv.w;
                float time = _Time.y;

                // Two layers of noise rising through the flame bend it sideways, more towards the tip.
                float2 lower = float2(uv.x * 0.55 + seed * 7.31, uv.y * 0.75 - time * 1.7 - seed * 3.17);
                float first = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, lower).r;
                float second = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, lower * 2.3 + float2(0.37, -time * 1.1)).g;
                float turbulence = first * 0.65 + second * 0.35;
                float y = uv.y;
                // Each flame sways on its own, more towards its tip.
                float sway = sin(time * (2.3 + seed * 1.7) + seed * 40.0) * 0.18;
                float x = (uv.x - 0.5) * 2.0 + ((turbulence - 0.5) * 1.6 + sway) * pow(saturate(y), 0.8);

                // A teardrop: widest a quarter of the way up, a point at the top, its tip eaten away by the noise.
                float width = lerp(0.8, 0.06, pow(saturate(y), 0.85)) * smoothstep(0.0, 0.22, y + 0.05);
                float body = smoothstep(0.0, 1.0, saturate(1.0 - abs(x) / max(width, 0.001)));
                float tip = saturate((1.0 - y) * 1.7 - (1.0 - turbulence) * 0.95 + 0.25);
                // The noise tears the flame into tongues rather than one smooth teardrop.
                float tongues = smoothstep(0.18, 0.5, turbulence + 0.4 * (1.0 - y));
                float density = body * tip * tongues * smoothstep(0.0, 0.06, y);
                // A flame flares up as it is born and dies away at the end of its life.
                density *= saturate((1.0 - age) * 2.5) * saturate(age * 10.0);

                float heat = saturate(density * 1.15 - y * 0.6 + 0.05);
                float alpha = saturate(density * 1.25) * input.color.a;

                // Soft where it meets the ground or a trunk.
                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.screen.w) / max(_SoftDistance, 0.01));

                float3 light = Blackbody(heat) * _Intensity * alpha;
                light = MixFogColor(light, half3(0.0, 0.0, 0.0), input.fog);
                float darken = alpha * 0.3 * saturate(y * 1.4 - 0.2);
                return float4(light, darken);
            }
            ENDHLSL
        }
    }
}
