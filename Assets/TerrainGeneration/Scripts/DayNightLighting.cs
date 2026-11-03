using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Boreal.TerrainGeneration
{
    // The island lit by the time of day (item 72): the sun crosses the sky, warming to orange near the horizon, and at
    // night the same light becomes a dim, cool moon (custom shaders only read the main light, so one light plays both),
    // without shadows. Ambient light, sky reflections and the atmosphere's haze follow the sky's brightness, stars come
    // out on a dome that turns round the pole, and a night colour grade keeps the dark readable. The procedural sky
    // follows the sun by itself.
    //
    // The scene's lighting (Tools > Boreal Ecosystem > Apply Realistic Lighting: an afternoon sun 32° up) is the day
    // view: it is put back whenever day and night are off, while N holds the day view, during map captures, and when this
    // component goes. Nothing here touches assets. The day clock publishes the moment (Publish) and adds this component.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-780)]
    public sealed class DayNightLighting : MonoBehaviour
    {
        [Tooltip("How light the night looks: 0 nearly black, as it really is, 1 a bright moonlit blue. What the animals " +
                 "see by is set by the day clock, not by this.")]
        [Range(0f, 1f)] public float nightBrightness = 0.6f;
        [Tooltip("The full moon's light, as the sun's light is about 2.3.")]
        [Min(0f)] public float moonIntensity = 0.45f;
        [Tooltip("The colour the night sky lends the shadows and the haze.")]
        public Color nightSky = new Color(0.13f, 0.17f, 0.3f);
        [Range(0, 4000)] public int starCount = 1600;

        // The moment to show, as the day clock published it.
        public static DayState Current { get; private set; } = DayCycle.Neutral;
        public static bool IsRunning { get; private set; }
        static float latitude = 62f;

        // Shows the day view while true, whatever the time: the N key, map captures and screenshots. Takes effect at
        // once, so a capture can set it and render in the same frame.
        public static bool HoldDayView
        {
            get => holdDayView;
            set
            {
                holdDayView = value;
                if (active != null) active.Refresh();
            }
        }

        static bool holdDayView;
        static DayNightLighting active;

        public static void Publish(DayState moment, bool running, float latitudeDegrees)
        {
            Current = moment;
            IsRunning = running;
            latitude = latitudeDegrees;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = DayCycle.Neutral;
            IsRunning = false;
            holdDayView = false;
            active = null;
        }

        static readonly int StarAlphaId = Shader.PropertyToID("_BorealStarAlpha");

        Light sun;
        // The day view as the scene set it up.
        Quaternion dayRotation;
        float dayIntensity;
        float dayTemperature;
        bool dayUsesTemperature;
        Color dayColour;
        LightShadows dayShadows;
        AmbientMode dayAmbientMode;
        SphericalHarmonicsL2 dayProbe;
        float dayReflection;
        bool captured;
        bool showingNight;

        GameObject starDome;
        Material starMaterial;
        Mesh starMesh;
        Volume nightVolume;
        VolumeProfile nightProfile;
        ColorAdjustments nightColour;

        void Start()
        {
            sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = null;
                foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type != LightType.Directional) continue;
                    sun = light;
                    break;
                }
            }

            BuildStars();
            BuildNightGrade();
        }

        void OnEnable() => active = this;

        void OnDisable()
        {
            if (active == this) active = null;
            ShowDay();
            Shader.SetGlobalFloat(StarAlphaId, 0f);
        }

        void OnDestroy()
        {
            if (starDome != null) Destroy(starDome);
            if (starMaterial != null) Destroy(starMaterial);
            if (starMesh != null) Destroy(starMesh);
            if (nightVolume != null) Destroy(nightVolume.gameObject);
            if (nightProfile != null) Destroy(nightProfile);
        }

        void Update() => Refresh();

        void Refresh()
        {
            if (sun == null) return;
            if (!captured) Capture();

            if (!IsRunning || HoldDayView)
            {
                ShowDay();
                return;
            }

            ShowMoment(Current);
        }

        // The scene's own lighting, read once. The ambient light is what the scene's sky gave at the start.
        void Capture()
        {
            dayRotation = sun.transform.rotation;
            dayIntensity = sun.intensity;
            dayTemperature = sun.colorTemperature;
            dayUsesTemperature = sun.useColorTemperature;
            dayColour = sun.color;
            dayShadows = sun.shadows;
            dayAmbientMode = RenderSettings.ambientMode;
            dayProbe = RenderSettings.ambientProbe;
            dayReflection = RenderSettings.reflectionIntensity;
            captured = true;
        }

        void ShowDay()
        {
            if (!showingNight || sun == null) return;

            sun.transform.rotation = dayRotation;
            sun.intensity = dayIntensity;
            sun.colorTemperature = dayTemperature;
            sun.useColorTemperature = dayUsesTemperature;
            sun.color = dayColour;
            sun.shadows = dayShadows;
            RenderSettings.ambientMode = dayAmbientMode;
            RenderSettings.ambientProbe = dayProbe;
            RenderSettings.reflectionIntensity = dayReflection;
            AtmosphericFog.SkyLight = Color.white;
            Shader.SetGlobalFloat(StarAlphaId, 0f);
            if (nightVolume != null) nightVolume.weight = 0f;
            showingNight = false;
        }

        void ShowMoment(DayState moment)
        {
            showingNight = true;
            float elevation = moment.sunElevation;

            // One light: the sun while it is near or above the horizon, the moon once the sun is well down. At the
            // hand-over both are too dim to see the jump.
            sun.useColorTemperature = true;
            sun.color = Color.white;
            if (elevation > -4f || moment.moonlight <= 0.001f)
            {
                float sunlight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 12f, elevation));
                sun.transform.rotation = Quaternion.LookRotation(-moment.sunDirection);
                sun.intensity = dayIntensity * sunlight;
                // About 2,200 K at sunrise, 5,300 K at the scene's afternoon height of 32°, a little bluer at noon.
                sun.colorTemperature = Mathf.Lerp(2200f, 5600f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 40f, elevation)));
                sun.shadows = sunlight > 0.01f ? dayShadows : LightShadows.None;
            }
            else
            {
                sun.transform.rotation = Quaternion.LookRotation(-moment.moonDirection);
                sun.intensity = moonIntensity * moment.moonlight * Mathf.Lerp(0.4f, 1f, nightBrightness);
                sun.colorTemperature = 8500f;
                sun.shadows = LightShadows.None;
            }

            // How bright the sky is: full by day, warm near the horizon, gone an hour or so after sunset.
            float sky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, 12f, elevation));
            Color warmth = Color.Lerp(new Color(1f, 0.72f, 0.55f), Color.white,
                                      Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 20f, elevation)));
            Color night = nightSky * (nightBrightness * (0.65f + 0.35f * moment.moonlight) * (1f - sky));

            SphericalHarmonicsL2 ambient = Scaled(dayProbe, warmth * sky);
            ambient.AddAmbientLight(night);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = ambient;
            RenderSettings.reflectionIntensity = dayReflection * Mathf.Lerp(0.1f, 1f, sky);

            Color hazeAtNight = nightSky * (1.2f * (0.5f + 0.5f * nightBrightness) * (0.6f + 0.4f * moment.moonlight));
            AtmosphericFog.SkyLight = Color.Lerp(hazeAtNight, warmth, sky);

            float darkness = 1f - sky;
            Shader.SetGlobalFloat(StarAlphaId, darkness * darkness * (1f - 0.5f * moment.moonlight));
            if (nightVolume != null)
            {
                nightVolume.weight = darkness;
                nightColour.postExposure.value = 0.25f + 0.6f * nightBrightness;
            }
            PlaceStars(moment);
        }

        static SphericalHarmonicsL2 Scaled(SphericalHarmonicsL2 probe, Color scale)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
            {
                probe[0, coefficient] *= scale.r;
                probe[1, coefficient] *= scale.g;
                probe[2, coefficient] *= scale.b;
            }
            return probe;
        }

        // The dome sits on the camera, inside its far plane, and turns once a day round the celestial pole, which stands
        // due north at the latitude's height.
        void PlaceStars(DayState moment)
        {
            Camera view = Camera.main;
            if (starDome == null || view == null) return;

            starDome.transform.position = view.transform.position;
            starDome.transform.localScale = Vector3.one * (view.farClipPlane * 0.85f);
            Vector3 pole = new Vector3(0f, Mathf.Sin(latitude * Mathf.Deg2Rad), Mathf.Cos(latitude * Mathf.Deg2Rad));
            starDome.transform.rotation = Quaternion.AngleAxis(-360f * moment.timeOfDay, pole);
        }

        void BuildStars()
        {
            Shader shader = Shader.Find("Hidden/Boreal/Stars");
            if (shader == null || starCount <= 0) return;

            System.Random random = new System.Random(7919);
            Vector3[] vertices = new Vector3[starCount * 4];
            Color[] colours = new Color[starCount * 4];
            int[] triangles = new int[starCount * 6];
            for (int star = 0; star < starCount; star++)
            {
                // A random direction on the unit sphere, and a small square facing the middle.
                float z = 2f * (float)random.NextDouble() - 1f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float ring = Mathf.Sqrt(1f - z * z);
                Vector3 direction = new Vector3(ring * Mathf.Cos(angle), z, ring * Mathf.Sin(angle));
                Vector3 across = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 along = Vector3.Cross(direction, across);

                // Most stars are faint; a few are bright, white to faintly blue or yellow.
                float brightness = Mathf.Pow((float)random.NextDouble(), 3f);
                float size = 0.0016f + 0.0026f * brightness;
                float tint = (float)random.NextDouble();
                Color colour = Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.92f, 0.8f), tint) *
                               (0.35f + 1.4f * brightness);
                colour.a = (float)random.NextDouble();

                int first = star * 4;
                vertices[first] = direction + (-across - along) * size;
                vertices[first + 1] = direction + (across - along) * size;
                vertices[first + 2] = direction + (across + along) * size;
                vertices[first + 3] = direction + (-across + along) * size;
                for (int corner = 0; corner < 4; corner++) colours[first + corner] = colour;
                int index = star * 6;
                triangles[index] = first;
                triangles[index + 1] = first + 1;
                triangles[index + 2] = first + 2;
                triangles[index + 3] = first;
                triangles[index + 4] = first + 2;
                triangles[index + 5] = first + 3;
            }

            starMesh = new Mesh { name = "Stars", indexFormat = IndexFormat.UInt32 };
            starMesh.vertices = vertices;
            starMesh.colors = colours;
            starMesh.triangles = triangles;
            starMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);

            starMaterial = new Material(shader) { name = "Stars (runtime)" };
            starDome = new GameObject("Stars", typeof(MeshFilter), typeof(MeshRenderer));
            starDome.GetComponent<MeshFilter>().sharedMesh = starMesh;
            MeshRenderer renderer = starDome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = starMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // A cool, slightly brighter and less colourful grade that fades in as the sky darkens, on top of the scene's own.
        void BuildNightGrade()
        {
            nightProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            nightProfile.name = "Night Grade (runtime)";
            nightColour = nightProfile.Add<ColorAdjustments>(true);
            nightColour.postExposure.Override(0.25f + 0.6f * nightBrightness);
            nightColour.saturation.Override(-30f);
            nightColour.colorFilter.Override(new Color(0.82f, 0.9f, 1f));

            GameObject holder = new GameObject("Night Grade");
            holder.transform.SetParent(transform, false);
            nightVolume = holder.AddComponent<Volume>();
            nightVolume.isGlobal = true;
            nightVolume.priority = 10f;
            nightVolume.sharedProfile = nightProfile;
            nightVolume.weight = 0f;
        }
    }
}
