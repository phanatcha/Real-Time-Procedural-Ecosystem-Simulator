using UnityEngine;

[ExecuteAlways]
public class TemperatureZone : MonoBehaviour
{
    public float temperatureCelsius = 20f;
    [Tooltip("Local footprint dimensions. Y controls gizmo height only; sampling uses X and Z.")]
    public Vector3 size = new Vector3(100f, 10f, 100f);
    [Min(0f)] [Tooltip("Fade width inside each edge, measured in local units. Clamped to half the smallest footprint dimension.")]
    public float blendDistance = 10f;
    [Tooltip("Higher priorities blend over lower ones. At equal priority, later entries in the provider's zone array blend last.")]
    public int priority;

    internal static uint ConfigurationRevision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRevision() { ConfigurationRevision = 0; }

    private void OnEnable() { ConfigurationRevision++; }
    private void OnDisable() { ConfigurationRevision++; }
    private void OnValidate() { ConfigurationRevision++; }

    public bool TryGetInfluence(Vector3 worldPosition, out float influence)
    {
        influence = 0f;
        if (!isActiveAndEnabled || !IsFinite(temperatureCelsius) || !IsFinite(size.x) || !IsFinite(size.z) ||
            !IsFinite(blendDistance) || size.x <= 0f || size.z <= 0f)
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        if (!IsFinite(localPosition.x) || !IsFinite(localPosition.y) || !IsFinite(localPosition.z))
            return false;

        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float distanceInside = Mathf.Min(halfX - Mathf.Abs(localPosition.x), halfZ - Mathf.Abs(localPosition.z));
        if (distanceInside < 0f)
            return false;

        float fadeWidth = Mathf.Clamp(blendDistance, 0f, Mathf.Min(halfX, halfZ));
        influence = fadeWidth > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceInside / fadeWidth)) : 1f;
        return influence > 0f;
    }

    internal static Color GetTemperatureColor(float temperature)
    {
        if (temperature <= 20f)
            return Color.Lerp(new Color(0.2f, 0.4f, 1f), new Color(0.2f, 0.9f, 0.5f), Mathf.InverseLerp(-10f, 20f, temperature));
        return Color.Lerp(new Color(0.2f, 0.9f, 0.5f), new Color(1f, 0.25f, 0.1f), Mathf.InverseLerp(20f, 40f, temperature));
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void OnDrawGizmos()
    {
        if (!isActiveAndEnabled || !IsFinite(size.x) || !IsFinite(size.y) || !IsFinite(size.z))
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 previewSize = new Vector3(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0f, size.z));
        Color zoneColor = GetTemperatureColor(temperatureCelsius);
        Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.08f);
        Gizmos.DrawCube(Vector3.zero, previewSize);
        Gizmos.color = zoneColor;
        Gizmos.DrawWireCube(Vector3.zero, previewSize);
        float fadeWidth = Mathf.Clamp(blendDistance, 0f, Mathf.Min(previewSize.x, previewSize.z) * 0.5f);
        if (fadeWidth > 0f)
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(previewSize.x - fadeWidth * 2f, previewSize.y, previewSize.z - fadeWidth * 2f));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position, $"{name}: {temperatureCelsius:0.#} °C (priority {priority})");
#endif
    }
}
