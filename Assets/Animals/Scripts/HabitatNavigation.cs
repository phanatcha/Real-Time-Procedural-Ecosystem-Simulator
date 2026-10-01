using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Streams the animals' NavMesh in square tiles, so animals can spread across the whole island instead
// of staying inside one pre-built area. The tiles around every animal are kept built, at least one tile
// ahead of where it can wander. Tile geometry is sampled from the terrain on background threads and
// added to a single NavMesh that Unity updates in the background, so new tiles connect seamlessly.
// With water included, each tile also carries the flat water surface as NavMesh areas for shallow water,
// deep water and dead zones (see WaterAccess and SeaBiomes), joined to the shore so animals can walk straight
// into the water.
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

    [Header("Water")]
    [Tooltip("Builds the water surface into the NavMesh and makes the shore walkable, so animals can wade and " +
             "swim. Off keeps animals on land and away from the shore.")]
    public bool includeWater = true;
    [Tooltip("Water up to this deep, in world units, is shallow: every animal can wade in it. Deeper water is " +
             "only open to swimmers.")]
    [Min(0f)] public float wadingDepth = 1f;

    [Header("Runtime")]
    [SerializeField] private int navigableTileCount;
    [SerializeField] private int tilesInProgress;

    private sealed class NavigationTile
    {
        public float requestTime;
        public bool geometryReady;
        public bool hasGround;
        public bool navigable;
        // One mesh per NavMesh area the tile has: land, shallow water, deep water, dead zone.
        public readonly List<Mesh> meshes = new List<Mesh>(4);
    }

    private sealed class TileGeometry
    {
        public int worldVersion;
        public Vector2Int coordinate;
        public Vector3[] vertices;
        public int[] landTriangles;
        public int[] shallowWaterTriangles;
        public int[] deepWaterTriangles;
        public int[] deadZoneTriangles;

        public bool IsEmpty => landTriangles.Length == 0 && shallowWaterTriangles.Length == 0 &&
                               deepWaterTriangles.Length == 0 && deadZoneTriangles.Length == 0;
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
        RefreshWaterLevel();

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
        if (includeWater && !WaterAccess.HasWater) RefreshWaterLevel();

        tiles.Add(coordinate, new NavigationTile { requestTime = Time.unscaledTime });

        // Copy everything the background thread needs; it must not touch Unity objects.
        int version = worldVersion;
        float size = tileSize;
        float spacing = sampleSpacing;
        float extent = terrainWorld.HabitableExtent;
        float maximumSlope = terrainWorld.maximumWalkableSlopeDegrees;
        // With water, the shore is walkable so the ground runs down into the water.
        bool withWater = includeWater && WaterAccess.HasWater;
        bool excludeShore = terrainWorld.excludeShore && !withWater;
        float waterLevel = WaterAccess.SurfaceHeight;
        float shallowDepth = wadingDepth;
        // Without sea biomes (no map in the scene yet) the classifier finds none, so there are no dead zones.
        SeaBiomeMap.TryCreateClassifier(terrainWorld, out SeaBiomeClassifier seaBiomes);
        ThreadedDataRequester.RequestData(
            () => BuildTileGeometry(version, coordinate, sampler, size, spacing, extent, maximumSlope, excludeShore,
                                    withWater, waterLevel, shallowDepth, seaBiomes),
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
        if (geometry.IsEmpty)
        {
            // Cliffs only (or water, when water isn't included): there is nothing to walk or swim on, so the
            // tile is already complete.
            MarkNavigable(tile);
            return;
        }

        tile.hasGround = true;
        AddSource(tile, geometry, geometry.landTriangles, 0, "Land");
        AddSource(tile, geometry, geometry.shallowWaterTriangles, WaterAccess.ShallowWaterArea, "Shallow Water");
        AddSource(tile, geometry, geometry.deepWaterTriangles, WaterAccess.DeepWaterArea, "Deep Water");
        AddSource(tile, geometry, geometry.deadZoneTriangles, WaterAccess.DeadZoneArea, "Dead Zone");
        tilesAwaitingUpdate.Add(geometry.coordinate);
    }

    // Each NavMesh area needs its own source. The meshes share the tile's vertices, so the areas join exactly.
    void AddSource(NavigationTile tile, TileGeometry geometry, int[] triangles, int area, string label)
    {
        if (triangles.Length == 0) return;

        Mesh mesh = new Mesh { name = $"Habitat Navigation {label} {geometry.coordinate}" };
        mesh.vertices = geometry.vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        tile.meshes.Add(mesh);

        // Tile meshes are in world space, so their bounds can be combined directly.
        if (sources.Count == 0) sourceBounds = mesh.bounds;
        else sourceBounds.Encapsulate(mesh.bounds);

        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Mesh,
            sourceObject = mesh,
            transform = Matrix4x4.identity,
            area = area
        });
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
    // their edge vertices exactly and the NavMesh has no seams. With water, water vertices sit on the flat
    // water surface, so triangles between the shore and the water form a ramp down into it.
    static TileGeometry BuildTileGeometry(int version, Vector2Int coordinate, TerrainEnvironmentSampler sampler,
        float size, float spacing, float extent, float maximumSlope, bool excludeShore, bool withWater,
        float waterLevel, float wadingDepth, SeaBiomeClassifier seaBiomes)
    {
        int cellsPerSide = Mathf.Max(1, Mathf.RoundToInt(size / spacing));
        float step = size / cellsPerSide;
        int pointsPerSide = cellsPerSide + 1;
        float minimumX = coordinate.x * size;
        float minimumZ = coordinate.y * size;

        Vector3[] vertices = new Vector3[pointsPerSide * pointsPerSide];
        WaterAccess.Corner[] corners = new WaterAccess.Corner[vertices.Length];
        for (int z = 0; z < pointsPerSide; z++)
        {
            for (int x = 0; x < pointsPerSide; x++)
            {
                int index = z * pointsPerSide + x;
                Vector2 position = new Vector2(minimumX + x * step, minimumZ + z * step);
                corners[index] = SampleCorner(sampler, position, extent, maximumSlope, excludeShore, withWater,
                    waterLevel, seaBiomes, out vertices[index]);
            }
        }

        // Triangle indices for each kind of surface, indexed by WaterAccess.Surface.
        List<int>[] surfaces = new List<int>[5];
        for (int surface = 0; surface < surfaces.Length; surface++) surfaces[surface] = new List<int>();
        for (int z = 0; z < cellsPerSide; z++)
        {
            for (int x = 0; x < cellsPerSide; x++)
            {
                // A quad whose middle is a cliff stays a hole, even when its corners are usable. Only whether the
                // middle is usable matters, so its sea biome isn't worked out.
                Vector2 quadCenter = new Vector2(minimumX + (x + 0.5f) * step, minimumZ + (z + 0.5f) * step);
                if (!SampleCorner(sampler, quadCenter, extent, maximumSlope, excludeShore, withWater, waterLevel,
                        default, out _).usable)
                {
                    continue;
                }

                int a = z * pointsPerSide + x;
                int b = a + 1;
                int c = a + pointsPerSide;
                int d = c + 1;
                AddTriangle(a, c, d, vertices, corners, maximumSlope, wadingDepth, surfaces);
                AddTriangle(a, d, b, vertices, corners, maximumSlope, wadingDepth, surfaces);
            }
        }

        return new TileGeometry
        {
            worldVersion = version,
            coordinate = coordinate,
            vertices = vertices,
            landTriangles = surfaces[(int)WaterAccess.Surface.Land].ToArray(),
            shallowWaterTriangles = surfaces[(int)WaterAccess.Surface.ShallowWater].ToArray(),
            deepWaterTriangles = surfaces[(int)WaterAccess.Surface.DeepWater].ToArray(),
            deadZoneTriangles = surfaces[(int)WaterAccess.Surface.DeadZone].ToArray()
        };
    }

    // A grid point is water (on the water surface, with the depth below it and whether it is in a dead zone),
    // walkable land, or unusable.
    static WaterAccess.Corner SampleCorner(TerrainEnvironmentSampler sampler, Vector2 position, float extent,
        float maximumSlope, bool excludeShore, bool withWater, float waterLevel, SeaBiomeClassifier seaBiomes,
        out Vector3 vertex)
    {
        vertex = new Vector3(position.x, 0f, position.y);
        if (Mathf.Abs(position.x) > extent || Mathf.Abs(position.y) > extent ||
            !sampler.TrySample(position, out EnvironmentSample sample) || !sample.isValid)
        {
            return WaterAccess.Corner.Unusable;
        }

        if (withWater && sample.isWater)
        {
            vertex = new Vector3(position.x, waterLevel, position.y);
            return WaterAccess.Corner.Water(waterLevel - sample.position.y, seaBiomes.IsDeadZone(sample));
        }

        if (!AnimalTerrainWorld.IsWalkable(sample, maximumSlope, excludeShore))
        {
            return WaterAccess.Corner.Unusable;
        }

        vertex = sample.position;
        return WaterAccess.Corner.Land;
    }

    static void AddTriangle(int a, int b, int c, Vector3[] vertices, WaterAccess.Corner[] corners,
        float maximumSlope, float wadingDepth, List<int>[] surfaces)
    {
        WaterAccess.Surface surface = WaterAccess.ClassifyTriangle(corners[a], corners[b], corners[c], wadingDepth);
        if (surface == WaterAccess.Surface.None) return;

        // Open water is flat; land and the ramps from the shore into the water must not be too steep to climb.
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
        if (Vector3.Angle(normal, Vector3.up) > maximumSlope) return;

        List<int> triangles = surfaces[(int)surface];
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

    // The water surface sits where the terrain starts counting as water, the level the water is drawn at.
    void RefreshWaterLevel()
    {
        WaterAccess.SurfaceHeight = float.NegativeInfinity;
        WaterAccess.WadingDepth = wadingDepth;
        TerrainEnvironmentSampler sampler = terrainWorld != null ? terrainWorld.Sampler : null;
        if (!includeWater || sampler == null || !sampler.IsConfigured ||
            sampler.EnvironmentDefinitions == null || sampler.HeightMapSettings == null)
        {
            return;
        }

        HeightMapSettings heights = sampler.HeightMapSettings;
        WaterAccess.SurfaceHeight = Mathf.Lerp(heights.minHeight, heights.maxHeight,
                                               sampler.EnvironmentDefinitions.ShorelineThreshold);
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
            foreach (Mesh mesh in tile.meshes) Destroy(mesh);
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
        WaterAccess.SurfaceHeight = float.NegativeInfinity;
    }
}
