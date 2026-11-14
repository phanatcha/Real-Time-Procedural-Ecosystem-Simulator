using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Breeding: when it breeds, looking for and courting a mate (item 11), litters (item 16), and the child's
    // genome with mutation, harmful mutations (item 12) and perk changes (items 13, 62).
    public partial class SeekFood
    {
        [Header("Reproduction")]
        [Tooltip("Inherited (the breeding threshold gene, 0.5 to 1). Share of its maximum energy an animal needs before " +
                 "it breeds. A clone takes half of what the parent has; each mate gives a quarter. Higher keeps more in " +
                 "reserve for winter but breeds less often. At 0.95 animals living around half energy rarely bred.")]
        [Range(0f, 1f)] public float reproductionThreshold = 0.8f;
        [Tooltip("Simulated seconds after giving birth before this animal can reproduce again. Without it, " +
                 "every meal can become a birth and populations explode.")]
        [Min(0f)] public float reproductionCooldown = 60f;
        [Tooltip("Inherited (the litter size gene, 1 to 4). How many young a birth has on average; the fraction is the " +
                 "chance of one more. The energy the birth hands over is split equally between them, so a bigger litter " +
                 "means weaker young. Off-screen populations grow at their usual rate whatever it is.")]
        [Range(1f, 4f)] public float litterSize = 1f;
        [Tooltip("Percent chance per gene per birth that it mutates, at a mutation rate of 1.")]
        [Range(0f, 100f)] public float mutationChance = 5f;
        [Min(0f)] public float mutationMagnitude = 0.1f;
        [Tooltip("Inherited (the mutation rate gene, 0.25 to 4). Multiplies every mutation chance at birth: genes, " +
                 "body parts, perks and harmful mutations, so a lineage that evolves faster also gathers harmful " +
                 "mutations faster. A clone mutates at its parent's rate, a child of two parents at the rate it took " +
                 "from one of them.")]
        [Range(0.25f, 4f)] public float mutationRate = 1f;

        [Header("Mutation Load")]
        [Tooltip("Percent chance per birth, at a mutation rate of 1, that the child gains a harmful mutation. A clone " +
                 "keeps all of its parent's; a child of two parents inherits each of each parent's with a 50% chance, " +
                 "so sexual lineages can shed them while cloning lineages only gain them (Muller's ratchet).")]
        [Range(0f, 100f)] public float harmfulMutationChance = 10f;
        [Tooltip("How much each harmful mutation adds to energy use: 0.05 adds 5% per mutation, so five cost 25%. At 0.02, twelve five-year runs showed no selection against the load at all.")]
        [Min(0f)] public float harmfulMutationEnergyCost = 0.05f;
        [SerializeField, Tooltip("Harmful mutations this animal carries, for reference.")]
        private int harmfulMutations;

        [Header("Perks")]
        [Tooltip("Percent chance per birth, at a mutation rate of 1, that a child gains each perk (thick fur, venom, " +
                 "camouflage, regeneration, scavenger gut) its parent lacked. Building a trait is rare, so a perk only " +
                 "spreads if it pays (item 62). AnimalPerks describes what each does and costs.")]
        [Range(0f, 100f)] public float perkGainChance = 0.2f;
        [Tooltip("Percent chance per birth, at a mutation rate of 1, that a child loses each perk its parent had. " +
                 "Losing a working trait is much easier than building one, so this is ten times the gain chance.")]
        [Range(0f, 100f)] public float perkLossChance = 2f;
        [SerializeField, Tooltip("This animal's perks, for reference.")]
        private string perks;

        [Header("Mating")]
        [Tooltip("Inherited. Below 0.15 the animal only clones itself; above 0.85 it only mates; in between it looks " +
                 "for a mate for up to this many times Mate Search Seconds Per Drive, then clones if it finds nobody.")]
        [Range(0f, 1f)] public float sexualDrive = 0.5f;
        [Tooltip("For experiments (item 60): 0 to 1 holds this animal's and all its descendants' sexual drive at that " +
                 "value whatever they inherit, so a whole population only clones (0) or only mates (1). Below 0 the " +
                 "gene decides, as usual.")]
        public float lockedSexualDrive = -1f;
        [Tooltip("Simulated seconds of looking for a mate per unit of sexual drive, before cloning instead.")]
        [Min(0f)] public float mateSearchSecondsPerDrive = 20f;
        [Tooltip("A partner must have at least this fraction of its maximum energy, and an animal looking for a mate " +
                 "gives up once it falls below it.")]
        [Range(0f, 1f)] public float minimumMateEnergyFraction = 0.6f;
        [Tooltip("Simulated seconds two animals court before a child can be born. Neither moves or reacts to " +
                 "threats meanwhile; an attack on either ends the courtship.")]
        [Min(0f)] public float courtshipDuration = 2f;
        [Tooltip("Fraction of its energy each parent gives a child of two parents. A clone gets half its one " +
                 "parent's energy.")]
        [Range(0f, 1f)] public float matingEnergyShare = 0.25f;
        [Tooltip("Fertility fades from this fraction of the species threshold (in genetic distance) to none at the " +
                 "threshold, so animals about to split into separate species rarely interbreed.")]
        [Range(0f, 1f)] public float fertilityFadeStart = 0.5f;
        // Whether this animal would court a partner now: mature, fed enough, able to mate, rested since its last
        // birth, and not fleeing, fighting or already courting.
        public bool IsAvailableAsMate => IsAlive && IsMature && MatingRules.CanMate(sexualDrive) &&
                                         reproductionCooldownTimer <= 0f &&
                                         currentEnergy >= maxEnergy * minimumMateEnergyFraction &&
                                         currentState != State.Fleeing && currentState != State.Fighting &&
                                         currentState != State.Escaping && currentState != State.Courting;
        // A child that can't swim, born to a parent out in deep water, is placed on the nearest ground or shallow
        // water within this distance. With none that close, the birth waits this many simulated seconds.
        private const float NonSwimmerBirthSearchRadius = 60f;
        private const float PostponedBirthDelay = 5f;
        // Simulated seconds a partner the animal couldn't reach, or courted without a child, is passed over.
        private const float AvoidedMateMemory = 30f;
        private float reproductionCooldownTimer;
        // Ready to breed, the animal looks for a mate until it finds one, gives up or clones itself instead. The
        // animal that reaches its partner leads the courtship and decides whether a child is born.
        private bool seekingMate;
        private float mateSearchTimer;
        private float mateChaseTimer;
        private float courtshipTimer;
        private bool leadsCourtship;
        // Partners passed over until the simulated time stored with them.
        private readonly List<AvoidedMate> avoidedMates = new List<AvoidedMate>();

        private struct AvoidedMate
        {
            public SeekFood mate;
            public float retryTime;
        }

        // Ready to breed (mature, fed up to the reproduction threshold, rested since its last birth and below the
        // population ceiling), the animal clones itself or starts looking for a mate, as its sexual drive decides.
        readonly BreedingWaitTracker breedingWait = new BreedingWaitTracker();

        void UpdateBreeding(float deltaTime)
        {
            bool atPopulationCeiling = SpeciesManager.Instance != null &&
                                       SpeciesManager.Instance.IsAtPopulationCeilingFor(inWater);
            SpeciesManager manager = SpeciesManager.Instance;
            bool eligible = IsMature && maxEnergy > 0f && reproductionCooldownTimer <= 0f &&
                currentEnergy >= maxEnergy * (seekingMate ? minimumMateEnergyFraction : reproductionThreshold);
            bool blocked = eligible && atPopulationCeiling;
            bool entered = breedingWait.Observe(blocked, deltaTime, manager != null ? manager.WorldRevision : 0);
            if (blocked && manager != null) manager.RecordBreedingWait(this, entered, deltaTime);
            if (seekingMate)
            {
                UpdateMateSearch(deltaTime, atPopulationCeiling);
                return;
            }

            if (!IsMature || maxEnergy <= 0f || currentEnergy < maxEnergy * reproductionThreshold ||
                reproductionCooldownTimer > 0f || atPopulationCeiling)
            {
                return;
            }

            // An ageing animal is less fertile (item 68): it takes each chance to breed with a probability equal to its
            // vigour, and otherwise rests as if it had bred.
            float fertility = Vigour;
            if (fertility < 1f && Random.value > fertility)
            {
                reproductionCooldownTimer = reproductionCooldown;
                return;
            }

            if (MatingRules.CanMate(sexualDrive))
            {
                seekingMate = true;
                mateSearchTimer = 0f;
                decisionTimer = 0f;
            }
            else
            {
                ReproduceAlone();
            }
        }

        // The search ends when the animal can no longer breed or falls below the energy a partner needs. Out of
        // time, it clones itself instead; animals that only mate keep looking.
        void UpdateMateSearch(float deltaTime, bool atPopulationCeiling)
        {
            mateSearchTimer += deltaTime;
            if (atPopulationCeiling || reproductionCooldownTimer > 0f ||
                currentEnergy < maxEnergy * minimumMateEnergyFraction)
            {
                StopMateSearch();
                return;
            }

            if (mateSearchTimer >= MatingRules.MateSearchTime(sexualDrive, mateSearchSecondsPerDrive))
            {
                StopMateSearch();
                ReproduceAlone();
            }
        }

        void StopMateSearch()
        {
            seekingMate = false;
            mateSearchTimer = 0f;
            if (currentState == State.SeekingMate)
            {
                ClearTargets();
                currentState = State.Idling;
                timer = actionTimer;
                decisionTimer = 0f;
                TryResetPath();
            }
        }

        // Clones itself, dropping whatever it was doing.
        void ReproduceAlone()
        {
            ClearTargets();
            currentState = State.Idling;
            timer = actionTimer;
            Reproduce();
            decisionTimer = 0f;
        }

        // Keeps the most promising partner in view, weighing fertility against distance.
        void ConsiderMate(SeekFood other, float visionRadiusSquared, ref AnimalPerception perception, ref float bestScore)
        {
            if (other == null || other == this)
            {
                return;
            }

            // A glowing partner is noticed from up to twice as far in the dark (item 76).
            float notice = other.Glows ? AnimalPerks.GlowMateNotice(1f - WorldDay.Current.daylight) : 1f;
            float distanceSquared = (transform.position - other.transform.position).sqrMagnitude;
            if (distanceSquared > visionRadiusSquared * notice * notice || IsAvoidedMate(other))
            {
                return;
            }

            float fertility = MateFertility(other);
            float distance = Mathf.Sqrt(distanceSquared);
            float score = fertility / (1f + distance / Mathf.Max(1f, visionRadius));
            if (score > bestScore)
            {
                bestScore = score;
                perception.mate = other;
                perception.mateDistance = distance;
                perception.mateFertility = fertility;
            }
        }

        // The chance a courtship with this animal would end in a child, or 0 when it isn't available as a partner.
        float MateFertility(SeekFood other)
        {
            return other == null || other == this || !other.IsAvailableAsMate ? 0f : GeneticFertility(other);
        }

        // Fertility from how far apart the two genomes are: full for close relatives, fading to none at the
        // species threshold.
        float GeneticFertility(SeekFood other)
        {
            if (genome == null || !genome.IsValid || other.genome == null || !other.genome.IsValid)
            {
                return 0f;
            }

            float threshold = SpeciesManager.Instance != null
                ? SpeciesManager.Instance.speciationThreshold
                : SpeciesManager.DefaultSpeciationThreshold;
            return MatingRules.Fertility(AnimalGenome.Distance(genome, other.genome), threshold, fertilityFadeStart);
        }

        // Like IsCurrentFoodOutOfReach, for a partner that moves: the chase is taking far longer than crossing the
        // field of view would, or the route stops well short of where the partner was (across water the animal
        // can't swim, or up a cliff).
        bool IsCurrentMateOutOfReach()
        {
            float speed = Mathf.Max(0.1f, GetCurrentMovementSpeed());
            if (mateChaseTimer >= Mathf.Max(maximumFoodChaseDuration, 3f * visionRadius / speed))
            {
                return true;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending || !agent.hasPath ||
                agent.pathStatus == NavMeshPathStatus.PathComplete || !hasPreyPathDestination)
            {
                return false;
            }

            Vector3 shortfall = agent.pathEndPosition - lastPreyPathDestination;
            shortfall.y = 0f;
            float range = CurrentAttackRange;
            return shortfall.sqrMagnitude > range * range;
        }

        void GiveUpOnCurrentMate()
        {
            AvoidMate(currentMate);
            ClearTargets();
            mateChaseTimer = 0f;
            currentState = State.Idling;
            timer = 0f;
            decisionTimer = 0f;
            TryResetPath();
        }

        void AvoidMate(SeekFood mate)
        {
            if (mate == null)
            {
                return;
            }

            // A short memory is enough; the oldest entry makes room.
            if (avoidedMates.Count >= 4)
            {
                avoidedMates.RemoveAt(0);
            }

            avoidedMates.Add(new AvoidedMate { mate = mate, retryTime = Time.time + AvoidedMateMemory });
        }

        bool IsAvoidedMate(SeekFood mate)
        {
            float now = Time.time;
            for (int index = avoidedMates.Count - 1; index >= 0; index--)
            {
                AvoidedMate entry = avoidedMates[index];
                if (entry.mate == null || now >= entry.retryTime)
                {
                    avoidedMates.RemoveAt(index);
                }
                else if (entry.mate == mate)
                {
                    return true;
                }
            }

            return false;
        }

        // Both animals stop where they are for the courtship. The partner was checked to be available just before.
        void BeginCourtship(SeekFood partner)
        {
            partner.EnterCourtship(this, leads: false);
            EnterCourtship(partner, leads: true);
        }

        void EnterCourtship(SeekFood partner, bool leads)
        {
            ClearTargets();
            currentMate = partner;
            currentState = State.Courting;
            leadsCourtship = leads;
            courtshipTimer = 0f;
            TryResetPath();
        }

        // Both animals count down the courtship; the one that led it then decides whether a child is born. Fertility
        // fades with genetic distance, and after a courtship without a child each looks for someone else.
        void UpdateCourtship(float deltaTime)
        {
            SeekFood partner = currentMate;
            if (partner == null || !partner.IsAlive || partner.currentState != State.Courting ||
                partner.currentMate != this)
            {
                EndCourtship();
                return;
            }

            courtshipTimer += deltaTime;
            if (!leadsCourtship || courtshipTimer < courtshipDuration)
            {
                return;
            }

            float fertility = GeneticFertility(partner);
            EndCourtship();
            if (fertility < 1f && Random.value >= fertility)
            {
                AvoidMate(partner);
                partner.AvoidMate(this);
                return;
            }

            bool atPopulationCeiling = SpeciesManager.Instance != null &&
                                       SpeciesManager.Instance.IsAtPopulationCeilingFor(inWater);
            // At the ceiling, or with nowhere for the child to be born, the mate search ends on the next frame.
            if (!atPopulationCeiling && ReproduceWith(partner))
            {
                StopMateSearch();
                partner.StopMateSearch();
            }
        }

        // Ends a courtship, finished or interrupted, for both animals.
        void EndCourtship()
        {
            SeekFood partner = currentMate;
            LeaveCourtship();
            if (partner != null && partner.currentState == State.Courting && partner.currentMate == this)
            {
                partner.LeaveCourtship();
            }
        }

        void LeaveCourtship()
        {
            currentMate = null;
            leadsCourtship = false;
            courtshipTimer = 0f;
            currentState = State.Idling;
            timer = actionTimer;
            decisionTimer = 0f;
        }

        // Clones itself: each child gets a copy of this genome, with all of its harmful mutations, mutated at this
        // animal's mutation rate, and a share of half this animal's energy.
        void Reproduce()
        {
            TryGiveBirth(null);
        }

        // Each child takes each gene, body site and perk from a random parent, and each parent's harmful mutations
        // with a 50% chance each, then mutates at the mutation rate it took. Each parent gives the litter a share of
        // its energy. Returns false when the first child had nowhere to be born.
        bool ReproduceWith(SeekFood partner)
        {
            return TryGiveBirth(partner);
        }

        AnimalGenome CreateChildGenome(SeekFood partner, out bool mutated)
        {
            AnimalGenome inherited = partner == null ? genome : AnimalGenome.Recombine(genome, partner.genome);
            // God tools can raise or lower every animal's mutation chances for a while (SpeciesManager.mutationMultiplier).
            float boost = SpeciesManager.Instance != null ? Mathf.Max(0f, SpeciesManager.Instance.mutationMultiplier) : 1f;
            return inherited.CreateChild(mutationChance * boost, mutationMagnitude, bodyPlanMutation,
                                         perkGainChance * boost, perkLossChance * boost, harmfulMutationChance * boost,
                                         out mutated);
        }

        // How many young this birth has: the litter size gene's whole part, plus one more with the fraction as the
        // chance, so 1.3 is one child or, three times in ten, two.
        int LitterCount()
        {
            float size = Mathf.Clamp(litterSize, 1f, 4f);
            int young = Mathf.FloorToInt(size);
            return young + (Random.value < size - young ? 1 : 0);
        }

        // The young are born beside this animal, with a partner as their second parent or none for clones. Each gets
        // its own genome and an equal share of the energy the birth hands over, which is the same amount whatever the
        // litter size, so reproduction never creates energy. Nothing is paid when the first child has nowhere to be.
        bool TryGiveBirth(SeekFood partner)
        {
            int young = LitterCount();
            AnimalGenome firstGenome = CreateChildGenome(partner, out bool firstMutated);
            // The child's genome comes first, because whether it can swim decides where it can be born.
            if (!TryFindBirthPosition(ChildCanSwimDeepWater(firstGenome), out Vector3 firstPosition))
            {
                // Out in deep water a child that can't swim would have nowhere to be: try again a little later.
                reproductionCooldownTimer = PostponedBirthDelay;
                return false;
            }

            float handedOver;
            if (partner == null)
            {
                handedOver = currentEnergy * 0.5f;
                SetEnergy(currentEnergy - handedOver, EnergyFlow.GivenToYoung);
            }
            else
            {
                handedOver = PayForChild() + partner.PayForChild();
            }
            reproductionCooldownTimer = reproductionCooldown;

            float energyEach = handedOver / young;
            float givenToYoung = energyEach;
            Bear(firstGenome, firstMutated, partner, firstPosition, energyEach);
            for (int index = 1; index < young; index++)
            {
                AnimalGenome childGenome = CreateChildGenome(partner, out bool mutated);
                if (!TryFindBirthPosition(ChildCanSwimDeepWater(childGenome), out Vector3 position))
                {
                    // A later child with nowhere to be born isn't born, and its share stays with this parent.
                    SetEnergy(currentEnergy + energyEach, EnergyFlow.GivenToYoung);
                    continue;
                }
                givenToYoung += energyEach;
                Bear(childGenome, mutated, partner, position, energyEach);
            }
            EnergyLedger.CheckBirth(handedOver, givenToYoung, name);
            return true;
        }

        bool ChildCanSwimDeepWater(AnimalGenome childGenome)
        {
            return waterMovement.CanSwimDeepWater(AnimalBodyPlan.Evaluate(childGenome).swimmingAbility);
        }

        void Bear(AnimalGenome childGenome, bool mutated, SeekFood partner, Vector3 spawnPosition, float childEnergy)
        {
            GameObject child = Instantiate(gameObject, spawnPosition, transform.rotation);
            SeekFood childScript = child.GetComponent<SeekFood>();
            if (childScript == null)
            {
                return;
            }

            childScript.currentEnergy = childEnergy;
            EnergyLedger.Record(EnergyFlow.BornWith, childEnergy);
            childScript.bodyEnergyPaid = 0f;
            childScript.currentHydration = maxHydration;
            childScript.currentAge = 0f;
            childScript.bornInSimulation = true;
            childScript.generation = Mathf.Max(generation, partner != null ? partner.generation : 0) + 1;
            childScript.firstParent = this;
            childScript.secondParent = partner;
            childScript.FirstParentId = AnimalId;
            childScript.SecondParentId = partner != null ? partner.AnimalId : 0;
            childScript.YoungBorn = 0;
            YoungBorn++;
            if (partner != null) partner.YoungBorn++;
            // The child copies this genome into its traits when it starts.
            childScript.genome = childGenome;

            // Offspring start in their (first) parent's species. SpeciesManager splits species whose members drift
            // genetically apart.
            childScript.speciesName = speciesName;
            childScript.speciesColor = speciesColor;
            child.name = $"Capsule_Species_{childScript.speciesName}";

            if (SpeciesManager.Instance != null)
            {
                SpeciesManager.Instance.RecordReproduction(this, mutated, sexual: partner != null);
            }
        }

        // One parent's share of a child of two parents. Paying it also starts this parent's rest before breeding again.
        float PayForChild()
        {
            float share = currentEnergy * Mathf.Clamp01(matingEnergyShare);
            SetEnergy(currentEnergy - share, EnergyFlow.GivenToYoung);
            reproductionCooldownTimer = reproductionCooldown;
            return share;
        }

        // Beside the parent, on firm ground nearby if there is any, in a part of the NavMesh the child can use.
        bool TryFindBirthPosition(bool childSwims, out Vector3 position)
        {
            Vector2 offset = Random.insideUnitCircle * 3f;
            position = transform.position + new Vector3(offset.x, 0f, offset.y);
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (terrainWorld != null &&
                terrainWorld.TryFindWalkableGround(position, 12f, out Vector3 groundPosition))
            {
                position = groundPosition;
            }

            int childAreas = WaterAccess.AreaMask(childSwims);
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, childAreas))
            {
                position = hit.position;
                return true;
            }

            if (childSwims || !inWater)
            {
                position = transform.position;
                return true;
            }

            // The parent is swimming and the child can't: the nearest ground or shallow water, if it's close enough.
            if (NavMesh.SamplePosition(transform.position, out hit, NonSwimmerBirthSearchRadius, childAreas))
            {
                position = hit.position;
                return true;
            }

            return false;
        }
    }
}
