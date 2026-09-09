using UnityEngine;

public class GradientTemperatureProvider : TemperatureProvider
{
    [Tooltip("Gradient endpoints in world coordinates, independent of this object's transform.")]
    public Vector3 startPosition = new Vector3(-100f, 0f, 0f);
    public Vector3 endPosition = new Vector3(100f, 0f, 0f);
    public float startTemperatureCelsius = 20f;
    public float endTemperatureCelsius = 20f;

    public override bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius)
    {
        temperatureCelsius = 20f;
        if (!isActiveAndEnabled || !IsFinite(worldPosition) || !IsFinite(startPosition) ||
            !IsFinite(endPosition) || !IsFinite(startTemperatureCelsius) || !IsFinite(endTemperatureCelsius))
            return false;

        Vector3 direction = endPosition - startPosition;
        float lengthSquared = direction.sqrMagnitude;
        if (!IsFinite(lengthSquared))
            return false;

        float progress = lengthSquared > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(worldPosition - startPosition, direction) / lengthSquared)
            : 0f;
        temperatureCelsius = Mathf.Lerp(startTemperatureCelsius, endTemperatureCelsius, progress);
        return IsFinite(temperatureCelsius);
    }

    private void OnDrawGizmos()
    {
        if (!isActiveAndEnabled || !IsFinite(startPosition) || !IsFinite(endPosition))
            return;

        Color previousColor = Gizmos.color;
        const int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float fraction = (i + 0.5f) / segments;
            Gizmos.color = TemperatureZone.GetTemperatureColor(Mathf.Lerp(startTemperatureCelsius, endTemperatureCelsius, fraction));
            Gizmos.DrawLine(Vector3.Lerp(startPosition, endPosition, (float)i / segments),
                            Vector3.Lerp(startPosition, endPosition, (float)(i + 1) / segments));
        }
        Gizmos.color = previousColor;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(startPosition, $"{startTemperatureCelsius:0.#} °C");
        UnityEditor.Handles.Label(endPosition, $"{endTemperatureCelsius:0.#} °C");
#endif
    }
}
