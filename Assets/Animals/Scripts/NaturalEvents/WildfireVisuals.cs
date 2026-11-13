using System.Collections.Generic;
using Boreal.World;
using Unity.Collections;
using UnityEngine;

namespace Boreal.Animals
{
    // How wildfires look (item 79). Two parts, both driven by the fire grid and neither costing anything while no fire
    // has burned in the world:
    //
    // - The fire map, one texel per fire cell, set as a global texture for the terrain, vegetation and grass shaders, as
    //   the survivability heatmap is: red is the flames now, green the charred ground (black until the ground plants
    //   come back, then fading over a year), blue the embers (under the flames and for a while after them), alpha the
    //   canopy lost (needles and leaves gone until the forest grows back). Its mipmaps blur the flames into the glow
    //   they cast on the ground around them.
    // - Shared particle systems, never one per tree: upright flames sized by the fuel (tall in burning crowns, low in
    //   grass), sparks rising and swirling downwind, smoke that rises into a column and leans with the wind, and a low
    //   haze downwind. Emission is shared out over the burning cells by their intensity and distance from the camera,
    //   with a fixed budget, so a large burn costs no more than a small one; far fires show only their glow and smoke.
    //   A few pooled flickering point lights give nearby animals firelight. Particles run in real time, so flames look
    //   the same at every simulation speed.
    [DisallowMultipleComponent]
    public sealed class WildfireVisuals : MonoBehaviour
    {
        [Header("Fire map")]
        [Tooltip("Real seconds between fire map updates while fires burn or embers cool.")]
        [Min(0.02f)] public float mapInterval = 0.12f;
        [Tooltip("Real seconds between updates once only charred ground is left, which changes slowly.")]
        [Min(0.1f)] public float quietMapInterval = 2f;
        [Tooltip("Simulated seconds embers glow after a cell has burned out.")]
        [Min(1f)] public float smoulderSeconds = 25f;
        [Tooltip("Simulated seconds charred ground and bare trees take to fade once plants and forest grow back.")]
        [Min(1f)] public float recoveryFadeSeconds = 400f;

        [Header("Flames")]
        [Min(0)] public int maximumFlames = 3500;
        [Tooltip("Flames per real second from a fully burning crown cell close to the camera.")]
        [Min(0f)] public float flamesPerCell = 10f;
        [Tooltip("Metres from the camera within which flames are drawn at full detail; further away fewer, larger ones.")]
        [Min(10f)] public float detailDistance = 350f;
        [Tooltip("Metres from the camera beyond which flames are not drawn, only the fire's glow and smoke.")]
        [Min(10f)] public float flameDistance = 1600f;
        [Tooltip("Flames emitted per real second at most, shared over all the burning cells.")]
        [Min(1f)] public float flameBudget = 2600f;

        [Header("Embers, smoke and haze")]
        [Min(0)] public int maximumEmbers = 1500;
        [Min(0f)] public float embersPerCell = 3f;
        [Min(10f)] public float emberDistance = 300f;
        [Min(0)] public int maximumSmoke = 1400;
        [Tooltip("Smoke puffs per real second from a 32 m block at full heat (four fully burning cells).")]
        [Min(0f)] public float smokePerBlock = 2f;
        [Min(0)] public int maximumHaze = 160;

        [Header("Firelight")]
        [Range(0, 16)] public int lightCount = 6;
        [Min(1f)] public float lightDistance = 450f;
        [Min(0f)] public float lightIntensity = 5f;

        // The camera flames are drawn for, when not the main camera: the soak harness's fire shots set it.
        public static Camera ViewerOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ViewerOverride = null;

        static readonly int MapId = Shader.PropertyToID("_BorealFireMap");
        static readonly int RectId = Shader.PropertyToID("_BorealFireMapRect");
        static readonly int StrengthId = Shader.PropertyToID("_BorealFireStrength");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int NoiseId = Shader.PropertyToID("_NoiseTex");

        WildfireGrid grid;
        Texture2D map;
        float mapTimer;
        readonly List<int> touched = new List<int>();
        readonly HashSet<int> touchedSet = new HashSet<int>();
        bool mapLive;

        ParticleSystem flames, embers, smoke, haze;
        Material flameMaterial, emberMaterial, smokeMaterial, hazeMaterial;
        Texture2D noise;
        float flameCarry, emberCarry, smokeCarry, hazeCarry;
        readonly List<Light> lights = new List<Light>();
        readonly List<int> nearest = new List<int>();
        readonly List<float> nearestDistance = new List<float>();
        Camera viewer;
        float viewerTimer;

        void OnDisable()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            foreach (Light light in lights) if (light != null) light.enabled = false;
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            Unhook();
            DestroyNow(map);
            DestroyNow(noise);
            DestroyNow(flameMaterial);
            DestroyNow(emberMaterial);
            DestroyNow(smokeMaterial);
            DestroyNow(hazeMaterial);
        }

        static void DestroyNow(Object thing)
        {
            if (thing != null) Destroy(thing);
        }

        void LateUpdate()
        {
            WildfireKind fires = Wildfires.Current;
            WildfireGrid current = fires != null ? fires.Grid : null;
            if (current != grid) Attach(current);
            if (grid == null)
            {
                if (mapLive)
                {
                    Shader.SetGlobalFloat(StrengthId, 0f);
                    mapLive = false;
                }
                return;
            }

            SpeciesManager manager = SpeciesManager.Instance;
            float now = manager != null ? Mathf.Max(0f, Time.time - manager.WorldStartTime) : Time.time;
            mapTimer -= Time.unscaledDeltaTime;
            bool busy = grid.BurningCount > 0 || grid.HasSmoke;
            if (touched.Count > 0 && mapTimer <= 0f)
            {
                mapTimer = busy ? mapInterval : quietMapInterval;
                UpdateMap(fires.Settings, now);
            }

            float realDelta = Time.unscaledDeltaTime;
            if (realDelta <= 0f || !busy)
            {
                foreach (Light light in lights) if (light != null) light.enabled = false;
                return;
            }
            viewerTimer -= realDelta;
            if (ViewerOverride != null) viewer = ViewerOverride;
            else if (viewer == null || viewerTimer <= 0f || viewer == ViewerOverride)
            {
                viewer = Camera.main;
                viewerTimer = 1f;
            }
            if (viewer == null) return;
            EnsureParticles();
            NaturalConditions conditions = NaturalEventDirector.Active != null
                ? NaturalEventDirector.Active.Conditions : default;
            Vector3 wind = new Vector3(conditions.windDirection.x, 0f, conditions.windDirection.y) * conditions.windSpeed;
            float darkness = 1f - (WorldDay.IsRunning ? Mathf.Clamp01(WorldDay.Current.daylight) : 1f);
            EmitFlamesAndEmbers(realDelta, wind);
            EmitSmoke(realDelta, wind, darkness);
            PlaceLights(darkness);
        }

        void Attach(WildfireGrid next)
        {
            Unhook();
            grid = next;
            touched.Clear();
            touchedSet.Clear();
            flameCarry = emberCarry = smokeCarry = hazeCarry = 0f;
            if (flames != null) flames.Clear();
            if (embers != null) embers.Clear();
            if (smoke != null) smoke.Clear();
            if (haze != null) haze.Clear();
            DestroyNow(map);
            map = null;
            Shader.SetGlobalFloat(StrengthId, 0f);
            mapLive = false;
            if (grid == null) return;

            grid.CellIgnited += OnCellIgnited;
            int size = grid.size;
            map = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Wildfire Map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                anisoLevel = 0
            };
            NativeArray<Color32> pixels = map.GetPixelData<Color32>(0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = default;
            map.Apply(true, false);
            float span = size * grid.cellSize;
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalVector(RectId, new Vector4(-grid.extent, -grid.extent, 1f / span, 1f / span));
        }

        void Unhook()
        {
            if (grid != null) grid.CellIgnited -= OnCellIgnited;
        }

        void OnCellIgnited(int index, int fireId)
        {
            if (touchedSet.Add(index)) touched.Add(index);
            mapTimer = Mathf.Min(mapTimer, mapInterval);
        }

        // Writes every cell a fire has reached; cells whose ground and trees have fully recovered are forgotten.
        void UpdateMap(WildfireSettings settings, float now)
        {
            NativeArray<Color32> pixels = map.GetPixelData<Color32>(0);
            for (int i = touched.Count - 1; i >= 0; i--)
            {
                int index = touched[i];
                if (!grid.TryGetTimes(index, out float ignitedAt, out float burnedOutAt, out float duration)) continue;
                grid.TryGetFuel(index, out FireFuel fuel);
                float flame = 0f, ember, ground, canopy;
                if (grid.StateOf(index) == WildfireGrid.CellState.Burning)
                {
                    float fraction = Mathf.Clamp01((now - ignitedAt) / Mathf.Max(0.01f, duration));
                    flame = grid.IntensityOf(index);
                    // Embers build up under the flames; ground and crowns char as the fire works through them.
                    ember = Mathf.Clamp01(0.3f + 0.7f * fraction) * (0.5f + 0.5f * fuel.load);
                    ground = Mathf.Clamp01(fraction * 2.5f);
                    canopy = Mathf.Clamp01(fraction * 1.6f);
                }
                else
                {
                    float cooling = Mathf.Max(0f, now - Mathf.Max(ignitedAt, burnedOutAt));
                    ember = Mathf.Exp(-cooling / smoulderSeconds) * (0.5f + 0.5f * fuel.load);
                    if (ember < 0.02f) ember = 0f;
                    float since = now - ignitedAt;
                    ground = 1f - Mathf.Clamp01((since - settings.groundRecoverySeconds) / recoveryFadeSeconds);
                    canopy = 1f - Mathf.Clamp01((since - settings.forestRecoverySeconds) / recoveryFadeSeconds);
                }
                pixels[index] = new Color32(ToByte(flame), ToByte(ground), ToByte(ember), ToByte(canopy));
                if (flame <= 0f && ember <= 0f && ground <= 0f && canopy <= 0f)
                {
                    touchedSet.Remove(index);
                    touched[i] = touched[touched.Count - 1];
                    touched.RemoveAt(touched.Count - 1);
                }
            }
            map.Apply(true, false);
            bool live = touched.Count > 0;
            if (live != mapLive)
            {
                Shader.SetGlobalFloat(StrengthId, live ? 1f : 0f);
                mapLive = live;
            }
        }

        static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        // Shares the flame budget over the burning cells by intensity and distance: full detail near the camera, fewer
        // and larger flames further off, none beyond the flame distance. Sparks only close by.
        void EmitFlamesAndEmbers(float realDelta, Vector3 wind)
        {
            Vector3 eye = viewer.transform.position;
            int count = grid.BurningCount;
            float detail2 = detailDistance * detailDistance;
            float wanted = 0f;
            for (int i = 0; i < count; i++)
            {
                grid.BurningCellAt(i, out Vector2 centre, out float intensity, out FireFuel fuel);
                float d2 = (new Vector3(centre.x, fuel.height, centre.y) - eye).sqrMagnitude;
                if (d2 > flameDistance * flameDistance) continue;
                wanted += intensity * (0.4f + 0.6f * fuel.crown) / (1f + d2 / detail2);
            }
            if (wanted <= 0f) return;
            float scale = Mathf.Min(flamesPerCell, flameBudget / Mathf.Max(0.0001f, wanted));
            float cell = grid.cellSize;
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
            float emberRange2 = emberDistance * emberDistance;
            for (int i = 0; i < count; i++)
            {
                grid.BurningCellAt(i, out Vector2 centre, out float intensity, out FireFuel fuel);
                Vector3 ground = new Vector3(centre.x, fuel.height, centre.y);
                float d2 = (ground - eye).sqrMagnitude;
                if (d2 > flameDistance * flameDistance || intensity <= 0.01f) continue;
                float near = 1f / (1f + d2 / detail2);
                float enlarge = 1f + Mathf.Sqrt(d2) / (detailDistance * 2.5f);
                flameCarry += intensity * (0.4f + 0.6f * fuel.crown) * near * scale * realDelta;
                while (flameCarry >= 1f)
                {
                    flameCarry -= 1f;
                    // Crown fires stand well above the spruces; grass fires knee to head high. Full size at the burn's
                    // peak, whatever the fuel's load.
                    float crown = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, fuel.crown));
                    float stage = intensity / WildfireRules.PeakIntensity(fuel);
                    float height = Mathf.Lerp(Random.Range(1.2f, 3f), Random.Range(9f, 20f), crown) *
                                   (0.5f + 0.5f * stage) * enlarge;
                    emit.position = ground + new Vector3(Random.Range(-0.5f, 0.5f) * cell,
                                                         Mathf.Lerp(-0.3f, Random.Range(1f, 6f), crown),
                                                         Random.Range(-0.5f, 0.5f) * cell);
                    emit.velocity = Vector3.up * Random.Range(1.2f, 2.8f) + wind * 0.25f;
                    emit.startSize3D = new Vector3(height * Random.Range(0.6f, 1.1f), height * Random.Range(0.75f, 1.15f), 1f);
                    emit.startLifetime = Random.Range(0.55f, 1.05f);
                    emit.rotation = 0f;
                    emit.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(0.55f + intensity));
                    flames.Emit(emit, 1);
                }
                if (d2 > emberRange2) continue;
                emberCarry += embersPerCell * intensity * (0.3f + 0.7f * fuel.crown) * near * realDelta;
                while (emberCarry >= 1f)
                {
                    emberCarry -= 1f;
                    emit.position = ground + new Vector3(Random.Range(-0.5f, 0.5f) * cell, Random.Range(1f, 9f) * fuel.crown + 0.5f,
                                                         Random.Range(-0.5f, 0.5f) * cell);
                    emit.velocity = Vector3.up * Random.Range(2.5f, 6.5f) + wind * Random.Range(0.4f, 0.9f) +
                                    Random.insideUnitSphere * 1.5f;
                    float size = Random.Range(0.08f, 0.22f);
                    emit.startSize3D = new Vector3(size, size, size);
                    emit.startLifetime = Random.Range(1.8f, 4.5f);
                    emit.startColor = new Color(1f, Random.Range(0.35f, 0.75f), Random.Range(0.05f, 0.2f), 1f);
                    embers.Emit(emit, 1);
                }
            }
        }

        // Smoke rises from each burning block in proportion to its heat and leans with the wind; a few huge, faint
        // sheets drift low downwind as haze. At night young smoke glows from the fire below it.
        void EmitSmoke(float realDelta, Vector3 wind, float darkness)
        {
            smokeMaterial.SetFloat(GlowId, 0.35f + 1.4f * darkness);
            Vector3 eye = viewer.transform.position;
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
            int blocks = grid.HeatedBlockCount;
            float totalHeat = 0f;
            for (int i = 0; i < blocks; i++)
            {
                grid.GetHeatedBlock(i, out _, out float heat);
                totalHeat += heat;
            }
            // Keep within the particle budget: puffs last about 18 s.
            float budget = maximumSmoke / 18f;
            float scale = Mathf.Min(smokePerBlock / 4f, budget / Mathf.Max(0.0001f, totalHeat));
            Vector3 lean = wind * 0.75f;
            for (int i = 0; i < blocks; i++)
            {
                grid.GetHeatedBlock(i, out Vector2 centre, out float heat);
                float height = GroundHeight(centre);
                smokeCarry += heat * scale * realDelta;
                while (smokeCarry >= 1f)
                {
                    smokeCarry -= 1f;
                    float spread = grid.cellSize * WildfireGrid.BlockCells * 0.5f;
                    emit.position = new Vector3(centre.x + Random.Range(-spread, spread), height + Random.Range(4f, 16f),
                                                centre.y + Random.Range(-spread, spread));
                    emit.velocity = Vector3.up * Random.Range(2.5f, 4.5f) + lean * Random.Range(0.7f, 1.1f);
                    float size = Random.Range(14f, 24f);
                    emit.startSize3D = new Vector3(size, size, size);
                    emit.startLifetime = Random.Range(14f, 24f);
                    emit.rotation = Random.Range(0f, 360f);
                    float shade = Random.Range(0.75f, 1f);
                    emit.startColor = new Color(shade, shade, shade, Random.Range(0.55f, 0.85f));
                    smoke.Emit(emit, 1);
                }
                hazeCarry += heat * 0.02f * realDelta;
                if (hazeCarry >= 1f && haze.particleCount < maximumHaze)
                {
                    hazeCarry -= 1f;
                    Vector3 downwind = wind.sqrMagnitude > 0.01f ? wind.normalized : Vector3.forward;
                    emit.position = new Vector3(centre.x, height + Random.Range(15f, 45f), centre.y) +
                                    downwind * Random.Range(40f, 160f);
                    emit.velocity = wind * 0.9f + Vector3.up * 0.3f;
                    float size = Random.Range(90f, 170f);
                    emit.startSize3D = new Vector3(size, size, size);
                    emit.startLifetime = Random.Range(35f, 55f);
                    emit.rotation = Random.Range(0f, 360f);
                    emit.startColor = new Color(1f, 1f, 1f, Random.Range(0.35f, 0.6f));
                    haze.Emit(emit, 1);
                }
            }
            if (hazeCarry > 2f) hazeCarry = 2f;
        }

        float GroundHeight(Vector2 position)
        {
            if (grid.TryGetCell(position, out int index) && grid.TryGetFuel(index, out FireFuel fuel)) return fuel.height;
            return 0f;
        }

        // The pooled lights go to the hottest blocks nearest the camera, flickering, brighter at night.
        void PlaceLights(float darkness)
        {
            if (lightCount <= 0) return;
            while (lights.Count < lightCount)
            {
                Light light = new GameObject("Firelight").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.52f, 0.18f);
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
                light.enabled = false;
                lights.Add(light);
            }
            Vector3 eye = viewer.transform.position;
            nearest.Clear();
            nearestDistance.Clear();
            int blocks = grid.HeatedBlockCount;
            for (int i = 0; i < blocks; i++)
            {
                grid.GetHeatedBlock(i, out Vector2 centre, out float heat);
                if (heat < 0.6f) continue;
                float distance = Vector2.Distance(centre, new Vector2(eye.x, eye.z));
                if (distance > lightDistance) continue;
                int at = nearestDistance.Count;
                while (at > 0 && nearestDistance[at - 1] > distance) at--;
                if (at >= lightCount) continue;
                nearest.Insert(at, i);
                nearestDistance.Insert(at, distance);
                if (nearest.Count > lightCount)
                {
                    nearest.RemoveAt(lightCount);
                    nearestDistance.RemoveAt(lightCount);
                }
            }
            float time = Time.unscaledTime;
            for (int l = 0; l < lights.Count; l++)
            {
                Light light = lights[l];
                if (l >= nearest.Count)
                {
                    light.enabled = false;
                    continue;
                }
                grid.GetHeatedBlock(nearest[l], out Vector2 centre, out float heat);
                float flicker = 0.75f + 0.25f * Mathf.Sin(time * 11f + l * 3.1f) * Mathf.Sin(time * 4.3f + l * 1.7f);
                light.transform.position = new Vector3(centre.x, GroundHeight(centre) + 5f, centre.y);
                light.range = 22f + 6f * Mathf.Min(4f, heat);
                light.intensity = lightIntensity * Mathf.Min(1.5f, 0.4f + heat * 0.25f) * flicker * (0.35f + 0.65f * darkness);
                light.enabled = true;
            }
        }

        void EnsureParticles()
        {
            if (flames != null) return;
            noise = MakeNoise(128);
            flameMaterial = MakeMaterial("Hidden/Boreal/WildfireFlame");
            emberMaterial = MakeMaterial("Hidden/Boreal/WildfireEmber");
            smokeMaterial = MakeMaterial("Hidden/Boreal/WildfireSmoke");
            hazeMaterial = MakeMaterial("Hidden/Boreal/WildfireSmoke");
            if (hazeMaterial != null)
            {
                hazeMaterial.SetFloat(OpacityId, 0.12f);
                hazeMaterial.SetFloat(GlowId, 0f);
                hazeMaterial.renderQueue = 2990;
            }

            flames = MakeSystem("Flames", flameMaterial, maximumFlames, ParticleSystemRenderMode.VerticalBillboard);
            ParticleSystem.SizeOverLifetimeModule flameSize = flames.sizeOverLifetime;
            flameSize.enabled = true;
            flameSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.6f)));

            embers = MakeSystem("Embers", emberMaterial, maximumEmbers, ParticleSystemRenderMode.Stretch);
            ParticleSystemRenderer emberRenderer = embers.GetComponent<ParticleSystemRenderer>();
            emberRenderer.velocityScale = 0.06f;
            emberRenderer.lengthScale = 2f;
            ParticleSystem.NoiseModule turbulence = embers.noise;
            turbulence.enabled = true;
            turbulence.strength = 2.2f;
            turbulence.frequency = 0.35f;
            turbulence.scrollSpeed = 0.6f;
            turbulence.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.MainModule emberMain = embers.main;
            emberMain.gravityModifier = -0.05f;

            smoke = MakeSystem("Smoke", smokeMaterial, maximumSmoke, ParticleSystemRenderMode.Billboard);
            ParticleSystem.SizeOverLifetimeModule smokeSize = smoke.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.3f, 1.4f), new Keyframe(1f, 4.5f)));
            ParticleSystem.RotationOverLifetimeModule smokeSpin = smoke.rotationOverLifetime;
            smokeSpin.enabled = true;
            smokeSpin.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            ParticleSystem.LimitVelocityOverLifetimeModule smokeDrag = smoke.limitVelocityOverLifetime;
            smokeDrag.enabled = true;
            smokeDrag.drag = 0.04f;
            smokeDrag.limit = 20f;
            ParticleSystem.MainModule smokeMain = smoke.main;
            smokeMain.gravityModifier = -0.012f;

            haze = MakeSystem("Haze", hazeMaterial, maximumHaze, ParticleSystemRenderMode.Billboard);
            ParticleSystem.SizeOverLifetimeModule hazeSize = haze.sizeOverLifetime;
            hazeSize.enabled = true;
            hazeSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
        }

        Material MakeMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Wildfire visuals: shader {shaderName} not found", this);
                return null;
            }
            Material material = new Material(shader) { name = shaderName.Substring(shaderName.LastIndexOf('/') + 1) };
            material.SetTexture(NoiseId, noise);
            return material;
        }

        ParticleSystem MakeSystem(string name, Material material, int maximum, ParticleSystemRenderMode mode)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            ParticleSystem system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(1, maximum);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 10f;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
            });
            system.Play();
            return system;
        }

        // Tileable value-noise fbm: red and green two independent fields, blue a softer one, for flames and smoke.
        static Texture2D MakeNoise(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Wildfire Noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Fbm(x, y, size, 4, 11);
                float g = Fbm(x, y, size, 4, 57);
                float b = Fbm(x, y, size, 2, 93);
                pixels[y * size + x] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        static float Fbm(int x, int y, int size, int octaves, int seed)
        {
            float total = 0f, amplitude = 0.5f, weight = 0f;
            int period = 4;
            for (int octave = 0; octave < octaves; octave++)
            {
                total += amplitude * TileNoise(x * period / (float)size, y * period / (float)size, period, seed + octave * 131);
                weight += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }
            return total / weight;
        }

        static float TileNoise(float u, float v, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            float fx = u - x0, fy = v - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0 % period, y0 % period, seed), b = Hash((x0 + 1) % period, y0 % period, seed);
            float c = Hash(x0 % period, (y0 + 1) % period, seed), d = Hash((x0 + 1) % period, (y0 + 1) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967295f;
        }
    }
}
