using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class TemperatureSystem : MonoBehaviour
{
    private const float DefaultTemperatureCelsius = 20f;
    private static readonly List<TemperatureSystem> activeSystems = new List<TemperatureSystem>();
    private static TemperatureSystem activeSystem;

    [Tooltip("Temperature source. A future terrain adapter only needs to implement TemperatureProvider.")]
    public TemperatureProvider provider;

    [Header("Controlled Testing")]
    public bool useGlobalOverride;
    public float overrideTemperatureCelsius = DefaultTemperatureCelsius;

    [Tooltip("Returned when the source is absent, disabled, invalid, or waiting for terrain data. TryGetTemperatureAt still returns false.")]
    public float fallbackTemperatureCelsius = DefaultTemperatureCelsius;

    public static TemperatureSystem Active => activeSystem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreRegistry()
    {
        activeSystems.Clear();
        activeSystem = null;

        TemperatureSystem[] systems = FindObjectsByType<TemperatureSystem>();
        foreach (TemperatureSystem system in systems)
        {
            if (system.isActiveAndEnabled && Application.IsPlaying(system.gameObject))
                system.Register();
        }
    }

    private void OnEnable()
    {
        if (CanSupplySimulation())
            Register();
    }

    private void OnDisable()
    {
        activeSystems.Remove(this);
        SelectActiveSystem();
    }

    private void Register()
    {
        if (!activeSystems.Contains(this))
            activeSystems.Add(this);
        SelectActiveSystem();
    }

    private static void SelectActiveSystem()
    {
        activeSystem = null;
        for (int i = activeSystems.Count - 1; i >= 0; i--)
        {
            TemperatureSystem candidate = activeSystems[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.CanSupplySimulation())
            {
                activeSystems.RemoveAt(i);
                continue;
            }

        }
        if (activeSystems.Count > 0) activeSystem = activeSystems[0];
    }

    public static bool TryGetTemperatureAt(Vector3 worldPosition, out float temperatureCelsius)
    {
        if (activeSystem != null && activeSystem.isActiveAndEnabled)
            return activeSystem.TrySampleTemperature(worldPosition, out temperatureCelsius);

        temperatureCelsius = DefaultTemperatureCelsius;
        return false;
    }

    private bool CanSupplySimulation()
    {
        if (!gameObject.scene.IsValid() || (Application.isPlaying && !Application.IsPlaying(gameObject)))
            return false;
#if UNITY_EDITOR
        if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene))
            return false;
#endif
        return true;
    }

    public static float GetTemperatureAt(Vector3 worldPosition)
    {
        TryGetTemperatureAt(worldPosition, out float temperatureCelsius);
        return temperatureCelsius;
    }

    public bool TrySampleTemperature(Vector3 worldPosition, out float temperatureCelsius)
    {
        temperatureCelsius = IsFinite(fallbackTemperatureCelsius)
            ? fallbackTemperatureCelsius : DefaultTemperatureCelsius;

        if (!isActiveAndEnabled || !IsFinite(worldPosition.x) || !IsFinite(worldPosition.y) || !IsFinite(worldPosition.z))
            return false;

        if (useGlobalOverride)
        {
            if (!IsFinite(overrideTemperatureCelsius))
                return false;
            temperatureCelsius = overrideTemperatureCelsius;
            return true;
        }

        if (provider == null || !provider.isActiveAndEnabled ||
            !provider.TryGetTemperature(worldPosition, out float sampledTemperature) || !IsFinite(sampledTemperature))
            return false;

        temperatureCelsius = sampledTemperature;
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
