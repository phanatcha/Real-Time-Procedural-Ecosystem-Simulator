using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boreal.Animals
{
    // What the ground at one spot offers a fire, from the terrain and the season (item 79).
    public struct FireGround
    {
        public bool isLand;
        public float treeCover;
        public float grassBiomass;
        public float rockDensity;
        // The terrain's moisture, 0 dry to 1 wet.
        public float moisture;
        // 0 to 1: rivers, lakes and bogs close by, which keep the ground wet.
        public float wetness;
        public float snowCover;
        // The season's dormancy: dead, cured grass that burns more easily.
        public float dormancy;
        public float height;
    }

    // The fuel in one cell.
    public struct FireFuel
    {
        public bool burnable;
        // 0 to 1: how much there is to burn, a dense forest 1, a meadow about 0.25.
        public float load;
        // The share of the load up in the trees: 0 a grass fire, 1 a crown fire with tall flames.
        public float crown;
        // 0 to 1. At or above the rules' extinction moisture it doesn't carry fire.
        public float moisture;
        public float height;
    }

    // Where the fire burns: the square [-Extent, Extent] on x and z, and the fuel at each spot.
    public interface IFireLandscape
    {
        float Extent { get; }
        // False outside the world, or where the ground can't be read yet.
        bool TrySampleFuel(Vector2 position, float dryness, out FireFuel fuel);
    }

    // A landscape from a function, for tests and scenes without terrain.
    public sealed class FireLandscapeFunction : IFireLandscape
    {
        readonly Func<Vector2, float, FireFuel?> sample;
        public float Extent { get; }

        public FireLandscapeFunction(float extent, Func<Vector2, float, FireFuel?> sample)
        {
            Extent = extent;
            this.sample = sample;
        }

        public bool TrySampleFuel(Vector2 position, float dryness, out FireFuel fuel)
        {
            FireFuel? found = sample(position, dryness);
            fuel = found ?? default;
            return found.HasValue;
        }
    }

    // The weather a fire step reads.
    public struct FireWeather
    {
        public float dryness;
        public Vector2 windDirection;
        public float windSpeed;
        public float daylight;
    }

    // How fuel burns and fire spreads. Every number is in simulated seconds and metres.
    [Serializable]
    public sealed class WildfireRules
    {
        [Tooltip("Side of a fire cell in metres. Fuel, burning and charring are kept per cell.")]
        [Min(2f)] public float cellSize = 8f;
        [Tooltip("Simulated seconds between fire steps.")]
        [Range(0.1f, 2f)] public float stepSeconds = 0.5f;
        [Tooltip("How likely fire is to pass to a neighbouring cell over the burning cell's whole burn, on the flat with " +
                 "no wind: the chance is 1 - e^-(this x the neighbour's flammability x the burning cell's peak " +
                 "intensity), more downwind and uphill. The passing is spread over the burn, so grass, which burns " +
                 "briefly, carries fire fast and forest slowly.")]
        [Min(0f)] public float spreadStrength = 9f;
        [Tooltip("How strongly wind pushes the fire, per m/s: downwind spread is multiplied by e^(this x speed), " +
                 "upwind divided by it. Crown fires feel it half as much again.")]
        [Min(0f)] public float windCoupling = 0.12f;
        [Tooltip("How much faster fire runs uphill: spread is multiplied by e^(this x rise over run).")]
        [Min(0f)] public float slopeCoupling = 2.5f;
        [Tooltip("Fuel moisture at which fuel stops carrying fire.")]
        [Range(0.1f, 1f)] public float extinctionMoisture = 0.7f;
        [Tooltip("How steeply moisture damps flammability: (1 - moisture / extinction moisture) to this power.")]
        [Range(0.5f, 4f)] public float moistureDamping = 2f;
        [Tooltip("Spread at night, as a share of spread by day: the air is damper and the wind calmer.")]
        [Range(0f, 1f)] public float nightSpread = 0.45f;
        [Tooltip("Simulated seconds a cell of grass burns for.")]
        [Min(1f)] public float grassBurnSeconds = 6f;
        [Tooltip("Simulated seconds a cell of dense forest burns for.")]
        [Min(1f)] public float forestBurnSeconds = 20f;
        [Tooltip("Wind speed in m/s above which burning trees throw embers that start spot fires downwind.")]
        [Min(0f)] public float spottingWindSpeed = 7f;
        [Tooltip("Spot fires thrown per second by a fully burning crown, per m/s of wind above the spotting speed.")]
        [Min(0f)] public float spottingRate = 0.004f;
        [Tooltip("How far embers fly, in cells.")]
        public Vector2Int spottingCells = new Vector2Int(3, 9);
        [Tooltip("Simulated seconds smoke takes to thin to a third, once its fire stops feeding it.")]
        [Min(1f)] public float smokeLifetime = 25f;
        [Tooltip("Smoke a fully burning cell of dense forest adds to its 32 m block per second; a block's smoke is " +
                 "felt in full at 1.")]
        [Min(0f)] public float smokeEmission = 0.03f;
        [Tooltip("New fuel cells read from the ground per step at most, so a large front spreads its cost over frames.")]
        [Min(16)] public int maximumSamplesPerStep = 400;

        // Less fuel than this (bare rock, sand) doesn't carry fire.
        public const float MinimumLoad = 0.03f;

        // Fuel from the ground: load from trees and grass (less where rock shows), moisture from the ground's own
        // moisture, how dry the weather has been, water close by and snow. Dormant grass is cured and drier.
        public static FireFuel Fuel(in FireGround ground, float dryness)
        {
            float grass = 0.25f * Mathf.Clamp01(ground.grassBiomass);
            float trees = 0.85f * Mathf.Clamp01(ground.treeCover);
            float load = Mathf.Clamp01(grass + trees) * (1f - 0.8f * Mathf.Clamp01(ground.rockDensity));
            float crown = grass + trees > 0.0001f ? trees / (grass + trees) : 0f;
            float moisture = 0.2f * Mathf.Clamp01(ground.moisture) + 0.6f * (1f - Mathf.Clamp01(dryness)) +
                             0.9f * Mathf.Clamp01(ground.wetness) + 1.6f * Mathf.Clamp01(ground.snowCover) -
                             0.15f * Mathf.Clamp01(ground.dormancy) * (1f - crown);
            return new FireFuel
            {
                burnable = ground.isLand && load > MinimumLoad,
                load = load,
                crown = crown,
                moisture = Mathf.Clamp01(moisture),
                height = ground.height
            };
        }

        // 0 to about 1.35 (typically 0.05 to 0.3 on the island in summer): how readily this fuel catches. Fine fuels (grass) catch faster than heavy ones, and moisture
        // damps it to nothing at the extinction moisture.
        public float Flammability(in FireFuel fuel)
        {
            if (!fuel.burnable || fuel.load <= MinimumLoad) return 0f;
            float dry = 1f - fuel.moisture / Mathf.Max(0.01f, extinctionMoisture);
            if (dry <= 0f) return 0f;
            float fineness = Mathf.Lerp(1.35f, 0.85f, fuel.crown);
            return Mathf.Pow(fuel.load, 0.4f) * fineness * Mathf.Pow(dry, moistureDamping);
        }

        public float BurnSeconds(in FireFuel fuel)
        {
            return Mathf.Lerp(grassBurnSeconds, forestBurnSeconds, fuel.crown) * (0.6f + 0.4f * Mathf.Clamp01(fuel.load));
        }

        // Intensity over a cell's burn, 0 to its peak: it flares up over the first fifth and dies down to embers.
        public static float Intensity(float burnFraction, in FireFuel fuel) => PeakIntensity(fuel) * BurnShape(burnFraction);

        // Denser fuel burns hotter: 0.35 for the lightest to 1 for dense forest.
        public static float PeakIntensity(in FireFuel fuel) => 0.35f + 0.65f * Mathf.Clamp01(fuel.load);

        // 0 to 1 over the burn; its average over the burn is BurnShapeMean.
        public static float BurnShape(float burnFraction)
        {
            float x = Mathf.Clamp01(burnFraction);
            return x < 0.2f ? x / 0.2f : Mathf.Pow(1f - (x - 0.2f) / 0.8f, 0.7f);
        }

        public const float BurnShapeMean = 0.1f + 0.8f / 1.7f;

        // How much faster fire runs in a direction (unit, x and z) with this wind: e^(coupling x speed x cos angle).
        public float WindFactor(Vector2 direction, in FireWeather weather, float crown)
        {
            float alignment = Vector2.Dot(direction, weather.windDirection);
            return Mathf.Exp(windCoupling * (1f + 0.5f * crown) * weather.windSpeed * alignment);
        }

        public float SlopeFactor(float rise, float run)
        {
            return Mathf.Clamp(Mathf.Exp(slopeCoupling * rise / Mathf.Max(0.01f, run)), 0.35f, 3f);
        }

        public float DaylightFactor(float daylight) => Mathf.Lerp(nightSpread, 1f, Mathf.Clamp01(daylight));
    }

    // The fire cells of one world (item 79): fuel read lazily at the fire front, cells that burn, spread to their
    // neighbours with the wind and the slope, and burn out, and the smoke they give off. Pure C#, so the spread rules
    // can be tested without a scene. Several fires share one grid, so they can run into each other; each cell remembers
    // which fire reached it first.
    public sealed class WildfireGrid
    {
        public enum CellState : byte { Unburned, Burning, Burned }

        struct Cell
        {
            public int index;
            public FireFuel fuel;
            public float sampledAt;
            public float burnStart;
            public float burnDuration;
            public float intensity;
            public float ignitedAt;
            public float burnedOutAt;
            public int fireId;
        }

        public struct FireTally
        {
            public int burning;
            public int burned;
            public int peakBurning;
            public float lastIgnition;
        }

        // Smoke and danger are kept in blocks of this many cells a side.
        public const int BlockCells = 4;
        const float FuelResampleSeconds = 30f;
        static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1)
        };

        public readonly WildfireRules rules;
        public readonly float cellSize;
        public readonly float extent;
        public readonly int size;
        readonly CellState[] states;
        // 0 for a cell never read; otherwise one more than its place in cells.
        readonly int[] slots;
        readonly List<Cell> cells = new List<Cell>();
        readonly List<int> burning = new List<int>();
        readonly List<FireTally> tallies = new List<FireTally> { default };

        public readonly int blocksPerSide;
        readonly float[] smoke;
        readonly float[] smokeNext;
        readonly float[] blockHeat;
        readonly float[] blockX;
        readonly float[] blockZ;
        readonly List<int> heatedBlocks = new List<int>();
        RectInt smokeArea;
        bool hasSmoke;
        int samplesThisStep;

        // Raised as a cell catches fire and as it burns out, with the cell's index and its fire.
        public event Action<int, int> CellIgnited;
        public event Action<int, int> CellBurnedOut;

        public int BurningCount => burning.Count;
        // Cells that have caught fire in this world, burning or burned out.
        public int EverBurnedCount { get; private set; }
        public bool HasSmoke => hasSmoke;
        public bool IsActive => burning.Count > 0 || hasSmoke;
        public float CellArea => cellSize * cellSize;

        public WildfireGrid(WildfireRules rules, float extent)
        {
            this.rules = rules ?? new WildfireRules();
            cellSize = Mathf.Max(2f, this.rules.cellSize);
            this.extent = Mathf.Max(cellSize, extent);
            size = Mathf.Max(1, Mathf.CeilToInt(2f * this.extent / cellSize));
            states = new CellState[size * size];
            slots = new int[size * size];
            blocksPerSide = Mathf.CeilToInt(size / (float)BlockCells);
            int blocks = blocksPerSide * blocksPerSide;
            smoke = new float[blocks];
            smokeNext = new float[blocks];
            blockHeat = new float[blocks];
            blockX = new float[blocks];
            blockZ = new float[blocks];
        }

        public bool TryGetCell(Vector2 position, out int index)
        {
            int x = Mathf.FloorToInt((position.x + extent) / cellSize);
            int z = Mathf.FloorToInt((position.y + extent) / cellSize);
            index = z * size + x;
            return x >= 0 && z >= 0 && x < size && z < size;
        }

        public Vector2 CellCentre(int index)
        {
            int x = index % size;
            int z = index / size;
            return new Vector2(-extent + (x + 0.5f) * cellSize, -extent + (z + 0.5f) * cellSize);
        }

        public CellState StateOf(int index) => states[index];

        public CellState StateAt(Vector2 position) =>
            TryGetCell(position, out int index) ? states[index] : CellState.Unburned;

        // When the cell at this point caught fire, or a negative number if it never has.
        public float IgnitedAt(Vector2 position)
        {
            if (!TryGetCell(position, out int index) || states[index] == CellState.Unburned) return -1f;
            return cells[slots[index] - 1].ignitedAt;
        }

        public float IgnitedAt(int index) => states[index] == CellState.Unburned ? -1f : cells[slots[index] - 1].ignitedAt;

        public int FireOf(int index) => states[index] == CellState.Unburned ? 0 : cells[slots[index] - 1].fireId;

        public FireTally TallyOf(int fireId) =>
            fireId > 0 && fireId < tallies.Count ? tallies[fireId] : default;

        public bool TryGetFuel(int index, out FireFuel fuel)
        {
            int slot = slots[index];
            fuel = slot > 0 ? cells[slot - 1].fuel : default;
            return slot > 0;
        }

        // The burning cells, for drawing: the cell's centre, its intensity now and its fuel.
        public int BurningCellAt(int burningIndex, out Vector2 centre, out float intensity, out FireFuel fuel)
        {
            Cell cell = cells[burning[burningIndex]];
            centre = CellCentre(cell.index);
            intensity = cell.intensity;
            fuel = cell.fuel;
            return cell.index;
        }

        // When a cell caught fire, when it burned out (negative while burning) and how long its burn lasts, for drawing.
        public bool TryGetTimes(int index, out float ignitedAt, out float burnedOutAt, out float burnDuration)
        {
            ignitedAt = burnedOutAt = -1f;
            burnDuration = 0f;
            if (states[index] == CellState.Unburned) return false;
            Cell cell = cells[slots[index] - 1];
            ignitedAt = cell.ignitedAt;
            burnedOutAt = cell.burnedOutAt;
            burnDuration = cell.burnDuration;
            return true;
        }

        // The 32 m blocks with fire in them at the last step: each block's centre of heat and its heat (the sum of its
        // burning cells' intensities), for drawing smoke and placing firelight.
        public int HeatedBlockCount => heatedBlocks.Count;

        public void GetHeatedBlock(int heatedIndex, out Vector2 centre, out float heat)
        {
            int block = heatedBlocks[heatedIndex];
            heat = blockHeat[block];
            centre = heat > 0f ? new Vector2(blockX[block] / heat, blockZ[block] / heat) : Vector2.zero;
        }

        public float IntensityOf(int index) =>
            states[index] == CellState.Burning ? cells[slots[index] - 1].intensity : 0f;

        // A new fire's ID, for IgniteNear. IDs count from 1 in each grid.
        public int NewFire()
        {
            tallies.Add(default);
            return tallies.Count - 1;
        }

        // Sets fire to the most flammable unburned cell within searchRadius of the position, nearest first, if its
        // flammability is at least minimumFlammability (and above 0). Returns false when nothing there can burn.
        public bool IgniteNear(Vector2 position, float searchRadius, int fireId, float time, float dryness,
                               IFireLandscape landscape, float minimumFlammability = 0f)
        {
            if (fireId <= 0 || fireId >= tallies.Count) return false;
            int best = -1;
            float bestFlammability = Mathf.Max(0f, minimumFlammability);
            int reach = Mathf.Max(0, Mathf.CeilToInt(searchRadius / cellSize));
            int cx = Mathf.FloorToInt((position.x + extent) / cellSize);
            int cz = Mathf.FloorToInt((position.y + extent) / cellSize);
            // Nearer cells first, so a tie keeps the fire close to where it was asked for.
            for (int ring = 0; ring <= reach && best < 0; ring++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= size || z >= size) continue;
                    int index = z * size + x;
                    if (states[index] != CellState.Unburned) continue;
                    if (!TrySample(index, time, dryness, landscape, true, out FireFuel fuel)) continue;
                    float flammability = rules.Flammability(fuel);
                    if (flammability > bestFlammability || (best < 0 && flammability > 0f && flammability >= bestFlammability))
                    {
                        bestFlammability = flammability;
                        best = index;
                    }
                }
            }
            if (best < 0) return false;
            Ignite(best, fireId, time);
            return true;
        }

        // The flammability of the cell at this point now, reading its fuel if need be.
        public float FlammabilityAt(Vector2 position, float time, float dryness, IFireLandscape landscape)
        {
            if (!TryGetCell(position, out int index) || states[index] != CellState.Unburned) return 0f;
            return TrySample(index, time, dryness, landscape, true, out FireFuel fuel) ? rules.Flammability(fuel) : 0f;
        }

        // Puts a fire out at once: its burning cells count as burned.
        public void Extinguish(int fireId, float time)
        {
            for (int i = burning.Count - 1; i >= 0; i--)
            {
                Cell cell = cells[burning[i]];
                if (fireId > 0 && cell.fireId != fireId) continue;
                BurnOut(i, time);
            }
        }

        public void Step(float deltaTime, float time, in FireWeather weather, IFireLandscape landscape, System.Random random)
        {
            samplesThisStep = 0;
            float daylight = rules.DaylightFactor(weather.daylight);
            // Cells that catch fire this step start burning next step, so the order of the burning list can't let a
            // fire cross several cells in one step.
            int burningAtStart = burning.Count;
            for (int i = burningAtStart - 1; i >= 0; i--)
            {
                int slot = burning[i];
                Cell cell = cells[slot];
                float fraction = (time - cell.burnStart) / Mathf.Max(0.01f, cell.burnDuration);
                if (fraction >= 1f)
                {
                    BurnOut(i, time);
                    continue;
                }
                cell.intensity = WildfireRules.Intensity(fraction, cell.fuel);
                cells[slot] = cell;
                Spread(cell, WildfireRules.BurnShape(fraction), deltaTime, time, weather, daylight, landscape, random);
            }
            for (int i = 0; i < burning.Count; i++) RecountPeak(cells[burning[i]].fireId);
            UpdateBlocks(deltaTime, weather);
        }

        // The chance of passing fire over the whole burn is set by the strength, and the passing is spread over the burn
        // in proportion to the burn's shape, so its hazard now is strength x shape / (mean shape x burn length).
        void Spread(Cell source, float shape, float deltaTime, float time, in FireWeather weather, float daylight,
                    IFireLandscape landscape, System.Random random)
        {
            int sx = source.index % size;
            int sz = source.index / size;
            float strength = rules.spreadStrength * WildfireRules.PeakIntensity(source.fuel) * daylight * shape /
                             (WildfireRules.BurnShapeMean * Mathf.Max(0.5f, source.burnDuration));
            if (strength <= 0f) return;
            foreach (Vector2Int offset in Neighbours)
            {
                int x = sx + offset.x, z = sz + offset.y;
                if (x < 0 || z < 0 || x >= size || z >= size) continue;
                int index = z * size + x;
                if (states[index] != CellState.Unburned) continue;
                if (!TrySample(index, time, weather.dryness, landscape, false, out FireFuel fuel)) continue;
                float flammability = rules.Flammability(fuel);
                if (flammability <= 0f) continue;
                bool diagonal = offset.x != 0 && offset.y != 0;
                float run = diagonal ? cellSize * 1.41421356f : cellSize;
                Vector2 direction = new Vector2(offset.x, offset.y).normalized;
                float rate = strength * flammability * rules.WindFactor(direction, weather, source.fuel.crown) *
                             rules.SlopeFactor(fuel.height - source.fuel.height, run) * (cellSize / run);
                float chance = 1f - Mathf.Exp(-rate * deltaTime);
                if (random.NextDouble() < chance) Ignite(index, source.fireId, time);
            }

            // Embers from burning crowns in a strong wind start spot fires downwind.
            float excessWind = weather.windSpeed - rules.spottingWindSpeed;
            if (excessWind <= 0f || source.fuel.crown < 0.3f || rules.spottingRate <= 0f) return;
            float spotChance = rules.spottingRate * source.intensity * source.fuel.crown * excessWind * deltaTime * daylight;
            if (random.NextDouble() >= spotChance) return;
            int lowest = Mathf.Max(1, Mathf.Min(rules.spottingCells.x, rules.spottingCells.y));
            int highest = Mathf.Max(lowest, Mathf.Max(rules.spottingCells.x, rules.spottingCells.y));
            float distance = (lowest + (float)random.NextDouble() * (highest - lowest)) * cellSize;
            float angle = ((float)random.NextDouble() - 0.5f) * 50f * Mathf.Deg2Rad;
            Vector2 wind = weather.windDirection;
            Vector2 heading = new Vector2(wind.x * Mathf.Cos(angle) - wind.y * Mathf.Sin(angle),
                                          wind.x * Mathf.Sin(angle) + wind.y * Mathf.Cos(angle));
            if (!TryGetCell(CellCentre(source.index) + heading * distance, out int landing)) return;
            if (states[landing] != CellState.Unburned) return;
            if (!TrySample(landing, time, weather.dryness, landscape, false, out FireFuel landingFuel)) return;
            if (random.NextDouble() < Mathf.Clamp01(rules.Flammability(landingFuel))) Ignite(landing, source.fireId, time);
        }

        bool TrySample(int index, float time, float dryness, IFireLandscape landscape, bool force, out FireFuel fuel)
        {
            int slot = slots[index];
            if (slot > 0)
            {
                Cell cell = cells[slot - 1];
                if (states[index] != CellState.Unburned || time - cell.sampledAt < FuelResampleSeconds)
                {
                    fuel = cell.fuel;
                    return true;
                }
            }
            fuel = default;
            if (landscape == null) return false;
            if (!force && samplesThisStep >= rules.maximumSamplesPerStep) return false;
            samplesThisStep++;
            if (!landscape.TrySampleFuel(CellCentre(index), dryness, out fuel))
            {
                fuel = default;
            }
            if (slot == 0)
            {
                cells.Add(new Cell { index = index, fuel = fuel, sampledAt = time, ignitedAt = -1f, burnedOutAt = -1f });
                slots[index] = cells.Count;
            }
            else
            {
                Cell cell = cells[slot - 1];
                cell.fuel = fuel;
                cell.sampledAt = time;
                cells[slot - 1] = cell;
            }
            return true;
        }

        void Ignite(int index, int fireId, float time)
        {
            int slot = slots[index] - 1;
            Cell cell = cells[slot];
            cell.fireId = fireId;
            cell.burnStart = time;
            cell.ignitedAt = time;
            cell.burnDuration = rules.BurnSeconds(cell.fuel);
            cell.intensity = WildfireRules.Intensity(0f, cell.fuel);
            cells[slot] = cell;
            states[index] = CellState.Burning;
            burning.Add(slot);
            EverBurnedCount++;
            FireTally tally = tallies[fireId];
            tally.burning++;
            tally.lastIgnition = time;
            tallies[fireId] = tally;
            CellIgnited?.Invoke(index, fireId);
        }

        void BurnOut(int burningIndex, float time)
        {
            int slot = burning[burningIndex];
            Cell cell = cells[slot];
            cell.intensity = 0f;
            cell.burnedOutAt = time;
            cells[slot] = cell;
            states[cell.index] = CellState.Burned;
            burning[burningIndex] = burning[burning.Count - 1];
            burning.RemoveAt(burning.Count - 1);
            FireTally tally = tallies[cell.fireId];
            tally.burning--;
            tally.burned++;
            tallies[cell.fireId] = tally;
            CellBurnedOut?.Invoke(cell.index, cell.fireId);
        }

        void RecountPeak(int fireId)
        {
            FireTally tally = tallies[fireId];
            if (tally.burning > tally.peakBurning)
            {
                tally.peakBurning = tally.burning;
                tallies[fireId] = tally;
            }
        }

        // Radiant heat at a point, 0 to about 1.5: the burning cells within about two cells, each weighed by its
        // intensity and falling off with distance. Standing in a burning cell is close to its intensity.
        public float HeatAt(Vector2 position)
        {
            if (burning.Count == 0) return 0f;
            int cx = Mathf.FloorToInt((position.x + extent) / cellSize);
            int cz = Mathf.FloorToInt((position.y + extent) / cellSize);
            float reach = 1.6f * cellSize;
            float heat = 0f;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= size || z >= size) continue;
                int index = z * size + x;
                if (states[index] != CellState.Burning) continue;
                float distance = Vector2.Distance(position, CellCentre(index));
                float falloff = Mathf.Clamp01(1f - distance / reach);
                if (falloff > 0f) heat += cells[slots[index] - 1].intensity * falloff;
            }
            return Mathf.Min(1.5f, heat);
        }

        // Smoke at a point, 0 (none) to 1 (thick), from the 32 m smoke blocks.
        public float SmokeAt(Vector2 position)
        {
            if (!hasSmoke) return 0f;
            float blockSize = cellSize * BlockCells;
            float u = (position.x + extent) / blockSize - 0.5f;
            float v = (position.y + extent) / blockSize - 0.5f;
            return Mathf.Clamp01(SampleSmoke(u, v));
        }

        // What an animal sees and smells of the fires within radius: the distance to the nearest burning block's
        // centre of heat, a direction away from the fires (each block weighed by its heat over distance squared), and
        // the heat in reach. Returns false when nothing burns within radius.
        public bool Sense(Vector2 position, float radius, out float nearestDistance, out Vector2 away, out float heatInReach)
        {
            nearestDistance = float.PositiveInfinity;
            away = Vector2.zero;
            heatInReach = 0f;
            if (burning.Count == 0) return false;
            float blockSize = cellSize * BlockCells;
            int bx = Mathf.FloorToInt((position.x + extent) / blockSize);
            int bz = Mathf.FloorToInt((position.y + extent) / blockSize);
            int reach = Mathf.CeilToInt(radius / blockSize) + 1;
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = bx + dx, z = bz + dz;
                if (x < 0 || z < 0 || x >= blocksPerSide || z >= blocksPerSide) continue;
                int block = z * blocksPerSide + x;
                float heat = blockHeat[block];
                if (heat <= 0f) continue;
                Vector2 centre = new Vector2(blockX[block] / heat, blockZ[block] / heat);
                Vector2 offset = position - centre;
                float distance = offset.magnitude;
                if (distance > radius + blockSize * 0.5f) continue;
                nearestDistance = Mathf.Min(nearestDistance, distance);
                heatInReach += heat;
                float weight = heat / Mathf.Max(cellSize * cellSize, distance * distance);
                away += distance > 0.01f ? offset / distance * weight : Vector2.zero;
            }
            if (away.sqrMagnitude > 1e-12f) away.Normalize();
            return heatInReach > 0f;
        }

        // Heat and smoke blocks: the burning cells' heat and centre of heat per block, and smoke given off, blown along
        // by the wind, spreading out and thinning.
        void UpdateBlocks(float deltaTime, in FireWeather weather)
        {
            foreach (int block in heatedBlocks)
            {
                blockHeat[block] = 0f;
                blockX[block] = 0f;
                blockZ[block] = 0f;
            }
            heatedBlocks.Clear();
            float blockSize = cellSize * BlockCells;
            foreach (int slot in burning)
            {
                Cell cell = cells[slot];
                int x = cell.index % size / BlockCells;
                int z = cell.index / size / BlockCells;
                int block = z * blocksPerSide + x;
                if (blockHeat[block] <= 0f) heatedBlocks.Add(block);
                Vector2 centre = CellCentre(cell.index);
                float heat = Mathf.Max(0.001f, cell.intensity);
                blockHeat[block] += heat;
                blockX[block] += centre.x * heat;
                blockZ[block] += centre.y * heat;
                smoke[block] += rules.smokeEmission * cell.intensity * (0.4f + 0.6f * cell.fuel.load) * deltaTime;
                if (!hasSmoke)
                {
                    smokeArea = new RectInt(x, z, 1, 1);
                    hasSmoke = true;
                }
                else
                {
                    smokeArea = Encapsulate(smokeArea, x, z);
                }
            }
            if (!hasSmoke) return;

            // Semi-Lagrangian: each block takes the smoke from upwind of it, a little of its neighbours', and thins.
            int margin = 2;
            int xMin = Mathf.Max(0, smokeArea.xMin - margin), zMin = Mathf.Max(0, smokeArea.yMin - margin);
            int xMax = Mathf.Min(blocksPerSide - 1, smokeArea.xMax - 1 + margin);
            int zMax = Mathf.Min(blocksPerSide - 1, smokeArea.yMax - 1 + margin);
            Vector2 shift = weather.windDirection * (weather.windSpeed * deltaTime / blockSize);
            float thinning = Mathf.Exp(-deltaTime / Mathf.Max(1f, rules.smokeLifetime));
            float spread = Mathf.Clamp01(0.08f * deltaTime);
            bool any = false;
            RectInt next = default;
            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                float carried = SampleSmoke(x - shift.x, z - shift.y);
                float around = 0.25f * (Smoke(x + 1, z) + Smoke(x - 1, z) + Smoke(x, z + 1) + Smoke(x, z - 1));
                float value = Mathf.Lerp(carried, around, spread) * thinning;
                if (value < 0.0005f) value = 0f;
                smokeNext[z * blocksPerSide + x] = value;
                if (value > 0f)
                {
                    next = any ? Encapsulate(next, x, z) : new RectInt(x, z, 1, 1);
                    any = true;
                }
            }
            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                int block = z * blocksPerSide + x;
                smoke[block] = smokeNext[block];
                smokeNext[block] = 0f;
            }
            hasSmoke = any;
            smokeArea = next;
        }

        float Smoke(int x, int z) =>
            x < 0 || z < 0 || x >= blocksPerSide || z >= blocksPerSide ? 0f : smoke[z * blocksPerSide + x];

        float SampleSmoke(float u, float v)
        {
            int x0 = Mathf.FloorToInt(u), z0 = Mathf.FloorToInt(v);
            float fx = u - x0, fz = v - z0;
            float a = Smoke(x0, z0), b = Smoke(x0 + 1, z0), c = Smoke(x0, z0 + 1), d = Smoke(x0 + 1, z0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        public float SmokeOfBlock(int x, int z) => Smoke(x, z);

        static RectInt Encapsulate(RectInt rect, int x, int z)
        {
            int xMin = Mathf.Min(rect.xMin, x), zMin = Mathf.Min(rect.yMin, z);
            int xMax = Mathf.Max(rect.xMax, x + 1), zMax = Mathf.Max(rect.yMax, z + 1);
            return new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
        }
    }
}
