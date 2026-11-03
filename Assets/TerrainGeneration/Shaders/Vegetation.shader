// Trees, grass and rocks in a painterly style (after Genshin Impact) that keeps real shadows, sky light and ambient
// occlusion. Stylisation blends the diffuse light from a smooth, realistic falloff (0) to a soft two-tone step with
// cool shadows (1). Vertex colours carry the colour, and their alpha how flexible a part is: flexible parts sway in
// the wind and let sunlight through (see VegetationMeshBuilder). The third UV set says what the seasons do to each part
// (VegetationMeshBuilder's SeasonalPart): with SeasonalLook's timing, birch and bush leaves turn fresh green in spring
// and gold to red in fall, then drop clump by clump, berries come and go, and snow settles on upward faces.
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

    // Set globally by SeasonalLook. Leaves: autumn colour, leaf fall, spring freshness, fruit gone. Ground: snow (the
    // rest is for the terrain, grass and water).
    float4 _SeasonLeaves;
    float4 _SeasonGround;

    float SeasonHash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float SeasonNoise(float2 p)
    {
        float2 cell = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(SeasonHash(cell), SeasonHash(cell + float2(1, 0)), u.x),
                    lerp(SeasonHash(cell + float2(0, 1)), SeasonHash(cell + float2(1, 1)), u.x), u.y);
    }

    // 1 where a part is gone for the season, 0 where it is there: leaves once they have fallen, berries out of season.
    // Each clump or berry has its own moment, nudged by place so neighbouring trees don't turn in step. Every pass
    // clips on it, so fallen leaves leave no shadow either.
    float SeasonHidden(float3 season, float3 positionWS)
    {
        int part = (int)round(season.x);
        float moment = 0.02 + 0.96 * (season.y * 0.75 + SeasonNoise(positionWS.xz / 9.0) * 0.25);
        if (part == 1) return step(moment, _SeasonLeaves.y);
        if (part == 3) return step(moment, _SeasonLeaves.w);
        return 0.0;
    }

    // A part's colour in this season: leaves fresh in spring and gold, orange or red in fall (leaves of food plants
    // stay coloured through winter), and snow on faces turned to the sky. Brightness is kept, so the leaves' shading
    // from dark insides to bright tips survives the change of colour.
    float3 SeasonColour(float3 albedo, float3 season, float3 normalWS, float3 positionWS)
    {
        int part = (int)round(season.x);
        float brightness = dot(albedo, float3(0.3, 0.59, 0.11));
        if (part == 1 || part == 4)
        {
            float3 fall = season.z < 0.5 ? lerp(float3(0.95, 0.72, 0.12), float3(0.95, 0.42, 0.08), season.z * 2.0)
                                         : lerp(float3(0.95, 0.42, 0.08), float3(0.72, 0.1, 0.07), season.z * 2.0 - 1.0);
            fall *= brightness * 1.5 / dot(fall, float3(0.3, 0.59, 0.11));
            float turned = part == 1 ? _SeasonLeaves.x : max(_SeasonLeaves.x, _SeasonLeaves.y * 0.85);
            albedo = lerp(albedo, fall, saturate((turned - season.y * 0.4) / 0.6));
            if (part == 1)
            {
                float3 fresh = float3(0.45, 0.72, 0.2) * (brightness * 1.25 / 0.6);
                albedo = lerp(albedo, fresh, _SeasonLeaves.z * 0.55);
            }
        }

        if (part != 1 && part != 3 && _SeasonGround.x > 0.001)
        {
            // Spruce tiers hold snow on their whole upper skirt, whose soft normals lean outwards.
            float facingUp = part == 2 ? smoothstep(-0.1, 0.45, normalWS.y) * 1.4 : smoothstep(0.15, 0.7, normalWS.y);
            float lying = _SeasonGround.x * facingUp *
                          (0.7 + 0.6 * SeasonNoise(positionWS.xz / 3.0 + positionWS.y));
            albedo = lerp(albedo, float3(0.9, 0.93, 0.97), smoothstep(0.2, 0.6, lying));
        }

        return albedo;
    }

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
                float3 season : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD3;
                float4 color : TEXCOORD4;
                float hidden : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 restWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = ApplyWind(restWS, IN.color.a);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.color = float4(SeasonColour(IN.color.rgb, IN.season, OUT.normalWS, restWS), IN.color.a);
                OUT.hidden = SeasonHidden(IN.season, restWS);
                return OUT;
            }

            float4 frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                clip(0.5 - IN.hidden);
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
                float3 season : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float hidden : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 restWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.hidden = SeasonHidden(IN.season, restWS);
                float3 positionWS = ApplyWind(restWS, IN.color.a);
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
                clip(0.5 - IN.hidden);
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
                float3 season : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float hidden : TEXCOORD0;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                float3 restWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(ApplyWind(restWS, IN.color.a));
                OUT.hidden = SeasonHidden(IN.season, restWS);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target
            {
                clip(0.5 - IN.hidden);
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
                float3 season : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float hidden : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;
                float3 restWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(ApplyWind(restWS, IN.color.a));
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.hidden = SeasonHidden(IN.season, restWS);
                return OUT;
            }

            // Back faces get the flipped normal, as in the lit pass, so occlusion treats both sides of a leaf alike.
            void DepthNormalsFrag(Varyings IN, bool frontFace : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                clip(0.5 - IN.hidden);
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
