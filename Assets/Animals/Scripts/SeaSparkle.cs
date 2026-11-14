using System.Collections.Generic;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEngine;

namespace Boreal.Animals
{
    // Item 76: at night the open sea, where the plankton live, sparkles where swimmers pass, as dinoflagellates flash
    // when disturbed and light up wakes. A look only: one shared particle system, fed a few blue sparks behind each
    // animal swimming through open sea near the camera, more the darker it is and the further it moved. It adds itself
    // to a scene with a Species Manager, as the day clock does, and does nothing by day or with day and night off.
    public sealed class SeaSparkle : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<SeaSparkle>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Sea Sparkle").AddComponent<SeaSparkle>();
            }
        }

        [Tooltip("Only animals this close to the camera, in metres, leave sparks.")]
        [Min(10f)] public float cameraRadius = 220f;
        [Tooltip("Real seconds between looks at the swimmers.")]
        [Min(0.05f)] public float interval = 0.2f;
        [Tooltip("Sparks per metre swum, in full dark.")]
        [Min(0f)] public float sparksPerMetre = 3f;
        [Tooltip("How dark it must be (0 day, 1 full night) before the sea sparkles.")]
        [Range(0f, 1f)] public float startsAtDarkness = 0.25f;
        [Min(1)] public int maximumSparks = 800;

        static readonly Color SparkColour = new Color(0.35f, 0.8f, 1f);

        readonly List<SeekFood> nearby = new List<SeekFood>();
        readonly Dictionary<SeekFood, Vector3> lastPositions = new Dictionary<SeekFood, Vector3>();
        readonly List<SeekFood> gone = new List<SeekFood>();
        ParticleSystem sparks;
        float timer;

        // Sparks made since the scene started, for the soak runner's status line.
        public static int SparksMade { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCount() => SparksMade = 0;

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = interval;

            float darkness = WorldDay.IsRunning ? 1f - WorldDay.Current.daylight : 0f;
            SpeciesManager manager = SpeciesManager.Instance;
            Camera view = Camera.main;
            if (darkness < startsAtDarkness || manager == null || view == null || !WaterAccess.HasWater)
            {
                lastPositions.Clear();
                return;
            }

            AnimalTerrainWorld world = AnimalTerrainWorld.Active;
            if (world == null || !SeaBiomeMap.TryCreateClassifier(world, out SeaBiomeClassifier biomes))
            {
                lastPositions.Clear();
                return;
            }

            manager.FindAgentsNear(view.transform.position, cameraRadius, nearby);
            float strength = Mathf.InverseLerp(startsAtDarkness, 1f, darkness);
            foreach (SeekFood animal in nearby)
            {
                if (animal == null || !animal.IsAlive || !animal.IsInWater) continue;
                Vector3 position = animal.transform.position;
                if (lastPositions.TryGetValue(animal, out Vector3 last) &&
                    world.TryGetCachedSample(position, out EnvironmentSample sample) &&
                    biomes.Classify(sample) == SeaBiome.OpenSea)
                {
                    Emit(last, position, strength);
                }
                lastPositions[animal] = position;
            }

            // Forget animals that left the water or the camera's reach.
            gone.Clear();
            foreach (SeekFood animal in lastPositions.Keys)
            {
                if (animal == null || !nearby.Contains(animal) || !animal.IsInWater) gone.Add(animal);
            }
            foreach (SeekFood animal in gone) lastPositions.Remove(animal);
        }

        void Emit(Vector3 from, Vector3 to, float strength)
        {
            float moved = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            int count = Mathf.Min(12, Mathf.RoundToInt(moved * sparksPerMetre * strength));
            if (count <= 0) return;
            if (sparks == null) sparks = MakeSystem();
            if (sparks == null) return;

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
            float surface = WaterAccess.SurfaceHeight + 0.05f;
            for (int i = 0; i < count; i++)
            {
                Vector3 along = Vector3.Lerp(from, to, Random.value);
                Vector2 spread = Random.insideUnitCircle * 1.2f;
                emit.position = new Vector3(along.x + spread.x, surface, along.z + spread.y);
                emit.velocity = new Vector3(Random.Range(-0.15f, 0.15f), 0f, Random.Range(-0.15f, 0.15f));
                emit.startSize = Random.Range(0.15f, 0.4f);
                emit.startLifetime = Random.Range(0.7f, 1.5f);
                emit.startColor = SparkColour;
                sparks.Emit(emit, 1);
            }
            SparksMade += count;
        }

        ParticleSystem MakeSystem()
        {
            Shader shader = Shader.Find("Hidden/Boreal/SeaSparkle");
            if (shader == null) return null;
            GameObject holder = new GameObject("Sparks");
            holder.transform.SetParent(transform, false);
            ParticleSystem system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(1, maximumSparks);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = new Material(shader) { name = "Sea Sparkle" };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
            });
            system.Play();
            return system;
        }
    }
}
