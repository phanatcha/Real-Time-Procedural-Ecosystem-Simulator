using UnityEngine;
using UnityEngine.AI;

public class FoodSpawner : MonoBehaviour
{
    public GameObject foodPrefab;
    
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
    [Tooltip("Limits placement work per frame. Remaining checks carry forward at high simulation speeds.")]
    [Min(1)] public int maxSpawnChecksPerFrame = 8;

    [Header("Temperature Runtime")]
    [SerializeField] private bool lastPlacementHasTemperature;
    [SerializeField] private float lastPlacementTemperatureCelsius;
    [SerializeField] private float lastGrowthMultiplier = 1f;

    private float timer;

    void Update()
    {
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        timer += Time.deltaTime;
        
        float interval = Mathf.Max(0.01f, spawnCheckInterval);
        int checks = 0;
        while (timer >= interval && checks < Mathf.Max(1, maxSpawnChecksPerFrame))
        {
            timer -= interval;
            checks++;
            TrySpawnFood(interval);
        }
    }

    void TrySpawnFood(float interval)
    {
        if (foodPrefab == null || FoodItem.ActiveCount >= maxFoodCount)
        {
            return;
        }

        float basePerSecondProbability = Mathf.Clamp01(spawnChancePerSecond / 100f);
        float probabilityRoll = Random.value;
        float idealIntervalProbability = 1f - Mathf.Pow(1f - basePerSecondProbability, interval);
        if (probabilityRoll >= idealIntervalProbability)
        {
            return;
        }

        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            candidate.y = terrain.transform.position.y + terrain.SampleHeight(candidate);
        }
        
        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            lastPlacementHasTemperature = TemperatureSystem.TryGetTemperatureAt(hit.position, out lastPlacementTemperatureCelsius);
            FoodItem profile = foodPrefab.GetComponent<FoodItem>();
            lastGrowthMultiplier = lastPlacementHasTemperature && profile != null ?
                profile.EvaluateGrowthMultiplier(lastPlacementTemperatureCelsius) : 1f;

            float perSecondProbability = basePerSecondProbability * lastGrowthMultiplier;
            float intervalProbability = 1f - Mathf.Pow(1f - perSecondProbability, interval);
            if (probabilityRoll < intervalProbability)
            {
                Instantiate(foodPrefab, hit.position, Quaternion.identity);
            }
        }
    }
}
