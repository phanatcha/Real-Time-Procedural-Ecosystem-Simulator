using UnityEngine;

public abstract class TemperatureProvider : MonoBehaviour
{
    public abstract bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius);

    protected static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    protected static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
