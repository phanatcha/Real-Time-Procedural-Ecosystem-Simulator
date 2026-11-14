using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). What it notices and when it is active: sight by day and night (item 75), the chronotype and resting at its
    // off hours (item 74), how visible it is to others (camouflage, glow from item 76), and the perception the
    // decision policy works from.
    public partial class SeekFood
    {
        [Header("Day and Night")]
        [Tooltip("Inherited (0 to 1): when the animal is active. 0 by day, 1 by night, around 0.5 at dawn and dusk and " +
                 "in bursts round the clock. At its off hours it rests (item 74). Founders 0.35: mostly by day.")]
        [Range(0f, 1f)] public float nocturnality = 0.35f;
        [Tooltip("Inherited (0 to 1): how well the animal sees in the dark (item 75). Under a full moon ordinary eyes " +
                 "see 40% of their range and night eyes 80%; by day night eyes are dazzled to 85%, and they cost up to " +
                 "6% more energy. Founders 0.2: ordinary eyes.")]
        [Range(0f, 1f)] public float nightVision = 0.2f;
        [Tooltip("Energy use while resting, as a share of the usual.")]
        [Range(0.1f, 1f)] public float restEnergyMultiplier = 0.7f;
        [Tooltip("How far a resting animal notices food, partners and danger, as a share of its vision radius, so a " +
                 "sleeper is easier to surprise.")]
        [Range(0.05f, 1f)] public float restPerception = 0.33f;
        [Tooltip("Share of the day's temperature swing a resting animal feels, curled up out of the wind.")]
        [Range(0f, 1f)] public float restSwingFelt = 0.5f;
        [Tooltip("How far an animal goes to rest under trees, in metres, when it isn't under them already. 0 rests " +
                 "where it stands.")]
        [Min(0f)] public float shelterSearchRadius = 20f;
        [Tooltip("Below this share of its energy an animal forages at any hour; from twice it, it rests fully at its " +
                 "off hours. A thirsty animal (under Thirst Threshold) drinks before it rests.")]
        [Range(0.01f, 0.5f)] public float wakeWhenBelow = 0.25f;
        public bool IsResting => currentState == State.Resting;
        // The share of its usual energy use the animal spends now: less once settled into rest.
        public float RestEnergyMultiplier => currentState == State.Resting && restSettled ? restEnergyMultiplier : 1f;
        // How far the animal notices food, partners and danger: its vision, or part of it while resting.
        float PerceptionRadius => visionRadius * SightShareNow *
                                  (currentState == State.Resting ? restPerception : 1f);

        // Extra energy use of the best night eyes (night vision 1): eyes and the brain behind them are costly.
        public const float NightVisionEnergyCost = 0.06f;

        // Light levels where SightShare's targets are set, as log10 of light for seeing: starlight, a full moon,
        // sunrise and day.
        static readonly float[] SightLevels = { -2f, Mathf.Log10(DayCycle.FullMoonLight), Mathf.Log10(0.3f), 0f };
        static readonly float[] OrdinarySight = { 0.15f, 0.4f, 0.8f, 1f };
        static readonly float[] NightSight = { 0.55f, 0.8f, 0.95f, 0.85f };

        // Share of its vision radius an animal sees by this light (item 75), for ordinary eyes (night vision 0) and
        // night eyes (1): by starlight 15% and 55%, under a full moon 40% and 80%, at sunrise 80% and 95%, and by day
        // 100% and 85%, since night eyes are dazzled. Between them it follows the logarithm of the light, as eyes
        // do. With day and night off it is always day.
        public static float SightShare(float light, float nightVision)
        {
            float level = Mathf.Log10(Mathf.Clamp(light, DayCycle.StarLight, 1f));
            int upper = 1;
            while (upper < SightLevels.Length - 1 && level > SightLevels[upper]) upper++;
            float t = Mathf.InverseLerp(SightLevels[upper - 1], SightLevels[upper], level);
            float ordinary = Mathf.Lerp(OrdinarySight[upper - 1], OrdinarySight[upper], t);
            float night = Mathf.Lerp(NightSight[upper - 1], NightSight[upper], t);
            return Mathf.Lerp(ordinary, night, Mathf.Clamp01(nightVision));
        }

        // Share of its vision radius the animal sees by the light now.
        public float SightShareNow => SightShare(WorldDay.Current.light, nightVision);

        // "ordinary", "fair" or "night eyes", for the inspector and panel.
        public static string DescribeNightVision(float nightVision) =>
            nightVision < 0.35f ? "ordinary eyes" : nightVision < 0.65f ? "fair night eyes" : "night eyes";

        // "by day", "mostly by day", "at dawn and dusk", "mostly by night", "by night".
        public static string DescribeChronotype(float nocturnality) =>
            nocturnality < 0.15f ? "by day" : nocturnality < 0.4f ? "mostly by day"
            : nocturnality <= 0.6f ? "at dawn and dusk" : nocturnality <= 0.85f ? "mostly by night" : "by night";

        // How much an animal of this chronotype wants to be active, from the light (item 74): 0 by day and 1 by night
        // swap over as nocturnality goes from 0 to 1, and around 0.5 a bump at twilight makes dawn and dusk the busiest
        // hours. It rests while this is under one half.
        public static float ActivityAt(float nocturnality, float daylight, float twilight)
        {
            float n = Mathf.Clamp01(nocturnality);
            return (1f - n) * daylight + n * (1f - daylight) + 4f * n * (1f - n) * twilight;
        }

        // Share of a day an animal of this chronotype spends at its off hours, for animals kept as numbers, which have
        // no hours: night for day animals, day for night animals, little for those in between. dayLength is the share
        // of the day the sun is up.
        public static float RestShareOfDay(float nocturnality, float dayLength)
        {
            float n = Mathf.Clamp01(nocturnality);
            float light = Mathf.Clamp01(dayLength);
            float dayActivity = 1f - n;
            float nightActivity = n;
            return (dayActivity < 0.5f ? light : 0f) + (nightActivity < 0.5f ? 1f - light : 0f);
        }

        // How much the animal wants to rest now: 0 at its active hours, 1 deep in its off hours. Hunger and thirst
        // override it: under Wake When Below of its energy it forages at any hour, and once thirsty it goes to water
        // first. Thirst is not let run down like hunger: an animal that rests on until a third of its water is left
        // can die on a long walk to the water, most often through the long winter nights. Resting goes on a little
        // longer than it would start, so animals don't flicker between resting and waking at the edge.
        public float RestUrgency
        {
            get
            {
                if (!WorldDay.IsRunning || maxEnergy <= 0f) return 0f;

                DayState day = WorldDay.Current;
                float activity = ActivityAt(nocturnality, day.daylight, day.twilight);
                float threshold = currentState == State.Resting ? 0.55f : 0.5f;
                float offHours = Mathf.Clamp01((threshold - activity) / 0.15f);
                float fed = Mathf.InverseLerp(wakeWhenBelow, 2f * wakeWhenBelow, currentEnergy / maxEnergy);
                float watered = enableThirst
                    ? Mathf.InverseLerp(thirstThreshold, Mathf.Min(1f, thirstThreshold + 0.1f), HydrationFraction)
                    : 1f;
                return offHours * fed * watered;
            }
        }
        // How far other animals can spot this one, as a fraction of their vision: camouflage hides it, except while
        // it flees.
        // Camouflage (the only perk that lowers visibility) hides better in the dark (item 75).
        public float Visibility
        {
            get
            {
                float daylight = WorldDay.Current.daylight;
                float hidden = currentState == State.Fleeing ? 1f
                    : perkEffects.visibility < 1f
                        ? perkEffects.visibility * Mathf.Lerp(AnimalPerks.CamouflageNightVisibility, 1f, daylight)
                        : perkEffects.visibility;
                if (!perkEffects.glows) return hidden;
                // A glowing animal (item 76) can't hide in the dark, and is seen from further the darker it is.
                float darkness = 1f - daylight;
                return Mathf.Lerp(hidden, 1f, darkness) * AnimalPerks.GlowSpotting(darkness);
            }
        }
        // Whether it has the glow perk (item 76), and how many living animals do: while none do, nobody looks further.
        public bool Glows => perkEffects.glows;
        public static int GlowingAnimals => glowingAnimals;
        static int glowingAnimals;
        private bool countedAsGlowing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlowingAnimals() => glowingAnimals = 0;
        // Shared scratch list for perception; animals only update on the main thread.
        private static readonly List<FoodItem> nearbyFood = new List<FoodItem>();
        private static readonly List<SeekFood> nearbyAnimals = new List<SeekFood>();

        // Resting (item 74): false while still walking to shelter, true once settled.
        private bool restSettled;
        private float restWalkTimer;

        AnimalPerception BuildPerception()
        {
            AnimalPerception perception = new AnimalPerception
            {
                nearestPlantDistance = Mathf.Infinity,
                nearestMeatDistance = Mathf.Infinity,
                nearestPreyDistance = Mathf.Infinity,
                mateDistance = Mathf.Infinity,
                threats = new List<AnimalThreat>()
            };

            float perceptionRadius = PerceptionRadius;
            float visionRadiusSquared = perceptionRadius * perceptionRadius;

            // A wildfire is noticed by its smoke and glow from further than the animal can see (item 79), and food
            // in burning cells is left alone.
            WildfireKind fires = Wildfires.Current;
            bool fireNearby = false;
            perception.fireDistance = Mathf.Infinity;
            if (fires != null && fires.Sense(transform.position, Mathf.Max(perceptionRadius, fireSenseRadius),
                                             out float fireDistance, out Vector3 fireAway, out _))
            {
                fireNearby = true;
                perception.fireSensed = true;
                perception.fireDistance = fireDistance;
                perception.fireAway = fireAway;
            }
            perception.fireHeat = fireHeat;
            perception.smoke = smokeFelt;

            FoodItem.FindNearby(transform.position, perceptionRadius, nearbyFood);
            foreach (FoodItem food in nearbyFood)
            {
                if (food == null || !food.IsAvailable)
                {
                    continue;
                }

                if (!CanEat(food) || IsRememberedAsUnreachable(food))
                {
                    continue;
                }

                if (fireNearby && fires.IsBurningAt(food.transform.position))
                {
                    continue;
                }

                float distanceSquared = (transform.position - food.transform.position).sqrMagnitude;
                if (distanceSquared > visionRadiusSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                if (food.foodType == FoodType.Plant && distance < perception.nearestPlantDistance)
                {
                    perception.nearestPlant = food;
                    perception.nearestPlantDistance = distance;
                }
                else if (food.foodType == FoodType.Meat && distance < perception.nearestMeatDistance)
                {
                    perception.nearestMeat = food;
                    perception.nearestMeatDistance = distance;
                }
            }

            SpeciesManager manager = SpeciesManager.Instance;
            if (manager != null)
            {
                // Only animals in the grid cells this animal's vision covers can be seen, so the rest aren't checked.
                // In the dark a glowing animal is seen from further (item 76), so while any animal glows the search
                // reaches as far as one could be seen.
                float darkness = 1f - WorldDay.Current.daylight;
                float searchRadius = perceptionRadius;
                if (glowingAnimals > 0 && darkness > 0f)
                {
                    searchRadius *= Mathf.Max(AnimalPerks.GlowSpotting(darkness),
                                              seekingMate ? AnimalPerks.GlowMateNotice(darkness) : 1f);
                }
                manager.FindAgentsNear(transform.position, searchRadius, nearbyAnimals);
                float bestMateScore = 0f;
                foreach (SeekFood other in nearbyAnimals)
                {
                    if (seekingMate)
                    {
                        ConsiderMate(other, visionRadiusSquared, ref perception, ref bestMateScore);
                    }

                    if (!IsOtherLivingAnimal(other))
                    {
                        continue;
                    }

                    float distanceSquared = (transform.position - other.transform.position).sqrMagnitude;
                    float distance = Mathf.Sqrt(distanceSquared);
                    // Camouflaged animals are only spotted, as prey or as hunters, within part of the vision radius.
                    float spottingRadiusSquared = visionRadiusSquared * other.Visibility * other.Visibility;
                    if (IsViablePrey(other) && !IsTemporarilyAvoidedPrey(other) &&
                        distanceSquared <= spottingRadiusSquared &&
                        distanceSquared < perception.nearestPreyDistance * perception.nearestPreyDistance)
                    {
                        perception.nearestPrey = other;
                        perception.nearestPreyDistance = distance;
                    }

                    bool recentlyAttacked = IsRememberedThreat(other);
                    bool isActivelyHunting = distanceSquared <= spottingRadiusSquared &&
                                             other.IsTargetingAsPrey(this);
                    if (recentlyAttacked || isActivelyHunting)
                    {
                        perception.threats.Add(new AnimalThreat(other, distance,
                                                                isActivelyHunting, recentlyAttacked));
                    }
                }

                AddRememberedThreatsOutOfView(manager, perception.threats);
            }

            perception.drinkingSpotDistance = Mathf.Infinity;
            if ((IsThirsty || WantsToKeepDrinking) && TryFindDrinkingSpot(out Vector3 drinkingSpot))
            {
                perception.hasDrinkingSpot = true;
                perception.drinkingSpot = drinkingSpot;
                perception.drinkingSpotDistance = Vector3.Distance(transform.position, drinkingSpot);
            }

            return perception;
        }

        // Starts resting: under the trees nearby if it isn't under them already (they shelter it from the night's cold,
        // see DailyTemperature), otherwise where it stands.
        void BeginRest()
        {
            restSettled = true;
            restWalkTimer = 0f;
            TryResetPath();

            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (shelterSearchRadius <= 0f || inWater || terrainWorld == null ||
                !terrainWorld.TryGetCachedSample(transform.position, out EnvironmentSample here))
            {
                return;
            }

            const int Directions = 8;
            float bestCover = here.treeCover + 0.2f;
            Vector3 best = transform.position;
            float firstAngle = Random.value * Mathf.PI * 2f;
            for (int ring = 1; ring <= 2; ring++)
            {
                for (int direction = 0; direction < Directions; direction++)
                {
                    float angle = firstAngle + direction * Mathf.PI * 2f / Directions;
                    Vector3 point = transform.position +
                                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (shelterSearchRadius * ring * 0.5f);
                    if (terrainWorld.TryGetCachedSample(point, out EnvironmentSample there) && there.isLand &&
                        there.treeCover > bestCover)
                    {
                        bestCover = there.treeCover;
                        best = point;
                    }
                }
            }

            if (best != transform.position &&
                NavMesh.SamplePosition(best, out NavMeshHit hit, 5f, WaterAccess.WalkableAreas) &&
                TrySetDestination(hit.position))
            {
                restSettled = false;
            }
        }
    }
}
