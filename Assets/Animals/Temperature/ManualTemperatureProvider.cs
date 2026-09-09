using UnityEngine;

public class ManualTemperatureProvider : TemperatureProvider
{
    public float temperatureCelsius = 20f;

    public override bool TryGetTemperature(Vector3 worldPosition, out float sampledTemperatureCelsius)
    {
        sampledTemperatureCelsius = temperatureCelsius;
        return isActiveAndEnabled && IsFinite(worldPosition) && IsFinite(sampledTemperatureCelsius);
    }
}
