using System.Collections.Generic;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEngine;

namespace Boreal.Animals
{
    // Grows plant food on square tiles laid out procedurally from the terrain, both where the camera looks
    // and where animals are, so food appears as terrain streams in:
    // - Density follows the terrain's grass biomass, so lush lowland holds more plants than tundra.
    // - Nutrition follows moisture, so wetter ground grows richer (and larger) plants.
    // - Regrowth speed follows temperature, so an eaten plant comes back fastest where it is warm.
    // - Well-wooded ground also grows richer leaves high up, which only animals that reach high enough can eat.
    // - The sea grows its own plants, one kind per sea biome (see SeaFood), floating at the water surface and drawn
    //   as seagrass, kelp, coral or plankton (see SeaPlantLooks). Those in water too deep to wade are for swimmers only.
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

        [Header("Sea Food")]
        [Tooltip("Grows plants in the sea, one kind per sea biome, floating at the water surface. Needs the water built " +
                 "into navigation (Include Water on Habitat Navigation).")]
        public bool growSeaFood = true;
        [Tooltip("How much each sea biome grows, and the surface temperatures sea plants grow in. Changes apply to " +
                 "tiles loaded afterwards.")]
        public SeaFoodRules seaFood = SeaFoodRules.Default;
        [Tooltip("How far a sea plant's centre floats above the water surface, in world units.")]
        [Min(0f)] public float seaFoodSurfaceOffset = 0.3f;

        [Header("Runtime")]
        [SerializeField] private int loadedTileCount;
        [SerializeField] private int shownTileCount;
        [SerializeField] private int plantSiteCount;
        [SerializeField] private int shownPlantCount;
        [SerializeField] private int shownSeaPlantCount;
        [SerializeField] private int seaPlantsEaten;

        private sealed class PlantSite
        {
            public Vector3 position;
            public float nutrition;
            public float sizeMultiplier;
            public float regrowSeconds;
            // How far the plant's centre is above the ground it grows from.
            public float heightAboveGround;
            public bool isTall;
            // Ground plants only: the summer forest food it is drawn as (see ForestFoodLooks). Looks only.
            public ForestFood forestFood;
            // Sea plants only: the biome they grow in (None on land), the depth of the water under them, and whether
            // that is too deep to wade.
            public SeaBiome seaBiome;
            public float waterDepth;
            public bool inDeepWater;
            // Progress towards the next plant, advancing only while the site is empty; 1 or more means a
            // grown plant is ready to appear.
            public float regrowProgress;
            public FoodItem plant;
            // Tall food only: the spruce the shoots grow on. It stays while they are eaten and regrow.
            public GameObject tree;
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
            public readonly List<float> treeCovers = new List<float>();
            // 0 for ground plants, otherwise the height of tall food above the ground.
            public readonly List<float> heights = new List<float>();

            // Sea plants, kept apart because they need no temperature lookup on the main thread: each one's spot on
            // the water surface, its biome, the depth below it and the surface temperature.
            public readonly List<Vector3> seaPositions = new List<Vector3>();
            public readonly List<SeaBiome> seaBiomes = new List<SeaBiome>();
            public readonly List<float> seaDepths = new List<float>();
            public readonly List<float> seaCelsius = new List<float>();

            public void Add(EnvironmentSample environment, float height)
            {
                positions.Add(environment.position);
                moistures.Add(environment.moisture);
                treeCovers.Add(environment.treeCover);
                heights.Add(height);
            }

            public void AddSea(Vector3 surfacePosition, SeaBiome biome, float depth, float celsius)
            {
                seaPositions.Add(surfacePosition);
                seaBiomes.Add(biome);
                seaDepths.Add(depth);
                seaCelsius.Add(celsius);
            }
        }

        // Tall food is a clump of bright fresh shoots on the side of a Norway spruce, drawn like the island's own trees
        // (the terrain's vegetation material and the world's vegetation colours). The shoots are the food; the spruce
        // belongs to the site and stays standing while they are eaten and regrow.
        private const int FoodSpruceVariants = 3;
        private const float FoodSpruceHeight = 15f;
        // How far up the spruce's tiers the shoots grow, between the lowest (0) and the top (1).
        private const float ShootTierFraction = 0.55f;
        private static Mesh[] foodSpruces;
        private static Mesh[] shootClumps;
        private static VegetationSettings foodLooksBuiltFor;

        private readonly Dictionary<Vector2Int, FoodTile> tiles = new Dictionary<Vector2Int, FoodTile>();
        private readonly HashSet<Vector2Int> neededTiles = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> expiredRequests = new List<Vector2Int>();
        private int worldSeed;
        private int worldVersion;
        private float refreshTimer;

        public int PlantSiteCount => plantSiteCount;
        public int ShownPlantCount => shownPlantCount;
        // Of the shown plants, those in the sea.
        public int ShownSeaPlantCount => shownSeaPlantCount;
        // Sea plants eaten since the world was created.
        public int SeaPlantsEaten => seaPlantsEaten;

        // Forgets every plant, for a newly generated world. The seed decides the new plant layout.
        public void ResetWorld(int seed)
        {
            foreach (FoodTile tile in tiles.Values)
            {
                if (tile.sites == null) continue;
                foreach (PlantSite site in tile.sites)
                {
                    if (site.plant != null) Destroy(site.plant.gameObject);
                    if (site.tree != null) Destroy(site.tree);
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
            shownSeaPlantCount = 0;
            seaPlantsEaten = 0;
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
            shownSeaPlantCount = 0;
            foreach (FoodTile tile in tiles.Values)
            {
                if (tile.sites == null) continue;

                foreach (PlantSite site in tile.sites)
                {
                    if (site.isTall && tile.shown && site.tree == null) AddSpruce(site);

                    if (site.plant != null && site.plant.IsAvailable)
                    {
                        CountShown(site);
                        continue;
                    }

                    // Eaten or spoiled. The plant destroys itself, so from the next frame it reads as null
                    // here; its site regrows from the zero set when it was planted either way. Plants don't spoil
                    // and hidden tiles let go of their plants themselves, so a plant still held here was eaten.
                    if (site.seaBiome != SeaBiome.None && !ReferenceEquals(site.plant, null)) seaPlantsEaten++;
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
                        CountShown(site);
                    }
                }
            }
        }

        void CountShown(PlantSite site)
        {
            shownPlantCount++;
            if (site.seaBiome != SeaBiome.None) shownSeaPlantCount++;
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
            // Without water in the navigation there is no water level or sea map, and so no sea food.
            SeaFoodSettings seaFoodSettings = new SeaFoodSettings { rules = seaFood, waterLevel = WaterAccess.SurfaceHeight };
            seaFoodSettings.enabled = growSeaFood &&
                                      SeaBiomeMap.TryCreateClassifier(terrainWorld, out seaFoodSettings.biomes);
            ThreadedDataRequester.RequestData(
                () => FindPlantCandidates(version, coordinate, sampler, seed, size, spacing, minimumGrass,
                                          extent, maximumSlope, excludeShore, tallFood, seaFoodSettings),
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
                    forestFood = ForestFoodLooks.Choose(moisture, candidates.treeCovers[index],
                                                        SiteHash(position) % 1000 / 1000f),
                    regrowProgress = 1f
                });
            }

            // Sea plants float on the water, so animals reach them at the surface, and grow by the sea's own rules.
            float wadingDepth = WaterAccess.WadingDepth;
            for (int index = 0; index < candidates.seaPositions.Count; index++)
            {
                float growthRate = seaFood.GrowthRate(candidates.seaCelsius[index]);
                if (growthRate < minimumGrowthRate) continue;

                SeaFoodYield yield = seaFood.Yield(candidates.seaBiomes[index]);
                tile.sites.Add(new PlantSite
                {
                    position = candidates.seaPositions[index] + Vector3.up * seaFoodSurfaceOffset,
                    nutrition = baseNutrition * yield.nutrition,
                    sizeMultiplier = 1f,
                    regrowSeconds = baseRegrowSeconds * Mathf.Max(0.1f, yield.regrowth) / growthRate,
                    heightAboveGround = seaFoodSurfaceOffset,
                    seaBiome = candidates.seaBiomes[index],
                    waterDepth = candidates.seaDepths[index],
                    inDeepWater = candidates.seaDepths[index] > wadingDepth,
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
            // Tall food is a wide, flat clump of leaves. Sea food is a flatter box floating at the surface, smaller for
            // plankton; animals eat by touching it, and SeaPlantLooks draws it as its biome's plant.
            Vector3 shape = site.isTall ? new Vector3(1.5f, 0.7f, 1.5f)
                : site.seaBiome == SeaBiome.OpenSea ? new Vector3(0.7f, 0.25f, 0.7f)
                : site.seaBiome != SeaBiome.None ? new Vector3(1.4f, 0.4f, 1.4f)
                : Vector3.one;
            plantObject.transform.localScale = Vector3.Scale(scale, shape);
            if (site.seaBiome != SeaBiome.None) plantObject.name = SeaFoodRules.PlantName(site.seaBiome);
            if (!plantObject.activeSelf) plantObject.SetActive(true);

            // Plants stay until eaten; the next one starts growing once this one is gone.
            site.plant = plantObject.GetComponent<FoodItem>();
            site.plant.Configure(FoodType.Plant, site.nutrition);
            site.plant.heightAboveGround = site.heightAboveGround;
            site.plant.seaBiome = site.seaBiome;
            site.plant.inDeepWater = site.inDeepWater;
            site.regrowProgress = 0f;

            if (site.isTall)
            {
                DressAsShoots(plantObject);
                AddSpruce(site);
            }
            else if (site.seaBiome == SeaBiome.None && FoodLooksReady(out Material material))
            {
                ForestFoodLooks.Dress(plantObject, site.forestFood, site.heightAboveGround, site.sizeMultiplier, material,
                                      terrainWorld.Sampler.VegetationSettings);
            }
            else if (site.seaBiome != SeaBiome.None)
            {
                SeaPlantLooks.Dress(plantObject, site.seaBiome, site.waterDepth, site.heightAboveGround);
            }
        }

        // Hides the food's box and draws fresh shoots in its place; the box still decides where animals reach and touch.
        void DressAsShoots(GameObject food)
        {
            if (!food.TryGetComponent(out MeshRenderer foodRenderer) || !FoodLooksReady(out Material material)) return;
            foodRenderer.enabled = false;

            // The food box is scaled; this unrotated holder undoes that, so the shoots are in metres.
            Vector3 foodScale = food.transform.lossyScale;
            Transform holder = new GameObject("Look").transform;
            holder.gameObject.layer = food.layer;
            holder.SetParent(food.transform, false);
            holder.localScale = new Vector3(1f / foodScale.x, 1f / foodScale.y, 1f / foodScale.z);

            int hash = SiteHash(food.transform.position);
            GameObject shoots = NewTreePart("Fresh Shoots", shootClumps[hash % shootClumps.Length], holder, food.layer, material);
            shoots.transform.localRotation = Quaternion.Euler(0f, (hash >> 3) % 360, 0f);
        }

        // A spruce sized so its tiers reach the shoots' height, standing to one side so the shoots sit on the outside of
        // its branches, where they can be seen.
        void AddSpruce(PlantSite site)
        {
            if (site.tree != null || !FoodLooksReady(out Material material)) return;

            int hash = SiteHash(site.position);
            Mesh mesh = foodSpruces[hash % foodSpruces.Length];
            float shootHeight = VegetationMeshBuilder.SpruceSkirt +
                                ShootTierFraction * (FoodSpruceHeight - VegetationMeshBuilder.SpruceSkirt - 2.3f);
            float scale = Mathf.Clamp(site.heightAboveGround / shootHeight, 0.7f, 1.9f);
            float angle = (hash >> 5) % 360 * Mathf.Deg2Rad;
            Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 ground = site.position - Vector3.up * site.heightAboveGround -
                             side * (VegetationMeshBuilder.SpruceRadius(ShootTierFraction) * scale * 0.85f);

            TerrainEnvironmentSampler sampler = terrainWorld != null ? terrainWorld.Sampler : null;
            if (sampler != null && sampler.TrySample(new Vector2(ground.x, ground.z), out EnvironmentSample below))
            {
                ground.y = below.height;
            }

            site.tree = NewTreePart("Spruce", mesh, spawnedFoodParent, foodPrefab.layer, material);
            site.tree.transform.position = ground - Vector3.up * 0.4f;
            site.tree.transform.rotation = Quaternion.Euler(0f, (hash >> 9) % 360, 0f);
            site.tree.transform.localScale = Vector3.one * scale;
        }

        static int SiteHash(Vector3 position)
        {
            int hash = (Mathf.FloorToInt(position.x * 3.1f) * 73856093) ^ (Mathf.FloorToInt(position.z * 3.1f) * 19349663);
            return hash & int.MaxValue;
        }

        static GameObject NewTreePart(string partName, Mesh mesh, Transform parent, int layer, Material material)
        {
            GameObject part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
            part.layer = layer;
            part.transform.SetParent(parent, false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        // Builds the spruces and shoots in the world's vegetation colours, again when a new world brings new settings.
        bool FoodLooksReady(out Material material)
        {
            TerrainGenerator terrain = terrainWorld != null ? terrainWorld.terrainGenerator : null;
            TerrainEnvironmentSampler sampler = terrainWorld != null ? terrainWorld.Sampler : null;
            VegetationSettings vegetation = sampler != null ? sampler.VegetationSettings : null;
            material = terrain != null ? terrain.vegetationMaterial : null;
            if (material == null || vegetation == null) return false;

            if (foodLooksBuiltFor != vegetation || foodSpruces == null || foodSpruces[0] == null)
            {
                DestroyMeshes(foodSpruces);
                DestroyMeshes(shootClumps);
                System.Random random = new System.Random(1729);
                foodSpruces = new Mesh[FoodSpruceVariants];
                for (int i = 0; i < FoodSpruceVariants; i++)
                {
                    foodSpruces[i] = VegetationMeshBuilder.BuildTree(vegetation.trunkColour,
                        i % 2 == 0 ? vegetation.foliageColourA : vegetation.foliageColourB, random, FoodSpruceHeight);
                }

                shootClumps = new Mesh[FoodSpruceVariants];
                for (int i = 0; i < FoodSpruceVariants; i++)
                {
                    shootClumps[i] = VegetationMeshBuilder.BuildShoots(vegetation.freshShootColour, random);
                }

                foodLooksBuiltFor = vegetation;
            }

            return true;
        }

        static void DestroyMeshes(Mesh[] meshes)
        {
            if (meshes == null) return;
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null) Destroy(mesh);
            }
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
                if (site.tree != null)
                {
                    Destroy(site.tree);
                    site.tree = null;
                }
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

        // Copied settings for sea food, with the sea map's rules for this world, so the background thread never reads
        // the component or the scene.
        private struct SeaFoodSettings
        {
            public bool enabled;
            public SeaBiomeClassifier biomes;
            public SeaFoodRules rules;
            public float waterLevel;
        }

        // Runs on a background thread. Cells are fixed in world space, so a tile's plants are the same
        // whenever it loads for the same seed.
        static PlantCandidates FindPlantCandidates(int version, Vector2Int coordinate, TerrainEnvironmentSampler sampler,
            int seed, float size, float spacing, float minimumGrass, float extent, float maximumSlope, bool excludeShore,
            TallFoodRules tallFood, SeaFoodSettings sea)
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
                        sampler.TrySample(position, out EnvironmentSample environment))
                    {
                        if (AnimalTerrainWorld.IsWalkable(environment, maximumSlope, excludeShore) &&
                            environment.grassBiomass >= minimumGrass &&
                            Hash01(seed, cellX, cellZ, 2) < environment.grassBiomass)
                        {
                            candidates.Add(environment, 0f);
                        }
                        else if (sea.enabled)
                        {
                            // On water, the same spot can hold a sea plant instead, as often as its biome allows.
                            SeaBiome biome = sea.biomes.Classify(environment, out float depth, out float celsius);
                            if (biome != SeaBiome.None && Hash01(seed, cellX, cellZ, 7) < sea.rules.Yield(biome).chance)
                            {
                                candidates.AddSea(new Vector3(position.x, sea.waterLevel, position.y), biome, depth,
                                                  celsius);
                            }
                        }
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

        // Expected nutrition regrowing per simulated minute on one plant cell (siteSpacing across) with this
        // environment and temperature, by the same rules that place and regrow plants. The survivability
        // heatmap uses it to judge how much food an area supplies.
        public float EstimateNutritionPerMinute(EnvironmentSample environment, float celsius)
        {
            FoodItem growthProfile = foodPrefab != null ? foodPrefab.GetComponent<FoodItem>() : null;
            float growthRate = growthProfile != null ? growthProfile.EvaluateGrowthMultiplier(celsius) : 1f;
            if (growthRate < minimumGrowthRate)
            {
                return 0f;
            }

            // The chance of a ground plant and, in woods, of leaves up a tree, weighted by their nutrition.
            float ground = environment.grassBiomass >= minimumGrassBiomass ? Mathf.Clamp01(environment.grassBiomass) : 0f;
            float tall = growTallFood && environment.treeCover >= minimumTreeCover
                ? tallFoodChance * Mathf.Clamp01(environment.treeCover) * tallFoodNutritionMultiplier
                : 0f;
            float nutrition = baseNutrition * Mathf.Lerp(moistureNutritionMultiplier.x, moistureNutritionMultiplier.y,
                                                         environment.moisture);
            return (ground + tall) * nutrition * growthRate * 60f / baseRegrowSeconds;
        }

        // The same for a water cell in the given sea biome, by the rules that place and regrow sea plants. The
        // temperature is the water surface's, which is what sea plants grow in.
        public float EstimateSeaNutritionPerMinute(SeaBiome biome, float surfaceCelsius)
        {
            if (!growSeaFood || biome == SeaBiome.None) return 0f;

            float growthRate = seaFood.GrowthRate(surfaceCelsius);
            if (growthRate < minimumGrowthRate) return 0f;

            SeaFoodYield yield = seaFood.Yield(biome);
            return yield.chance * baseNutrition * yield.nutrition * growthRate * 60f /
                   (baseRegrowSeconds * Mathf.Max(0.1f, yield.regrowth));
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
}
