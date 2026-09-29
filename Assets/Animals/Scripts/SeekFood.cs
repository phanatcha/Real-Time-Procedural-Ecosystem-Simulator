using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent), typeof(MeshRenderer))]
public class SeekFood : MonoBehaviour, IEcosystemMaterializationLifecycle
{
    private NavMeshAgent agent;
    private AnimalDecisionPolicy decisionPolicy;
    private AnimalTemperature thermalResponse;
    private AnimalGenome genome;
    private FoodItem currentFoodTarget;
    private SeekFood currentPreyTarget;
    private SeekFood currentFleeThreat;
    private Vector3 currentFleeDirection;

    [Header("Species Identity")]
    public string speciesName = "A";
    public Color speciesColor = Color.white;

    [Header("Survival Stats")]
    public float currentEnergy = 50f;
    public float maxEnergy = 150f;

    [Header("Health Stats")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Metabolism & Starvation")]
    [FormerlySerializedAs("energyDrainPerSecond")]
    [Min(0f)] public float baseEnergyDrainPerSecond = 1f;
    [Min(0f)] public float starvationDamagePerSecond = 5f;

    [Header("Metabolic Trait Tradeoffs")]
    [Min(0f)] public float speedMetabolicWeight = 0.4f;
    [Min(0f)] public float strengthMetabolicWeight = 0.2f;
    [FormerlySerializedAs("bodySizeMetabolicWeight")]
    [Min(0f)] public float bodyMassMetabolicWeight = 0.3f;
    [Min(0f)] public float visionMetabolicWeight = 0.2f;
    [Min(0f)] public float energyCapacityMetabolicWeight = 0.2f;
    [Min(0f)] public float staminaCapacityMetabolicWeight = 0.2f;
    [Min(0f)] public float healthMetabolicWeight = 0.2f;
    [Min(0f)] public float minimumMetabolicMultiplier = 0.25f;
    [Min(0f)] public float maximumMetabolicMultiplier = 4f;

    [Header("Founder Trait Reference (0 = Capture at Birth)")]
    [SerializeField, Min(0f)] private float referenceMoveSpeed;
    [SerializeField, Min(0f)] private float referenceStrength;
    [FormerlySerializedAs("referenceBodySize")]
    [SerializeField, Min(0f)] private float referenceBodyBulk;
    [SerializeField, Min(0f)] private float referenceBodyHeight;
    [SerializeField, Min(0f)] private float referenceVisionRadius;
    [SerializeField, Min(0f)] private float referenceMaxEnergy;
    [SerializeField, Min(0f)] private float referenceMaxStamina;
    [SerializeField, Min(0f)] private float referenceMaxHealth;
    [SerializeField] private Vector3 referenceLocalScale;
    [SerializeField, Min(0f)] private float referenceAgentRadius;
    [SerializeField, Min(0f)] private float referenceAgentHeight;
    [SerializeField, Min(0f)] private float referenceAgentBaseOffset;
    [SerializeField, Min(0f)] private float referenceAgentStoppingDistance;
    [SerializeField, Tooltip("Calculated metabolic drain after applying all trait tradeoffs.")]
    private float currentEnergyDrainPerSecond = 1f;

    [Header("Dietary Genetics")]
    [Tooltip("0 = herbivore specialist, 0.5 = omnivore, 1 = carnivore specialist.")]
    [Range(0f, 1f)] public float dietAffinity = 0.5f;
    [Tooltip("Minimum efficiency retained for the non-specialized food type.")]
    [Range(0f, 1f)] public float minimumDietEfficiency = 0.2f;

    [Header("Agent Traits")]
    public float visionRadius = 100f;
    public float moveSpeed = 24f;
    [FormerlySerializedAs("attackDamage")]
    [Tooltip("Damage dealt per successful attack and the main offensive combat-power trait.")]
    [Min(0f)] public float strength = 25f;

    [Header("Body Proportions")]
    [FormerlySerializedAs("bodySize")]
    [Tooltip("Heritable horizontal build. This scales the animal along the X and Z axes.")]
    [Min(0.01f)] public float bodyBulk = 1f;
    [FormerlySerializedAs("minimumBodySize")]
    [Min(0.01f)] public float minimumBodyBulk = 0.6f;
    [FormerlySerializedAs("maximumBodySize")]
    [Min(0.01f)] public float maximumBodyBulk = 1.8f;
    [Tooltip("Heritable vertical build. This scales the animal along the Y axis and determines feeding reach.")]
    [Min(0.01f)] public float bodyHeight = 1f;
    [Min(0.01f)] public float minimumBodyHeight = 0.6f;
    [Min(0.01f)] public float maximumBodyHeight = 2.2f;
    [Tooltip("How much vertical growth contributes to estimated body mass. Bulk always contributes fully.")]
    [Range(0f, 1f)] public float heightMassContribution = 0.35f;
    [Tooltip("Vertical feeding reach at body height 1.0, in world units.")]
    [Min(0f)] public float baseFeedingReach = 15f;

    [Header("Stamina")]
    [Min(0.01f)] public float maxStamina = 100f;
    [Min(0f)] public float currentStamina;
    [Min(0f)] public float staminaRecoveryPerSecond = 8f;
    [Min(0f)] public float attackStaminaCost = 10f;
    [Min(0f)] public float fleeStaminaCostPerSecond = 15f;
    [Min(1f)] public float fleeSpeedMultiplier = 1.35f;
    [Range(0.1f, 1f)] public float exhaustedSpeedMultiplier = 0.65f;

    [Header("Life Cycle")]
    [Tooltip("Simulated seconds from birth before this animal can reproduce.")]
    [InspectorName("Maturity Time (Simulated Seconds)")]
    [Min(0f)] public float maturityTime = 30f;
    [SerializeField, Min(0f), Tooltip("Current age in simulated seconds.")]
    [InspectorName("Current Age (Simulated Seconds)")]
    private float currentAge;
    [Tooltip("Simulated seconds this animal can live before dying of old age.")]
    [InspectorName("Maximum Lifespan (Simulated Seconds)")]
    [Min(0.01f)] public float maxLifespan = 200f;

    [Header("Behavior Settings")]
    public float actionTimer = 5f;
    public float wanderRadius = 50f;
    [FormerlySerializedAs("foodSearchInterval")]
    [Min(0.05f)] public float decisionInterval = 0.25f;
    [Tooltip("Real-time CPU safeguard. Perception scans will not run more often than this even during fast-forward.")]
    [Min(0f)] public float minimumDecisionIntervalRealSeconds = 0.05f;
    [Tooltip("Allows food to be consumed by distance as well as trigger contact, preventing skipped meals during fast-forward.")]
    [Min(0.1f)] public float foodInteractionRange = 15f;
    [Tooltip("Longest real-time interval allowed between useful pursuit path refreshes.")]
    [Min(0.02f)] public float preyPathRefreshIntervalRealSeconds = 0.1f;
    [Tooltip("Desired maximum simulated seconds between pursuit path refreshes.")]
    [Min(0.1f)] public float preyPathRefreshIntervalSimulatedSeconds = 2f;
    [Tooltip("Hard CPU safeguard for pursuit path refreshes, even at extreme simulation speeds.")]
    [Min(0.02f)] public float minimumPreyPathRefreshIntervalRealSeconds = 0.05f;
    [Tooltip("A moving prey must shift at least this far before its existing path is refreshed.")]
    [Min(0f)] public float preyPathTargetMovementThreshold = 5f;
    [Tooltip("Abandon a voluntary hunt after this much simulated time without landing an attack.")]
    [Min(0.1f)] public float maximumUnproductiveHuntDuration = 10f;
    [Tooltip("Abandon a voluntary hunt when energy falls below this fraction of maximum energy.")]
    [Range(0f, 1f)] public float minimumHuntEnergyFraction = 0.15f;
    [Tooltip("How long an abandoned prey is ignored as a voluntary hunting target.")]
    [Min(0.1f)] public float failedHuntCooldown = 8f;
    [Tooltip("Give up on a plant or carcass after chasing it for this many simulated seconds without eating it.")]
    [Min(0.1f)] public float maximumFoodChaseDuration = 20f;
    [Tooltip("Simulated seconds that food the animal could not get to (across water, up a cliff) is ignored.")]
    [Min(0.1f)] public float unreachableFoodMemory = 30f;

    [Header("Predation")]
    [Min(0f)] public float attackEnergyCost = 2f;
    [Min(0.01f)] public float attackCooldown = 1f;
    [Tooltip("Maximum overdue attacks that can be resolved in one rendered frame during fast-forward.")]
    [Min(1)] public int maximumCatchUpAttacksPerFrame = 4;
    [Min(0.1f)] public float attackRange = 15f;
    [Tooltip("Fraction of attack range used as the NavMesh stopping distance. This prevents two combatants from trying to occupy the same point.")]
    [Range(0.1f, 0.95f)] public float attackApproachRangeFraction = 0.8f;
    [Min(0.1f)] public float fleeDistance = 80f;
    [Min(0.1f)] public float fleeDuration = 3f;
    [Tooltip("How long an attacker remains recognized as a threat after dealing damage.")]
    [Min(0.1f)] public float threatMemoryDuration = 5f;
    public bool allowCannibalism;
    [Tooltip("Prevents predation between parents, children, and siblings, including mutated offspring assigned to a new species.")]
    public bool avoidCloseKinPredation = true;
    [Range(0f, 1f)] public float carcassEnergyTransferEfficiency = 0.75f;
    [Min(1f)] public float carcassLifetime = 120f;
    [Min(0.1f)] public float carcassScale = 6f;

    [Header("Reproduction (Mitosis)")]
    [Range(0f, 1f)] public float reproductionThreshold = 0.95f;
    [Tooltip("Simulated seconds after giving birth before this animal can reproduce again. Without it, " +
             "every meal can become a birth and populations explode.")]
    [Min(0f)] public float reproductionCooldown = 60f;
    [Range(0f, 100f)] public float mutationChance = 5f;
    [Min(0f)] public float mutationMagnitude = 0.1f;

    [Header("Body Plan")]
    [Tooltip("How body parts sprout, grow, shrink and turn into other parts at birth.")]
    public BodyPlanMutation bodyPlanMutation = BodyPlanMutation.Default;
    [Tooltip("How far a full-size leg pair lifts the body off the ground at body height 1, in world units. " +
             "Standing taller also raises the animal's feeding reach.")]
    [Min(0f)] public float fullLegLength = 1.6f;
    [SerializeField, Tooltip("This animal's body parts and their sizes (0-1), for reference.")]
    private string bodyPlan;

    public AnimalTemperature ThermalResponse => thermalResponse;
    public AnimalGenome Genome => genome;
    public float CurrentEnergyDrainPerSecond => currentEnergyDrainPerSecond *
        (thermalResponse != null ? thermalResponse.EnergyDrainMultiplier : 1f);
    public float CurrentAge => currentAge;
    public int Generation => generation;
    public float RemainingLifespan => Mathf.Max(0f, maxLifespan - currentAge);
    public float AgeFraction => maxLifespan <= Mathf.Epsilon
        ? 1f
        : Mathf.Clamp01(currentAge / maxLifespan);
    public float HealthFraction => currentHealth / Mathf.Max(0.01f, maxHealth);
    public float StaminaFraction => currentStamina / Mathf.Max(0.01f, maxStamina);
    // A neck adds reach on top of the body's own; legs add it by lifting the whole animal.
    public float FeedingReach => Mathf.Max(0f, baseFeedingReach) * Mathf.Max(0.01f, bodyHeight) +
                                 bodyEffects.extraFeedingReach;
    public float CurrentFoodInteractionRange => foodInteractionRange * Mathf.Max(0.01f, bodyBulk);
    public float CurrentAttackRange => attackRange * Mathf.Max(0.01f, bodyBulk);
    // Includes the weight of body parts, which also feeds the carcass and the metabolism.
    public float BodyMassFactor
    {
        get
        {
            float bulk = Mathf.Max(0.01f, bodyBulk);
            float heightFactor = Mathf.Lerp(1f, Mathf.Max(0.01f, bodyHeight),
                                            Mathf.Clamp01(heightMassContribution));
            return bulk * bulk * heightFactor * (1f + bodyEffects.partsMass);
        }
    }
    public float BodyPartsMass => bodyEffects.partsMass;
    // How far the longest legs lift the body off the ground, in world units.
    public float LegLift => Mathf.Max(0f, fullLegLength) * bodyEffects.longestLegs * Mathf.Max(0.01f, bodyHeight);
    public float EstimatedCarcassRawEnergy => Mathf.Max(0f, currentEnergy) *
                                               carcassEnergyTransferEfficiency *
                                               BodyMassFactor;
    public bool IsAlive => !isDying && currentHealth > 0f && isActiveAndEnabled;
    public bool IsMature => currentAge >= Mathf.Max(0f, maturityTime);
    public float MaturityProgress => maturityTime <= Mathf.Epsilon
        ? 1f
        : Mathf.Clamp01(currentAge / maturityTime);
    public string DietClassification => ClassifyDiet(dietAffinity);

    public static string ClassifyDiet(float affinity)
    {
        return affinity < 1f / 3f ? "Herbivore" :
               affinity > 2f / 3f ? "Carnivore" : "Omnivore";
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private float timer;
    private float decisionTimer;
    private float decisionRealTimeCooldown;
    private float preyPathRefreshTimer;
    private float attackCooldownTimer;
    private float reproductionCooldownTimer;
    [SerializeField, Tooltip("Founders are generation 0; each child is one more than its parent.")]
    private int generation;
    private SeekFood lastPreyPathTarget;
    private Vector3 lastPreyPathDestination;
    private bool hasPreyPathDestination;
    private SeekFood temporarilyAvoidedPrey;
    private float failedHuntCooldownTimer;
    private float unproductiveHuntTimer;
    private float foodChaseTimer;
    private BodyPlanEffects bodyEffects = BodyPlanEffects.None;
    private AnimalBodyView bodyView;
    private bool registeredWithSpeciesManager;
    private bool isDying;
    private AgentDeathCause deathCause = AgentDeathCause.Other;
    private SeekFood firstParent;
    private SeekFood secondParent;
    private readonly List<ThreatMemory> rememberedThreats = new List<ThreatMemory>();
    // Food this animal recently failed to get to, ignored until the simulated time stored with it.
    private readonly List<UnreachableFood> unreachableFood = new List<UnreachableFood>();
    // Shared scratch list for perception; animals only update on the main thread.
    private static readonly List<FoodItem> nearbyFood = new List<FoodItem>();

    private sealed class ThreatMemory
    {
        public SeekFood attacker;
        public float timeRemaining;
    }

    private struct UnreachableFood
    {
        public FoodItem food;
        public float retryTime;
    }

    private enum State { Idling, Wandering, Chasing, Hunting, Fighting, Fleeing, Escaping }
    private State currentState = State.Wandering;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        thermalResponse = GetComponent<AnimalTemperature>();
        if (thermalResponse == null) thermalResponse = gameObject.AddComponent<AnimalTemperature>();
        InitializeGenome();
        minimumBodyBulk = Mathf.Max(0.01f, minimumBodyBulk);
        maximumBodyBulk = Mathf.Max(minimumBodyBulk, maximumBodyBulk);
        bodyBulk = Mathf.Clamp(bodyBulk, minimumBodyBulk, maximumBodyBulk);
        minimumBodyHeight = Mathf.Max(0.01f, minimumBodyHeight);
        maximumBodyHeight = Mathf.Max(minimumBodyHeight, maximumBodyHeight);
        bodyHeight = Mathf.Clamp(bodyHeight, minimumBodyHeight, maximumBodyHeight);
        InitializeBodyProportionReferences();
        ApplyBodyProportions();
        agent.speed = moveSpeed;

        decisionPolicy = GetComponent<AnimalDecisionPolicy>();
        if (decisionPolicy == null)
        {
            decisionPolicy = gameObject.AddComponent<UtilityDecisionPolicy>();
        }

        timer = actionTimer;
        decisionTimer = Random.Range(0f, Mathf.Max(0.05f, decisionInterval));
        decisionRealTimeCooldown = Random.Range(0f, Mathf.Max(0f, minimumDecisionIntervalRealSeconds));
        currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
        currentHealth = maxHealth;
        strength = Mathf.Max(0f, strength);
        maxStamina = Mathf.Max(0.01f, maxStamina);
        currentStamina = maxStamina;
        maturityTime = Mathf.Max(0f, maturityTime);
        currentAge = Mathf.Max(0f, currentAge);
        maxLifespan = Mathf.Max(0.01f, maxLifespan);
        dietAffinity = Mathf.Clamp01(dietAffinity);
        InitializeMetabolicReference();
        RefreshMetabolicRate();
        thermalResponse.ResetRuntimeState();
        RefreshMovementSpeed();

        // Join the parent's current species in case it split off while this animal was being born.
        if (firstParent != null && firstParent.speciesName != speciesName)
        {
            speciesName = firstParent.speciesName;
            speciesColor = firstParent.speciesColor;
            gameObject.name = $"Capsule_Species_{speciesName}";
        }
        ApplySpeciesColor();
        BuildBodyView();

        if (SpeciesManager.Instance != null)
        {
            SpeciesManager.Instance.RegisterAgent(this);
            registeredWithSpeciesManager = true;
        }
    }

    void OnDestroy()
    {
        if (registeredWithSpeciesManager && SpeciesManager.Instance != null)
        {
            SpeciesManager.Instance.DeregisterAgent(this, deathCause);
            registeredWithSpeciesManager = false;
        }
    }

    public void PrepareForAbstraction()
    {
        if (!registeredWithSpeciesManager || SpeciesManager.Instance == null) return;
        SpeciesManager.Instance.UnregisterAgentWithoutDeath(this);
        registeredWithSpeciesManager = false;
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;
        if (isDying || deltaTime <= 0f) return;
        float unscaledDeltaTime = Time.unscaledDeltaTime;
        currentAge += deltaTime;

        if (currentAge >= maxLifespan)
        {
            Die(AgentDeathCause.OldAge, null);
            return;
        }

        decisionRealTimeCooldown = Mathf.Max(0f, decisionRealTimeCooldown - unscaledDeltaTime);
        preyPathRefreshTimer = Mathf.Max(0f, preyPathRefreshTimer - unscaledDeltaTime);
        attackCooldownTimer = attackCooldownTimer > 0f
            ? attackCooldownTimer - deltaTime
            : 0f;
        reproductionCooldownTimer = Mathf.Max(0f, reproductionCooldownTimer - deltaTime);
        UpdateThreatMemory(deltaTime);
        UpdateFailedHuntMemory(deltaTime);
        thermalResponse.Tick(deltaTime);
        if (thermalResponse.ThermalDamagePerSecond > 0f)
        {
            currentHealth -= thermalResponse.ThermalDamagePerSecond * deltaTime;
            if (currentHealth <= 0f)
            {
                Die(thermalResponse.ColdStress > thermalResponse.HeatStress
                    ? AgentDeathCause.ColdExposure : AgentDeathCause.HeatExposure, null);
                return;
            }
        }
        UpdateStamina(deltaTime);
        RefreshMovementSpeed();

        if (currentEnergy > 0f)
        {
            currentEnergy = Mathf.Max(0f, currentEnergy - CurrentEnergyDrainPerSecond * deltaTime);
        }
        else
        {
            TakeDamage(starvationDamagePerSecond * deltaTime);
            if (isDying)
            {
                return;
            }
        }

        bool atPopulationCeiling = SpeciesManager.Instance != null && SpeciesManager.Instance.IsAtPopulationCeiling;
        if (IsMature && maxEnergy > 0f && currentEnergy >= maxEnergy * reproductionThreshold &&
            reproductionCooldownTimer <= 0f && !atPopulationCeiling)
        {
            ClearTargets();
            currentState = State.Idling;
            timer = actionTimer;
            Reproduce();
            decisionTimer = 0f;
        }

        if (currentState != State.Escaping && IsOutsideTerrainBounds())
        {
            BeginBoundaryEscape();
            return;
        }

        if (currentState == State.Escaping)
        {
            UpdateEscaping(deltaTime);
            return;
        }

        if (currentState == State.Fleeing)
        {
            UpdateFleeing(deltaTime);
            return;
        }

        if (TryAbandonUnproductiveHunt(deltaTime))
        {
            return;
        }

        UpdateDecision(deltaTime);
        ExecuteCurrentIntent(deltaTime);

        if (currentState == State.Chasing || currentState == State.Hunting ||
            currentState == State.Fighting)
        {
            return;
        }

        UpdateIdleAndWander(deltaTime);
    }

    void UpdateDecision(float deltaTime)
    {
        bool hasFoodTarget = IsCurrentFoodTargetAvailable();
        bool hasPreyTarget = IsViablePrey(currentPreyTarget);
        bool targetBecameInvalid = (currentState == State.Chasing && !hasFoodTarget) ||
                                   ((currentState == State.Hunting || currentState == State.Fighting) &&
                                    !hasPreyTarget);
        if (targetBecameInvalid)
        {
            ClearTargets();
            currentState = State.Idling;
            timer = actionTimer;
            TryResetPath();
        }

        decisionTimer -= deltaTime;
        if (decisionTimer > 0f || decisionRealTimeCooldown > 0f)
        {
            return;
        }

        AnimalPerception perception = BuildPerception();
        AnimalDecision decision = decisionPolicy != null
            ? decisionPolicy.Decide(this, perception)
            : new AnimalDecision(AgentIntent.Wander);
        ApplyDecision(decision);
        decisionTimer = Mathf.Max(0.05f, decisionInterval);
        decisionRealTimeCooldown = Mathf.Max(0f, minimumDecisionIntervalRealSeconds);
    }

    AnimalPerception BuildPerception()
    {
        AnimalPerception perception = new AnimalPerception
        {
            nearestPlantDistance = Mathf.Infinity,
            nearestMeatDistance = Mathf.Infinity,
            nearestPreyDistance = Mathf.Infinity,
            threats = new List<AnimalThreat>()
        };

        float visionRadiusSquared = visionRadius * visionRadius;
        FoodItem.FindNearby(transform.position, visionRadius, nearbyFood);
        foreach (FoodItem food in nearbyFood)
        {
            if (food == null || !food.IsAvailable)
            {
                continue;
            }

            if (!CanReachFood(food) || IsRememberedAsUnreachable(food))
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

        if (SpeciesManager.Instance != null)
        {
            foreach (SeekFood possiblePrey in SpeciesManager.Instance.ActiveAgents)
            {
                if (!IsViablePrey(possiblePrey))
                {
                    continue;
                }

                float distanceSquared = (transform.position - possiblePrey.transform.position).sqrMagnitude;
                float distance = Mathf.Sqrt(distanceSquared);
                if (!IsTemporarilyAvoidedPrey(possiblePrey) &&
                    distanceSquared <= visionRadiusSquared &&
                    distanceSquared < perception.nearestPreyDistance * perception.nearestPreyDistance)
                {
                    perception.nearestPrey = possiblePrey;
                    perception.nearestPreyDistance = distance;
                }

                bool recentlyAttacked = IsRememberedThreat(possiblePrey);
                bool isActivelyHunting = distanceSquared <= visionRadiusSquared &&
                                         possiblePrey.IsTargetingAsPrey(this);
                if (recentlyAttacked || isActivelyHunting)
                {
                    perception.threats.Add(new AnimalThreat(possiblePrey, distance,
                                                            isActivelyHunting, recentlyAttacked));
                }
            }
        }

        return perception;
    }

    void ApplyDecision(AnimalDecision decision)
    {
        State previousState = currentState;
        FoodItem previousFoodTarget = currentFoodTarget;
        SeekFood previousPreyTarget = currentPreyTarget;
        ClearTargets();

        switch (decision.intent)
        {
            case AgentIntent.SeekPlant:
            case AgentIntent.SeekMeat:
                if (decision.foodTarget != null && decision.foodTarget.IsAvailable)
                {
                    currentFoodTarget = decision.foodTarget;
                    currentState = State.Chasing;
                    bool isNewTarget = previousFoodTarget != currentFoodTarget;
                    if (isNewTarget)
                    {
                        foodChaseTimer = 0f;
                    }

                    if (isNewTarget || (!agent.pathPending && !agent.hasPath))
                    {
                        TrySetDestination(currentFoodTarget.GroundPosition);
                    }
                }
                break;

            case AgentIntent.HuntPrey:
                if (IsViablePrey(decision.preyTarget))
                {
                    currentPreyTarget = decision.preyTarget;
                    currentState = State.Hunting;
                    if (previousPreyTarget != currentPreyTarget)
                    {
                        ResetPreyPathTracking();
                        unproductiveHuntTimer = 0f;
                    }
                }
                break;

            case AgentIntent.FightThreat:
                if (IsViablePrey(decision.preyTarget))
                {
                    currentPreyTarget = decision.preyTarget;
                    currentState = State.Fighting;
                    if (previousPreyTarget != currentPreyTarget)
                    {
                        ResetPreyPathTracking();
                        unproductiveHuntTimer = 0f;
                    }
                    if (previousState != State.Fighting && SpeciesManager.Instance != null)
                    {
                        SpeciesManager.Instance.RecordThreatResponse(this, foughtBack: true);
                    }
                }
                break;

            case AgentIntent.Flee:
                if (IsViablePrey(decision.preyTarget))
                {
                    currentFleeThreat = decision.preyTarget;
                    currentFleeDirection = decision.fleeDirection;
                    currentState = State.Fleeing;
                    timer = 0f;
                    SetFleeDestination(currentFleeDirection, currentFleeThreat);
                    if (previousState != State.Fleeing && SpeciesManager.Instance != null)
                    {
                        SpeciesManager.Instance.RecordThreatResponse(this, foughtBack: false);
                    }
                }
                break;

            default:
                if (currentState != State.Idling && currentState != State.Wandering)
                {
                    currentState = State.Idling;
                    timer = actionTimer;
                    TryResetPath();
                }
                break;
        }
    }

    void ExecuteCurrentIntent(float deltaTime)
    {
        if (currentState == State.Chasing)
        {
            if (IsCurrentFoodTargetAvailable())
            {
                foodChaseTimer += deltaTime;
                float interactionRangeSquared = CurrentFoodInteractionRange * CurrentFoodInteractionRange;
                Vector3 horizontalOffset = transform.position - currentFoodTarget.transform.position;
                horizontalOffset.y = 0f;
                if (horizontalOffset.sqrMagnitude <= interactionRangeSquared &&
                    CanReachFood(currentFoodTarget))
                {
                    TryConsumeFood(currentFoodTarget);
                }
                else if (IsCurrentFoodOutOfReach())
                {
                    GiveUpOnCurrentFood();
                }
                else if (!agent.pathPending && !agent.hasPath)
                {
                    TrySetDestination(currentFoodTarget.GroundPosition);
                }
            }
            else
            {
                ClearTargets();
                currentState = State.Idling;
                decisionTimer = 0f;
            }
        }
        else if (currentState == State.Hunting || currentState == State.Fighting)
        {
            if (!IsViablePrey(currentPreyTarget))
            {
                ClearTargets();
                currentState = State.Idling;
                decisionTimer = 0f;
                return;
            }

            Vector3 preyPosition = currentPreyTarget.transform.position;
            float movementThreshold = Mathf.Max(0f, preyPathTargetMovementThreshold);
            bool targetChanged = lastPreyPathTarget != currentPreyTarget;
            bool targetMoved = !hasPreyPathDestination ||
                               (preyPosition - lastPreyPathDestination).sqrMagnitude >=
                               movementThreshold * movementThreshold;
            bool pathMissing = !agent.pathPending && !agent.hasPath;

            if (preyPathRefreshTimer <= 0f &&
                (targetChanged || targetMoved || pathMissing))
            {
                if (TrySetPreyDestination(preyPosition))
                {
                    lastPreyPathTarget = currentPreyTarget;
                    lastPreyPathDestination = preyPosition;
                    hasPreyPathDestination = true;
                }
                preyPathRefreshTimer = CalculatePreyPathRefreshInterval();
            }

            float attackRangeSquared = CurrentAttackRange * CurrentAttackRange;
            if ((transform.position - currentPreyTarget.transform.position).sqrMagnitude <= attackRangeSquared)
            {
                TryAttack(currentPreyTarget);
            }
        }
    }

    void TryAttack(SeekFood prey)
    {
        if (attackCooldownTimer > 0f || !IsViablePrey(prey) ||
            currentEnergy < attackEnergyCost || currentStamina < attackStaminaCost)
        {
            return;
        }

        float cooldown = Mathf.Max(0.01f, attackCooldown);
        int catchUpLimit = Mathf.Max(1, maximumCatchUpAttacksPerFrame);
        int attacksResolved = 0;

        while (attackCooldownTimer <= 0f && attacksResolved < catchUpLimit &&
               IsViablePrey(prey) && currentEnergy >= attackEnergyCost &&
               currentStamina >= attackStaminaCost)
        {
            currentEnergy = Mathf.Max(0f, currentEnergy - attackEnergyCost);
            currentStamina = Mathf.Max(0f, currentStamina - attackStaminaCost);
            attackCooldownTimer += cooldown;
            prey.TakeDamage(strength, this);
            attacksResolved++;
        }

        if (attacksResolved > 0 && currentState == State.Hunting)
        {
            unproductiveHuntTimer = 0f;
        }

        if (attacksResolved >= catchUpLimit && attackCooldownTimer <= 0f)
        {
            attackCooldownTimer = cooldown;
        }
    }

    float CalculatePreyPathRefreshInterval()
    {
        float maximumRealInterval = Mathf.Max(0.02f, preyPathRefreshIntervalRealSeconds);
        float minimumRealInterval = Mathf.Clamp(minimumPreyPathRefreshIntervalRealSeconds,
                                                0.02f, maximumRealInterval);
        float simulatedInterval = Mathf.Max(0.1f, preyPathRefreshIntervalSimulatedSeconds);
        float speed = Mathf.Max(0.01f, Time.timeScale);
        float simulationDrivenRealInterval = simulatedInterval / speed;
        return Mathf.Clamp(simulationDrivenRealInterval,
                           minimumRealInterval, maximumRealInterval);
    }

    void ResetPreyPathTracking()
    {
        preyPathRefreshTimer = 0f;
        lastPreyPathTarget = null;
        lastPreyPathDestination = Vector3.zero;
        hasPreyPathDestination = false;
    }

    void UpdateFailedHuntMemory(float deltaTime)
    {
        if (temporarilyAvoidedPrey == null)
        {
            failedHuntCooldownTimer = 0f;
            return;
        }

        failedHuntCooldownTimer -= deltaTime;
        if (failedHuntCooldownTimer <= 0f)
        {
            temporarilyAvoidedPrey = null;
            failedHuntCooldownTimer = 0f;
        }
    }

    bool IsTemporarilyAvoidedPrey(SeekFood possiblePrey)
    {
        return possiblePrey != null && possiblePrey == temporarilyAvoidedPrey &&
               failedHuntCooldownTimer > 0f;
    }

    bool TryAbandonUnproductiveHunt(float deltaTime)
    {
        if (currentState != State.Hunting || !IsViablePrey(currentPreyTarget))
        {
            unproductiveHuntTimer = 0f;
            return false;
        }

        unproductiveHuntTimer += deltaTime;
        float energyFraction = maxEnergy <= Mathf.Epsilon
            ? 0f
            : Mathf.Clamp01(currentEnergy / maxEnergy);
        bool timedOut = unproductiveHuntTimer >=
                        Mathf.Max(0.1f, maximumUnproductiveHuntDuration);
        bool energyTooLow = energyFraction <= Mathf.Clamp01(minimumHuntEnergyFraction);
        if (!timedOut && !energyTooLow)
        {
            return false;
        }

        SeekFood abandonedPrey = currentPreyTarget;
        temporarilyAvoidedPrey = abandonedPrey;
        failedHuntCooldownTimer = Mathf.Max(0.1f, failedHuntCooldown);
        unproductiveHuntTimer = 0f;
        ClearTargets();
        ResetPreyPathTracking();
        currentState = State.Idling;
        timer = 0f;
        decisionTimer = 0f;
        TryResetPath();

        if (SpeciesManager.Instance != null && SpeciesManager.Instance.ShouldLogLifecycleEvents)
        {
            string reason = energyTooLow ? "low energy" : "an unsuccessful pursuit";
            Debug.Log($"{gameObject.name} abandoned {abandonedPrey.gameObject.name} after {reason}.");
        }

        return true;
    }

    void UpdateThreatMemory(float deltaTime)
    {
        for (int index = rememberedThreats.Count - 1; index >= 0; index--)
        {
            ThreatMemory memory = rememberedThreats[index];
            memory.timeRemaining -= deltaTime;
            if (!IsViablePrey(memory.attacker) || memory.timeRemaining <= 0f)
            {
                rememberedThreats.RemoveAt(index);
            }
        }
    }

    void RememberThreat(SeekFood attacker)
    {
        if (!IsViablePrey(attacker))
        {
            return;
        }

        foreach (ThreatMemory memory in rememberedThreats)
        {
            if (memory.attacker == attacker)
            {
                memory.timeRemaining = Mathf.Max(0.1f, threatMemoryDuration);
                return;
            }
        }

        rememberedThreats.Add(new ThreatMemory
        {
            attacker = attacker,
            timeRemaining = Mathf.Max(0.1f, threatMemoryDuration)
        });
    }

    bool IsRememberedThreat(SeekFood possibleThreat)
    {
        foreach (ThreatMemory memory in rememberedThreats)
        {
            if (memory.attacker == possibleThreat && memory.timeRemaining > 0f)
            {
                return true;
            }
        }

        return false;
    }

    void UpdateStamina(float deltaTime)
    {
        if (currentState == State.Fleeing)
        {
            currentStamina = Mathf.Max(0f, currentStamina - fleeStaminaCostPerSecond * deltaTime);
        }
        else
        {
            currentStamina = Mathf.Min(maxStamina,
                                       currentStamina + staminaRecoveryPerSecond * deltaTime *
                                       (thermalResponse != null ? thermalResponse.StaminaRecoveryMultiplier : 1f));
        }
    }

    void RefreshMovementSpeed()
    {
        if (agent == null)
        {
            return;
        }

        float multiplier = 1f;
        if (currentState == State.Fleeing)
        {
            multiplier = currentStamina > 0f ? fleeSpeedMultiplier : exhaustedSpeedMultiplier;
        }

        float temperatureMultiplier = thermalResponse != null ? thermalResponse.MovementMultiplier : 1f;
        agent.speed = Mathf.Max(0.01f, moveSpeed * multiplier * temperatureMultiplier);
    }

    public float GetCurrentMovementSpeed()
    {
        return agent == null ? Mathf.Max(0.01f, moveSpeed) : Mathf.Max(0.01f, agent.speed);
    }

    public float GetCombatPower()
    {
        float attacksPerSecond = 1f / Mathf.Max(0.01f, attackCooldown);
        float staminaReadiness = 0.25f + 0.75f * Mathf.Clamp01(StaminaFraction);
        return Mathf.Max(0f, strength) * attacksPerSecond *
               Mathf.Clamp01(HealthFraction) * staminaReadiness;
    }

    void UpdateFleeing(float deltaTime)
    {
        timer += deltaTime;
        if (timer >= fleeDuration)
        {
            currentFleeThreat = null;
            currentFleeDirection = Vector3.zero;
            currentState = State.Idling;
            timer = actionTimer;
            decisionTimer = 0f;
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            SetFleeDestination(currentFleeDirection, currentFleeThreat);
        }
    }

    void SetFleeDestination(Vector3 direction, SeekFood fallbackThreat)
    {
        Vector3 away = direction;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f && IsViablePrey(fallbackThreat))
        {
            away = transform.position - fallbackThreat.transform.position;
            away.y = 0f;
        }

        if (away.sqrMagnitude < 0.01f)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            away = new Vector3(randomDirection.x, 0f, randomDirection.y);
        }

        Vector3 candidate = transform.position + away.normalized * fleeDistance;
        ClampCandidateToTerrain(ref candidate);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 25f, NavMesh.AllAreas))
        {
            TrySetDestination(hit.position);
        }
    }

    void UpdateEscaping(float deltaTime)
    {
        timer += deltaTime;
        if (timer >= actionTimer)
        {
            currentState = State.Idling;
            timer = actionTimer;
            decisionTimer = 0f;
        }
    }

    void UpdateIdleAndWander(float deltaTime)
    {
        if (currentState == State.Idling)
        {
            timer += deltaTime;
            if (timer >= actionTimer)
            {
                PickNextAction();
                timer = 0f;
            }
            return;
        }

        if (currentState == State.Wandering && !agent.pathPending &&
            (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.1f))
        {
            PickNextAction();
        }
    }

    void PickNextAction()
    {
        if (Random.value <= 0.5f)
        {
            currentState = State.Idling;
            TryResetPath();
            return;
        }

        currentState = State.Wandering;
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
        ClampCandidateToTerrain(ref candidate);

        float sampleDistance = Mathf.Clamp(wanderRadius, 1f, 25f);
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            TrySetDestination(hit.position);
        }
        else
        {
            currentState = State.Idling;
            TryResetPath();
        }
    }

    void ClampCandidateToTerrain(ref Vector3 candidate)
    {
        AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld != null)
        {
            candidate = terrainWorld.ClampToHabitableBounds(candidate);
            if (terrainWorld.TryFindWalkableGround(candidate, 30f, out Vector3 ground))
            {
                candidate = ground;
            }
            return;
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            return;
        }

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        const float borderInset = 6f;
        candidate.x = Mathf.Clamp(candidate.x, terrainPosition.x + borderInset, terrainPosition.x + terrainSize.x - borderInset);
        candidate.z = Mathf.Clamp(candidate.z, terrainPosition.z + borderInset, terrainPosition.z + terrainSize.z - borderInset);
        candidate.y = terrainPosition.y + terrain.SampleHeight(candidate);
    }

    bool IsCurrentFoodTargetAvailable()
    {
        return currentFoodTarget != null && currentFoodTarget.IsAvailable;
    }

    // True once chasing the current food can't work: its route stops short of it (across water or up a cliff),
    // the animal has walked as far as the route goes and still can't eat it, or the chase is taking far longer
    // than crossing its whole field of view would. Without this, relatives that all pick the same unreachable
    // plant wait at the shore until they starve.
    bool IsCurrentFoodOutOfReach()
    {
        float speed = Mathf.Max(0.1f, GetCurrentMovementSpeed());
        float allowedChaseTime = Mathf.Max(maximumFoodChaseDuration, 3f * visionRadius / speed);
        if (foodChaseTimer >= allowedChaseTime)
        {
            return true;
        }

        if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending)
        {
            return false;
        }

        if (agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            Vector3 shortfall = agent.pathEndPosition - currentFoodTarget.transform.position;
            shortfall.y = 0f;
            float range = CurrentFoodInteractionRange;
            if (shortfall.sqrMagnitude > range * range)
            {
                return true;
            }
        }

        return !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.5f;
    }

    void GiveUpOnCurrentFood()
    {
        FoodItem abandonedFood = currentFoodTarget;
        RememberUnreachableFood(abandonedFood);
        ClearTargets();
        foodChaseTimer = 0f;
        currentState = State.Idling;
        timer = 0f;
        decisionTimer = 0f;
        TryResetPath();

        if (SpeciesManager.Instance != null && SpeciesManager.Instance.ShouldLogLifecycleEvents)
        {
            Debug.Log($"{gameObject.name} gave up on {abandonedFood.gameObject.name}, which it could not reach.");
        }
    }

    void RememberUnreachableFood(FoodItem food)
    {
        if (food == null)
        {
            return;
        }

        // A short memory is enough; the oldest entry makes room.
        if (unreachableFood.Count >= 16)
        {
            unreachableFood.RemoveAt(0);
        }

        unreachableFood.Add(new UnreachableFood
        {
            food = food,
            retryTime = Time.time + Mathf.Max(0.1f, unreachableFoodMemory)
        });
    }

    bool IsRememberedAsUnreachable(FoodItem food)
    {
        float now = Time.time;
        for (int index = unreachableFood.Count - 1; index >= 0; index--)
        {
            UnreachableFood entry = unreachableFood[index];
            if (entry.food == null || now >= entry.retryTime)
            {
                unreachableFood.RemoveAt(index);
            }
            else if (entry.food == food)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTargetingAsPrey(SeekFood possibleTarget)
    {
        return possibleTarget != null && currentPreyTarget == possibleTarget &&
               (currentState == State.Hunting || currentState == State.Fighting);
    }

    public bool IsFightingThreat(SeekFood possibleThreat)
    {
        return possibleThreat != null && currentState == State.Fighting &&
               currentPreyTarget == possibleThreat;
    }

    public bool IsViablePrey(SeekFood possiblePrey)
    {
        if (possiblePrey == null || possiblePrey == this || !possiblePrey.IsAlive)
        {
            return false;
        }

        if (avoidCloseKinPredation && IsCloseRelative(possiblePrey))
        {
            return false;
        }

        return allowCannibalism || possiblePrey.speciesName != speciesName;
    }

    bool IsCloseRelative(SeekFood other)
    {
        if (other == null)
        {
            return false;
        }

        bool isParentOrChild = other == firstParent || other == secondParent ||
                               other.firstParent == this || other.secondParent == this;
        if (isParentOrChild)
        {
            return true;
        }

        bool sharesFirstParent = firstParent != null &&
                                 (other.firstParent == firstParent || other.secondParent == firstParent);
        bool sharesSecondParent = secondParent != null &&
                                  (other.firstParent == secondParent || other.secondParent == secondParent);
        return sharesFirstParent || sharesSecondParent;
    }

    public float GetDigestionEfficiency(FoodType foodType)
    {
        float affinity = Mathf.Clamp01(dietAffinity);
        float minimum = Mathf.Clamp01(minimumDietEfficiency);
        return foodType == FoodType.Plant
            ? minimum + (1f - minimum) * (1f - affinity)
            : minimum + (1f - minimum) * affinity;
    }

    public bool CanReachFood(FoodItem food)
    {
        if (food == null)
        {
            return false;
        }

        float verticalReachNeeded = Mathf.Max(0f, food.transform.position.y - transform.position.y);
        float requiredReach = Mathf.Max(verticalReachNeeded, Mathf.Max(0f, food.requiredFeedingReach));
        return FeedingReach >= requiredReach;
    }

    public float EstimateDigestibleEnergy(FoodItem food)
    {
        return !CanReachFood(food)
            ? 0f
            : food.nutritionValue * GetDigestionEfficiency(food.foodType);
    }

    bool TryConsumeFood(FoodItem food)
    {
        if (food == null || !CanReachFood(food) || currentEnergy >= maxEnergy ||
            !food.TryConsume(out float rawNutrition, out FoodType foodType))
        {
            return false;
        }

        float digestibleEnergy = rawNutrition * GetDigestionEfficiency(foodType);
        float acceptedEnergy = Mathf.Min(maxEnergy - currentEnergy, digestibleEnergy);
        currentEnergy += acceptedEnergy;

        if (SpeciesManager.Instance != null)
        {
            SpeciesManager.Instance.RecordConsumption(this, foodType, rawNutrition, acceptedEnergy);
        }

        ClearTargets();
        currentState = State.Idling;
        timer = actionTimer;
        decisionTimer = 0f;
        TryResetPath();
        return true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Food"))
        {
            TryConsumeFood(other.GetComponent<FoodItem>());
        }
        else if (other.CompareTag("Border") && currentState != State.Escaping)
        {
            BeginBoundaryEscape();
        }
    }

    bool IsOutsideTerrainBounds()
    {
        AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld != null)
        {
            return !terrainWorld.IsInsideHabitableBounds(transform.position);
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            return false;
        }

        Vector3 position = transform.position;
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        return position.x < terrainPosition.x || position.x > terrainPosition.x + terrainSize.x ||
               position.z < terrainPosition.z || position.z > terrainPosition.z + terrainSize.z;
    }

    void BeginBoundaryEscape()
    {
        ClearTargets();
        currentState = State.Escaping;
        timer = 0f;

        AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld != null)
        {
            if (terrainWorld.TryFindWalkableGroundTowardCenter(
                transform.position, Mathf.Max(50f, wanderRadius), out Vector3 destination) &&
                NavMesh.SamplePosition(destination, out NavMeshHit terrainHit, 25f, NavMesh.AllAreas))
            {
                TrySetDestination(terrainHit.position);
            }
            return;
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            return;
        }

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        Vector3 center = new Vector3(terrainPosition.x + terrainSize.x / 2f,
                                     transform.position.y,
                                     terrainPosition.z + terrainSize.z / 2f);
        if (NavMesh.SamplePosition(center, out NavMeshHit hit, 25f, NavMesh.AllAreas))
        {
            TrySetDestination(hit.position);
        }
    }

    public void TakeDamage(float amount, SeekFood attacker = null)
    {
        if (isDying || amount <= 0f)
        {
            return;
        }

        if (IsViablePrey(attacker))
        {
            RememberThreat(attacker);
            ClearTargets();
            currentState = State.Idling;
            decisionTimer = 0f;
            TryResetPath();
        }

        // Plates absorb part of every attack, but not starvation.
        if (attacker != null)
        {
            amount *= bodyEffects.damageTakenMultiplier;
        }

        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            Die(attacker == null ? AgentDeathCause.Starvation : AgentDeathCause.Predation, attacker);
        }
    }

    void Die(AgentDeathCause cause, SeekFood killer)
    {
        if (isDying)
        {
            return;
        }

        isDying = true;
        deathCause = cause;
        float carcassEnergy = EstimatedCarcassRawEnergy;
        currentEnergy = 0f;

        if (carcassEnergy > 0f)
        {
            float carcassLinearScale = Mathf.Pow(BodyMassFactor, 1f / 3f);
            FoodItem.CreateCarcass(transform.position, carcassEnergy, speciesName,
                                   carcassLifetime, carcassScale * carcassLinearScale);
        }

        if (cause == AgentDeathCause.Predation && SpeciesManager.Instance != null)
        {
            SpeciesManager.Instance.RecordKill(killer);
        }

        if (SpeciesManager.Instance != null && SpeciesManager.Instance.ShouldLogLifecycleEvents)
        {
            Debug.Log($"{gameObject.name} died from {cause}.");
        }
        Destroy(gameObject);
    }

    void Reproduce()
    {
        float childEnergy = currentEnergy * 0.5f;
        currentEnergy -= childEnergy;
        reproductionCooldownTimer = reproductionCooldown;

        Vector2 offset = Random.insideUnitCircle * 3f;
        Vector3 spawnPosition = transform.position + new Vector3(offset.x, 0f, offset.y);
        AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld != null &&
            terrainWorld.TryFindWalkableGround(spawnPosition, 12f, out Vector3 groundPosition))
        {
            spawnPosition = groundPosition;
        }
        if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            spawnPosition = hit.position;
        }
        else
        {
            spawnPosition = transform.position;
        }

        GameObject child = Instantiate(gameObject, spawnPosition, transform.rotation);
        SeekFood childScript = child.GetComponent<SeekFood>();
        if (childScript == null)
        {
            return;
        }

        childScript.currentEnergy = childEnergy;
        childScript.currentAge = 0f;
        childScript.generation = generation + 1;
        childScript.firstParent = this;
        childScript.secondParent = null;
        // The child copies this genome into its traits when it starts.
        childScript.genome = genome.CreateMutatedCopy(mutationChance, mutationMagnitude, bodyPlanMutation,
                                                      out bool mutated);

        // Offspring start in their parent's species. SpeciesManager splits species whose members drift
        // genetically apart.
        childScript.speciesName = speciesName;
        childScript.speciesColor = speciesColor;
        child.name = $"Capsule_Species_{childScript.speciesName}";

        if (SpeciesManager.Instance != null)
        {
            SpeciesManager.Instance.RecordReproduction(this, mutated);
        }
    }

    void InitializeGenome()
    {
        // Offspring receive a genome from their parent. Founders placed by a scene or the demo
        // bootstrap describe their genes through their inspector values instead.
        if (genome == null || !genome.IsValid)
        {
            genome = CaptureGenome();
        }

        ApplyGenome();
        ApplyBodyPlan();
    }

    // Body parts adjust the traits the genes have just set (see AnimalBodyPlan). The genome keeps the
    // unadjusted values, so parts never compound over generations.
    void ApplyBodyPlan()
    {
        bodyEffects = AnimalBodyPlan.Evaluate(genome);
        moveSpeed *= bodyEffects.speedMultiplier;
        strength *= bodyEffects.strengthMultiplier;
        visionRadius *= bodyEffects.visionMultiplier;
        thermalResponse.coldTolerance = Mathf.Max(0f, thermalResponse.coldTolerance + bodyEffects.coldToleranceChange);
        thermalResponse.heatTolerance = Mathf.Max(0f, thermalResponse.heatTolerance + bodyEffects.heatToleranceChange);
        bodyPlan = AnimalBodyPlan.Describe(genome);
    }

    AnimalGenome CaptureGenome()
    {
        AnimalGenome captured = AnimalGenome.Create();
        captured[AnimalGene.MoveSpeed] = moveSpeed;
        captured[AnimalGene.Strength] = strength;
        captured[AnimalGene.BodyBulk] = bodyBulk;
        captured[AnimalGene.BodyHeight] = bodyHeight;
        captured[AnimalGene.VisionRadius] = visionRadius;
        captured[AnimalGene.MaxEnergy] = maxEnergy;
        captured[AnimalGene.MaxStamina] = maxStamina;
        captured[AnimalGene.MaxHealth] = maxHealth;
        captured[AnimalGene.MaturityTime] = maturityTime;
        captured[AnimalGene.MaxLifespan] = maxLifespan;
        captured[AnimalGene.DietAffinity] = dietAffinity;
        captured[AnimalGene.PreferredTemperature] = thermalResponse.preferredTemperature;
        captured[AnimalGene.ColdTolerance] = thermalResponse.coldTolerance;
        captured[AnimalGene.HeatTolerance] = thermalResponse.heatTolerance;
        return captured;
    }

    void ApplyGenome()
    {
        moveSpeed = genome[AnimalGene.MoveSpeed];
        strength = genome[AnimalGene.Strength];
        bodyBulk = genome[AnimalGene.BodyBulk];
        bodyHeight = genome[AnimalGene.BodyHeight];
        visionRadius = genome[AnimalGene.VisionRadius];
        maxEnergy = genome[AnimalGene.MaxEnergy];
        maxStamina = genome[AnimalGene.MaxStamina];
        maxHealth = genome[AnimalGene.MaxHealth];
        maturityTime = genome[AnimalGene.MaturityTime];
        maxLifespan = genome[AnimalGene.MaxLifespan];
        dietAffinity = genome[AnimalGene.DietAffinity];
        thermalResponse.preferredTemperature = genome[AnimalGene.PreferredTemperature];
        thermalResponse.coldTolerance = genome[AnimalGene.ColdTolerance];
        thermalResponse.heatTolerance = genome[AnimalGene.HeatTolerance];
    }

    void InitializeBodyProportionReferences()
    {
        if (referenceLocalScale == Vector3.zero)
        {
            referenceLocalScale = transform.localScale;
            referenceAgentStoppingDistance = Mathf.Max(0f, agent.stoppingDistance);
        }

        if (referenceAgentRadius <= 0f || referenceAgentHeight <= 0f)
        {
            referenceAgentRadius = Mathf.Max(0.01f, agent.radius);
            referenceAgentHeight = Mathf.Max(0.01f, agent.height);
            referenceAgentBaseOffset = agent.baseOffset;
        }
    }

    void ApplyBodyProportions()
    {
        bodyBulk = Mathf.Clamp(bodyBulk, minimumBodyBulk, maximumBodyBulk);
        bodyHeight = Mathf.Clamp(bodyHeight, minimumBodyHeight, maximumBodyHeight);
        transform.localScale = Vector3.Scale(referenceLocalScale,
                                             new Vector3(bodyBulk, bodyHeight, bodyBulk));

        agent.radius = Mathf.Max(0.01f, referenceAgentRadius * bodyBulk);
        agent.height = Mathf.Max(0.01f, referenceAgentHeight * bodyHeight);
        // Legs stand the body up off the ground. The agent's offset is in the scaled local space.
        agent.baseOffset = referenceAgentBaseOffset * bodyHeight + LegLift / Mathf.Max(0.01f, transform.lossyScale.y);
    }

    void InitializeMetabolicReference()
    {
        if (referenceMoveSpeed <= 0f) referenceMoveSpeed = Mathf.Max(0.01f, moveSpeed);
        if (referenceStrength <= 0f) referenceStrength = Mathf.Max(0.01f, strength);
        if (referenceBodyBulk <= 0f) referenceBodyBulk = Mathf.Max(0.01f, bodyBulk);
        if (referenceBodyHeight <= 0f) referenceBodyHeight = Mathf.Max(0.01f, bodyHeight);
        if (referenceVisionRadius <= 0f) referenceVisionRadius = Mathf.Max(0.01f, visionRadius);
        if (referenceMaxEnergy <= 0f) referenceMaxEnergy = Mathf.Max(0.01f, maxEnergy);
        if (referenceMaxStamina <= 0f) referenceMaxStamina = Mathf.Max(0.01f, maxStamina);
        if (referenceMaxHealth <= 0f) referenceMaxHealth = Mathf.Max(0.01f, maxHealth);
    }

    public void RefreshMetabolicRate()
    {
        InitializeMetabolicReference();

        float totalWeight = speedMetabolicWeight + strengthMetabolicWeight + bodyMassMetabolicWeight +
                            visionMetabolicWeight + energyCapacityMetabolicWeight +
                            staminaCapacityMetabolicWeight + healthMetabolicWeight;
        if (totalWeight <= Mathf.Epsilon)
        {
            currentEnergyDrainPerSecond = Mathf.Max(0f, baseEnergyDrainPerSecond);
            return;
        }

        float bulkRatio = Mathf.Max(0.01f, bodyBulk) / referenceBodyBulk;
        float heightRatio = Mathf.Max(0.01f, bodyHeight) / referenceBodyHeight;
        // Body parts add their weight; their other advantages (speed, strength, vision) are charged below.
        float relativeMassBurden = bulkRatio * bulkRatio *
                                   Mathf.Lerp(1f, heightRatio,
                                              Mathf.Clamp01(heightMassContribution)) *
                                   (1f + bodyEffects.partsMass);

        float weightedTraitCost =
            (Mathf.Max(0.01f, moveSpeed) / referenceMoveSpeed) * speedMetabolicWeight +
            (Mathf.Max(0.01f, strength) / referenceStrength) * strengthMetabolicWeight +
            relativeMassBurden * bodyMassMetabolicWeight +
            (Mathf.Max(0.01f, visionRadius) / referenceVisionRadius) * visionMetabolicWeight +
            (Mathf.Max(0.01f, maxEnergy) / referenceMaxEnergy) * energyCapacityMetabolicWeight +
            (Mathf.Max(0.01f, maxStamina) / referenceMaxStamina) * staminaCapacityMetabolicWeight +
            (Mathf.Max(0.01f, maxHealth) / referenceMaxHealth) * healthMetabolicWeight;

        float minimum = Mathf.Max(0f, minimumMetabolicMultiplier);
        float maximum = Mathf.Max(minimum, maximumMetabolicMultiplier);
        float multiplier = Mathf.Clamp(weightedTraitCost / totalWeight, minimum, maximum);
        currentEnergyDrainPerSecond = Mathf.Max(0f, baseEnergyDrainPerSecond) * multiplier;
    }

    // Called by SpeciesManager when this animal's group splits off into a new species.
    public void AssignSpecies(string newSpeciesName, Color newSpeciesColor)
    {
        speciesName = newSpeciesName;
        speciesColor = newSpeciesColor;
        gameObject.name = $"Capsule_Species_{speciesName}";
        ApplySpeciesColor();
    }

    void ApplySpeciesColor()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, speciesColor);
        propertyBlock.SetColor(ColorId, speciesColor);
        meshRenderer.SetPropertyBlock(propertyBlock);

        if (bodyView != null)
        {
            bodyView.SetColor(speciesColor);
        }
    }

    // Draws this animal's body parts. Offspring copy their parent's part objects, so every animal
    // rebuilds them from its own genome.
    void BuildBodyView()
    {
        if (bodyView == null && !TryGetComponent(out bodyView))
        {
            bodyView = gameObject.AddComponent<AnimalBodyView>();
        }

        bodyView.Build(genome, Mathf.Max(0f, fullLegLength) * Mathf.Max(0.01f, bodyHeight), speciesColor);
    }

    void ClearTargets()
    {
        currentFoodTarget = null;
        currentPreyTarget = null;
    }

    bool TrySetDestination(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        agent.stoppingDistance = Mathf.Max(0f, referenceAgentStoppingDistance);
        return agent.SetDestination(destination);
    }

    bool TrySetPreyDestination(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        float approachDistance = CurrentAttackRange *
                                 Mathf.Clamp(attackApproachRangeFraction, 0.1f, 0.95f);
        agent.stoppingDistance = Mathf.Max(referenceAgentStoppingDistance, approachDistance);
        return agent.SetDestination(destination);
    }

    void TryResetPath()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.stoppingDistance = Mathf.Max(0f, referenceAgentStoppingDistance);
            agent.ResetPath();
        }
    }
}
