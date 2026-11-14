using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Its energy: the store and what fills and drains it, the upkeep its traits cost (scaled by body size since
    // item 67), starvation, and the energy ledger's bookkeeping (item 61).
    public partial class SeekFood
    {
        [Header("Survival Stats")]
        public float currentEnergy = 50f;
        public float maxEnergy = 150f;

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
        [Tooltip("What a later onset of ageing costs (item 68): energy spent on repair. Charged against the founders' " +
                 "onset like the other traits; nothing while ageing is off.")]
        [Min(0f)] public float repairMetabolicWeight = 0.2f;
        [Min(0f)] public float minimumMetabolicMultiplier = 0.25f;
        [Min(0f)] public float maximumMetabolicMultiplier = 4f;

        [Header("Body Size Scaling")]
        [Tooltip("Item 67: the energy store grows in proportion to the body's mass, and the whole upkeep with mass to " +
                 "the 3/4 power (Kleiber's law), both against the founders' body. A bigger body needs more food but " +
                 "lasts longer between meals: mass to the 1/4 power. Body parts and growing up are charged as before. " +
                 "Off, the store ignores size and mass only adds its share through Body Mass Metabolic Weight.")]
        public bool scaleWithBodySize = true;
        [Tooltip("The power of body mass the energy store follows: 1 is in proportion.")]
        [Min(0f)] public float storeMassExponent = 1f;
        [Tooltip("The power of body mass the whole upkeep follows: 0.75 is Kleiber's law.")]
        [Min(0f)] public float upkeepMassExponent = 0.75f;
        // What the energy store gene has been multiplied by for this body's size (1 at the founders' size).
        [System.NonSerialized] private float energyStoreScale = 1f;
        // An arrival from an off-screen population starts with this share of its store; negative for everyone else.
        [System.NonSerialized] private float arrivalEnergyFraction = -1f;

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
        [SerializeField, Min(0f)] private float referenceAgeingOnset;
        [SerializeField] private Vector3 referenceLocalScale;
        [SerializeField, Min(0f)] private float referenceAgentRadius;
        [SerializeField, Min(0f)] private float referenceAgentHeight;
        [SerializeField, Min(0f)] private float referenceAgentBaseOffset;
        [SerializeField, Min(0f)] private float referenceAgentStoppingDistance;
        [SerializeField, Tooltip("Calculated metabolic drain after applying all trait tradeoffs.")]
        private float currentEnergyDrainPerSecond = 1f;
        public float CurrentEnergyDrainPerSecond => currentEnergyDrainPerSecond * RestEnergyMultiplier *
            (thermalResponse != null ? thermalResponse.EnergyDrainMultiplier : 1f) *
            (inWater ? waterMovement.EnergyMultiplier(bodyEffects.swimmingAbility) : 1f) *
            (inDeadZone ? SeaBiomeMap.CurrentRules.deadZoneEnergyCost : 1f) *
            (1f + Mathf.Max(0f, smokeUpkeepCost) * smokeFelt);

        // The energy ledger (item 61): the store as the ledger last saw it. Every change the simulation makes goes
        // through SetEnergy, so a difference at the start of a frame is a change nothing accounted for.
        private float ledgerEnergy;
        private bool leftTheLedger;
        public void SyncEnergyLedger() => ledgerEnergy = currentEnergy;

        void SetEnergy(float value, EnergyFlow flow)
        {
            if (EnergyLedger.Enabled) EnergyLedger.Record(flow, (double)value - currentEnergy);
            currentEnergy = value;
            ledgerEnergy = value;
        }
        // The adult body's mass against the founders', from its bulk and height alone: what sets the size of the
        // energy store and the scale of the upkeep (item 67). Body parts are bone, horn and muscle, not storage, and
        // keep their own charge.
        public float RelativeBodyMass
        {
            get
            {
                float bulkRatio = Mathf.Max(0.01f, bodyBulk) /
                                  (referenceBodyBulk > 0f ? referenceBodyBulk : Mathf.Max(0.01f, bodyBulk));
                float heightRatio = Mathf.Max(0.01f, bodyHeight) /
                                    (referenceBodyHeight > 0f ? referenceBodyHeight : Mathf.Max(0.01f, bodyHeight));
                return bulkRatio * bulkRatio * Mathf.Lerp(1f, heightRatio, Mathf.Clamp01(heightMassContribution));
            }
        }
        // What the energy store gene is multiplied by for this body's size, and the store per unit of the founders'
        // mass that the gene sets.
        public float EnergyStoreScale => energyStoreScale;
        public float EnergyStorePerMass => maxEnergy / Mathf.Max(0.0001f, energyStoreScale);
        // What the body's size multiplies the whole upkeep by.
        public float UpkeepSizeScale => scaleWithBodySize
            ? Mathf.Pow(RelativeBodyMass, Mathf.Max(0f, upkeepMassExponent))
            : 1f;

        // Sizes the energy store for the body (item 67): the gene is the store at the founders' mass. The founders'
        // reference is taken first, from the genes before scaling, so a founder's own store is its gene.
        void ApplyEnergyStoreScale()
        {
            InitializeMetabolicReference();
            float perMass = EnergyStorePerMass;
            energyStoreScale = scaleWithBodySize
                ? Mathf.Pow(RelativeBodyMass, Mathf.Max(0f, storeMassExponent))
                : 1f;
            maxEnergy = perMass * energyStoreScale;
        }

        // For a body whose size or switch changed after it started (tests, tools): resizes the store, losing what no
        // longer fits, and the upkeep.
        public void RefreshEnergyStore()
        {
            ApplyEnergyStoreScale();
            if (currentEnergy > maxEnergy) SetEnergy(maxEnergy, EnergyFlow.OverCapacity);
            RefreshMetabolicRate();
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
            if (referenceAgeingOnset <= 0f) referenceAgeingOnset = Mathf.Max(0.01f, ageingOnset);
        }

        // Harmful mutations and perks add their costs on top of the traits' cost, outside its limits.
        public void RefreshMetabolicRate()
        {
            InitializeMetabolicReference();

            float inheritedCost = perkEffects.energyUseMultiplier *
                                  (1f + NightVisionEnergyCost * Mathf.Clamp01(nightVision)) *
                                  (genome != null && genome.IsValid
                                      ? genome.EnergyUseMultiplier(harmfulMutationEnergyCost)
                                      : 1f);
            float repairWeight = enableAgeing ? repairMetabolicWeight : 0f;
            float totalWeight = speedMetabolicWeight + strengthMetabolicWeight + bodyMassMetabolicWeight +
                                visionMetabolicWeight + energyCapacityMetabolicWeight +
                                staminaCapacityMetabolicWeight + healthMetabolicWeight + repairWeight;
            // With size scaling (item 67) the adult body's mass scales the whole upkeep, below, rather than adding
            // its own share here.
            float sizeScale = UpkeepSizeScale;
            if (totalWeight <= Mathf.Epsilon)
            {
                currentEnergyDrainPerSecond = Mathf.Max(0f, baseEnergyDrainPerSecond) * sizeScale * inheritedCost;
                return;
            }

            // Body parts add their weight; their other advantages (speed, strength, vision) are charged below.
            // A young animal is cheaper to run while it is small; growing is charged separately (UpdateGrowth).
            float relativeMassBurden = (scaleWithBodySize ? 1f : RelativeBodyMass) *
                                       (1f + bodyEffects.partsMass) * growth * growth * growth;

            float weightedTraitCost =
                (Mathf.Max(0.01f, moveSpeed) * growth / referenceMoveSpeed) * speedMetabolicWeight +
                (Mathf.Max(0.01f, strength) / referenceStrength) * strengthMetabolicWeight +
                relativeMassBurden * bodyMassMetabolicWeight +
                (Mathf.Max(0.01f, visionRadius) / referenceVisionRadius) * visionMetabolicWeight +
                // The store the gene sets: the extra a bigger body holds is paid for by its size.
                (Mathf.Max(0.01f, EnergyStorePerMass) / referenceMaxEnergy) * energyCapacityMetabolicWeight +
                (Mathf.Max(0.01f, maxStamina) / referenceMaxStamina) * staminaCapacityMetabolicWeight +
                (Mathf.Max(0.01f, maxHealth) / referenceMaxHealth) * healthMetabolicWeight +
                (Mathf.Max(0.01f, ageingOnset) / referenceAgeingOnset) * repairWeight;

            float minimum = Mathf.Max(0f, minimumMetabolicMultiplier);
            float maximum = Mathf.Max(minimum, maximumMetabolicMultiplier);
            float multiplier = Mathf.Clamp(weightedTraitCost / totalWeight, minimum, maximum);
            currentEnergyDrainPerSecond = Mathf.Max(0f, baseEnergyDrainPerSecond) * multiplier * sizeScale * inheritedCost;
        }
    }
}
