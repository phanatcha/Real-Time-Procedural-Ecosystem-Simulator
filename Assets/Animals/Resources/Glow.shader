// Glow (item 76): bioluminescence, drawn by AnimalBodyView as a shell a little larger than the body of an animal with
// the glow perk. Added on top of what is behind it, softly over the body and brightest towards its outline, so the
// animal reads as giving off light rather than painted. Faded by _BorealEyeshine, which DayClock sets from how dark it
// is (0 by day), so nothing runs per frame. Vertex colour: the glow's colour. It lives in Resources so player builds
// always include it.
Shader "Hidden/Boreal/Glow"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
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
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                half3 color : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.color = input.color.rgb * _BorealEyeshine;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                // A soft body glow, rising to the outline, where the light passes through the most glowing tissue.
                float rim = 1.0 - facing;
                float glow = 0.25 + 0.75 * rim * rim;
                return half4(input.color * glow, 0.0);
            }
            ENDHLSL
        }
    }
}
