using UnityEngine;
using UnityEngine.AI;

public class FoodSpawner : MonoBehaviour
{
    public GameObject foodPrefab;
    public Transform spawnCenter;
    public Transform spawnedFoodParent;

    [Header("Spawn Probability")]
    [Tooltip("Percentage chance to spawn food every second (0 = never, 100 = every second)")]
    [Range(0f, 100f)]
    public float spawnChancePerSecond = 5f;

    [Min(0f)]
    public float spawnRadius = 450f;

    [Header("Population Limits")]
    [Min(1)] public int maxFoodCount = 500;

    [Header("Spawn Timing & Placement")]
    [Min(0.01f)] public float spawnCheckInterval = 1f;
    [Min(0.1f)] public float navMeshSampleDistance = 15f;
    [Min(0.1f)] public float terrainSearchRadius = 36f;
    [Min(0f)] public float surfaceOffset = 1f;
    [Range(1, 32)] public int placementAttempts = 8;
    public bool requireNavMesh = true;
    [Range(0f, 1f)] public float minimumGrassBiomass = 0.02f;
    [Tooltip("Limits placement work per frame. Remaining checks carry forward at high simulation speeds.")]
    [Min(1)] public int maxSpawnChecksPerFrame = 8;

    [Header("Temperature Runtime")]
    [SerializeField] private bool lastPlacementHasTemperature;
    [SerializeField] private float lastPlacementTemperatureCelsius;
    [SerializeField] private float lastGrowthMultiplier = 1f;

    float timer;

    void Update()
    {
        if (Time.deltaTime <= 0f) return;

        timer += Time.deltaTime;
        float interval = Mathf.Max(0.01f, spawnCheckInterval);
        int checks = 0;
        while (timer >= interval && checks < Mathf.Max(1, maxSpawnChecksPerFrame))
        {
            timer -= interval;
            checks++;
            TrySpawnFood(interval, false);
        }
    }

    public bool TrySpawnImmediately()
    {
        return TrySpawnFood(1f, true);
    }

    public int SpawnImmediately(int requestedCount)
    {
        int spawned = 0;
        int attempts = Mathf.Max(0, requestedCount) * Mathf.Max(2, placementAttempts);
        while (spawned < requestedCount && attempts-- > 0 && FoodItem.ActiveCount < maxFoodCount)
        {
            if (TrySpawnImmediately()) spawned++;
        }
        return spawned;
    }

    bool TrySpawnFood(float interval, bool guaranteedProbability)
    {
        if (foodPrefab == null || FoodItem.ActiveCount >= maxFoodCount) return false;

        float basePerSecondProbability = Mathf.Clamp01(spawnChancePerSecond / 100f);
        float probabilityRoll = guaranteedProbability ? 0f : Random.value;
        float idealIntervalProbability = 1f - Mathf.Pow(1f - basePerSecondProbability, interval);
        if (!guaranteedProbability && probabilityRoll >= idealIntervalProbability) return false;

        for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
        {
            if (!TryChoosePlacement(out Vector3 placement, out Vector3 temperaturePosition)) continue;

            lastPlacementHasTemperature = TemperatureSystem.TryGetTemperatureAt(
                temperaturePosition, out lastPlacementTemperatureCelsius);
            FoodItem profile = foodPrefab.GetComponent<FoodItem>();
            lastGrowthMultiplier = lastPlacementHasTemperature && profile != null
                ? profile.EvaluateGrowthMultiplier(lastPlacementTemperatureCelsius)
                : 1f;

            float perSecondProbability = guaranteedProbability
                ? lastGrowthMultiplier
                : basePerSecondProbability * lastGrowthMultiplier;
            float intervalProbability = guaranteedProbability
                ? Mathf.Clamp01(perSecondProbability)
                : 1f - Mathf.Pow(1f - perSecondProbability, interval);
            if (probabilityRoll >= intervalProbability) return false;

            GameObject food = Instantiate(foodPrefab, placement, Quaternion.identity, spawnedFoodParent);
            if (!food.activeSelf) food.SetActive(true);
            return true;
        }

        return false;
    }

    bool TryChoosePlacement(out Vector3 placement, out Vector3 temperaturePosition)
    {
        Vector3 center = spawnCenter == null ? transform.position : spawnCenter.position;
        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
        AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;

        if (terrainWorld != null)
        {
            if (!terrainWorld.TryFindWalkableGround(candidate, terrainSearchRadius, out Vector3 ground) ||
                !terrainWorld.TryGetWalkableSample(ground, out EnvironmentSample sample) ||
                sample.grassBiomass < minimumGrassBiomass)
            {
                placement = default;
                temperaturePosition = default;
                return false;
            }
            candidate = ground;
        }
        else
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                candidate.y = terrain.transform.position.y + terrain.SampleHeight(candidate);
            }
        }

        temperaturePosition = candidate;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit,
            Mathf.Max(0.1f, navMeshSampleDistance), NavMesh.AllAreas))
        {
            if (terrainWorld != null && !terrainWorld.TryGetWalkableSample(hit.position, out _))
            {
                placement = default;
                return false;
            }
            candidate = hit.position;
        }
        else if (requireNavMesh)
        {
            placement = default;
            return false;
        }

        placement = candidate + Vector3.up * surfaceOffset;
        return true;
    }
}
