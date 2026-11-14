using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Growing up, ageing and dying: juvenile growth (item 47), body proportions, ageing (item 68), and death.
    public partial class SeekFood
    {
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
        [Tooltip("Inherited: the age at which the animal starts to age (item 68). From then its vigour falls to nothing " +
                 "over Ageing Span, capping its health, slowing it and making it skip chances to breed, until it dies of " +
                 "old age. Maximum Lifespan stays an outer limit. A later onset costs upkeep (Repair Metabolic Weight).")]
        [InspectorName("Ageing Onset (Simulated Seconds)")]
        [Min(0f)] public float ageingOnset = 180f;
        [Tooltip("Simulated seconds from the onset of ageing until vigour reaches zero. Not inherited.")]
        [Min(0.01f)] public float ageingSpan = 120f;
        [Tooltip("Off, an animal stays in full health until Maximum Lifespan, as before item 68, and the onset costs nothing.")]
        public bool enableAgeing = true;
        [Tooltip("The share of its speed an animal keeps as its vigour runs out.")]
        [Range(0f, 1f)] public float agedSpeedFloor = 0.3f;

        [Header("Juvenile Growth")]
        [Tooltip("Share of its adult size, speed, vision and feeding reach a newborn starts with. It grows to full " +
                 "size by its maturity time. Founders placed by a scene and animals back from off-screen numbers are " +
                 "full-grown.")]
        [Range(0.1f, 1f)] public float newbornSizeFraction = 0.4f;
        [Tooltip("Energy spent growing from newborn to adult, as a share of the adult body's own meat (Carcass Body " +
                 "Energy times body mass). It is paid as the body fills out, on top of upkeep, so maturing sooner is a " +
                 "shorter but costlier youth rather than a free advantage.")]
        [Min(0f)] public float growthEnergyCost = 0.5f;
        // Size and ability relative to the adult, from Newborn Size Fraction up to 1.
        private float growth = 1f;
        private float adultVisionRadius;
        private float lastBuiltGrowth = 1f;
        // Set on a newborn before its first frame. Founders and animals from off-screen populations keep it false.
        [System.NonSerialized] public bool bornInSimulation;
        public float CurrentAge => currentAge;
        public float RemainingLifespan => Mathf.Max(0f, maxLifespan - currentAge);
        // 1 until the onset of ageing, then falling to 0 over Ageing Span (item 68). Health is capped at this share of
        // its maximum, speed falls towards Aged Speed Floor, and each chance to breed is taken with this probability.
        public float Vigour => !enableAgeing || currentAge <= ageingOnset
            ? 1f
            : Mathf.Clamp01(1f - (currentAge - ageingOnset) / Mathf.Max(0.01f, ageingSpan));
        public float AgeFraction => maxLifespan <= Mathf.Epsilon
            ? 1f
            : Mathf.Clamp01(currentAge / maxLifespan);
        // How far this animal has grown towards its adult size, speed, vision and reach: 1 once mature.
        public float Growth => growth;
        // Includes the weight of body parts, which also feeds the carcass and the metabolism. A young animal has the
        // mass of its current size, so it leaves a small carcass.
        public float BodyMassFactor => AdultBodyMassFactor * growth * growth * growth;
        // The mass the body has, or will have, full-grown.
        public float AdultBodyMassFactor
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
        // The energy that went into building this animal's body: what it paid to grow, or for a founder or an arrival
        // from off-screen, what a body its size costs to grow. It is the meat its carcass is made of.
        public float BodyEnergyPaid => bodyEnergyPaid;
        private float bodyEnergyPaid;
        public bool IsMature => currentAge >= Mathf.Max(0f, maturityTime);
        public float MaturityProgress => maturityTime <= Mathf.Epsilon
            ? 1f
            : Mathf.Clamp01(currentAge / maturityTime);
        private AnimalBodyView bodyView;
        private bool isDying;
        private AgentDeathCause deathCause = AgentDeathCause.Other;

        // Kills the animal outright, for god tools such as a mass extinction. It leaves a carcass like any death.
        public void Kill(AgentDeathCause cause)
        {
            if (!isDying) Die(cause, null);
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
            EnergyLedger.CheckCarcass(carcassEnergy, bodyEnergyPaid, currentEnergy, name);
            SetEnergy(0f, EnergyFlow.LostAtDeath);

            if (carcassEnergy > 0f)
            {
                float carcassLinearScale = Mathf.Pow(BodyMassFactor, 1f / 3f);
                FoodItem.CreateCarcass(transform.position, carcassEnergy, speciesName,
                                       carcassLifetime, carcassScale * carcassLinearScale, carcassSpoiledLifetime);
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
                                                 new Vector3(bodyBulk, bodyHeight, bodyBulk) * growth);

            agent.radius = Mathf.Max(0.01f, referenceAgentRadius * bodyBulk * growth);
            agent.height = Mathf.Max(0.01f, referenceAgentHeight * bodyHeight * growth);
            // Legs stand the body up off the ground. The agent's offset is in the scaled local space.
            agent.baseOffset = referenceAgentBaseOffset * bodyHeight + LegLift / Mathf.Max(0.01f, transform.lossyScale.y);
        }

        // Newborns start at Newborn Size Fraction of their adult size, speed, vision and feeding reach and grow to full
        // size by their maturity time, paying for the body mass they put on. Growing costs energy, so maturing sooner
        // is a shorter but costlier youth rather than a free advantage. Founders and animals back from off-screen
        // numbers are full-grown.
        void BeginGrowth()
        {
            adultVisionRadius = visionRadius;
            if (!bornInSimulation) return;

            growth = GrowthAt(MaturityProgress);
            lastBuiltGrowth = growth;
            visionRadius = adultVisionRadius * growth;
            ApplyBodyProportions();
        }

        float GrowthAt(float maturityProgress)
        {
            return Mathf.Lerp(Mathf.Clamp(newbornSizeFraction, 0.1f, 1f), 1f, Mathf.Clamp01(maturityProgress));
        }

        // Energy to grow one unit of body mass.
        float GrowthCostPerMass => Mathf.Max(0f, growthEnergyCost) * Mathf.Max(0f, carcassBodyEnergy);

        void UpdateGrowth()
        {
            float previous = growth;
            growth = GrowthAt(MaturityProgress);
            if (growth <= previous) return;

            // Mass goes with the cube of size, so the cost falls as the body fills out: a share of the adult body's own
            // meat, spread over the youth. An animal that can't pay starves like any other.
            float massGained = growth * growth * growth - previous * previous * previous;
            float before = currentEnergy;
            SetEnergy(Mathf.Max(0f, currentEnergy - GrowthCostPerMass * AdultBodyMassFactor * massGained), EnergyFlow.Growth);
            // What it actually paid becomes its body's meat; an animal too hungry to pay in full builds a leaner body.
            bodyEnergyPaid += before - currentEnergy;
            visionRadius = adultVisionRadius * growth;

            // The body, its parts and its upkeep follow in steps of 5%, so a growing animal doesn't rebuild its mesh
            // every frame.
            if (growth - lastBuiltGrowth < 0.05f && growth < 1f) return;
            lastBuiltGrowth = growth;
            ApplyBodyProportions();
            RefreshMovementSpeed();
            RefreshMetabolicRate();
            BuildBodyView();
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
    }
}
