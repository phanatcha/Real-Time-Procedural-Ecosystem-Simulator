using System.Collections.Generic;
using UnityEngine;

// Grows plant food on square tiles laid out procedurally from the terrain, both where the camera looks
// and where animals are, so food appears as terrain streams in:
// - Density follows the terrain's grass biomass, so lush lowland holds more plants than tundra.
// - Nutrition follows moisture, so wetter ground grows richer (and larger) plants.
// - Regrowth speed follows temperature, so an eaten plant comes back fastest where it is warm.
// - Well-wooded ground also grows richer leaves high up, which only animals that reach high enough can eat.
// Plant positions come from a hash of the world seed and fixed world cells, so a seed always gives the
// same layout. Tiles that nothing needs are unloaded but remember what was eaten, and keep regrowing.
public class FoodSpawner : MonoBehaviour
{
    public GameObject foodPrefab;
    public Transform spawnedFoodParent;
    [Tooltip("Plants load around this generator's viewer (the camera). Found automatically when empty.")]
    public TerrainGenerator terrainGenerator;
    [Tooltip("Found automatically when empty.")]
    public AnimalTerrainWorld terrainWorld;

    [Header("Where Plants Load")]
    [Min(32f)] public float tileSize = 128f;
    [Tooltip("Plants load within this distance of the camera.")]
    [Min(0f)] public float cameraRadius = 300f;
    [Tooltip("Tiles loaded on every side of each animal.")]
    [Range(0, 3)] public int tilesAroundAnimals = 1;
    [Tooltip("Real seconds before a tile nothing needs is unloaded. Unloaded tiles keep regrowing.")]
    [Min(0f)] public float unloadDelay = 10f;
    [Min(0.1f)] public float refreshInterval = 0.5f;
    [Tooltip("Real seconds before a tile whose terrain sampling never finished is requested again.")]
    [Min(1f)] public float retryAfterSeconds = 15f;
    [Tooltip("Limits plant creation per frame; the rest appear over the next frames.")]
    [Min(1)] public int maxPlantsCreatedPerFrame = 64;

    [Header("Plant Sites")]
    [Tooltip("Distance between candidate plant sites. Smaller values mean more plants.")]
    [Min(2f)] public float siteSpacing = 16f;
    [Tooltip("Ground with less grass biomass than this never holds a plant.")]
    [Range(0f, 1f)] public float minimumGrassBiomass = 0.02f;
    [Min(0f)] public float surfaceOffset = 1f;

    [Header("Food Quality")]
    [Min(1f)] public float baseNutrition = 75f;
    [Tooltip("Nutrition multiplier on the driest (x) and the wettest (y) ground.")]
    public Vector2 moistureNutritionMultiplier = new Vector2(0.6f, 1.4f);
    [Tooltip("Size multiplier for plants on the driest (x) and the wettest (y) ground.")]
    public Vector2 moistureSizeMultiplier = new Vector2(0.75f, 1.3f);

    [Header("Regrowth")]
    [Tooltip("Simulated seconds for an eaten plant to regrow at the ideal growth temperature. Longer " +
             "regrowth means less food per area, so fewer animals can live there.")]
    [Min(1f)] public float baseRegrowSeconds = 90f;
    [Tooltip("Sites too cold to regrow faster than this fraction of the ideal rate get no plant.")]
    [Range(0.01f, 1f)] public float minimumGrowthRate = 0.05f;

    [Header("Tall Forest Food")]
    [Tooltip("Leaves high up in well-wooded ground, which only animals with enough feeding reach (a tall " +
             "body, long legs or a neck) can eat. They are extra food; ground plants are unchanged.")]
    public bool growTallFood = true;
    [Tooltip("Tree cover a cell needs before it can hold tall food.")]
    [Range(0f, 1f)] public float minimumTreeCover = 0.5f;
    [Tooltip("Chance that a wooded cell holds tall food, multiplied by its tree cover.")]
    [Range(0f, 1f)] public float tallFoodChance = 0.35f;
    [Tooltip("Height of the leaves above the ground, lowest (x) to highest (y), in world units.")]
    public Vector2 tallFoodHeight = new Vector2(6f, 14f);
    [Tooltip("Tall food is richer than ground plants.")]
    [Min(1f)] public float tallFoodNutritionMultiplier = 1.6f;

    [Header("Runtime")]
    [SerializeField] private int loadedTileCount;
    [SerializeField] private int shownTileCount;
    [SerializeField] private int plantSiteCount;
    [SerializeField] private int shownPlantCount;

    private sealed class PlantSite
    {
        public Vector3 position;
        public float nutrition;
        public float sizeMultiplier;
        public float regrowSeconds;
        // How far the plant's centre is above the ground it grows from.
        public float heightAboveGround;
        public bool isTall;
        // Progress towards the next plant, advancing only while the site is empty; 1 or more means a
        // grown plant is ready to appear.
        public float regrowProgress;
        public FoodItem plant;
    }

    private sealed class FoodTile
    {
        public List<PlantSite> sites;
        public bool shown;
        public float requestTime;
        public float lastNeededTime;
    }

    private sealed class PlantCandidates
    {
        public int worldVersion;
        public Vector2Int coordinate;
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<float> moistures = new List<float>();
        // 0 for ground plants, otherwise the height of tall food above the ground.
        public readonly List<float> heights = new List<float>();

        public void Add(EnvironmentSample environment, float height)
        {
            positions.Add(environment.position);
            moistures.Add(environment.moisture);
            heights.Add(height);
        }
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Color TallFoodColor = new Color(0.16f, 0.5f, 0.2f);
    private static readonly Color TrunkColor = new Color(0.36f, 0.25f, 0.16f);

    private readonly Dictionary<Vector2Int, FoodTile> tiles = new Dictionary<Vector2Int, FoodTile>();
    private readonly HashSet<Vector2Int> neededTiles = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> expiredRequests = new List<Vector2Int>();
    private int worldSeed;
    private int worldVersion;
    private float refreshTimer;

    public int PlantSiteCount => plantSiteCount;
    public int ShownPlantCount => shownPlantCount;

    // Forgets every plant, for a newly generated world. The seed decides the new plant layout.
    public void ResetWorld(int seed)
    {
        foreach (FoodTile tile in tiles.Values)
        {
            if (tile.sites == null) continue;
            foreach (PlantSite site in tile.sites)
            {
                if (site.plant != null) Destroy(site.plant.gameObject);
            }
        }

        tiles.Clear();
        worldSeed = seed;
        worldVersion++;
        refreshTimer = 0f;
        loadedTileCount = 0;
        shownTileCount = 0;
        plantSiteCount = 0;
        shownPlantCount = 0;
    }

    void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            RefreshTiles();
        }

        UpdatePlants(Time.deltaTime);
    }

    void RefreshTiles()
    {
        ResolveReferences();
        if (foodPrefab == null || terrainWorld == null) return;

        float now = Time.unscaledTime;
        neededTiles.Clear();
        Transform viewer = terrainGenerator != null ? terrainGenerator.viewer : null;
        if (viewer != null && cameraRadius > 0f)
        {
            AddTilesInArea(viewer.position, cameraRadius);
        }

        if (tilesAroundAnimals > 0 && SpeciesManager.Instance != null)
        {
            foreach (SeekFood animal in SpeciesManager.Instance.ActiveAgents)
            {
                if (animal != null) AddTilesInArea(animal.transform.position, tilesAroundAnimals * tileSize);
            }
        }

        RetryExpiredRequests(now);
        foreach (Vector2Int coordinate in neededTiles)
        {
            if (!tiles.TryGetValue(coordinate, out FoodTile tile))
            {
                tile = RequestSites(coordinate, now);
                if (tile == null) continue;
            }

            tile.lastNeededTime = now;
            if (tile.sites != null) tile.shown = true;
        }

        shownTileCount = 0;
        foreach (FoodTile tile in tiles.Values)
        {
            if (tile.shown && now - tile.lastNeededTime > unloadDelay) HideTile(tile);
            if (tile.shown) shownTileCount++;
        }
    }

    void UpdatePlants(float deltaTime)
    {
        int created = 0;
        shownPlantCount = 0;
        foreach (FoodTile tile in tiles.Values)
        {
            if (tile.sites == null) continue;

            foreach (PlantSite site in tile.sites)
            {
                if (site.plant != null && site.plant.IsAvailable)
                {
                    shownPlantCount++;
                    continue;
                }

                // Eaten or spoiled. The plant destroys itself, so from the next frame it reads as null
                // here; its site regrows from the zero set when it was planted either way.
                site.plant = null;
                if (site.regrowProgress < 1f)
                {
                    site.regrowProgress += deltaTime / site.regrowSeconds;
                    if (site.regrowProgress < 1f) continue;
                }

                if (tile.shown && created < maxPlantsCreatedPerFrame)
                {
                    CreatePlant(site);
                    created++;
                    shownPlantCount++;
                }
            }
        }
    }

    FoodTile RequestSites(Vector2Int coordinate, float now)
    {
        TerrainEnvironmentSampler sampler = terrainWorld.Sampler;
        if (sampler == null || !sampler.IsConfigured) return null;

        FoodTile tile = new FoodTile { requestTime = now };
        tiles[coordinate] = tile;

        // Copy everything the background thread needs; it must not touch Unity objects.
        int version = worldVersion;
        int seed = worldSeed;
        float size = tileSize;
        float spacing = siteSpacing;
        float minimumGrass = minimumGrassBiomass;
        float extent = terrainWorld.HabitableExtent;
        float maximumSlope = terrainWorld.maximumWalkableSlopeDegrees;
        bool excludeShore = terrainWorld.excludeShore;
        TallFoodRules tallFood = new TallFoodRules
        {
            enabled = growTallFood,
            minimumTreeCover = minimumTreeCover,
            chance = tallFoodChance,
            lowest = Mathf.Min(tallFoodHeight.x, tallFoodHeight.y),
            highest = Mathf.Max(tallFoodHeight.x, tallFoodHeight.y)
        };
        ThreadedDataRequester.RequestData(
            () => FindPlantCandidates(version, coordinate, sampler, seed, size, spacing, minimumGrass,
                                      extent, maximumSlope, excludeShore, tallFood),
            OnPlantCandidatesFound);
        return tile;
    }

    // Growth rates need the temperature system, so candidates become plant sites on the main thread.
    void OnPlantCandidatesFound(object result)
    {
        PlantCandidates candidates = (PlantCandidates)result;
        if (candidates.worldVersion != worldVersion ||
            !tiles.TryGetValue(candidates.coordinate, out FoodTile tile) || tile.sites != null)
        {
            return;
        }

        FoodItem growthProfile = foodPrefab != null ? foodPrefab.GetComponent<FoodItem>() : null;
        tile.sites = new List<PlantSite>(candidates.positions.Count);
        for (int index = 0; index < candidates.positions.Count; index++)
        {
            Vector3 position = candidates.positions[index];
            float growthRate = GetGrowthRate(growthProfile, position);
            if (growthRate < minimumGrowthRate) continue;

            float moisture = candidates.moistures[index];
            bool isTall = candidates.heights[index] > 0f;
            float heightAboveGround = isTall ? candidates.heights[index] : surfaceOffset;
            tile.sites.Add(new PlantSite
            {
                position = position + Vector3.up * heightAboveGround,
                nutrition = baseNutrition * Mathf.Lerp(moistureNutritionMultiplier.x,
                                                       moistureNutritionMultiplier.y, moisture) *
                            (isTall ? tallFoodNutritionMultiplier : 1f),
                sizeMultiplier = Mathf.Lerp(moistureSizeMultiplier.x, moistureSizeMultiplier.y, moisture),
                regrowSeconds = baseRegrowSeconds / growthRate,
                heightAboveGround = heightAboveGround,
                isTall = isTall,
                regrowProgress = 1f
            });
        }

        loadedTileCount++;
        plantSiteCount += tile.sites.Count;
        tile.shown = Time.unscaledTime - tile.lastNeededTime <= unloadDelay;
    }

    void CreatePlant(PlantSite site)
    {
        GameObject plantObject = Instantiate(foodPrefab, site.position, Quaternion.identity, spawnedFoodParent);
        Vector3 scale = foodPrefab.transform.localScale * site.sizeMultiplier;
        // Tall food is a wide, flat clump of leaves.
        plantObject.transform.localScale = site.isTall ? Vector3.Scale(scale, new Vector3(1.5f, 0.7f, 1.5f)) : scale;
        if (!plantObject.activeSelf) plantObject.SetActive(true);

        // Plants stay until eaten; the next one starts growing once this one is gone.
        site.plant = plantObject.GetComponent<FoodItem>();
        site.plant.Configure(FoodType.Plant, site.nutrition);
        site.plant.heightAboveGround = site.heightAboveGround;
        site.regrowProgress = 0f;

        if (site.isTall)
        {
            DressAsTree(plantObject, site.heightAboveGround);
        }
    }

    // Darker leaves on a thin trunk that reaches down to the ground, so tall food reads as a small tree.
    static void DressAsTree(GameObject leaves, float heightAboveGround)
    {
        if (!leaves.TryGetComponent(out MeshRenderer leafRenderer))
        {
            return;
        }

        SetColor(leafRenderer, TallFoodColor);

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.layer = leaves.layer;
        DestroyImmediate(trunk.GetComponent<Collider>());
        trunk.transform.SetParent(leaves.transform, false);

        // The leaves are unrotated, so undoing their scale keeps the trunk's own proportions.
        Vector3 leafScale = leaves.transform.lossyScale;
        float leafHalfHeight = leafScale.y * 0.5f;
        float trunkLength = Mathf.Max(0.1f, heightAboveGround - leafHalfHeight);
        trunk.transform.localScale = new Vector3(0.35f / leafScale.x, trunkLength * 0.5f / leafScale.y, 0.35f / leafScale.z);
        trunk.transform.localPosition = new Vector3(0f, -(leafHalfHeight + trunkLength * 0.5f) / leafScale.y, 0f);

        MeshRenderer trunkRenderer = trunk.GetComponent<MeshRenderer>();
        trunkRenderer.sharedMaterial = leafRenderer.sharedMaterial;
        SetColor(trunkRenderer, TrunkColor);
    }

    static void SetColor(Renderer target, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        target.SetPropertyBlock(block);
    }

    void HideTile(FoodTile tile)
    {
        tile.shown = false;
        foreach (PlantSite site in tile.sites)
        {
            // Uneaten plants are put away fully grown, so they are back as soon as the tile shows again.
            // Eaten ones keep the regrowth they have made so far.
            if (site.plant != null && site.plant.IsAvailable)
            {
                Destroy(site.plant.gameObject);
                site.regrowProgress = 1f;
            }

            site.plant = null;
        }
    }

    // A failed background job never calls back, so forget stale requests and let them be made again.
    void RetryExpiredRequests(float now)
    {
        expiredRequests.Clear();
        foreach (KeyValuePair<Vector2Int, FoodTile> entry in tiles)
        {
            if (entry.Value.sites == null && now - entry.Value.requestTime > retryAfterSeconds)
            {
                expiredRequests.Add(entry.Key);
            }
        }

        foreach (Vector2Int coordinate in expiredRequests)
        {
            tiles.Remove(coordinate);
        }
    }

    void AddTilesInArea(Vector3 center, float radius)
    {
        float extent = terrainWorld.HabitableExtent;
        int minimumX = Mathf.FloorToInt((center.x - radius) / tileSize);
        int maximumX = Mathf.FloorToInt((center.x + radius) / tileSize);
        int minimumZ = Mathf.FloorToInt((center.z - radius) / tileSize);
        int maximumZ = Mathf.FloorToInt((center.z + radius) / tileSize);
        for (int tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
        {
            for (int tileX = minimumX; tileX <= maximumX; tileX++)
            {
                float tileMinimumX = tileX * tileSize;
                float tileMinimumZ = tileZ * tileSize;
                bool insideHabitat = tileMinimumX < extent && tileMinimumX + tileSize > -extent &&
                                     tileMinimumZ < extent && tileMinimumZ + tileSize > -extent;
                if (insideHabitat) neededTiles.Add(new Vector2Int(tileX, tileZ));
            }
        }
    }

    void ResolveReferences()
    {
        if (terrainWorld == null) terrainWorld = AnimalTerrainWorld.Active;
        if (terrainGenerator == null && terrainWorld != null) terrainGenerator = terrainWorld.terrainGenerator;
    }

    // Copied settings for tall food, so the background thread never reads the component.
    private struct TallFoodRules
    {
        public bool enabled;
        public float minimumTreeCover;
        public float chance;
        public float lowest;
        public float highest;
    }

    // Runs on a background thread. Cells are fixed in world space, so a tile's plants are the same
    // whenever it loads for the same seed.
    static PlantCandidates FindPlantCandidates(int version, Vector2Int coordinate, TerrainEnvironmentSampler sampler,
        int seed, float size, float spacing, float minimumGrass, float extent, float maximumSlope, bool excludeShore,
        TallFoodRules tallFood)
    {
        int cellsPerSide = Mathf.Max(1, Mathf.RoundToInt(size / spacing));
        float cellSize = size / cellsPerSide;
        PlantCandidates candidates = new PlantCandidates { worldVersion = version, coordinate = coordinate };
        for (int localZ = 0; localZ < cellsPerSide; localZ++)
        {
            for (int localX = 0; localX < cellsPerSide; localX++)
            {
                int cellX = coordinate.x * cellsPerSide + localX;
                int cellZ = coordinate.y * cellsPerSide + localZ;

                // Grass biomass is both the minimum requirement and the chance a cell holds a plant.
                Vector2 position = CellPoint(seed, cellX, cellZ, cellSize, 0);
                if (IsInside(position, extent) &&
                    sampler.TrySample(position, out EnvironmentSample environment) &&
                    AnimalTerrainWorld.IsWalkable(environment, maximumSlope, excludeShore) &&
                    environment.grassBiomass >= minimumGrass &&
                    Hash01(seed, cellX, cellZ, 2) < environment.grassBiomass)
                {
                    candidates.Add(environment, 0f);
                }

                // Wooded cells can also hold leaves up high, growing from their own spot in the cell.
                if (!tallFood.enabled)
                {
                    continue;
                }

                Vector2 treePosition = CellPoint(seed, cellX, cellZ, cellSize, 3);
                if (IsInside(treePosition, extent) &&
                    sampler.TrySample(treePosition, out EnvironmentSample woods) &&
                    AnimalTerrainWorld.IsWalkable(woods, maximumSlope, excludeShore) &&
                    woods.treeCover >= tallFood.minimumTreeCover &&
                    Hash01(seed, cellX, cellZ, 5) < tallFood.chance * woods.treeCover)
                {
                    candidates.Add(woods, Mathf.Lerp(tallFood.lowest, tallFood.highest, Hash01(seed, cellX, cellZ, 6)));
                }
            }
        }

        return candidates;
    }

    // A fixed random point in a world cell; each salt gives an independent point.
    static Vector2 CellPoint(int seed, int cellX, int cellZ, float cellSize, int salt)
    {
        return new Vector2((cellX + Hash01(seed, cellX, cellZ, salt)) * cellSize,
                           (cellZ + Hash01(seed, cellX, cellZ, salt + 1)) * cellSize);
    }

    static bool IsInside(Vector2 position, float extent)
    {
        return Mathf.Abs(position.x) <= extent && Mathf.Abs(position.y) <= extent;
    }

    // Fraction of the ideal regrowth rate at this position, from the food's growth temperature range.
    static float GetGrowthRate(FoodItem growthProfile, Vector3 position)
    {
        if (growthProfile == null || !TemperatureSystem.TryGetTemperatureAt(position, out float celsius))
        {
            return 1f;
        }

        return growthProfile.EvaluateGrowthMultiplier(celsius);
    }

    // Stable pseudo-random value in [0, 1) for a world cell, so layouts repeat for the same seed.
    static float Hash01(int seed, int cellX, int cellZ, int salt)
    {
        unchecked
        {
            uint hash = (uint)seed * 0x9E3779B1u;
            hash = (hash ^ (uint)cellX * 0x85EBCA77u) * 0xC2B2AE3Du;
            hash = (hash ^ (uint)cellZ * 0x27D4EB2Fu) * 0x165667B1u;
            hash = (hash ^ (uint)salt * 0x61C88647u) * 0x85EBCA6Bu;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (hash >> 8) / 16777216f;
        }
    }
}
