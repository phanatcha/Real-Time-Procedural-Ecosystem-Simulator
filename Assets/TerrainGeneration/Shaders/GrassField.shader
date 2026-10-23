// Dense grass and meadow flowers around the camera (GrassField), drawn with GPU instancing from the plants
// GrassField.compute places each frame. Lit like Custom/Vegetation: real shadows, sky light and ambient occlusion,
// with Stylisation blending towards soft two-tone light, light through the blades, a rim and wind. Flowers carry
// their own vertex colours; the grass tuft's vertex red is height up the blade (see
// VegetationMeshBuilder.BuildGrassTuft), turned into the grass colours here. Alpha is flexibility.
Shader "Custom/Grass Field"
{
    Properties
    {
        _RootColour ("Root colour", Color) = (0.45, 0.62, 0.3, 1)
        _TipColour ("Tip colour", Color) = (0.83, 0.93, 0.56, 1)
        _DryTint ("Dry ground tint (multiplier)", Vector) = (1.08, 1.02, 0.8, 0)
        _LushTint ("Lush ground tint (multiplier)", Vector) = (0.9, 1.04, 0.95, 0)
        _Stylisation ("Stylisation (0 realistic, 1 painted)", Range(0, 1)) = 0.75
        _ShadowTint ("Shadow tint (multiplier)", Vector) = (0.85, 0.92, 1.06, 0)
        _Translucency ("Light through blades", Range(0, 2)) = 0.7
        _Rim ("Rim light", Range(0, 1)) = 0.15
        _WindStrength ("Wind strength (m)", Range(0, 1)) = 0.25
        _WindSpeed ("Wind speed", Range(0, 4)) = 1.4
        _WindDirection ("Wind direction (x, z)", Vector) = (0.8, 0, 0.6, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RootColour;
                float4 _TipColour;
                float4 _DryTint;
                float4 _LushTint;
                float _Stylisation;
                float4 _ShadowTint;
                float _Translucency;
                float _Rim;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            struct Instance
            {
                float3 position;
                float yaw;
                float scale;
                float tint;
                float kind;
                float padding;
            };

            // Set per draw by GrassField: the placed plants, whether they are flowers, how tall grass grows, and how
            // large flowers are drawn and how much their petals glow.
            StructuredBuffer<Instance> _Instances;
            float _IsFlower;
            float _GrassHeight;
            float _FlowerScale;
            float _FlowerGlow;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
            };

            float3 ApplyWind(float3 positionWS, float flexibility)
            {
                float phase = dot(positionWS.xz, float2(0.05, 0.07));
                float time = _Time.y * _WindSpeed;
                float sway = sin(time + phase) * 0.7 + sin(time * 2.3 + phase * 1.9) * 0.3;
                float2 direction = normalize(_WindDirection.xz + float2(0.00001, 0.0));
                return positionWS + float3(direction.x, 0.0, direction.y) * (sway * _WindStrength * flexibility);
            }

            float3 Turn(float3 v, float s, float c)
            {
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            Varyings vert(Attributes IN)
            {
                Instance plant = _Instances[IN.instanceID];
                float3 stretch = _IsFlower > 0.5 ? float3(_FlowerScale, _FlowerScale, _FlowerScale) : float3(1.0, _GrassHeight, 1.0);
                float s = sin(plant.yaw);
                float c = cos(plant.yaw);
                float3 local = IN.positionOS * plant.scale * stretch;

                Varyings OUT;
                OUT.positionWS = ApplyWind(plant.position + Turn(local, s, c), IN.color.a);
                OUT.normalWS = normalize(Turn(IN.normalOS, s, c));
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);

                // Blades run from the root colour to the tip colour, tinted by how wet the ground is. Flowers keep
                // their own colours, a little lighter or darker from plant to plant.
                float3 colour = _IsFlower > 0.5
                    ? IN.color.rgb * lerp(0.92, 1.08, plant.tint)
                    : lerp(_RootColour.rgb, _TipColour.rgb, IN.color.r) * lerp(_DryTint.rgb, _LushTint.rgb, plant.tint);

                OUT.color = float4(colour, IN.color.a);
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
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS), IN.positionWS, half4(1, 1, 1, 1));
                AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionHCS));
                float shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                float facing = dot(normalWS, mainLight.direction);
                float wrap = 0.35;
                float realistic = saturate((facing + wrap) / (1.0 + wrap));
                float painted = smoothstep(-0.05, 0.25, facing + wrap * 0.5);
                float lit = lerp(realistic, painted, _Stylisation) * shadow * occlusion.directAmbientOcclusion;

                float3 ambient = SampleSH(normalWS) * occlusion.indirectAmbientOcclusion;
                float3 shadeTint = lerp(float3(1.0, 1.0, 1.0), _ShadowTint.rgb, _Stylisation * (1.0 - lit));
                float3 colour = albedo * (ambient * shadeTint + mainLight.color * lit);

                float3 viewDirection = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float backlight = pow(saturate(dot(-viewDirection, mainLight.direction)), 4.0);
                colour += albedo * mainLight.color * (backlight * flexibility * _Translucency * (0.4 + 0.6 * shadow));

                float rim = pow(1.0 - saturate(dot(normalWS, viewDirection)), 3.0) * _Rim;
                colour += albedo * rim * (ambient + mainLight.color * shadow * 0.5);

                // Flower petals glow a little whatever the light, so they catch the eye even in shade. Petals are
                // the bright parts of a flower; stems and leaves are darker and don't glow.
                if (_IsFlower > 0.5)
                {
                    float petal = smoothstep(0.55, 0.8, max(albedo.r, max(albedo.g, albedo.b)));
                    colour += albedo * (_FlowerGlow * petal);
                }

                return float4(colour, 1);
            }
            ENDHLSL
        }
    }
}
