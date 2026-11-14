using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Danger: hunting and being hunted (item 66), fighting, remembered threats, carcasses, and wildfire heat and
    // smoke (item 79).
    public partial class SeekFood
    {
        [Header("Wildfire")]
        [Tooltip("Health lost per simulated second at full radiant heat, standing in the flames (item 79). Heat falls " +
                 "off over about a cell and a half (12 m) from burning ground; in water the animal takes a tenth of it.")]
        [Min(0f)] public float fireDamagePerSecond = 30f;
        [Tooltip("Stamina lost per simulated second in thick smoke, which also stops stamina recovering.")]
        [Min(0f)] public float smokeStaminaDrainPerSecond = 6f;
        [Tooltip("Extra upkeep in thick smoke, as a share of the animal's upkeep: breathing hard through it.")]
        [Min(0f)] public float smokeUpkeepCost = 0.5f;
        [Tooltip("Metres within which the animal notices a fire, by its smoke and glow, whatever its sight.")]
        [Min(0f)] public float fireSenseRadius = 150f;

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
        [Tooltip("Every animal hunts its own species, whatever its diet. Off, only meat-leaning animals do (Hunt Own " +
                 "Kind From Diet).")]
        public bool allowCannibalism;
        [Tooltip("From this inherited diet (Diet Affinity) up an animal also hunts members of its own species that " +
                 "aren't close kin (item 66). Founders start at 0.15, so only the most meat-leaning animals hunt at " +
                 "first; above 1 only other species are hunted.")]
        [Min(0f)] public float huntOwnKindFromDiet = 0.25f;
        [Tooltip("Prevents predation between parents, children, and siblings, including mutated offspring assigned to a new species.")]
        public bool avoidCloseKinPredation = true;
        [Range(0f, 1f)] public float carcassEnergyTransferEfficiency = 0.75f;
        [Tooltip("What a full-grown body of mass 1 is made of, as energy. Growing up costs Growth Energy Cost times " +
                 "this, and that cost is the meat the carcass holds, on top of whatever energy the animal had stored. " +
                 "Without it a starved animal would leave no carcass, which makes meat scarce and meat-eating hard " +
                 "to evolve.")]
        [Min(0f)] public float carcassBodyEnergy = 50f;
        [Min(1f)] public float carcassLifetime = 120f;
        [Tooltip("Simulated seconds a carcass stays as spoiled meat once it goes off. Only animals with a scavenger " +
                 "gut can eat it.")]
        [Min(0f)] public float carcassSpoiledLifetime = 60f;
        [Min(0.1f)] public float carcassScale = 6f;
        // Radiant heat from a wildfire the animal feels now (0 to about 1.5), and smoke (0 to 1), item 79.
        public float FireHeat => fireHeat;
        public float SmokeFelt => smokeFelt;
        public bool IsFleeingFire => fleeingFire && currentState == State.Fleeing;
        public float CurrentAttackRange => attackRange * Mathf.Max(0.01f, bodyBulk);
        // A carcass holds the energy that went into building the body plus the energy the animal had stored, less the
        // share lost in the transfer. Neither is scaled up by anything, so a carcass never holds more than was put
        // into the animal (item 61). Hunters weigh prey by the same amount, so even a starving animal is worth its meat.
        public float EstimatedCarcassRawEnergy => (bodyEnergyPaid + Mathf.Max(0f, currentEnergy)) *
                                                   carcassEnergyTransferEfficiency;
        private float preyPathRefreshTimer;
        private float attackCooldownTimer;
        private SeekFood lastPreyPathTarget;
        private Vector3 lastPreyPathDestination;
        private bool hasPreyPathDestination;
        private SeekFood temporarilyAvoidedPrey;
        private float failedHuntCooldownTimer;
        private float unproductiveHuntTimer;
        private readonly List<ThreatMemory> rememberedThreats = new List<ThreatMemory>();

        private sealed class ThreatMemory
        {
            public SeekFood attacker;
            public float timeRemaining;
        }

        // Wildfire (item 79): what the animal feels now, and whether its flight is from a fire.
        private float fireHeat;
        private float smokeFelt;
        private bool fleeingFire;

        // Near a wildfire (item 79) radiant heat hurts, a tenth as much in water, and smoke costs stamina (and upkeep,
        // through CurrentEnergyDrainPerSecond). Flames close by break off a courtship and make the animal decide again
        // at once. Returns true when the fire killed it.
        bool UpdateFireExposure(float deltaTime)
        {
            WildfireKind fires = Wildfires.Current;
            if (fires == null || !fires.IsActive)
            {
                fireHeat = smokeFelt = 0f;
                return false;
            }

            fires.Exposure(transform.position, out float heat, out float smoke);
            fireHeat = inWater ? heat * 0.1f : heat;
            smokeFelt = smoke;
            if (fireHeat > 0f)
            {
                currentHealth -= fireDamagePerSecond * fireHeat * deltaTime;
                if (currentHealth <= 0f)
                {
                    Die(AgentDeathCause.Fire, null);
                    return true;
                }
                if (currentState == State.Courting) EndCourtship();
                else if (currentState != State.Fleeing) decisionTimer = 0f;
            }
            if (smokeFelt > 0f)
            {
                currentStamina = Mathf.Max(0f, currentStamina - smokeStaminaDrainPerSecond * smokeFelt * deltaTime);
            }
            return false;
        }

        // An animal that attacked this one counts as a threat while it is remembered, however far away it is. Those
        // near enough were added with the animals in view; this adds the rest.
        void AddRememberedThreatsOutOfView(SpeciesManager manager, List<AnimalThreat> threats)
        {
            foreach (ThreatMemory memory in rememberedThreats)
            {
                SeekFood attacker = memory.attacker;
                if (memory.timeRemaining <= 0f || !IsOtherLivingAnimal(attacker) || !manager.IsActiveAgent(attacker) ||
                    ContainsThreat(threats, attacker))
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, attacker.transform.position);
                threats.Add(new AnimalThreat(attacker, distance, false, true));
            }
        }

        static bool ContainsThreat(List<AnimalThreat> threats, SeekFood attacker)
        {
            foreach (AnimalThreat threat in threats)
            {
                if (threat.attacker == attacker) return true;
            }

            return false;
        }

        void TryAttack(SeekFood prey)
        {
            if (attackCooldownTimer > 0f || !CanAttack(prey) ||
                currentEnergy < attackEnergyCost || currentStamina < attackStaminaCost)
            {
                return;
            }

            float cooldown = Mathf.Max(0.01f, attackCooldown);
            int catchUpLimit = Mathf.Max(1, maximumCatchUpAttacksPerFrame);
            int attacksResolved = 0;

            while (!isDying && attackCooldownTimer <= 0f && attacksResolved < catchUpLimit &&
                   CanAttack(prey) && currentEnergy >= attackEnergyCost &&
                   currentStamina >= attackStaminaCost)
            {
                SetEnergy(Mathf.Max(0f, currentEnergy - attackEnergyCost), EnergyFlow.Attacks);
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
                if (!IsOtherLivingAnimal(memory.attacker) || memory.timeRemaining <= 0f)
                {
                    rememberedThreats.RemoveAt(index);
                }
            }
        }

        void RememberThreat(SeekFood attacker)
        {
            if (!IsOtherLivingAnimal(attacker))
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

        public float GetCombatPower()
        {
            float attacksPerSecond = 1f / Mathf.Max(0.01f, attackCooldown);
            float staminaReadiness = 0.25f + 0.75f * Mathf.Clamp01(StaminaFraction);
            return Mathf.Max(0f, strength) * attacksPerSecond *
                   Mathf.Clamp01(HealthFraction) * staminaReadiness;
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

            return possiblePrey.speciesName != speciesName || allowCannibalism || HuntsOwnKind;
        }

        // Whether this animal hunts members of its own species that aren't close kin (item 66).
        public bool HuntsOwnKind => dietAffinity >= huntOwnKindFromDiet;

        // Another animal still alive: one this animal can notice as a threat, remember and flee from. Prey is more
        // than that, since an animal that hunts its own kind is a threat to plant-eaters that would never hunt it.
        public bool IsOtherLivingAnimal(SeekFood other) => other != null && other != this && other.IsAlive;

        // Prey, or an animal hunting or attacking this one, which it may fight back against whether or not it would
        // ever hunt it.
        bool CanAttack(SeekFood target) =>
            IsViablePrey(target) ||
            IsOtherLivingAnimal(target) && (IsRememberedThreat(target) || target.IsTargetingAsPrey(this));

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

        public void TakeDamage(float amount, SeekFood attacker = null)
        {
            if (isDying || amount <= 0f)
            {
                return;
            }

            if (IsOtherLivingAnimal(attacker))
            {
                RememberThreat(attacker);
                ClearTargets();
                currentState = State.Idling;
                decisionTimer = 0f;
                TryResetPath();
            }

            if (attacker != null)
            {
                // Venom: the attacker takes back part of every bite, whatever this animal's plates absorb.
                if (perkEffects.venomDamageReturned > 0f)
                {
                    attacker.TakeVenomDamage(amount * perkEffects.venomDamageReturned);
                }

                // Plates absorb part of every attack, but not starvation.
                amount *= bodyEffects.damageTakenMultiplier;
            }

            currentHealth -= amount;
            if (currentHealth <= 0f)
            {
                Die(attacker == null ? AgentDeathCause.Starvation : AgentDeathCause.Predation, attacker);
            }
        }

        // Damage from biting a venomous animal. It isn't an attack, so plates don't absorb it and the bitten animal
        // isn't remembered as a threat.
        void TakeVenomDamage(float amount)
        {
            if (isDying || amount <= 0f)
            {
                return;
            }

            currentHealth -= amount;
            if (currentHealth <= 0f)
            {
                Die(AgentDeathCause.Venom, null);
            }
        }
    }
}
