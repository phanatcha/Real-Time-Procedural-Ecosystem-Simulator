using UnityEngine;

[CreateAssetMenu(menuName = "Ecosystem/Simulation Settings")]
public class EcosystemSimulationSettings : ScriptableObject
{
    [Header("Finite grid")]
    [Min(25f)]
    public float cellSize = 250f;
    [Range(1, 5)]
    public int environmentSamplesPerAxis = 3;
    [Range(0f, 1f)]
    public float minimumHabitableLandFraction = 0.34f;

    [Header("Simulation rates")]
    [Min(0.05f)]
    public float nearbyUpdateInterval = 0.5f;
    [Min(0.1f)]
    public float distantUpdateInterval = 6f;
    [Min(0f)]
    public float nearbySimulationRadius = 750f;
    [Range(0f, 1f)]
    public float defaultMigrationRatePerSecond = 0.004f;

    [Header("Animal materialization")]
    [Min(0f)]
    public float materializationRadius = 400f;
    [Min(0f)]
    public float abstractionRadius = 550f;
    [Min(0.05f)]
    public float materializationRefreshInterval = 0.5f;
    [Range(1, 64)]
    public int spawnAttemptsPerAnimal = 12;
    public int materializationSeed = 1947;

    void OnValidate()
    {
        cellSize = Mathf.Max(25f, cellSize);
        environmentSamplesPerAxis = Mathf.Clamp(environmentSamplesPerAxis, 1, 5);
        nearbyUpdateInterval = Mathf.Max(0.05f, nearbyUpdateInterval);
        distantUpdateInterval = Mathf.Max(nearbyUpdateInterval, distantUpdateInterval);
        nearbySimulationRadius = Mathf.Max(0f, nearbySimulationRadius);
        materializationRadius = Mathf.Max(0f, materializationRadius);
        abstractionRadius = Mathf.Max(materializationRadius + cellSize * 0.25f, abstractionRadius);
        materializationRefreshInterval = Mathf.Max(0.05f, materializationRefreshInterval);
        spawnAttemptsPerAnimal = Mathf.Clamp(spawnAttemptsPerAnimal, 1, 64);
    }
}
