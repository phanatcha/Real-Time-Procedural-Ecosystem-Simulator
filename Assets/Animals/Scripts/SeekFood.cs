using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // One animal. The class is split into partial files by concern, all in this folder:
    // SeekFood.cs (this file) holds its identity, state, Start and Update, and its decisions; SeekFood.Energy.cs,
    // .Senses, .Feeding, .Water, .Movement, .Danger, .Reproduction, .Genes and .LifeCycle hold the rest. Unity
    // knows the component by this file, so it keeps its name.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(MeshRenderer))]
    public partial class SeekFood : MonoBehaviour, IEcosystemMaterializationLifecycle
    {
        private NavMeshAgent agent;
        private AnimalDecisionPolicy decisionPolicy;
        private AnimalTemperature thermalResponse;
        private AnimalGenome genome;
        private FoodItem currentFoodTarget;
        private SeekFood currentPreyTarget;
        private SeekFood currentFleeThreat;
        private Vector3 currentFleeDirection;
        // The partner this animal is heading for or courting.
        private SeekFood currentMate;

        [Header("Species Identity")]
        public string speciesName = "A";
        public Color speciesColor = Color.white;

        [Header("Health Stats")]
        public float maxHealth = 100f;
        public float currentHealth;

        [Header("Agent Traits")]
        public float visionRadius = 100f;
        public float moveSpeed = 24f;
        [FormerlySerializedAs("attackDamage")]
        [Tooltip("Damage dealt per successful attack and the main offensive combat-power trait.")]
        [Min(0f)] public float strength = 25f;

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

        public AnimalTemperature ThermalResponse => thermalResponse;
        public AnimalGenome Genome => genome;
        public int Generation => generation;
        public SeekFood FirstParent => firstParent;
        public SeekFood SecondParent => secondParent;
        // The animal's life record (item 57). None of it is serialized, so a child copied from its parent starts blank.
        // The ID is unique within one world and given by the Species Manager when the animal joins; 0 means none.
        // Parent IDs outlive the parents themselves.
        public int AnimalId { get; private set; }
        public int FirstParentId { get; private set; }
        public int SecondParentId { get; private set; }
        // Simulated seconds since the world started. Negative for a founder placed with an age.
        public float BirthTime { get; private set; }
        // Where it joined the simulation: its birthplace, or where a founder was placed.
        public Vector3 BirthPosition { get; private set; }
        // Children born to it so far, as either parent.
        public int YoungBorn { get; private set; }
        public string Origin => fromOffscreenPopulation ? "offscreen" : bornInSimulation ? "born" : "founder";

        public void BeginLifeRecord(int id, float worldTime)
        {
            AnimalId = id;
            BirthTime = worldTime - currentAge;
            BirthPosition = transform.position;
        }
        // What the animal is doing now, for the inspector.
        public string Activity => currentState.ToString();
        public float HealthFraction => currentHealth / Mathf.Max(0.01f, maxHealth);
        public bool IsAlive => !isDying && currentHealth > 0f && isActiveAndEnabled;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float timer;
        private float decisionTimer;
        private float decisionRealTimeCooldown;
        [SerializeField, Tooltip("Founders are generation 0; each child is one more than its parent.")]
        private int generation;
        private bool registeredWithSpeciesManager;
        private SeekFood firstParent;
        private SeekFood secondParent;
        // Set when this animal comes from an off-screen population instead of being born.
        private bool fromOffscreenPopulation;

        private enum State { Idling, Wandering, Chasing, Hunting, Fighting, Fleeing, Escaping, SeekingMate, Courting, SeekingWater, Drinking, Resting }
        private State currentState = State.Wandering;

        void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            thermalResponse = GetComponent<AnimalTemperature>();
            if (thermalResponse == null) thermalResponse = gameObject.AddComponent<AnimalTemperature>();
            InitializeGenome();
            if (perkEffects.glows && !countedAsGlowing)
            {
                countedAsGlowing = true;
                glowingAnimals++;
            }
            minimumBodyBulk = Mathf.Max(0.01f, minimumBodyBulk);
            maximumBodyBulk = Mathf.Max(minimumBodyBulk, maximumBodyBulk);
            bodyBulk = Mathf.Clamp(bodyBulk, minimumBodyBulk, maximumBodyBulk);
            minimumBodyHeight = Mathf.Max(0.01f, minimumBodyHeight);
            maximumBodyHeight = Mathf.Max(minimumBodyHeight, maximumBodyHeight);
            bodyHeight = Mathf.Clamp(bodyHeight, minimumBodyHeight, maximumBodyHeight);
            InitializeBodyProportionReferences();
            ApplyBodyProportions();
            ConfigureWaterAccess();
            agent.speed = moveSpeed;

            decisionPolicy = GetComponent<AnimalDecisionPolicy>();
            if (decisionPolicy == null)
            {
                decisionPolicy = gameObject.AddComponent<UtilityDecisionPolicy>();
            }

            timer = actionTimer;
            decisionTimer = Random.Range(0f, Mathf.Max(0.05f, decisionInterval));
            decisionRealTimeCooldown = Random.Range(0f, Mathf.Max(0f, minimumDecisionIntervalRealSeconds));
            if (arrivalEnergyFraction >= 0f)
            {
                currentEnergy = arrivalEnergyFraction * maxEnergy;
                arrivalEnergyFraction = -1f;
            }
            float energyGiven = currentEnergy;
            currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
            if (EnergyLedger.Enabled)
            {
                // A child's share was entered when it was born (Bear); founders and arrivals bring theirs.
                if (!bornInSimulation) EnergyLedger.Record(EnergyFlow.Start, energyGiven);
                EnergyLedger.Record(EnergyFlow.OverCapacity, (double)currentEnergy - energyGiven);
            }
            ledgerEnergy = currentEnergy;
            if (currentHydration < 0f) currentHydration = maxHydration;
            currentHydration = Mathf.Min(currentHydration, maxHydration);
            currentHealth = maxHealth;
            strength = Mathf.Max(0f, strength);
            maxStamina = Mathf.Max(0.01f, maxStamina);
            currentStamina = maxStamina;
            maturityTime = Mathf.Max(0f, maturityTime);
            currentAge = Mathf.Max(0f, currentAge);
            maxLifespan = Mathf.Max(0.01f, maxLifespan);
            ageingOnset = Mathf.Max(0f, ageingOnset);
            dietAffinity = Mathf.Clamp01(dietAffinity);
            InitializeMetabolicReference();
            BeginGrowth();
            // A founder or an arrival is placed with the body it has; a newborn's is built from what it pays to grow.
            bodyEnergyPaid = bornInSimulation ? 0f : GrowthCostPerMass * BodyMassFactor;
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
                SpeciesManager.Instance.RegisterAgent(this, countAsBirth: !fromOffscreenPopulation);
                registeredWithSpeciesManager = true;
            }
        }

        // Turns a copy of an animal into a member of an off-screen population that has come near the camera.
        // Call it straight after Instantiate, before the animal's first frame, as Reproduce does for newborns.
        // The animal keeps the copy's metabolic reference (the founders') but takes this genome, age and energy.
        public void PrepareFromOffscreenPopulation(AnimalGenome inheritedGenome, string species, Color color,
                                                   int animalGeneration, float age, float energyFraction)
        {
            genome = inheritedGenome.Clone();
            speciesName = species;
            speciesColor = color;
            generation = animalGeneration;
            currentAge = Mathf.Max(0f, age);
            currentEnergy = Mathf.Clamp01(energyFraction) * inheritedGenome[AnimalGene.MaxEnergy];
            // The store depends on the body's size (item 67), which is known only once the genome is applied.
            arrivalEnergyFraction = Mathf.Clamp01(energyFraction);
            firstParent = null;
            secondParent = null;
            fromOffscreenPopulation = true;
            gameObject.name = $"Capsule_Species_{speciesName}";
        }

        void OnDestroy()
        {
            if (countedAsGlowing)
            {
                countedAsGlowing = false;
                glowingAnimals = Mathf.Max(0, glowingAnimals - 1);
            }
            // An animal removed without dying (a scene unloading, a test) takes its store out of the ledger.
            if (EnergyLedger.Enabled && !isDying && !leftTheLedger)
                EnergyLedger.Record(EnergyFlow.LostAtDeath, -(double)currentEnergy);
            if (registeredWithSpeciesManager && SpeciesManager.Instance != null)
            {
                SpeciesManager.Instance.DeregisterAgent(this, deathCause);
                registeredWithSpeciesManager = false;
            }
        }

        public void PrepareForAbstraction()
        {
            if (!registeredWithSpeciesManager || SpeciesManager.Instance == null) return;
            // Its store leaves the live animals' books with it.
            if (EnergyLedger.Enabled) EnergyLedger.Record(EnergyFlow.LostAtDeath, -(double)currentEnergy);
            leftTheLedger = true;
            SpeciesManager.Instance.UnregisterAgentWithoutDeath(this);
            registeredWithSpeciesManager = false;
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (isDying || deltaTime <= 0f) return;
            if (EnergyLedger.Enabled && currentEnergy != ledgerEnergy)
            {
                EnergyLedger.Record(EnergyFlow.Untracked, (double)currentEnergy - ledgerEnergy);
                EnergyLedger.Violation($"{name}'s energy changed by {currentEnergy - ledgerEnergy:0.###} outside the ledger");
                ledgerEnergy = currentEnergy;
            }
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            currentAge += deltaTime;
            if (growth < 1f) UpdateGrowth();

            if (currentAge >= maxLifespan)
            {
                Die(AgentDeathCause.OldAge, null);
                return;
            }

            // Ageing (item 68): past its onset the animal's vigour falls; it caps health and ends the animal's life.
            if (enableAgeing && currentAge > ageingOnset)
            {
                float vigour = Vigour;
                if (vigour <= 0f)
                {
                    Die(AgentDeathCause.OldAge, null);
                    return;
                }
                currentHealth = Mathf.Min(currentHealth, maxHealth * vigour);
            }

            decisionRealTimeCooldown = Mathf.Max(0f, decisionRealTimeCooldown - unscaledDeltaTime);
            preyPathRefreshTimer = Mathf.Max(0f, preyPathRefreshTimer - unscaledDeltaTime);
            attackCooldownTimer = attackCooldownTimer > 0f
                ? attackCooldownTimer - deltaTime
                : 0f;
            reproductionCooldownTimer = Mathf.Max(0f, reproductionCooldownTimer - deltaTime);
            UpdateThreatMemory(deltaTime);
            UpdateFailedHuntMemory(deltaTime);
            thermalResponse.swingFelt = currentState == State.Resting && restSettled ? restSwingFelt : 1f;
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
            if (UpdateFireExposure(deltaTime))
            {
                return;
            }
            UpdateStamina(deltaTime);
            UpdateWater(deltaTime);
            UpdateSnow(deltaTime);
            RefreshMovementSpeed();

            if (currentEnergy > 0f)
            {
                SetEnergy(Mathf.Max(0f, currentEnergy - CurrentEnergyDrainPerSecond * deltaTime), EnergyFlow.Upkeep);
            }
            else
            {
                TakeDamage(starvationDamagePerSecond * deltaTime);
                if (isDying)
                {
                    return;
                }
            }

            if (UpdateThirst(deltaTime))
            {
                return;
            }

            // Regeneration slowly heals the animal while it has energy.
            if (perkEffects.regenerationPerSecond > 0f && currentEnergy > 0f && currentHealth < maxHealth)
            {
                currentHealth = Mathf.Min(maxHealth * Vigour,
                                          currentHealth + perkEffects.regenerationPerSecond * maxHealth * deltaTime);
            }

            // Courting animals stand still and don't react to threats until the courtship ends or one is attacked.
            if (currentState == State.Courting)
            {
                UpdateCourtship(deltaTime);
                return;
            }

            UpdateBreeding(deltaTime);

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
                currentState == State.Fighting || currentState == State.SeekingMate ||
                currentState == State.SeekingWater || currentState == State.Drinking ||
                currentState == State.Resting)
            {
                return;
            }

            UpdateIdleAndWander(deltaTime);
        }

        void UpdateDecision(float deltaTime)
        {
            bool hasFoodTarget = IsCurrentFoodTargetAvailable();
            bool hasPreyTarget = CanAttack(currentPreyTarget);
            bool targetBecameInvalid = (currentState == State.Chasing && !hasFoodTarget) ||
                                       ((currentState == State.Hunting || currentState == State.Fighting) &&
                                        !hasPreyTarget) ||
                                       (currentState == State.SeekingMate && MateFertility(currentMate) <= 0f);
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

        void ApplyDecision(AnimalDecision decision)
        {
            State previousState = currentState;
            FoodItem previousFoodTarget = currentFoodTarget;
            SeekFood previousPreyTarget = currentPreyTarget;
            SeekFood previousMate = currentMate;
            bool wasFleeingFire = fleeingFire && previousState == State.Fleeing;
            fleeingFire = false;
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
                    if (CanAttack(decision.preyTarget))
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
                    if (decision.fromFire)
                    {
                        // Away from a wildfire (item 79): no animal to watch, only a direction, chosen again at every
                        // decision while the danger lasts.
                        currentFleeThreat = null;
                        currentFleeDirection = decision.fleeDirection;
                        currentState = State.Fleeing;
                        fleeingFire = true;
                        timer = 0f;
                        SetFleeDestination(currentFleeDirection, null);
                        if (!wasFleeingFire) Wildfires.Current?.NoteFlight(this);
                    }
                    else if (IsOtherLivingAnimal(decision.preyTarget))
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

                case AgentIntent.Drink:
                    if (enableThirst)
                    {
                        Vector3 change = decision.drinkingSpot - currentDrinkingSpot;
                        change.y = 0f;
                        bool sameSpot = hasDrinkingSpot && change.sqrMagnitude < 1f;
                        currentDrinkingSpot = decision.drinkingSpot;
                        hasDrinkingSpot = true;
                        if (previousState == State.Drinking && sameSpot)
                        {
                            // Already at the water: keep drinking.
                            currentState = State.Drinking;
                        }
                        else
                        {
                            currentState = State.SeekingWater;
                            if (!sameSpot || previousState != State.SeekingWater) waterChaseTimer = 0f;
                            if (!sameSpot || previousState != State.SeekingWater || (!agent.pathPending && !agent.hasPath))
                            {
                                TrySetDestination(currentDrinkingSpot);
                            }
                        }
                    }
                    break;

                case AgentIntent.Rest:
                    if (previousState != State.Resting) BeginRest();
                    currentState = State.Resting;
                    break;

                case AgentIntent.SeekMate:
                    if (seekingMate && MateFertility(decision.mateTarget) > 0f)
                    {
                        currentMate = decision.mateTarget;
                        currentState = State.SeekingMate;
                        if (previousMate != currentMate)
                        {
                            ResetPreyPathTracking();
                            mateChaseTimer = 0f;
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
            else if (currentState == State.SeekingWater)
            {
                waterChaseTimer += deltaTime;
                Vector3 offset = transform.position - currentDrinkingSpot;
                offset.y = 0f;
                // The spot itself, not the food reach, or a drinking animal stops on the bank short of the shallows.
                float range = Mathf.Min(CurrentFoodInteractionRange, 1.5f + (agent != null ? agent.radius : 0f));
                if (offset.sqrMagnitude <= range * range)
                {
                    // At the water's edge: stand and drink (UpdateThirst refills).
                    currentState = State.Drinking;
                    TryResetPath();
                }
                else if (IsDrinkingSpotOutOfReach())
                {
                    GiveUpOnWater();
                }
                else if (!agent.pathPending && !agent.hasPath)
                {
                    TrySetDestination(currentDrinkingSpot);
                }
            }
            else if (currentState == State.Drinking)
            {
                if (!enableThirst || HydrationFraction >= 0.95f)
                {
                    hasDrinkingSpot = false;
                    currentState = State.Idling;
                    timer = actionTimer;
                    decisionTimer = 0f;
                }
            }
            else if (currentState == State.Resting)
            {
                // Walking to shelter until it gets there, the route ends, or it has walked for 15 s; then it settles.
                if (!restSettled)
                {
                    restWalkTimer += deltaTime;
                    if (restWalkTimer >= 15f || !agent.pathPending &&
                        (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.5f))
                    {
                        restSettled = true;
                        TryResetPath();
                    }
                }
            }
            else if (currentState == State.Hunting || currentState == State.Fighting)
            {
                if (!CanAttack(currentPreyTarget))
                {
                    ClearTargets();
                    currentState = State.Idling;
                    decisionTimer = 0f;
                    return;
                }

                UpdatePathToMovingTarget(currentPreyTarget);

                float attackRangeSquared = CurrentAttackRange * CurrentAttackRange;
                if ((transform.position - currentPreyTarget.transform.position).sqrMagnitude <= attackRangeSquared)
                {
                    TryAttack(currentPreyTarget);
                }
            }
            else if (currentState == State.SeekingMate)
            {
                if (MateFertility(currentMate) <= 0f)
                {
                    ClearTargets();
                    currentState = State.Idling;
                    decisionTimer = 0f;
                    return;
                }

                mateChaseTimer += deltaTime;
                UpdatePathToMovingTarget(currentMate);

                // Close enough to court is the same distance as close enough to strike.
                float courtshipRangeSquared = CurrentAttackRange * CurrentAttackRange;
                if ((transform.position - currentMate.transform.position).sqrMagnitude <= courtshipRangeSquared)
                {
                    BeginCourtship(currentMate);
                }
                else if (IsCurrentMateOutOfReach())
                {
                    GiveUpOnCurrentMate();
                }
            }
        }

        void PickNextAction()
        {
            // An animal wading where it can't swim doesn't stop to rest: every moment in the water is costly.
            bool wading = inWater && !CanSwimDeepWater;
            if (!wading && Random.value <= 0.5f)
            {
                currentState = State.Idling;
                TryResetPath();
                return;
            }

            currentState = State.Wandering;
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            ClampDestinationCandidate(ref candidate);

            float sampleDistance = Mathf.Clamp(wanderRadius, 1f, 25f);
            // A wader with no land near its random spot heads for the nearest land instead.
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, DestinationAreas) ||
                (wading && NavMesh.SamplePosition(transform.position, out hit, Mathf.Max(wanderRadius, 25f),
                                                  DestinationAreas)))
            {
                TrySetDestination(hit.position);
            }
            else
            {
                currentState = State.Idling;
                TryResetPath();
            }
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

        // Also ends a courtship, for both animals.
        void ClearTargets()
        {
            if (currentState == State.Courting)
            {
                EndCourtship();
            }

            currentFoodTarget = null;
            currentPreyTarget = null;
            currentMate = null;
        }
    }
}
