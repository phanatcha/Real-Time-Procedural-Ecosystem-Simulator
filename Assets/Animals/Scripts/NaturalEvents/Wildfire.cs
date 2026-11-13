using System;
using System.Collections.Generic;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEngine;

namespace Boreal.Animals
{
    // How often wildfires start, where, and what a burn leaves behind (item 79).
    [Serializable]
    public sealed class WildfireSettings
    {
        [Tooltip("Wildfires can start. Off, neither random nor god-tool fires burn.")]
        public bool enabled = true;
        [Tooltip("Lightning strikes on land with something to burn, per simulated year at full fire danger. Fire " +
                 "danger is dryness squared, times 0.4 to 1 for warmth, so it is rarely above 0.4; many strikes land " +
                 "where the fuel is too damp to catch.")]
        [Min(0f)] public float strikesPerYear = 45f;
        [Tooltip("Simulated seconds from the world's start before lightning can start a fire (god tools can at any " +
                 "time). A year (400 s) lets the founders settle: the seasons are still easing in then, so the first " +
                 "fall stays dry into winter, and a fire among three founders can end a world.")]
        [Min(0f)] public float firstFireAfter = 400f;
        [Tooltip("A natural fire starts within this many metres of a random live animal (or the camera), so fires " +
                 "happen where the simulation is.")]
        [Min(0f)] public float strikeRadius = 500f;
        [Tooltip("A strike starts a fire only where the fuel's flammability is at least this, and then with the " +
                 "flammability times Strike Catch as the chance.")]
        [Range(0f, 1f)] public float minimumStrikeFlammability = 0.05f;
        [Tooltip("The chance a strike on fuel starts a fire, per unit of flammability (at most 1).")]
        [Min(0f)] public float strikeCatch = 4f;
        [Tooltip("Day temperatures in °C between which warmth raises fire danger from 0.4 to 1 of its dry value.")]
        public Vector2 dangerCelsius = new Vector2(-2f, 16f);
        [Tooltip("Simulated seconds before ground plants grow back on burned ground (400 is a year), so a fire in " +
                 "late summer leaves the ground bare until about winter, and plants are back the next spring.")]
        [Min(0f)] public float groundRecoverySeconds = 150f;
        [Tooltip("Simulated seconds before the shoots on burned spruces grow back.")]
        [Min(0f)] public float forestRecoverySeconds = 1200f;
        public WildfireRules rules = new WildfireRules();

        // 0 to 1: dryness squared, times 0.4 to 1 for warmth. NaN temperatures count as mild.
        public float Danger(in NaturalConditions conditions)
        {
            float warmth = float.IsNaN(conditions.celsius) ? 0.5f
                : Mathf.InverseLerp(dangerCelsius.x, dangerCelsius.y, conditions.celsius);
            float dryness = Mathf.Clamp01(conditions.dryness);
            return dryness * dryness * Mathf.Lerp(0.4f, 1f, warmth);
        }
    }

    // The terrain as fuel: trees and grass from the environment sample, moisture and snow from the season.
    public sealed class TerrainFireLandscape : IFireLandscape
    {
        public float Extent
        {
            get
            {
                AnimalTerrainWorld world = AnimalTerrainWorld.Active;
                return world != null ? world.WorldRadius : 0f;
            }
        }

        public bool TrySampleFuel(Vector2 position, float dryness, out FireFuel fuel)
        {
            fuel = default;
            AnimalTerrainWorld world = AnimalTerrainWorld.Active;
            if (world == null) return false;
            Vector3 point = new Vector3(position.x, 0f, position.y);
            if (!world.TryGetCachedSample(point, out EnvironmentSample environment) &&
                !world.TryGetSample(position, out environment))
            {
                return false;
            }
            if (!environment.isValid) return false;
            SeasonState season = SeasonClock.Current;
            SeasonalEnvironmentSample seasonal = SeasonalEnvironment.Evaluate(environment, season);
            FireGround ground = new FireGround
            {
                isLand = environment.isLand && !environment.isWater,
                treeCover = environment.treeCover,
                grassBiomass = environment.grassBiomass,
                rockDensity = environment.rockDensity,
                moisture = seasonal.IsValid ? seasonal.moisture : environment.moisture,
                wetness = Mathf.Max(Mathf.Max(environment.riverStrength, environment.lakeStrength),
                                    environment.isShore ? 0.4f : 0f),
                snowCover = seasonal.snowCover,
                dormancy = season.Validated().dormancy,
                height = environment.height
            };
            fuel = WildfireRules.Fuel(ground, dryness);
            return true;
        }
    }

    // The wildfire kind: its grid, its fires and what they do to plants and animals.
    public sealed class WildfireKind : NaturalEventKind
    {
        // For tests and scenes without terrain: the landscape fires burn in. Unset, the terrain is used.
        public static IFireLandscape LandscapeOverride;

        readonly NaturalEventDirector director;
        readonly TerrainFireLandscape terrain = new TerrainFireLandscape();
        readonly List<WildfireEvent> fires = new List<WildfireEvent>();
        readonly List<FoodItem> nearbyFood = new List<FoodItem>();
        WildfireGrid grid;
        float stepTimer;
        int nextId = 1;
        System.Random random;
        FoodSpawner foodSpawner;
        bool lookedForFood;
        SpeciesManager watchedManager;

        public WildfireKind(NaturalEventDirector director)
        {
            this.director = director;
        }

        public override string Name => "wildfire";
        public override string ProgressName => "wildfire-spread";
        public WildfireSettings Settings => director.wildfire;
        public WildfireGrid Grid => grid;
        public IReadOnlyList<WildfireEvent> Fires => fires;
        public IFireLandscape Landscape => LandscapeOverride ?? terrain;
        public int BurningFires
        {
            get
            {
                int count = 0;
                foreach (WildfireEvent fire in fires) if (!fire.IsOver) count++;
                return count;
            }
        }
        // Burning now and burned in this world, in hectares.
        public float BurningHectares => grid == null ? 0f : grid.BurningCount * grid.CellArea / 10000f;
        public float BurnedHectares => grid == null ? 0f : grid.EverBurnedCount * grid.CellArea / 10000f;
        public bool IsActive => grid != null && grid.IsActive;

        public float Danger(in NaturalConditions conditions) => Settings.Danger(conditions);
        // Lightning strikes in this world, and how many started a fire, for tuning.
        public int Strikes { get; private set; }
        public int StrikesCaught { get; private set; }
        // The flammability where the last strike landed.
        public float LastStrikeFlammability { get; private set; }

        public override float StartsPerSecond(in NaturalConditions conditions)
        {
            if (!Settings.enabled || Settings.strikesPerYear <= 0f || conditions.time < Settings.firstFireAfter) return 0f;
            return Settings.strikesPerYear * Settings.Danger(conditions) / Mathf.Max(1f, conditions.yearLength);
        }

        public override NaturalEvent TryStartAtRandom(in NaturalConditions conditions, System.Random random)
        {
            if (!EnsureGrid()) return null;
            // A strike on land with fuel: a few tries for a spot, since much of the island is water and bare rock.
            Vector3 around = RandomFocus(random);
            Vector2 strike = Vector2.zero;
            bool onFuel = false;
            for (int attempt = 0; attempt < 6 && !onFuel; attempt++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                float distance = Settings.strikeRadius * Mathf.Sqrt((float)random.NextDouble());
                strike = new Vector2(around.x + Mathf.Cos(angle) * distance, around.z + Mathf.Sin(angle) * distance);
                onFuel = grid.TryGetCell(strike, out int cell) && grid.StateOf(cell) == WildfireGrid.CellState.Unburned &&
                         Landscape.TrySampleFuel(strike, conditions.dryness, out FireFuel fuel) && fuel.burnable;
            }
            if (!onFuel) return null;
            float flammability = grid.FlammabilityAt(strike, conditions.time, conditions.dryness, Landscape);
            Strikes++;
            LastStrikeFlammability = flammability;
            if (flammability < Mathf.Max(0.0001f, Settings.minimumStrikeFlammability)) return null;
            if (random.NextDouble() >= Mathf.Clamp01(flammability * Settings.strikeCatch)) return null;
            WildfireEvent fire = Start(strike, 1, 0f, "lightning", conditions, random);
            if (fire != null) StrikesCaught++;
            return fire;
        }

        public override NaturalEvent StartAt(Vector3 position, float amount, float duration, string cause,
                                             in NaturalConditions conditions, System.Random random)
        {
            if (!Settings.enabled || !EnsureGrid()) return null;
            int points = Mathf.Clamp(Mathf.RoundToInt(amount), 1, 20);
            return Start(new Vector2(position.x, position.z), points, duration, cause, conditions, random);
        }

        WildfireEvent Start(Vector2 at, int points, float duration, string cause, in NaturalConditions conditions,
                            System.Random random)
        {
            int fireId = grid.NewFire();
            int lit = 0;
            for (int point = 0; point < points; point++)
            {
                // Further ignition points ring the first, 20 m apart.
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                Vector2 offset = point == 0 ? Vector2.zero
                    : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (20f * Mathf.Sqrt(point));
                if (grid.IgniteNear(at + offset, 40f, fireId, conditions.time, conditions.dryness, Landscape)) lit++;
            }
            if (lit == 0) return null;
            WildfireEvent fire = new WildfireEvent(this, nextId++, fireId, conditions.time,
                                                   new Vector3(at.x, 0f, at.y), cause, duration);
            fires.Add(fire);
            return fire;
        }

        Vector3 RandomFocus(System.Random random)
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager != null && manager.TotalPopulation > 0)
            {
                int pick = random.Next(manager.TotalPopulation);
                foreach (SeekFood animal in manager.ActiveAgents)
                {
                    if (animal != null && pick-- <= 0) return animal.transform.position;
                }
            }
            Camera camera = Camera.main;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        public override void Tick(float deltaTime, in NaturalConditions conditions)
        {
            if (grid == null) return;
            WatchDeaths();
            float step = Mathf.Max(0.05f, Settings.rules.stepSeconds);
            stepTimer += deltaTime;
            // Catch up at most a few steps per frame; a slow frame then only slows the fire.
            int steps = 0;
            if (grid.IsActive)
            {
                FireWeather weather = new FireWeather
                {
                    dryness = conditions.dryness, windDirection = conditions.windDirection,
                    windSpeed = conditions.windSpeed, daylight = conditions.daylight
                };
                while (stepTimer >= step && steps < 4)
                {
                    stepTimer -= step;
                    steps++;
                    float time = conditions.time - stepTimer;
                    grid.Step(step, time, weather, Landscape, random);
                }
            }
            if (stepTimer > step) stepTimer = step;

            foreach (WildfireEvent fire in fires)
            {
                if (fire.IsOver) continue;
                if (fire.StopAt > 0f && conditions.time >= fire.StopAt)
                {
                    fire.Stop(conditions.time, "put out at the end of its time");
                    continue;
                }
                fire.Refresh(conditions.time);
            }
            // The director keeps its own list and records the end.
            fires.RemoveAll(fire => fire.IsOver);
        }

        public override void ResetWorld()
        {
            Detach();
            if (grid != null) grid.CellIgnited -= OnCellIgnited;
            grid = null;
            foodSpawner = null;
            lookedForFood = false;
            fires.Clear();
            nextId = 1;
            Strikes = StrikesCaught = 0;
            stepTimer = 0f;
            random = null;
            FoodSpawner.DisturbedUntil = null;
        }

        // Stops listening to the world's animals; the next step listens again.
        public void Detach()
        {
            if (watchedManager != null) watchedManager.AnimalDied -= OnAnimalDied;
            watchedManager = null;
        }

        public void Extinguish(WildfireEvent fire, float time) => grid?.Extinguish(fire.FireId, time);

        bool EnsureGrid()
        {
            if (grid != null) return true;
            float extent = Landscape.Extent;
            if (extent <= 0f) return false;
            grid = new WildfireGrid(Settings.rules, extent);
            grid.CellIgnited += OnCellIgnited;
            SpeciesManager manager = SpeciesManager.Instance;
            random = new System.Random(unchecked(SeedManagerSeed() * 31 + (manager != null ? manager.WorldRevision : 0)));
            FoodSpawner.DisturbedUntil = DisturbedUntil;
            return true;
        }

        static int SeedManagerSeed()
        {
            Boreal.Core.SeedManager seeds = Boreal.Core.SeedManager.Instance;
            return seeds != null ? seeds.GetSeed("Wildfire") : 1;
        }

        void WatchDeaths()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == watchedManager) return;
            if (watchedManager != null) watchedManager.AnimalDied -= OnAnimalDied;
            watchedManager = manager;
            if (manager != null) manager.AnimalDied += OnAnimalDied;
        }

        void OnAnimalDied(SeekFood animal, AgentDeathCause cause)
        {
            if (cause != AgentDeathCause.Fire || animal == null) return;
            WildfireEvent fire = FireNear(animal.transform.position);
            if (fire != null) fire.AnimalsKilled++;
        }

        // An animal running from a fire, counted once per fire.
        public void NoteFlight(SeekFood animal)
        {
            if (animal == null) return;
            WildfireEvent fire = FireNear(animal.transform.position);
            fire?.NoteFlight(animal.AnimalId);
        }

        // The fire that reached the cells around a point, or the most recent running one.
        WildfireEvent FireNear(Vector3 position)
        {
            if (grid == null) return null;
            int found = 0;
            if (grid.TryGetCell(new Vector2(position.x, position.z), out int centre))
            {
                int size = grid.size;
                int cx = centre % size, cz = centre / size;
                for (int ring = 0; ring <= 6 && found == 0; ring++)
                for (int dz = -ring; dz <= ring && found == 0; dz++)
                for (int dx = -ring; dx <= ring && found == 0; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= size || z >= size) continue;
                    found = grid.FireOf(z * size + x);
                }
            }
            WildfireEvent latest = null;
            foreach (WildfireEvent fire in fires)
            {
                if (found != 0 && fire.FireId == found) return fire;
                if (!fire.IsOver) latest = fire;
            }
            return latest;
        }

        // A cell catching fire burns the plants in it: ground plants and forest shoots stay gone for a while.
        void OnCellIgnited(int index, int fireId)
        {
            Vector2 centre = grid.CellCentre(index);
            float half = grid.cellSize * 0.5f;
            int burned = 0;
            if (foodSpawner == null && !lookedForFood)
            {
                foodSpawner = UnityEngine.Object.FindAnyObjectByType<FoodSpawner>();
                lookedForFood = true;
            }
            float now = Time.time;
            if (foodSpawner != null)
            {
                burned += foodSpawner.Disturb(new Rect(centre.x - half, centre.y - half, grid.cellSize, grid.cellSize),
                                              now + Settings.groundRecoverySeconds, now + Settings.forestRecoverySeconds);
            }
            // Plants that belong to no site (dropped by god tools, or placed by tests) burn too.
            FoodItem.FindNearby(new Vector3(centre.x, 0f, centre.y), half * 1.5f, nearbyFood);
            foreach (FoodItem food in nearbyFood)
            {
                if (food == null || food.foodType != FoodType.Plant || !food.IsAvailable) continue;
                Vector3 p = food.transform.position;
                if (Mathf.Abs(p.x - centre.x) > half || Mathf.Abs(p.z - centre.y) > half) continue;
                UnityEngine.Object.Destroy(food.gameObject);
                burned++;
            }
            if (burned == 0) return;
            foreach (WildfireEvent fire in fires)
            {
                if (fire.FireId == fireId)
                {
                    fire.PlantsBurned += burned;
                    break;
                }
            }
        }

        // When a plant site at this position may grow again (simulated Time.time), for tiles loaded after a fire.
        float DisturbedUntil(Vector3 position, bool tall)
        {
            if (grid == null) return 0f;
            float ignited = grid.IgnitedAt(new Vector2(position.x, position.z));
            if (ignited < 0f) return 0f;
            SpeciesManager manager = SpeciesManager.Instance;
            float worldStart = manager != null ? manager.WorldStartTime : 0f;
            return worldStart + ignited + (tall ? Settings.forestRecoverySeconds : Settings.groundRecoverySeconds);
        }

        // What an animal at this point feels: radiant heat (0 to about 1.5) and smoke (0 to 1).
        public void Exposure(Vector3 position, out float heat, out float smoke)
        {
            heat = smoke = 0f;
            if (grid == null || !grid.IsActive) return;
            Vector2 at = new Vector2(position.x, position.z);
            heat = grid.HeatAt(at);
            smoke = grid.SmokeAt(at);
        }

        public bool Sense(Vector3 position, float radius, out float distance, out Vector3 away, out float heat)
        {
            distance = float.PositiveInfinity;
            away = Vector3.zero;
            heat = 0f;
            if (grid == null || grid.BurningCount == 0) return false;
            bool found = grid.Sense(new Vector2(position.x, position.z), radius, out distance, out Vector2 flat, out heat);
            away = new Vector3(flat.x, 0f, flat.y);
            return found;
        }

        public bool IsBurningAt(Vector3 position) =>
            grid != null && grid.StateAt(new Vector2(position.x, position.z)) == WildfireGrid.CellState.Burning;
    }

    // One wildfire, from the strike or the god tool that lit it to its last burning cell.
    public sealed class WildfireEvent : NaturalEvent
    {
        readonly WildfireKind kind;
        readonly HashSet<int> fled = new HashSet<int>();
        string endReason = "burned out";

        public int FireId { get; }
        // Simulated seconds since the world began when it is put out; 0 lets it burn out.
        public float StopAt { get; }
        public int AnimalsKilled { get; set; }
        public int AnimalsFled => fled.Count;
        public int PlantsBurned { get; set; }

        public WildfireEvent(WildfireKind kind, int id, int fireId, float startTime, Vector3 origin, string cause,
                             float duration)
            : base(kind, id, startTime, origin, cause)
        {
            this.kind = kind;
            FireId = fireId;
            StopAt = duration > 0f ? startTime + duration : 0f;
        }

        public WildfireGrid.FireTally Tally => kind.Grid != null ? kind.Grid.TallyOf(FireId) : default;
        public float BurnedHectares
        {
            get
            {
                WildfireGrid.FireTally tally = Tally;
                return kind.Grid == null ? 0f : (tally.burning + tally.burned) * kind.Grid.CellArea / 10000f;
            }
        }

        public void NoteFlight(int animalId) => fled.Add(animalId);

        public void Refresh(float time)
        {
            if (!IsOver && Tally.burning <= 0) EndTime = time;
        }

        public override void Stop(float time, string reason)
        {
            if (IsOver) return;
            endReason = reason;
            kind.Extinguish(this, time);
            EndTime = time;
        }

        public override string DescribeStart(in NaturalConditions conditions)
        {
            string fuel = "grass";
            if (kind.Grid != null && kind.Grid.TryGetCell(new Vector2(Origin.x, Origin.z), out int index) &&
                kind.Grid.TryGetFuel(index, out FireFuel found) && found.crown >= 0.5f)
            {
                fuel = "forest";
            }
            string celsius = float.IsNaN(conditions.celsius) ? "unknown temperature" : $"{conditions.celsius:0} °C";
            return $"fire {Id} started by {Cause} in {fuel} at x {Origin.x:0} z {Origin.z:0} in " +
                   $"{conditions.seasonName.ToLowerInvariant()}, {celsius}, dryness {conditions.dryness:0.00}, " +
                   $"fire danger {kind.Danger(conditions):0.00}, wind {conditions.windSpeed:0.0} m/s towards " +
                   $"{conditions.WindTowards}";
        }

        public override string DescribeProgress(float time)
        {
            WildfireGrid.FireTally tally = Tally;
            return $"fire {Id}: {tally.burning} cells burning, {BurnedHectares:0.0} ha burned, " +
                   $"{AnimalsKilled} animals killed, {AnimalsFled} fled so far";
        }

        public override string DescribeEnd()
        {
            WildfireGrid.FireTally tally = Tally;
            float duration = Mathf.Max(0f, EndTime - StartTime);
            return $"fire {Id} {endReason} after {duration:0} s: {BurnedHectares:0.0} ha burned, peak " +
                   $"{tally.peakBurning} cells burning, {AnimalsKilled} animals killed, {AnimalsFled} fled, " +
                   $"{PlantsBurned} plants burned";
        }
    }

    // Shortcuts for the animals and the HUD to the running director's wildfires.
    public static class Wildfires
    {
        public static WildfireKind Current =>
            NaturalEventDirector.Active != null ? NaturalEventDirector.Active.Wildfire : null;

        // Fires burn or their smoke lingers somewhere: only then do animals need to ask.
        public static bool IsActive
        {
            get
            {
                WildfireKind kind = Current;
                return kind != null && kind.IsActive;
            }
        }
    }
}
