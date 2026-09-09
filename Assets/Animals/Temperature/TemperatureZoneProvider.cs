using System;
using UnityEngine;

[ExecuteAlways]
public class TemperatureZoneProvider : TemperatureProvider
{
    public float defaultTemperatureCelsius = 20f;
    public TemperatureZone[] zones = Array.Empty<TemperatureZone>();

    private struct OrderedZone
    {
        public TemperatureZone zone;
        public int arrayIndex;
    }

    private OrderedZone[] orderedZones = Array.Empty<OrderedZone>();
    private TemperatureZone[] cachedZoneArray;
    private uint cachedConfigurationRevision;

    private void OnEnable() { RefreshZoneOrder(); }
    private void OnValidate() { RefreshZoneOrder(); }

    [ContextMenu("Refresh Zone Order")]
    public void RefreshZoneOrder()
    {
        cachedZoneArray = zones;
        cachedConfigurationRevision = TemperatureZone.ConfigurationRevision;
        int count = zones == null ? 0 : zones.Length;
        orderedZones = new OrderedZone[count];
        for (int i = 0; i < count; i++)
            orderedZones[i] = new OrderedZone { zone = zones[i], arrayIndex = i };

        Array.Sort(orderedZones, CompareZones);
    }

    private static int CompareZones(OrderedZone a, OrderedZone b)
    {
        int aPriority = a.zone != null ? a.zone.priority : int.MinValue;
        int bPriority = b.zone != null ? b.zone.priority : int.MinValue;
        int priorityOrder = aPriority.CompareTo(bPriority);
        return priorityOrder != 0 ? priorityOrder : a.arrayIndex.CompareTo(b.arrayIndex);
    }

    public override bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius)
    {
        temperatureCelsius = defaultTemperatureCelsius;
        if (!isActiveAndEnabled || !IsFinite(worldPosition) || !IsFinite(temperatureCelsius))
            return false;

        if (cachedZoneArray != zones || cachedConfigurationRevision != TemperatureZone.ConfigurationRevision)
            RefreshZoneOrder();

        foreach (OrderedZone entry in orderedZones)
        {
            if (entry.zone != null && entry.zone.TryGetInfluence(worldPosition, out float influence))
                temperatureCelsius = Mathf.Lerp(temperatureCelsius, entry.zone.temperatureCelsius, influence);
        }
        return IsFinite(temperatureCelsius);
    }
}
