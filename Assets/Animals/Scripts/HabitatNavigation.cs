using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Streams the animals' NavMesh in square tiles, so animals can spread across the whole island instead
// of staying inside one pre-built area. The tiles around every animal are kept built, at least one tile
// ahead of where it can wander. Tile geometry is sampled from the terrain on background threads and
// added to a single NavMesh that Unity updates in the background, so new tiles connect seamlessly.
[DisallowMultipleComponent]
public sealed class HabitatNavigation : MonoBehaviour
{
    public AnimalTerrainWorld terrainWorld;

    [Header("Tiles")]
    [Tooltip("Width of one navigation tile in world units.")]
    [Min(32f)] public float tileSize = 128f;
    [Tooltip("Distance between terrain samples in a tile. Smaller follows the ground more closely but builds slower.")]
    [Min(2f)] public float sampleSpacing = 8f;
    [Tooltip("Tiles kept built on every side of each animal, so animals never reach the NavMesh edge.")]
    [Range(1, 3)] public int tilesAroundAnimals = 1;
    [Tooltip("Real seconds between checks for animals approaching unbuilt tiles.")]
    [Min(0.1f)] public float refreshInterval = 0.5f;
    [Tooltip("Real seconds before a tile whose terrain sampling never finished is requested again.")]
    [Min(1f)] public float retryAfterSeconds = 15f;
    public int agentTypeId;

    [Header("Runtime")]
    [SerializeField] private int navigableTileCount;
    [SerializeField] private int tilesInProgress;

    private sealed class NavigationTile
    {
        public float requestTime;
        public bool geometryReady;
        public bool hasGround;
        public bool navigable;
        public Mesh mesh;
    }

    private sealed class TileGeometry
    {
        public int worldVersion;
        public Vector2Int coordinate;
        public Vector3[] vertices;
        public int[] triangles;
    }

    private readonly Dictionary<Vector2Int, NavigationTile> tiles = new Dictionary<Vector2Int, NavigationTile>();
    private readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
    private readonly List<Vector2Int> tilesAwaitingUpdate = new List<Vector2Int>();
    private readonly List<Vector2Int> tilesInRunningUpdate = new List<Vector2Int>();
    private readonly List<Vector2Int> expiredRequests = new List<Vector2Int>();
    private NavMeshData navMeshData;
    private NavMeshDataInstance navMeshInstance;
    private NavMeshBuildSettings buildSettings;
    private Bounds sourceBounds;
    private AsyncOperation runningUpdate;
    private int worldVersion;
    private float refreshTimer;

    public int AgentTypeId => agentTypeId;
    public int NavigableTileCount => navigableTileCount;

    // Discards all navigation (for example because new terrain was generated) and starts an empty NavMesh.
    public void ResetWorld()
    {
        DiscardNavigation();
        worldVersion++;
        ResolveTerrainWorld();

        buildSettings = ResolveBuildSettings();
        if (buildSettings.agentTypeID < 0)
        {
            Debug.LogError("No NavMesh agent type is available for animal navigation.", this);
            return;
        }

        navMeshData = new NavMeshData(buildSettings.agentTypeID);
        navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
    }

    public void RequestArea(Vector3 center, float radius)
    {
        GetTileRange(center, radius, out Vector2Int minimum, out Vector2Int maximum);
        for (int tileZ = minimum.y; tileZ <= maximum.y; tileZ++)
        {
            for (int tileX = minimum.x; tileX <= maximum.x; tileX++)
            {
                RequestTile(new Vector2Int(tileX, tileZ));
            }
        }
    }

    // True once every habitable tile in the area has finished building, including the NavMesh update.
    public bool IsAreaReady(Vector3 center, float radius)
    {
        GetTileRange(center, radius, out Vector2Int minimum, out Vector2Int maximum);
        for (int tileZ = minimum.y; tileZ <= maximum.y; tileZ++)
        {
            for (int tileX = minimum.x; tileX <= maximum.x; tileX++)
            {
                Vector2Int coordinate = new Vector2Int(tileX, tileZ);
                if (!IsTileInsideHabitat(coordinate)) continue;
                if (!tiles.TryGetValue(coordinate, out NavigationTile tile) || !tile.navigable) return false;
            }
        }

        return true;
    }

    // Plain-language summary of an area's navigation, for troubleshooting when animals cannot be placed.
    public string DescribeArea(Vector3 center, float radius)
    {
        int requested = 0;
        int sampled = 0;
        int withGround = 0;
        int navigable = 0;
        GetTileRange(center, radius, out Vector2Int minimum, out Vector2Int maximum);
        for (int tileZ = minimum.y; tileZ <= maximum.y; tileZ++)
        {
            for (int tileX = minimum.x; tileX <= maximum.x; tileX++)
            {
                if (!tiles.TryGetValue(new Vector2Int(tileX, tileZ), out NavigationTile tile)) continue;

                requested++;
                if (tile.geometryReady) sampled++;
                if (tile.hasGround) withGround++;
                if (tile.navigable) navigable++;
            }
        }

        bool navMeshNearCenter = NavMesh.SamplePosition(center, out _, Mathf.Max(radius, 50f), NavMesh.AllAreas);
        return $"{requested} navigation tiles requested, {sampled} sampled, {withGround} with walkable ground, " +
               $"{navigable} navigable; {sources.Count} NavMesh sources in total; " +
               $"NavMesh found near the centre: {(navMeshNearCenter ? "yes" : "no")}.";
    }

    public bool TryProjectToNavigation(Vector3 candidate, float maximumDistance, out Vector3 navigationPosition)
    {
        navigationPosition = default;
        if (terrainWorld == null || !terrainWorld.TryFindWalkableGround(candidate, maximumDistance, out Vector3 ground))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(ground, out NavMeshHit hit, Mathf.Max(0.1f, maximumDistance), NavMesh.AllAreas) ||
            !terrainWorld.TryGetWalkableSample(hit.position, out _))
        {
            return false;
        }

        navigationPosition = hit.position;
        return true;
    }

    void Update()
    {
        if (navMeshData == null) return;

        if (runningUpdate != null && runningUpdate.isDone)
        {
            foreach (Vector2Int coordinate in tilesInRunningUpdate)
            {
                if (tiles.TryGetValue(coordinate, out NavigationTile tile)) MarkNavigable(tile);
            }

            tilesInRunningUpdate.Clear();
            runningUpdate = null;
        }

        if (runningUpdate == null && tilesAwaitingUpdate.Count > 0)
        {
            tilesInRunningUpdate.AddRange(tilesAwaitingUpdate);
            tilesAwaitingUpdate.Clear();

            // Each update gets its own copy of the sources, since new tiles arrive while it runs. The
            // bounds cover every tile built so far, padded vertically so agents fit above the ground.
            Bounds buildBounds = sourceBounds;
            buildBounds.Expand(new Vector3(2f, 20f, 2f));
            runningUpdate = NavMeshBuilder.UpdateNavMeshDataAsync(navMeshData, buildSettings,
                new List<NavMeshBuildSource>(sources), buildBounds);
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            RetryExpiredRequests();
            RequestTilesAroundAnimals();
        }

        tilesInProgress = tiles.Count - navigableTileCount;
    }

    void OnDestroy()
    {
        DiscardNavigation();
    }

    void RequestTilesAroundAnimals()
    {
        if (SpeciesManager.Instance == null) return;

        float reach = tilesAroundAnimals * tileSize;
        foreach (SeekFood animal in SpeciesManager.Instance.ActiveAgents)
        {
            if (animal != null) RequestArea(animal.transform.position, reach);
        }
    }

    void RequestTile(Vector2Int coordinate)
    {
        if (navMeshData == null || tiles.ContainsKey(coordinate) || !IsTileInsideHabitat(coordinate)) return;

        TerrainEnvironmentSampler sampler = terrainWorld.Sampler;
        if (sampler == null || !sampler.IsConfigured) return;

        tiles.Add(coordinate, new NavigationTile { requestTime = Time.unscaledTime });

        // Copy everything the background thread needs; it must not touch Unity objects.
        int version = worldVersion;
        float size = tileSize;
        float spacing = sampleSpacing;
        float extent = terrainWorld.HabitableExtent;
        float maximumSlope = terrainWorld.maximumWalkableSlopeDegrees;
        bool excludeShore = terrainWorld.excludeShore;
        ThreadedDataRequester.RequestData(
            () => BuildTileGeometry(version, coordinate, sampler, size, spacing, extent, maximumSlope, excludeShore),
            OnTileGeometryReady);
    }

    void OnTileGeometryReady(object result)
    {
        TileGeometry geometry = (TileGeometry)result;
        if (geometry.worldVersion != worldVersion ||
            !tiles.TryGetValue(geometry.coordinate, out NavigationTile tile) || tile.geometryReady)
        {
            return;
        }

        tile.geometryReady = true;
        if (geometry.triangles.Length == 0)
        {
            // Water or cliffs only: there is nothing to walk on, so the tile is already complete.
            MarkNavigable(tile);
            return;
        }

        tile.hasGround = true;
        tile.mesh = new Mesh { name = $"Habitat Navigation Tile {geometry.coordinate}" };
        tile.mesh.vertices = geometry.vertices;
        tile.mesh.triangles = geometry.triangles;
        tile.mesh.RecalculateBounds();

        // Tile meshes are in world space, so their bounds can be combined directly.
        if (sources.Count == 0) sourceBounds = tile.mesh.bounds;
        else sourceBounds.Encapsulate(tile.mesh.bounds);

        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Mesh,
            sourceObject = tile.mesh,
            transform = Matrix4x4.identity,
            area = 0
        });
        tilesAwaitingUpdate.Add(geometry.coordinate);
    }

    void MarkNavigable(NavigationTile tile)
    {
        if (tile.navigable) return;

        tile.navigable = true;
        navigableTileCount++;
    }

    // A failed background job never calls back, so forget stale requests and let them be made again.
    void RetryExpiredRequests()
    {
        float now = Time.unscaledTime;
        expiredRequests.Clear();
        foreach (KeyValuePair<Vector2Int, NavigationTile> entry in tiles)
        {
            if (!entry.Value.geometryReady && now - entry.Value.requestTime > retryAfterSeconds)
            {
                expiredRequests.Add(entry.Key);
            }
        }

        foreach (Vector2Int coordinate in expiredRequests)
        {
            tiles.Remove(coordinate);
        }
    }

    // Runs on a background thread. Vertices lie on a world-aligned grid, so neighbouring tiles share
    // their edge vertices exactly and the NavMesh has no seams.
    static TileGeometry BuildTileGeometry(int version, Vector2Int coordinate, TerrainEnvironmentSampler sampler,
        float size, float spacing, float extent, float maximumSlope, bool excludeShore)
    {
        int cellsPerSide = Mathf.Max(1, Mathf.RoundToInt(size / spacing));
        float step = size / cellsPerSide;
        int pointsPerSide = cellsPerSide + 1;
        float minimumX = coordinate.x * size;
        float minimumZ = coordinate.y * size;

        Vector3[] vertices = new Vector3[pointsPerSide * pointsPerSide];
        bool[] walkable = new bool[vertices.Length];
        for (int z = 0; z < pointsPerSide; z++)
        {
            for (int x = 0; x < pointsPerSide; x++)
            {
                int index = z * pointsPerSide + x;
                Vector2 position = new Vector2(minimumX + x * step, minimumZ + z * step);
                walkable[index] = TrySampleWalkable(sampler, position, extent, maximumSlope, excludeShore,
                    out EnvironmentSample sample);
                vertices[index] = walkable[index] ? sample.position : new Vector3(position.x, 0f, position.y);
            }
        }

        List<int> triangles = new List<int>(cellsPerSide * cellsPerSide * 6);
        for (int z = 0; z < cellsPerSide; z++)
        {
            for (int x = 0; x < cellsPerSide; x++)
            {
                Vector2 quadCenter = new Vector2(minimumX + (x + 0.5f) * step, minimumZ + (z + 0.5f) * step);
                if (!TrySampleWalkable(sampler, quadCenter, extent, maximumSlope, excludeShore, out _)) continue;

                int a = z * pointsPerSide + x;
                int b = a + 1;
                int c = a + pointsPerSide;
                int d = c + 1;
                AddTriangleIfWalkable(a, c, d, vertices, walkable, maximumSlope, triangles);
                AddTriangleIfWalkable(a, d, b, vertices, walkable, maximumSlope, triangles);
            }
        }

        return new TileGeometry
        {
            worldVersion = version,
            coordinate = coordinate,
            vertices = vertices,
            triangles = triangles.ToArray()
        };
    }

    static bool TrySampleWalkable(TerrainEnvironmentSampler sampler, Vector2 position, float extent,
        float maximumSlope, bool excludeShore, out EnvironmentSample sample)
    {
        sample = default;
        if (Mathf.Abs(position.x) > extent || Mathf.Abs(position.y) > extent) return false;

        return sampler.TrySample(position, out sample) &&
               AnimalTerrainWorld.IsWalkable(sample, maximumSlope, excludeShore);
    }

    static void AddTriangleIfWalkable(int a, int b, int c, Vector3[] vertices, bool[] walkable,
        float maximumSlope, List<int> triangles)
    {
        if (!walkable[a] || !walkable[b] || !walkable[c]) return;

        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
        if (Vector3.Angle(normal, Vector3.up) > maximumSlope) return;

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    bool IsTileInsideHabitat(Vector2Int coordinate)
    {
        if (terrainWorld == null) return false;

        float extent = terrainWorld.HabitableExtent;
        float minimumX = coordinate.x * tileSize;
        float minimumZ = coordinate.y * tileSize;
        return minimumX < extent && minimumX + tileSize > -extent &&
               minimumZ < extent && minimumZ + tileSize > -extent;
    }

    void GetTileRange(Vector3 center, float radius, out Vector2Int minimum, out Vector2Int maximum)
    {
        minimum = new Vector2Int(Mathf.FloorToInt((center.x - radius) / tileSize),
                                 Mathf.FloorToInt((center.z - radius) / tileSize));
        maximum = new Vector2Int(Mathf.FloorToInt((center.x + radius) / tileSize),
                                 Mathf.FloorToInt((center.z + radius) / tileSize));
    }

    NavMeshBuildSettings ResolveBuildSettings()
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeId);
        if (settings.agentTypeID < 0 && NavMesh.GetSettingsCount() > 0)
        {
            settings = NavMesh.GetSettingsByIndex(0);
            agentTypeId = settings.agentTypeID;
        }

        return settings;
    }

    void ResolveTerrainWorld()
    {
        if (terrainWorld == null) terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld == null) terrainWorld = FindAnyObjectByType<AnimalTerrainWorld>();
    }

    void DiscardNavigation()
    {
        if (navMeshData != null && runningUpdate != null && !runningUpdate.isDone)
        {
            NavMeshBuilder.Cancel(navMeshData);
        }

        if (navMeshInstance.valid) navMeshInstance.Remove();
        if (navMeshData != null) Destroy(navMeshData);
        foreach (NavigationTile tile in tiles.Values)
        {
            if (tile.mesh != null) Destroy(tile.mesh);
        }

        navMeshData = null;
        navMeshInstance = default;
        runningUpdate = null;
        tiles.Clear();
        sources.Clear();
        tilesAwaitingUpdate.Clear();
        tilesInRunningUpdate.Clear();
        navigableTileCount = 0;
        tilesInProgress = 0;
    }
}
