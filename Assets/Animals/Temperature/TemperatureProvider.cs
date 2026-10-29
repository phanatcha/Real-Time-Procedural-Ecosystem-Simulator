
using UnityEngine;

namespace Boreal.Temperature
{
    public abstract class TemperatureProvider : MonoBehaviour
    {
        public abstract bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius);

        // The temperature without the season's shift: the place's climate, for things fixed once from it, such as how
        // fast a plant site regrows. Providers that don't follow the seasons return TryGetTemperature.
        public virtual bool TryGetClimateTemperature(Vector3 worldPosition, out float temperatureCelsius)
        {
            return TryGetTemperature(worldPosition, out temperatureCelsius);
        }

        protected static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        protected static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
