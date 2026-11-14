using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Water: thirst and drinking (item 27), wading and swimming (item 10), frozen lakes (item 21) and snow.
    public partial class SeekFood
    {
        [Header("Thirst")]
        [Tooltip("Animals need fresh water as well as food (item 27): a thirsty animal walks to the nearest river or " +
                 "lake bank it can reach and drinks. The sea is salt. Off, hydration stays full.")]
        public bool enableThirst = true;
        [Min(1f)] public float maxHydration = 100f;
        [Tooltip("Hydration lost per simulated second while comfortable; each unit of heat stress adds as much again.")]
        [Min(0f)] public float hydrationDrainPerSecond = 0.4f;
        [Tooltip("Share of maximum hydration below which the animal looks for water. A drink is worth more the " +
                 "further below it the animal is (UtilityDecisionPolicy.thirstValue).")]
        [Range(0f, 1f)] public float thirstThreshold = 0.5f;
        [Tooltip("Hydration gained per simulated second while drinking or standing in fresh water.")]
        [Min(0f)] public float drinkPerSecond = 25f;
        [Tooltip("Health lost per simulated second once the animal has no water left.")]
        [Min(0f)] public float thirstDamagePerSecond = 2f;
        [Tooltip("Simulated seconds a drinking spot the animal couldn't reach is avoided, as with unreachable food.")]
        [Min(0.1f)] public float unreachableWaterMemory = 60f;
        [Tooltip("Current hydration. Negative means full when the animal starts.")]
        public float currentHydration = -1f;

        [Header("Water")]
        [Tooltip("How fast the animal moves and how much energy it uses in water. Its swimming ability comes from " +
                 "its body: mostly fins, a little from long legs.")]
        public WaterMovement waterMovement = WaterMovement.Default;
        [SerializeField, Tooltip("This animal's swimming ability (0-1), for reference.")]
        private float swimmingAbility;
        public float SwimmingAbility => bodyEffects.swimmingAbility;
        public bool CanSwimDeepWater => waterMovement.CanSwimDeepWater(bodyEffects.swimmingAbility);
        public bool IsInWater => inWater;
        public bool IsInDeadZone => inDeadZone;
        // Standing on a frozen river or lake: on the water surface, but walking as on land.
        public bool IsOnIce => onIce;
        public float HydrationFraction => enableThirst && maxHydration > 0f ? Mathf.Clamp01(currentHydration / maxHydration) : 1f;
        public bool IsThirsty => enableThirst && HydrationFraction < thirstThreshold;
        // 0 at the thirst threshold, 1 with no water left. An animal already at the water keeps a quarter of the
        // urgency until it is nearly full, so it finishes its drink instead of wandering off at the threshold.
        public float ThirstUrgency
        {
            get
            {
                if (!enableThirst) return 0f;
                float urgency = Mathf.Clamp01(Mathf.InverseLerp(Mathf.Max(0.01f, thirstThreshold), 0f, HydrationFraction));
                return WantsToKeepDrinking ? Mathf.Max(urgency, 0.25f) : urgency;
            }
        }
        public bool IsDrinking => currentState == State.Drinking;
        // On the way to water or drinking, and not yet nearly full.
        bool WantsToKeepDrinking => (currentState == State.Drinking || currentState == State.SeekingWater) &&
                                    HydrationFraction < 0.95f;
        // How much snow under the animal slows it: 1 without snow, lower in snow. Not part of its genes.
        public float SnowSpeedMultiplier => snowSpeedMultiplier;

        // Simulated seconds for the body to settle into the water or rise out of it.
        private const float FloatTransitionSeconds = 0.5f;
        // How near a dead zone's NavMesh a swimmer must be to count as in it, in world units.
        private const float DeadZoneProbeRadius = 1f;
        // Simulated seconds between checks of the snow under the animal, which changes slowly.
        private const float SnowSampleInterval = 1f;
        private bool inWater;
        private bool inDeadZone;
        private bool onIce;
        // Whether the area mask and costs were last set for frozen rivers and lakes, and whether this animal, unable
        // to swim, was left on deep fresh water as the ice thawed and may still cross it to reach the shore.
        private bool waterAccessFrozen;
        private bool strandedOnThawingIce;
        private float snowSpeedMultiplier = 1f;
        private float snowSampleTimer;
        // The agent's offset standing on land and floating with the body half under water, and how far between
        // the two the body is (0 = standing, 1 = floating).
        private float standingBaseOffset;
        private float floatingBaseOffset;
        private float floatAmount;

        struct UnreachableWater
        {
            public Vector3 spot;
            public float retryTime;
        }

        // The place the animal is going to drink at, kept once found until it has drunk or given up on it.
        private Vector3 currentDrinkingSpot;
        private bool hasDrinkingSpot;
        private float waterChaseTimer;
        private float nextWaterSearchTime;
        private readonly List<UnreachableWater> unreachableWater = new List<UnreachableWater>();
        // For tests and scenes without terrain: given a position and a search radius, a point of fresh water, or
        // null. Unset, the terrain's rivers and lakes are used.
        public static System.Func<Vector3, float, Vector3?> DrinkingWaterLocator;

        // Hydration falls with time, faster in the heat, and refills while the animal drinks or stands in fresh water.
        // With none left the animal loses health. Returns true when it died of thirst.
        bool UpdateThirst(float deltaTime)
        {
            if (!enableThirst) return false;

            if (currentState == State.Drinking || IsStandingInFreshWater())
            {
                currentHydration = Mathf.Min(maxHydration, currentHydration + drinkPerSecond * deltaTime);
                return false;
            }

            float heat = thermalResponse != null ? thermalResponse.HeatStress : 0f;
            currentHydration = Mathf.Max(0f, currentHydration - hydrationDrainPerSecond * (1f + heat) * deltaTime);
            if (currentHydration > 0f) return false;

            currentHealth -= thirstDamagePerSecond * deltaTime;
            if (currentHealth <= 0f)
            {
                Die(AgentDeathCause.Thirst, null);
                return true;
            }
            return false;
        }

        bool IsStandingInFreshWater()
        {
            if (!inWater) return false;
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            return terrainWorld != null && terrainWorld.TryGetCachedSample(transform.position, out EnvironmentSample ground) &&
                   IsFreshWater(ground);
        }

        // Rivers and lakes; the sea is salt.
        static bool IsFreshWater(in EnvironmentSample sample) => WaterAccess.IsFresh(sample);

        // The nearest fresh water in view and a place at its edge on the NavMesh where this animal can wade in, or
        // failing that stand, to drink. The spot is kept until it has been drunk from or given up on; the search itself runs at most
        // every two seconds, since it samples the terrain along rays.
        bool TryFindDrinkingSpot(out Vector3 spot)
        {
            spot = currentDrinkingSpot;
            if (hasDrinkingSpot) return true;
            if (Time.time < nextWaterSearchTime) return false;
            nextWaterSearchTime = Time.time + 2f;

            Vector3? water = DrinkingWaterLocator != null
                ? DrinkingWaterLocator(transform.position, visionRadius)
                : FindFreshWater();
            if (!water.HasValue) return false;
            // Animals wade in to drink, as real ones do: the spot is in the shallows when there are any by the water
            // found (lakes have a wadeable shelf inside a walkable bank), otherwise anywhere the animal can stand or
            // swim, which for a river in a gorge is the top of its bank.
            // Through the ice in winter: animals break it at the edge (or eat the snow on it) and still drink.
            if (!NavMesh.SamplePosition(water.Value, out NavMeshHit hit, 15f, WaterAccess.ShallowWaterAreas) &&
                !NavMesh.SamplePosition(water.Value, out hit, 15f, WaterAccess.AreaMask(CanSwimDeepWater))) return false;
            if (IsRememberedUnreachableWater(hit.position)) return false;

            spot = hit.position;
            currentDrinkingSpot = spot;
            hasDrinkingSpot = true;
            return true;
        }

        // Walks rays out from the animal and takes the first water each meets: that is the shore, and past it is
        // only more water. Returns the nearest fresh water found.
        Vector3? FindFreshWater()
        {
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (terrainWorld == null) return null;

            const int Directions = 12;
            float step = Mathf.Max(8f, visionRadius / 10f);
            Vector3 origin = transform.position;
            Vector3? nearest = null;
            float nearestDistance = float.MaxValue;
            for (int direction = 0; direction < Directions; direction++)
            {
                float angle = direction * Mathf.PI * 2f / Directions;
                Vector3 along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                for (float distance = step; distance <= visionRadius && distance < nearestDistance; distance += step)
                {
                    Vector3 point = origin + along * distance;
                    if (!terrainWorld.TryGetSample(point, out EnvironmentSample sample) || !sample.isValid) break;
                    if (!sample.isWater) continue;
                    if (IsFreshWater(sample))
                    {
                        nearest = sample.position;
                        nearestDistance = distance;
                    }
                    break;
                }
            }
            return nearest;
        }

        // True once walking to the drinking spot can't work: the route stops short of it, the animal has walked as
        // far as the route goes, or it is taking far longer than crossing its field of view would.
        bool IsDrinkingSpotOutOfReach()
        {
            float speed = Mathf.Max(0.1f, GetCurrentMovementSpeed());
            if (waterChaseTimer >= Mathf.Max(maximumFoodChaseDuration, 3f * visionRadius / speed)) return true;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending || RouteJustSet) return false;

            if (agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Vector3 shortfall = agent.pathEndPosition - currentDrinkingSpot;
                shortfall.y = 0f;
                float range = CurrentFoodInteractionRange;
                if (shortfall.sqrMagnitude > range * range) return true;
            }

            return !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.5f;
        }

        void GiveUpOnWater()
        {
            if (unreachableWater.Count >= 16) unreachableWater.RemoveAt(0);
            unreachableWater.Add(new UnreachableWater
            {
                spot = currentDrinkingSpot,
                retryTime = Time.time + Mathf.Max(0.1f, unreachableWaterMemory)
            });
            hasDrinkingSpot = false;
            waterChaseTimer = 0f;
            ClearTargets();
            currentState = State.Idling;
            timer = 0f;
            decisionTimer = 0f;
            TryResetPath();

            if (SpeciesManager.Instance != null && SpeciesManager.Instance.ShouldLogLifecycleEvents)
            {
                Debug.Log($"{gameObject.name} gave up on drinking at x {currentDrinkingSpot.x:0} z {currentDrinkingSpot.z:0}, which it could not reach.");
            }
        }

        bool IsRememberedUnreachableWater(Vector3 spot)
        {
            float now = Time.time;
            for (int index = unreachableWater.Count - 1; index >= 0; index--)
            {
                UnreachableWater entry = unreachableWater[index];
                if (entry.retryTime <= now)
                {
                    unreachableWater.RemoveAt(index);
                    continue;
                }
                Vector3 offset = entry.spot - spot;
                offset.y = 0f;
                if (offset.sqrMagnitude < 15f * 15f) return true;
            }
            return false;
        }

        // The animal is in water when its NavMesh position is down on the flat water surface, unless that is a frozen
        // river or lake, where it walks on the ice. Water changes its speed (RefreshMovementSpeed) and energy use
        // (CurrentEnergyDrainPerSecond), and the body settles half under. Only swimmers reach dead zones, which are
        // deep, so only they look for one under them.
        void UpdateWater(float deltaTime)
        {
            if (waterAccessFrozen != WaterAccess.FreshWaterFrozen || strandedOnThawingIce) RefreshFreshWaterAccess();

            bool onWaterSurface = agent != null && agent.isOnNavMesh && WaterAccess.IsInWater(agent.nextPosition.y);
            AnimalTerrainWorld iceWorld = AnimalTerrainWorld.Active;
            onIce = onWaterSurface && WaterAccess.FreshWaterFrozen && iceWorld != null &&
                    iceWorld.TryGetCachedSample(transform.position, out EnvironmentSample underfoot) &&
                    WaterAccess.IsFresh(underfoot);
            inWater = onWaterSurface && !onIce;
            inDeadZone = inWater && CanSwimDeepWater &&
                         NavMesh.SamplePosition(agent.nextPosition, out _, DeadZoneProbeRadius,
                                                1 << WaterAccess.DeadZoneArea);

            float target = inWater ? 1f : 0f;
            // The legs measure their height in the first frames, so the body only moves once they have.
            if (floatAmount == target || agent == null || (bodyView != null && !bodyView.HasMeasuredLift))
            {
                return;
            }

            floatAmount = Mathf.MoveTowards(floatAmount, target, deltaTime / FloatTransitionSeconds);
            agent.baseOffset = Mathf.Lerp(standingBaseOffset, floatingBaseOffset, floatAmount);
        }

        // Snow on land slows the animal by the terrain's seasonal movement cost (SeasonalGround), down to 57% of its
        // speed in the deepest snow.
        void UpdateSnow(float deltaTime)
        {
            snowSampleTimer -= deltaTime;
            if (snowSampleTimer > 0f) return;

            snowSampleTimer = SnowSampleInterval;
            snowSpeedMultiplier = 1f;
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (inWater || !SeasonClock.IsRunning || terrainWorld == null ||
                !terrainWorld.TryGetCachedSample(transform.position, out EnvironmentSample ground))
            {
                return;
            }

            snowSpeedMultiplier = 1f / SeasonalGround.At(ground, SeasonClock.Current).movementCost;
        }

        // Deep water is closed to animals that can't swim in it, and water costs each animal's route planning what it
        // really costs that animal in time and energy, so routes only cross water when it is worth it.
        void ConfigureWaterAccess()
        {
            float ability = bodyEffects.swimmingAbility;
            swimmingAbility = ability;
            float waterCost = waterMovement.PathCost(ability, bodyEffects.speedMultiplier);
            agent.SetAreaCost(WaterAccess.ShallowWaterArea, waterCost);
            agent.SetAreaCost(WaterAccess.DeepWaterArea, waterCost);
            agent.SetAreaCost(WaterAccess.DeadZoneArea, waterCost * SeaBiomeMap.CurrentRules.deadZoneEnergyCost);
            strandedOnThawingIce = false;
            RefreshFreshWaterAccess();

            // Floating puts the middle of the body at the waterline; legs and the lower half are under water.
            standingBaseOffset = agent.baseOffset;
            MeshFilter bodyFilter = GetComponent<MeshFilter>();
            floatingBaseOffset = bodyFilter != null && bodyFilter.sharedMesh != null
                ? -bodyFilter.sharedMesh.bounds.center.y
                : standingBaseOffset;
            floatAmount = 0f;
        }

        // Rivers and lakes are water, priced as the sea is, until they freeze: then everyone may cross them, and the ice
        // costs no more than land. When they thaw, an animal that can't swim and is still out on deep water keeps
        // the right to cross it, at the water's cost, until it is back in the shallows or on land.
        void RefreshFreshWaterAccess()
        {
            if (agent == null) return;

            bool frozen = WaterAccess.FreshWaterFrozen;
            int mask = WaterAccess.AreaMask(CanSwimDeepWater);
            strandedOnThawingIce = !frozen && !CanSwimDeepWater && agent.isOnNavMesh &&
                                   NavMesh.SamplePosition(agent.nextPosition, out _, 1f, 1 << WaterAccess.FreshDeepWaterArea);
            if (strandedOnThawingIce) mask |= 1 << WaterAccess.FreshDeepWaterArea;
            agent.areaMask = mask;

            float waterCost = frozen ? 1f : waterMovement.PathCost(swimmingAbility, bodyEffects.speedMultiplier);
            agent.SetAreaCost(WaterAccess.FreshShallowWaterArea, waterCost);
            agent.SetAreaCost(WaterAccess.FreshDeepWaterArea, waterCost);
            waterAccessFrozen = frozen;
        }
    }
}
