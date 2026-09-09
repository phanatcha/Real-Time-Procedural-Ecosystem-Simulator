using UnityEngine;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class AnimalTerrainWorld : MonoBehaviour
{
    public static AnimalTerrainWorld Active { get; private set; }

    [Header("Terrain Source")]
    public TerrainGenerator terrainGenerator;

    [Header("Terrestrial Movement")]
    [Range(0f, 60f)] public float maximumWalkableSlopeDegrees = 32f;
    public bool excludeShore = true;
    [Min(0f)] public float boundaryInset = 20f;
    [Min(0.1f)] public float walkableSearchStep = 6f;

    public TerrainEnvironmentSampler Sampler
    {
        get
        {
            ResolveTerrainGenerator();
            return terrainGenerator == null ? null : terrainGenerator.WorldEnvironmentSampler;
        }
    }

    public float WorldRadius
    {
        get
        {
            ResolveTerrainGenerator();
            if (terrainGenerator == null || terrainGenerator.heightMapSettings == null ||
                terrainGenerator.meshSettings == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, terrainGenerator.heightMapSettings.worldRadius) *
                   Mathf.Max(0f, terrainGenerator.meshSettings.meshScale);
        }
    }

    public bool IsConfigured
    {
        get
        {
            TerrainEnvironmentSampler sampler = Sampler;
            return sampler != null && sampler.IsConfigured && WorldRadius > boundaryInset;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = null;
    }

    void Awake()
    {
        ResolveTerrainGenerator();
    }

    void OnEnable()
    {
        Active = this;
    }

    void OnDisable()
    {
        if (Active == this) Active = null;
    }

    public bool TryGetSample(Vector3 worldPosition, out EnvironmentSample sample)
    {
        sample = default;
        TerrainEnvironmentSampler sampler = Sampler;
        return sampler != null && sampler.TrySample(worldPosition, out sample);
    }

    public bool TryGetSample(Vector2 worldPosition, out EnvironmentSample sample)
    {
        sample = default;
        TerrainEnvironmentSampler sampler = Sampler;
        return sampler != null && sampler.TrySample(worldPosition, out sample);
    }

    public bool TryGetCachedSample(Vector3 worldPosition, out EnvironmentSample sample)
    {
        sample = default;
        TerrainEnvironmentSampler sampler = Sampler;
        return sampler != null && sampler.TrySampleCached(worldPosition, out sample);
    }

    public bool TryGetWalkableSample(Vector3 worldPosition, out EnvironmentSample sample)
    {
        return TryGetWalkableSample(new Vector2(worldPosition.x, worldPosition.z), out sample);
    }

    public bool TryGetWalkableSample(Vector2 worldPosition, out EnvironmentSample sample)
    {
        if (!IsInsideHabitableBounds(worldPosition) || !TryGetSample(worldPosition, out sample))
        {
            sample = default;
            return false;
        }

        return IsWalkable(sample);
    }

    public bool IsWalkable(EnvironmentSample sample)
    {
        if (!sample.isValid || !sample.isLand || sample.isWater) return false;
        if (excludeShore && sample.isShore) return false;
        return sample.slopeDegrees <= Mathf.Clamp(maximumWalkableSlopeDegrees, 0f, 60f);
    }

    public bool IsInsideHabitableBounds(Vector3 worldPosition)
    {
        return IsInsideHabitableBounds(new Vector2(worldPosition.x, worldPosition.z));
    }

    public bool IsInsideHabitableBounds(Vector2 worldPosition)
    {
        float extent = Mathf.Max(0f, WorldRadius - Mathf.Max(0f, boundaryInset));
        return extent > 0f && Mathf.Abs(worldPosition.x) <= extent && Mathf.Abs(worldPosition.y) <= extent;
    }

    public Vector3 ClampToHabitableBounds(Vector3 worldPosition)
    {
        float extent = Mathf.Max(0f, WorldRadius - Mathf.Max(0f, boundaryInset));
        worldPosition.x = Mathf.Clamp(worldPosition.x, -extent, extent);
        worldPosition.z = Mathf.Clamp(worldPosition.z, -extent, extent);
        return worldPosition;
    }

    public bool TryProjectToWalkableGround(Vector3 candidate, out Vector3 groundPosition)
    {
        candidate = ClampToHabitableBounds(candidate);
        if (TryGetWalkableSample(candidate, out EnvironmentSample sample))
        {
            groundPosition = sample.position;
            return true;
        }

        groundPosition = default;
        return false;
    }

    public bool TryFindWalkableGround(Vector3 candidate, float searchRadius, out Vector3 groundPosition)
    {
        candidate = ClampToHabitableBounds(candidate);
        if (TryProjectToWalkableGround(candidate, out groundPosition)) return true;

        float step = Mathf.Max(0.1f, walkableSearchStep);
        int ringCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, searchRadius) / step));
        float phase = Mathf.Repeat(candidate.x * 0.1031f + candidate.z * 0.11369f, 1f) * Mathf.PI * 2f;

        for (int ring = 1; ring <= ringCount; ring++)
        {
            float radius = Mathf.Min(searchRadius, ring * step);
            int samples = Mathf.Max(8, Mathf.CeilToInt(Mathf.PI * 2f * radius / step));
            for (int index = 0; index < samples; index++)
            {
                float angle = phase + index * Mathf.PI * 2f / samples;
                Vector3 point = candidate + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (TryProjectToWalkableGround(point, out groundPosition)) return true;
            }
        }

        groundPosition = default;
        return false;
    }

    public bool TryFindWalkableGroundTowardCenter(Vector3 candidate, float searchRadius,
        out Vector3 groundPosition)
    {
        Vector3 clamped = ClampToHabitableBounds(candidate);
        Vector3 towardCenter = Vector3.MoveTowards(clamped, Vector3.zero, Mathf.Max(0f, searchRadius));
        if (TryFindWalkableGround(towardCenter, searchRadius, out groundPosition)) return true;
        return TryFindWalkableGround(Vector3.zero, searchRadius, out groundPosition);
    }

    void ResolveTerrainGenerator()
    {
        if (terrainGenerator == null)
        {
            terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        maximumWalkableSlopeDegrees = Mathf.Clamp(maximumWalkableSlopeDegrees, 0f, 60f);
        boundaryInset = Mathf.Max(0f, boundaryInset);
        walkableSearchStep = Mathf.Max(0.1f, walkableSearchStep);
    }
#endif
}
