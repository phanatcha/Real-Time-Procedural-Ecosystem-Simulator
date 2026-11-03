using UnityEngine;
using UnityEngine.Rendering;

namespace Boreal.TerrainGeneration
{
    // Weather for the seasons around the camera: leaves drifting down while the birches and bushes drop theirs in fall,
    // and snow falling as winter sets in, both settling on the terrain. It follows SeasonalLook, so it shows whatever the
    // season clock has set, and nothing in summer. Leaves only fall over land. The flakes and leaves are tiny meshes
    // drawn with URP's lit particle shader, which takes each particle's colour (Custom/Vegetation if a build left it out).
    [DisallowMultipleComponent]
    public sealed class SeasonalWeather : MonoBehaviour
    {
        [Tooltip("Leaves a second at the height of leaf fall.")]
        [Range(0f, 400f)] public float leavesPerSecond = 70f;
        [Tooltip("Flakes a second while snow is building up.")]
        [Range(0f, 4000f)] public float snowflakesPerSecond = 900f;
        [Tooltip("How far from the point below the camera leaves and snow fall, in metres.")]
        [Range(10f, 150f)] public float radius = 45f;

        static readonly int WaterSurfaceId = Shader.PropertyToID("_TerrainWaterSurface");
        static readonly int WaterHeightId = Shader.PropertyToID("_TerrainWaterHeight");

        ParticleSystem leaves;
        ParticleSystem snow;
        Material material;
        Mesh leafMesh;
        Mesh flakeMesh;
        float previousSnow;

        void Start()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null) shader = Shader.Find("Custom/Vegetation");
            if (shader == null) return;

            material = new Material(shader) { name = "Seasonal Weather (runtime)" };
            leafMesh = Flake(0.5f, 1f);
            flakeMesh = Flake(1f, 1f);
            leaves = Create("Falling Leaves", leafMesh, 600, new Vector2(9f, 13f), new Vector2(0.18f, 0.3f), -1.1f, 0.9f,
                            new Gradient
                            {
                                colorKeys = new[]
                                {
                                    new GradientColorKey(new Color(0.95f, 0.72f, 0.14f), 0f),
                                    new GradientColorKey(new Color(0.93f, 0.45f, 0.1f), 0.6f),
                                    new GradientColorKey(new Color(0.7f, 0.14f, 0.08f), 1f)
                                }
                            });
            snow = Create("Falling Snow", flakeMesh, 5000, new Vector2(16f, 22f), new Vector2(0.05f, 0.09f), -1.6f, 0.35f,
                          new Gradient { colorKeys = new[] { new GradientColorKey(new Color(0.95f, 0.97f, 1f), 0f) } });
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (leafMesh != null) Destroy(leafMesh);
            if (flakeMesh != null) Destroy(flakeMesh);
        }

        void LateUpdate()
        {
            Camera view = Camera.main;
            if (view == null || leaves == null || snow == null) return;

            SeasonalLookState look = SeasonalLook.Current;
            Vector3 cameraPosition = view.transform.position;
            Vector3 ground = Physics.Raycast(cameraPosition, Vector3.down, out RaycastHit hit, 2000f)
                ? hit.point
                : cameraPosition - Vector3.up * 20f;
            bool overWater = Shader.GetGlobalFloat(WaterSurfaceId) > 0.5f &&
                             ground.y < Shader.GetGlobalFloat(WaterHeightId) + 0.3f;

            // Most leaves fall halfway through the fall, while the trees are still coloured.
            float falling = look.leafFall > 0f && look.leafFall < 1f && look.autumnColour > 0.2f
                ? 4f * look.leafFall * (1f - look.leafFall) + 0.15f
                : 0f;
            leaves.transform.position = ground + Vector3.up * 13f;
            SetRate(leaves, overWater ? 0f : leavesPerSecond * Mathf.Clamp01(falling));

            // It snows while the snow is building up, and only lightly once it starts to melt.
            float building = look.snow >= previousSnow - 1e-5f ? 1f : 0.15f;
            previousSnow = look.snow;
            snow.transform.position = ground + Vector3.up * 28f;
            SetRate(snow, snowflakesPerSecond * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.5f, look.snow)) * building);
        }

        static void SetRate(ParticleSystem system, float rate)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;
        }

        ParticleSystem Create(string label, Mesh mesh, int maximum, Vector2 lifetime, Vector2 size, float fallSpeed,
                              float flutter, Gradient colours)
        {
            GameObject holder = new GameObject(label);
            holder.transform.SetParent(transform, false);
            ParticleSystem system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = maximum;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colours) { mode = ParticleSystemGradientMode.RandomColor };

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, 2f, radius * 2f);

            // Falling with a drift on the wind, fluttering on the way down.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            velocity.y = new ParticleSystem.MinMaxCurve(fallSpeed * 1.2f, fallSpeed * 0.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = flutter;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;

            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
            spin.y = new ParticleSystem.MinMaxCurve(-1f, 1f);
            spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

            // They come to rest on the ground and lie there until they fade.
            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 1f;
            collision.bounce = 0f;
            collision.lifetimeLoss = 0f;

            ParticleSystem.SizeOverLifetimeModule shrink = system.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
            });
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.alignment = ParticleSystemRenderSpace.World;

            system.Play();
            return system;
        }

        // A flat four-pointed shape one unit long, white so the particle colour shows through: width 0.5 makes a leaf,
        // 1 a flake.
        static Mesh Flake(float width, float length)
        {
            Mesh mesh = new Mesh { name = width < 1f ? "Falling Leaf" : "Snowflake" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, -length * 0.5f), new Vector3(width * 0.5f, 0.05f, 0f),
                new Vector3(0f, 0f, length * 0.5f), new Vector3(-width * 0.5f, 0.05f, 0f)
            };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
