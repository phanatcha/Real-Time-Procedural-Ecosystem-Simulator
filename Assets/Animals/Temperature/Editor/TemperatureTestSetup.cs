using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TemperatureTestSetup
{
    private const string CreateMenu = "Tools/Ecosystem/Create Temperature Test Setup";
    private const string ChecksMenu = "Tools/Ecosystem/Run Temperature Source Checks";
    private const string CreationLabel = "Create Temperature Test Setup";

    [MenuItem(CreateMenu)]
    public static void Create()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        TemperatureSystem[] existingSystems = Object.FindObjectsByType<TemperatureSystem>(
            FindObjectsInactive.Include);

        foreach (TemperatureSystem existing in existingSystems)
        {
            if (existing.gameObject.scene != activeScene)
                continue;

            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("This scene already has a Temperature System. Selected it without adding another setup.", existing);
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(CreationLabel);

        GameObject root = new GameObject("Temperature Test Setup");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, activeScene);
        Undo.RegisterCreatedObjectUndo(root, CreationLabel);

        TemperatureSystem system = Undo.AddComponent<TemperatureSystem>(root);
        ManualTemperatureProvider manual = Undo.AddComponent<ManualTemperatureProvider>(root);
        GradientTemperatureProvider gradient = Undo.AddComponent<GradientTemperatureProvider>(root);
        TemperatureZoneProvider zoneProvider = Undo.AddComponent<TemperatureZoneProvider>(root);
        Undo.RecordObjects(new Object[] { system, manual, gradient, zoneProvider }, CreationLabel);

        Bounds map = GetTestBounds();
        Undo.RecordObject(root.transform, CreationLabel);
        root.transform.position = map.center;
        manual.temperatureCelsius = 20f;
        gradient.startPosition = new Vector3(map.min.x, map.center.y, map.center.z);
        gradient.endPosition = new Vector3(map.max.x, map.center.y, map.center.z);
        gradient.startTemperatureCelsius = -10f;
        gradient.endTemperatureCelsius = 40f;
        zoneProvider.defaultTemperatureCelsius = 20f;
        zoneProvider.zones = new TemperatureZone[3];

        float[] temperatures = { -10f, 20f, 40f };
        string[] labels = { "Cold Zone (-10 C)", "Temperate Zone (20 C)", "Hot Zone (40 C)" };
        float zoneWidth = map.size.x / 3f;
        float blendDistance = Mathf.Min(zoneWidth, map.size.z, map.size.y) * 0.2f;

        for (int index = 0; index < temperatures.Length; index++)
        {
            GameObject zoneObject = new GameObject(labels[index]);
            Undo.RegisterCreatedObjectUndo(zoneObject, CreationLabel);
            Undo.SetTransformParent(zoneObject.transform, root.transform, CreationLabel);
            Undo.RecordObject(zoneObject.transform, CreationLabel);
            zoneObject.transform.position = new Vector3(
                map.min.x + zoneWidth * (index + 0.5f), map.center.y, map.center.z);

            TemperatureZone zone = Undo.AddComponent<TemperatureZone>(zoneObject);
            Undo.RecordObject(zone, CreationLabel);
            zone.temperatureCelsius = temperatures[index];
            zone.size = new Vector3(zoneWidth, map.size.y, map.size.z);
            zone.blendDistance = blendDistance;
            zone.priority = 0;
            Undo.RecordObject(zoneProvider, CreationLabel);
            zoneProvider.zones[index] = zone;
        }

        Undo.RecordObject(system, CreationLabel);
        system.provider = manual;
        system.fallbackTemperatureCelsius = 20f;
        system.useGlobalOverride = false;
        system.overrideTemperatureCelsius = 20f;
        zoneProvider.RefreshZoneOrder();
        Undo.RecordObject(root, CreationLabel);
        root.SetActive(true);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = root;

        Debug.Log(
            "Temperature test setup created at a uniform 20 C. In Temperature System, drag the Gradient or Zone " +
            "Provider component header into Provider to test a climate gradient or three regions. " +
            "Use Global Override for controlled whole-map tests. Enable Scene gizmos to see source regions. " +
            "Run Tools > Ecosystem > Run Temperature Source Checks to verify source behavior. " +
            "Compare animals over the same simulated duration at 1x and 50x. Save the scene to keep this setup.", root);
    }

    [MenuItem(CreateMenu, true)]
    [MenuItem(ChecksMenu, true)]
    private static bool CanCreate()
    {
        Scene scene = SceneManager.GetActiveScene();
        return !EditorApplication.isPlayingOrWillChangePlaymode && scene.IsValid() && scene.isLoaded;
    }

    private static Bounds GetTestBounds()
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 center = terrain.transform.position + terrainSize * 0.5f;
            Vector3 size = new Vector3(
                Mathf.Max(3f, terrainSize.x), Mathf.Max(200f, terrainSize.y + 200f), Mathf.Max(3f, terrainSize.z));
            return new Bounds(center, size);
        }

        return new Bounds(Vector3.zero, new Vector3(900f, 1000f, 900f));
    }

    [MenuItem(ChecksMenu)]
    public static void RunSourceChecks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Run temperature source checks outside Play mode.");

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Temporary Temperature Source Checks");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            root.hideFlags = HideFlags.HideAndDontSave;

            TemperatureSystem system = root.AddComponent<TemperatureSystem>();
            ManualTemperatureProvider manual = root.AddComponent<ManualTemperatureProvider>();
            GradientTemperatureProvider gradient = root.AddComponent<GradientTemperatureProvider>();
            TemperatureZoneProvider zones = root.AddComponent<TemperatureZoneProvider>();
            TemperatureZone cold = AddCheckZone(root, "Cold", -10f);
            TemperatureZone hot = AddCheckZone(root, "Hot", 40f);
            manual.temperatureCelsius = 20f;
            gradient.startPosition = Vector3.zero;
            gradient.endPosition = Vector3.right * 100f;
            gradient.startTemperatureCelsius = -10f;
            gradient.endTemperatureCelsius = 40f;
            zones.defaultTemperatureCelsius = 20f;
            zones.zones = new[] { cold };
            root.SetActive(true);

            int checks = 0;
            ExpectProvider(manual, new Vector3(100f, 50f, -300f), 20f, "Manual source is uniform", ref checks);
            ExpectProvider(gradient, Vector3.zero, -10f, "Gradient start", ref checks);
            ExpectProvider(gradient, Vector3.right * 50f, 15f, "Gradient midpoint", ref checks);
            ExpectProvider(gradient, Vector3.right * 100f, 40f, "Gradient end", ref checks);
            ExpectProvider(gradient, Vector3.left * 100f, -10f, "Gradient clamps before start", ref checks);
            ExpectProvider(gradient, Vector3.right * 200f, 40f, "Gradient clamps after end", ref checks);
            gradient.endPosition = gradient.startPosition;
            ExpectProvider(gradient, Vector3.right, -10f, "Coincident endpoints remain finite", ref checks);

            ExpectProvider(zones, Vector3.zero, -10f, "Zone interior", ref checks);
            ExpectProvider(zones, Vector3.right * 10f, 20f, "Outside zones uses background", ref checks);
            ExpectProvider(zones, Vector3.right * 5f, 20f, "Zone boundary blends to background", ref checks);
            ExpectProvider(zones, Vector3.right * 4f, 5f, "Zone blend midpoint", ref checks);
            ExpectProvider(zones, Vector3.up * 500f, -10f, "Biome test regions span terrain elevation", ref checks);

            hot.priority = 5;
            zones.zones = new[] { hot, cold };
            zones.RefreshZoneOrder();
            ExpectProvider(zones, Vector3.zero, 40f, "Higher priority wins regardless of array order", ref checks);
            hot.priority = 0;
            zones.RefreshZoneOrder();
            ExpectProvider(zones, Vector3.zero, -10f, "Equal priorities use later array entry", ref checks);
            cold.enabled = false;
            ExpectProvider(zones, Vector3.zero, 40f, "Disabled zone is ignored", ref checks);

            system.fallbackTemperatureCelsius = 17f;
            system.provider = null;
            ExpectService(system, false, 17f, "Missing source reports unavailable with fallback", ref checks);
            system.useGlobalOverride = true;
            system.overrideTemperatureCelsius = 35f;
            ExpectService(system, true, 35f, "Override works without a provider", ref checks);
            system.provider = manual;
            ExpectService(system, true, 35f, "Override takes precedence over provider", ref checks);
            system.useGlobalOverride = false;
            ExpectService(system, true, 20f, "Provider resumes after disabling override", ref checks);
            manual.enabled = false;
            ExpectService(system, false, 17f, "Disabled provider reports unavailable", ref checks);
            manual.enabled = true;
            manual.temperatureCelsius = float.NaN;
            ExpectService(system, false, 17f, "Invalid source value uses finite fallback", ref checks);
            system.fallbackTemperatureCelsius = float.NaN;
            ExpectService(system, false, 20f, "Invalid fallback uses safe default", ref checks);

            Debug.Log($"Temperature source checks passed ({checks} checks). Temporary test objects were isolated from the saved scene.");
        }
        finally
        {
            if (root != null)
                Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static TemperatureZone AddCheckZone(GameObject root, string name, float temperature)
    {
        GameObject zoneObject = new GameObject(name);
        SceneManager.MoveGameObjectToScene(zoneObject, root.scene);
        zoneObject.transform.SetParent(root.transform, false);
        TemperatureZone zone = zoneObject.AddComponent<TemperatureZone>();
        zone.size = Vector3.one * 10f;
        zone.blendDistance = 2f;
        zone.temperatureCelsius = temperature;
        return zone;
    }

    private static void ExpectProvider(TemperatureProvider provider, Vector3 position, float expected, string label, ref int checks)
    {
        bool valid = provider.TryGetTemperature(position, out float actual);
        if (!valid || float.IsNaN(actual) || Mathf.Abs(actual - expected) > 0.001f)
            throw new System.InvalidOperationException($"Temperature source check failed: {label}. Expected {expected} C, got {actual} C (valid: {valid}).");
        checks++;
    }

    private static void ExpectService(TemperatureSystem system, bool expectedValid, float expected, string label, ref int checks)
    {
        bool valid = system.TrySampleTemperature(Vector3.zero, out float actual);
        if (valid != expectedValid || float.IsNaN(actual) || Mathf.Abs(actual - expected) > 0.001f)
            throw new System.InvalidOperationException($"Temperature source check failed: {label}. Expected {expected} C and valid={expectedValid}, got {actual} C and valid={valid}.");
        checks++;
    }
}
