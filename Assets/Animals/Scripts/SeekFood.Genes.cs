using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Its genes made real: capturing a founder's genome, applying a genome to the traits, body parts and perks
    // (items 7, 13), and giving or taking a perk on a live animal.
    public partial class SeekFood
    {
        [Header("Body Plan")]
        [Tooltip("How body parts sprout, grow, shrink and turn into other parts at birth. The chances are at a mutation " +
                 "rate of 1.")]
        public BodyPlanMutation bodyPlanMutation = BodyPlanMutation.Default;
        [Tooltip("How far a full-size leg pair lifts the body off the ground at body height 1, in world units. " +
                 "Standing taller also raises the animal's feeding reach.")]
        [Min(0f)] public float fullLegLength = 1.6f;
        [SerializeField, Tooltip("This animal's body parts and their sizes (0-1), for reference.")]
        private string bodyPlan;
        public string BodyPlanDescription => bodyPlan;
        public string PerkDescription => perks;
        public int HarmfulMutations => harmfulMutations;
        public bool HasPerk(AnimalPerk perk) => genome != null && genome.IsValid && genome.HasPerk(perk);
        private BodyPlanEffects bodyEffects = BodyPlanEffects.None;
        private PerkEffects perkEffects = PerkEffects.None;

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
            ApplyEnergyStoreScale();
        }

        // Body parts and perks adjust the traits the genes have just set (see AnimalBodyPlan and AnimalPerks). The
        // genome keeps the unadjusted values, so their effects never compound over generations.
        void ApplyBodyPlan()
        {
            bodyEffects = AnimalBodyPlan.Evaluate(genome);
            perkEffects = AnimalPerks.Evaluate(genome);
            geneMoveSpeed = moveSpeed;
            moveSpeed *= bodyEffects.speedMultiplier;
            strength *= bodyEffects.strengthMultiplier;
            visionRadius *= bodyEffects.visionMultiplier;
            thermalResponse.coldTolerance = Mathf.Max(0f, thermalResponse.coldTolerance + bodyEffects.coldToleranceChange +
                                                          perkEffects.coldToleranceChange);
            thermalResponse.heatTolerance = Mathf.Max(0f, thermalResponse.heatTolerance + bodyEffects.heatToleranceChange +
                                                          perkEffects.heatToleranceChange);
            bodyPlan = AnimalBodyPlan.Describe(genome);
            perks = AnimalPerks.Describe(genome);
        }

        // Gives or takes a perk now, for god tools and experiments (the soak runner's --founder-perk): the genome, the
        // perk's effects, the upkeep, the look and the count of glowing animals follow. Children inherit it as usual.
        public void SetPerk(AnimalPerk perk, bool on)
        {
            if (genome == null || !genome.IsValid || genome.HasPerk(perk) == on) return;
            PerkEffects before = perkEffects;
            genome.SetPerk(perk, on);
            perkEffects = AnimalPerks.Evaluate(genome);
            perks = AnimalPerks.Describe(genome);
            if (thermalResponse != null)
            {
                thermalResponse.coldTolerance = Mathf.Max(0f, thermalResponse.coldTolerance +
                                                              perkEffects.coldToleranceChange - before.coldToleranceChange);
                thermalResponse.heatTolerance = Mathf.Max(0f, thermalResponse.heatTolerance +
                                                              perkEffects.heatToleranceChange - before.heatToleranceChange);
            }
            if (perkEffects.glows && !countedAsGlowing)
            {
                countedAsGlowing = true;
                glowingAnimals++;
            }
            else if (!perkEffects.glows && countedAsGlowing)
            {
                countedAsGlowing = false;
                glowingAnimals = Mathf.Max(0, glowingAnimals - 1);
            }
            RefreshMetabolicRate();
            BuildBodyView();
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
            captured[AnimalGene.SexualDrive] = sexualDrive;
            captured[AnimalGene.BreedingThreshold] = reproductionThreshold;
            captured[AnimalGene.Torpor] = thermalResponse.torpor;
            captured[AnimalGene.MutationRate] = mutationRate;
            captured[AnimalGene.LitterSize] = litterSize;
            captured[AnimalGene.Nocturnality] = nocturnality;
            captured[AnimalGene.NightVision] = nightVision;
            // Founders start the markers in the middle, so chance can move them either way.
            captured[AnimalGene.MarkerA] = 0.5f;
            captured[AnimalGene.MarkerB] = 0.5f;
            captured[AnimalGene.MarkerC] = 0.5f;
            captured[AnimalGene.AgeingOnset] = ageingOnset;
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
            energyStoreScale = 1f;
            maxStamina = genome[AnimalGene.MaxStamina];
            maxHealth = genome[AnimalGene.MaxHealth];
            maturityTime = genome[AnimalGene.MaturityTime];
            maxLifespan = genome[AnimalGene.MaxLifespan];
            ageingOnset = genome[AnimalGene.AgeingOnset];
            dietAffinity = genome[AnimalGene.DietAffinity];
            thermalResponse.preferredTemperature = genome[AnimalGene.PreferredTemperature];
            thermalResponse.coldTolerance = genome[AnimalGene.ColdTolerance];
            thermalResponse.heatTolerance = genome[AnimalGene.HeatTolerance];
            // A locked drive overwrites the inherited gene, so the genome, the recordings and the behaviour agree.
            if (lockedSexualDrive >= 0f) genome[AnimalGene.SexualDrive] = Mathf.Clamp01(lockedSexualDrive);
            sexualDrive = genome[AnimalGene.SexualDrive];
            reproductionThreshold = genome[AnimalGene.BreedingThreshold];
            thermalResponse.torpor = genome[AnimalGene.Torpor];
            mutationRate = genome[AnimalGene.MutationRate];
            litterSize = genome[AnimalGene.LitterSize];
            nocturnality = genome[AnimalGene.Nocturnality];
            nightVision = genome[AnimalGene.NightVision];
            harmfulMutations = genome.HarmfulMutations;
        }
    }
}
