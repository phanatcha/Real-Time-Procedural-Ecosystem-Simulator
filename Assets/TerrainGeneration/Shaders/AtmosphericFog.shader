// Full-screen atmosphere, drawn by a Full Screen Pass Renderer Feature before post-processing. AtmosphericFog sets
// the values each frame. Three parts:
//  - haze: blue air that thickens with distance, so far mountains fade towards the sky (aerial perspective);
//  - mist: fog that is densest just above the water and thins with height, so valleys fill first (height fog);
//  - sun glow: both are lit warmer when looking towards the sun (forward scattering).
// The edge of the drawn world and the sky near the horizon fade to the same haze colour, so they meet without a seam.
Shader "Hidden/Boreal/Atmospheric Fog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Atmospheric Fog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _BorealFogStrength;
            float4 _BorealFogHazeColour;
            float4 _BorealFogMistColour;
            float4 _BorealFogSunColour;
            float4 _BorealFogSunDirection;
            // x: haze density per metre, y: mist density per metre at its base, z: mist falloff height in metres,
            // w: mist base height above the water.
            float4 _BorealFogDensity;
            // x, y: distances where the world's edge fades in and is fully hidden, z: height of the sky's horizon band
            // (sine of its angle).
            float4 _BorealFogEdge;

            // Set by WaterSurface while the water is drawn.
            float _TerrainWaterSurface;
            float _TerrainWaterHeight;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_BorealFogStrength <= 0.0) return scene;

                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = rawDepth <= 0.000001;
                #else
                    bool sky = rawDepth >= 0.999999;
                #endif
                float3 cameraPosition = _WorldSpaceCameraPos;
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                // The water is transparent and writes no depth, so the depth here is the bed below it. The fog stops
                // at the water's surface instead, or the water would gather the bed's extra distance and low mist.
                float waterHeight = _TerrainWaterSurface > 0.5 ? _TerrainWaterHeight : -1000.0;
                if (!sky && positionWS.y < waterHeight && cameraPosition.y > waterHeight)
                {
                    float travelled = (cameraPosition.y - waterHeight) / max(cameraPosition.y - positionWS.y, 0.0001);
                    positionWS = cameraPosition + (positionWS - cameraPosition) * travelled;
                }

                float3 ray = positionWS - cameraPosition;
                float rayLength = length(ray);
                float3 direction = ray / max(rayLength, 0.0001);

                // A tight bright core around the sun plus a wide, weak spread.
                float towardsSun = saturate(dot(direction, _BorealFogSunDirection.xyz));
                float glow = 0.7 * pow(towardsSun, 32.0) + 0.3 * pow(towardsSun, 4.0);
                float3 haze = _BorealFogHazeColour.rgb + _BorealFogSunColour.rgb * glow;

                float amount;
                float3 fogColour;
                if (sky)
                {
                    float belowBand = 1.0 - saturate(direction.y / max(_BorealFogEdge.z, 0.001));
                    amount = belowBand * belowBand;
                    fogColour = haze;
                }
                else
                {
                    float hazeDepth = rayLength * _BorealFogDensity.x;

                    // Mist density is base * exp(-(height - baseHeight) / falloff); this is its exact integral along
                    // the straight ray from the camera.
                    float falloff = max(_BorealFogDensity.z, 0.1);
                    float baseHeight = max(waterHeight, 0.0) + _BorealFogDensity.w;
                    float climb = clamp(ray.y / falloff, -20.0, 20.0);
                    float along = abs(climb) > 0.0001 ? (1.0 - exp(-climb)) / climb : 1.0;
                    float atCamera = exp(min((baseHeight - cameraPosition.y) / falloff, 10.0));
                    float mistDepth = _BorealFogDensity.y * atCamera * rayLength * along;

                    float transmittance = exp(-(hazeDepth + mistDepth));
                    float mistShare = mistDepth / max(hazeDepth + mistDepth, 0.00001);
                    fogColour = lerp(haze, _BorealFogMistColour.rgb + _BorealFogSunColour.rgb * glow * 0.5, mistShare);

                    float edge = smoothstep(_BorealFogEdge.x, _BorealFogEdge.y, rayLength);
                    amount = max(1.0 - transmittance, edge);
                    fogColour = lerp(fogColour, haze, edge);
                }

                return float4(lerp(scene.rgb, fogColour, saturate(amount * _BorealFogStrength)), scene.a);
            }
            ENDHLSL
        }
    }
}
