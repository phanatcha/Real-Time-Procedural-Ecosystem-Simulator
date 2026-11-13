
using System.Collections.Generic;
using UnityEngine;

namespace Boreal.Animals
{
    public enum AnimalGene
    {
        MoveSpeed,
        Strength,
        BodyBulk,
        BodyHeight,
        VisionRadius,
        MaxEnergy,
        MaxStamina,
        MaxHealth,
        MaturityTime,
        MaxLifespan,
        DietAffinity,
        PreferredTemperature,
        ColdTolerance,
        HeatTolerance,
        // 0 to 1: how much the animal looks for a mate instead of cloning itself (see MatingRules).
        SexualDrive,
        // 0.5 to 1: the share of its maximum energy an animal needs before it breeds. Higher keeps more in reserve, for
        // winter, at the cost of breeding less often.
        BreedingThreshold,
        // 0 to 1: how far the animal slows down in the cold to save energy (see AnimalTemperature).
        Torpor,
        // 0.25 to 4: multiplies every mutation chance when this genome is copied for a child (see CreateChild), so how
        // fast a lineage evolves also evolves. It raises the chance of harmful mutations too.
        MutationRate,
        // 1 to 4: how many young a birth has on average. The energy a birth hands over is split between them, so a
        // bigger litter means weaker young (see SeekFood.TryGiveBirth).
        LitterSize,
        // 0 to 1: when the animal is active (item 74). 0 by day, 1 by night, around 0.5 at dawn and dusk and in bursts
        // round the clock. It rests at its off hours (see SeekFood.ActivityAt).
        Nocturnality,
        // 0 to 1: how well the animal sees in the dark (item 75). Night eyes see much further by moonlight and
        // starlight, but are dazzled by day and cost energy (see SeekFood.SightShare).
        NightVision,
        // 0 to 1, starting at 0.5 in founders: three markers that are inherited and mutate like any gene but do
        // nothing and don't count towards genetic distance (item 56). They wander by chance alone, so how far they
        // move in a run is how far any gene could move by luck; a real gene has been selected only when it moves well
        // outside their spread (traits.csv).
        MarkerA,
        MarkerB,
        MarkerC,
        // 10 to 1200 simulated seconds: the age at which the animal starts to age (item 68). From then its vigour falls
        // over SeekFood's Ageing Span, taking its health, speed and fertility with it, until it dies of old age. A later
        // onset costs upkeep, as repair does (SeekFood.repairMetabolicWeight). Last in the list so genomes saved before
        // it keep their genes in place.
        AgeingOnset
    }

    // Where a body part can grow on the capsule body. Paired sites grow the same part mirrored left and right.
    public enum BodySite
    {
        Head,
        Back,
        Tail,
        FrontPair,
        MiddlePair,
        RearPair
    }

    public enum BodyPartType
    {
        None,
        Legs,
        Neck,
        Horn,
        Plates,
        EyeStalks,
        Fins
    }

    // On/off inherited traits. At birth a perk is rarely gained and more often lost (item 62). See AnimalPerks for what
    // each does and costs.
    public enum AnimalPerk
    {
        ThickFur,
        Venom,
        Camouflage,
        Regeneration,
        ScavengerGut,
        // Does nothing and doesn't count towards genetic distance: the perks' yardstick (item 56). It is gained and lost
        // at the same rates as the real perks, so at SeekFood's defaults (0.2% and 2% per birth) chance alone takes it
        // to about 2% of animals after 10 generations, 3% after 20 and 4% after 30, levelling out near 9%
        // (0.2 / (0.2 + 2)). A real perk well above that has been selected for.
        Marker
    }

    // How the body plan changes at birth. Chances are percentages per birth at a mutation rate of 1; the mutation
    // rate gene multiplies them.
    [System.Serializable]
    public struct BodyPlanMutation
    {
        [Tooltip("Percent chance per birth that an empty site sprouts a stub of a random part allowed there.")]
        public float sproutChance;
        [Tooltip("Size of a newly sprouted part, from 0 (nothing) to 1 (full size).")]
        public float sproutSize;
        [Tooltip("Percent chance per birth that each part grows or shrinks.")]
        public float growthChance;
        [Tooltip("Largest change in a part's size per mutation.")]
        public float growthStep;
        [Tooltip("A part that shrinks below this size disappears.")]
        public float lossSize;
        [Tooltip("Percent chance per birth that a part becomes another part allowed at its site, keeping its size " +
                 "(as fins became legs in real evolution).")]
        public float repurposeChance;
        [Tooltip("Lets fins sprout. Fins give swimming ability, which opens deep water and speeds the animal up in water.")]
        public bool allowFins;

        public static BodyPlanMutation Default => new BodyPlanMutation
        {
            sproutChance = 1f,
            sproutSize = 0.05f,
            growthChance = 10f,
            growthStep = 0.08f,
            lossSize = 0.02f,
            repurposeChance = 0.2f,
            allowFins = true
        };

        // The same rules with every chance multiplied by a mutation rate. Sizes and steps stay as they are.
        public BodyPlanMutation WithChancesScaledBy(float rate)
        {
            BodyPlanMutation scaled = this;
            scaled.sproutChance *= rate;
            scaled.growthChance *= rate;
            scaled.repurposeChance *= rate;
            return scaled;
        }
    }

    public enum GeneMutationStyle
    {
        // Steps by a fraction of the current value, e.g. speed 12 with a 10% step moves by up to 1.2.
        Proportional,
        // Steps by a fraction of a fixed scale, e.g. diet moves by up to 0.1 whatever its value.
        Additive
    }

    public readonly struct AnimalGeneDefinition
    {
        public readonly float minimum;
        public readonly float maximum;
        public readonly GeneMutationStyle style;

        // Proportional genes: the smallest value a step is based on, so traits near zero can still change.
        // Additive genes: the step size at a mutation magnitude of 1.
        public readonly float stepScale;

        // How much a difference in this gene counts towards genetic distance, relative to other genes.
        public readonly float distanceWeight;

        public AnimalGeneDefinition(float minimum, float maximum, GeneMutationStyle style, float stepScale,
                                    float distanceWeight = 1f)
        {
            this.minimum = minimum;
            this.maximum = maximum;
            this.style = style;
            this.stepScale = stepScale;
            this.distanceWeight = distanceWeight;
        }

        public float Clamp(float value)
        {
            return Mathf.Clamp(value, minimum, maximum);
        }
    }

    // Everything an animal inherits: numeric trait genes, a body plan of one part (type and size) per site, its perks
    // and the harmful mutations it carries. SeekFood copies the genes into its trait fields at birth and lets the
    // body parts and perks adjust them.
    [System.Serializable]
    public sealed class AnimalGenome
    {
        public static readonly int GeneCount = System.Enum.GetValues(typeof(AnimalGene)).Length;
        public static readonly int SiteCount = System.Enum.GetValues(typeof(BodySite)).Length;
        public static readonly int PerkCount = System.Enum.GetValues(typeof(AnimalPerk)).Length;

        // How much one body site counts towards genetic distance, next to the numeric genes' weights (24 in
        // total: twenty-one genes at 1, diet at 3, the markers at 0). At 0.5 a whole full-size part of difference is about
        // 0.018, under half the default species threshold (0.05). SpeciesManager.DefaultSpeciationThreshold describes how the weights and threshold were
        // calibrated.
        public const float BodySiteDistanceWeight = 0.5f;

        // How much one perk of difference counts towards genetic distance. At 0.25 it is about 0.009, under a fifth of the
        // default species threshold, so a single perk neither splits a species nor lowers fertility on its own.
        public const float PerkDistanceWeight = 0.25f;

        // Per gene: distance weight divided by the gene's range, so Distance is one multiply per gene.
        // Declared after GeneCount because static fields initialise in order.
        static readonly float[] DistanceScales = BuildDistanceScales();
        static readonly float TotalDistanceWeight = SumDistanceWeights() + BodySiteDistanceWeight * SiteCount +
                                                    PerkDistanceWeight * CountedPerks();

        // The parts each site can grow, indexed by BodySite.
        static readonly BodyPartType[][] AllowedParts =
        {
            new[] { BodyPartType.Neck, BodyPartType.Horn, BodyPartType.EyeStalks }, // Head
            new[] { BodyPartType.Plates, BodyPartType.Fins },                        // Back
            new[] { BodyPartType.Fins, BodyPartType.Plates },                        // Tail
            new[] { BodyPartType.Legs, BodyPartType.Fins, BodyPartType.Plates },     // FrontPair
            new[] { BodyPartType.Legs, BodyPartType.Fins, BodyPartType.Plates },     // MiddlePair
            new[] { BodyPartType.Legs, BodyPartType.Fins, BodyPartType.Plates }      // RearPair
        };

        [SerializeField] private float[] genes;
        [SerializeField] private BodyPartType[] partTypes;
        [SerializeField] private float[] partSizes;
        // Harmful mutations carried, each adding the same small cost to energy use (Muller's ratchet). A clone
        // keeps all of its parent's; a child of two parents inherits each of each parent's with a 50% chance, so
        // sexual lineages can shed them while cloning lineages only gain them. They are not genes, so they don't
        // count towards genetic distance.
        [SerializeField] private int harmfulMutations;
        // One bit per AnimalPerk.
        [SerializeField] private int perks;

        // An unset genome has no genes. SeekFood replaces it with one captured from the
        // animal's inspector values, which is how founders get their genome.
        public bool IsValid => genes != null && genes.Length == GeneCount &&
                               partTypes != null && partTypes.Length == SiteCount &&
                               partSizes != null && partSizes.Length == SiteCount;

        public float this[AnimalGene gene]
        {
            get => genes[(int)gene];
            set => genes[(int)gene] = GetDefinition(gene).Clamp(value);
        }

        public int HarmfulMutations => harmfulMutations;

        public bool HasPerk(AnimalPerk perk) => (perks & PerkBit(perk)) != 0;

        public void SetPerk(AnimalPerk perk, bool on)
        {
            perks = on ? perks | PerkBit(perk) : perks & ~PerkBit(perk);
        }

        static int PerkBit(AnimalPerk perk) => 1 << (int)perk;

        // How much the harmful mutations multiply energy use: 1 plus the cost of each, e.g. 1.25 for five at 0.05.
        public float EnergyUseMultiplier(float costPerHarmfulMutation) =>
            1f + harmfulMutations * Mathf.Max(0f, costPerHarmfulMutation);

        public static bool IsPaired(BodySite site) => site >= BodySite.FrontPair;

        public static IReadOnlyList<BodyPartType> GetAllowedParts(BodySite site) => AllowedParts[(int)site];

        public static bool IsAllowed(BodySite site, BodyPartType type) =>
            System.Array.IndexOf(AllowedParts[(int)site], type) >= 0;

        public BodyPartType GetPartType(BodySite site) => partTypes[(int)site];

        // 0 for an empty site, up to 1 for a full-size part.
        public float GetPartSize(BodySite site) =>
            partTypes[(int)site] == BodyPartType.None ? 0f : partSizes[(int)site];

        // Sets or clears one site. None or a size of 0 clears it.
        public void SetPart(BodySite site, BodyPartType type, float size)
        {
            if (type != BodyPartType.None && !IsAllowed(site, type))
            {
                throw new System.ArgumentException($"{type} cannot grow at {site}.", nameof(type));
            }

            int index = (int)site;
            size = Mathf.Clamp01(size);
            bool empty = type == BodyPartType.None || size <= 0f;
            partTypes[index] = empty ? BodyPartType.None : type;
            partSizes[index] = empty ? 0f : size;
        }

        // Bounds are roughly a quarter to four times the placeholder founder's values. Body and
        // temperature bounds match the limits SeekFood and AnimalTemperature already use.
        public static AnimalGeneDefinition GetDefinition(AnimalGene gene) => gene switch
        {
            AnimalGene.MoveSpeed => new AnimalGeneDefinition(2f, 48f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.Strength => new AnimalGeneDefinition(1f, 100f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.BodyBulk => new AnimalGeneDefinition(0.6f, 1.8f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.BodyHeight => new AnimalGeneDefinition(0.6f, 2.2f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.VisionRadius => new AnimalGeneDefinition(10f, 250f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.MaxEnergy => new AnimalGeneDefinition(50f, 400f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.MaxStamina => new AnimalGeneDefinition(20f, 300f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.MaxHealth => new AnimalGeneDefinition(20f, 400f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.MaturityTime => new AnimalGeneDefinition(5f, 200f, GeneMutationStyle.Proportional, 0.01f),
            AnimalGene.MaxLifespan => new AnimalGeneDefinition(30f, 1200f, GeneMutationStyle.Proportional, 0.01f),
            // Diet counts triple towards genetic distance because it changes how an animal lives.
            AnimalGene.DietAffinity => new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f, 3f),
            AnimalGene.PreferredTemperature => new AnimalGeneDefinition(-40f, 60f, GeneMutationStyle.Additive, 10f),
            AnimalGene.ColdTolerance => new AnimalGeneDefinition(0f, 40f, GeneMutationStyle.Proportional, 1f),
            AnimalGene.HeatTolerance => new AnimalGeneDefinition(0f, 40f, GeneMutationStyle.Proportional, 1f),
            AnimalGene.SexualDrive => new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f),
            AnimalGene.BreedingThreshold => new AnimalGeneDefinition(0.5f, 1f, GeneMutationStyle.Additive, 0.5f),
            AnimalGene.Torpor => new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f),
            // Proportional, so it steps by a share of its value and halving takes about as many mutations as doubling.
            AnimalGene.MutationRate => new AnimalGeneDefinition(0.25f, 4f, GeneMutationStyle.Proportional, 0.01f),
            // Additive, so a mutation moves it by a tenth of a child at the default magnitude.
            AnimalGene.LitterSize => new AnimalGeneDefinition(1f, 4f, GeneMutationStyle.Additive, 1f),
            // Steps like sexual drive: a tenth of its range at the default magnitude.
            AnimalGene.Nocturnality => new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f),
            AnimalGene.NightVision => new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f),
            // Markers step like the other 0-1 additive genes and weigh nothing towards genetic distance.
            AnimalGene.MarkerA or AnimalGene.MarkerB or AnimalGene.MarkerC =>
                new AnimalGeneDefinition(0f, 1f, GeneMutationStyle.Additive, 1f, 0f),
            // Steps like the lifespan: by a share of its value.
            AnimalGene.AgeingOnset => new AnimalGeneDefinition(10f, 1200f, GeneMutationStyle.Proportional, 0.01f),
            _ => throw new System.ArgumentOutOfRangeException(nameof(gene), gene, null)
        };

        // Creates a genome with every gene at its minimum and no body parts (the plain capsule), ready to be
        // filled in.
        public static AnimalGenome Create()
        {
            AnimalGenome genome = new AnimalGenome
            {
                genes = new float[GeneCount],
                partTypes = new BodyPartType[SiteCount],
                partSizes = new float[SiteCount]
            };
            for (int index = 0; index < GeneCount; index++)
            {
                genome.genes[index] = GetDefinition((AnimalGene)index).minimum;
            }

            return genome;
        }

        public AnimalGenome Clone()
        {
            return new AnimalGenome
            {
                genes = (float[])genes.Clone(),
                partTypes = (BodyPartType[])partTypes.Clone(),
                partSizes = (float[])partSizes.Clone(),
                harmfulMutations = harmfulMutations,
                perks = perks
            };
        }

        // A child of two parents, before mutation: each gene, each body site (its part and size together) and each
        // perk comes from one parent or the other at random, and each harmful mutation of either parent is passed on
        // with a 50% chance.
        public static AnimalGenome Recombine(AnimalGenome first, AnimalGenome second)
        {
            AnimalGenome child = first.Clone();
            child.harmfulMutations = InheritHalf(first.harmfulMutations) + InheritHalf(second.harmfulMutations);
            for (int index = 0; index < GeneCount; index++)
            {
                if (Random.value < 0.5f) child.genes[index] = second.genes[index];
            }

            for (int site = 0; site < SiteCount; site++)
            {
                if (Random.value < 0.5f)
                {
                    child.partTypes[site] = second.partTypes[site];
                    child.partSizes[site] = second.partSizes[site];
                }
            }

            for (int perk = 0; perk < PerkCount; perk++)
            {
                if (Random.value < 0.5f) child.SetPerk((AnimalPerk)perk, second.HasPerk((AnimalPerk)perk));
            }

            return child;
        }

        // How many of a parent's harmful mutations a child of two parents inherits: each one with a 50% chance.
        static int InheritHalf(int count)
        {
            int inherited = 0;
            for (int mutation = 0; mutation < count; mutation++)
            {
                if (Random.value < 0.5f) inherited++;
            }

            return inherited;
        }

        // chance is a percentage per birth. Call it once on each newborn's genome. Returns true when the genome
        // gained a harmful mutation.
        public bool TryAddHarmfulMutation(float chance)
        {
            if (chance <= 0f || (chance < 100f && Random.Range(0f, 100f) >= chance))
            {
                return false;
            }

            harmfulMutations++;
            return true;
        }

        // Chances are percentages per perk per birth. Call it once on each newborn's genome. A perk this genome lacks
        // turns on with gainChance; one it has turns off with lossChance. Losing a working trait is much easier than
        // building one, so SeekFood's defaults make losses ten times as likely (item 62). Returns true when any perk
        // changed.
        public bool MutatePerks(float gainChance, float lossChance)
        {
            if (gainChance <= 0f && lossChance <= 0f)
            {
                return false;
            }

            bool changed = false;
            for (int perk = 0; perk < PerkCount; perk++)
            {
                int bit = PerkBit((AnimalPerk)perk);
                float chance = (perks & bit) != 0 ? lossChance : gainChance;
                if (chance <= 0f || (chance < 100f && Random.Range(0f, 100f) >= chance))
                {
                    continue;
                }

                perks ^= bit;
                changed = true;
            }

            return changed;
        }

        // The same chance both ways: each perk that flips turns on if it was off and off if it was on.
        public bool MutatePerks(float chance) => MutatePerks(chance, chance);

        // A child's genome: a mutated copy of this one, as SeekFood makes at every birth. Chances are percentages at a
        // mutation rate of 1, and this genome's mutation rate gene multiplies every one of them: for the genes, the
        // body plan, the perks and harmful mutations. The magnitude stays as it is. A clone mutates at its parent's
        // rate; for a child of two parents, call it on the recombined genome, so it mutates at the rate it inherited.
        public AnimalGenome CreateChild(float mutationChance, float mutationMagnitude, BodyPlanMutation bodyPlanMutation,
                                        float perkGainChance, float perkLossChance, float harmfulMutationChance,
                                        out bool mutated)
        {
            float rate = this[AnimalGene.MutationRate];
            AnimalGenome child = CreateMutatedCopy(mutationChance * rate, mutationMagnitude,
                                                   bodyPlanMutation.WithChancesScaledBy(rate), out mutated);
            mutated |= child.TryAddHarmfulMutation(harmfulMutationChance * rate);
            mutated |= child.MutatePerks(perkGainChance * rate, perkLossChance * rate);
            return child;
        }

        // Mutates the numeric genes only; the body plan is copied unchanged.
        public AnimalGenome CreateMutatedCopy(float mutationChance, float mutationMagnitude, out bool mutated)
        {
            return CreateMutatedCopy(mutationChance, mutationMagnitude, default, out mutated);
        }

        // mutationChance is a percentage per gene. mutationMagnitude is the largest step as a
        // fraction, e.g. 0.1 moves a proportional gene by up to 10% of its value. The body plan then
        // mutates site by site following bodyPlanMutation.
        public AnimalGenome CreateMutatedCopy(float mutationChance, float mutationMagnitude,
                                              BodyPlanMutation bodyPlanMutation, out bool mutated)
        {
            AnimalGenome child = Clone();
            float chance = Mathf.Clamp(mutationChance, 0f, 100f);
            float magnitude = Mathf.Max(0f, mutationMagnitude);
            mutated = false;

            for (int index = 0; index < GeneCount; index++)
            {
                if (Random.Range(0f, 100f) >= chance)
                {
                    continue;
                }

                AnimalGeneDefinition definition = GetDefinition((AnimalGene)index);
                float parentValue = genes[index];
                float stepBase = definition.style == GeneMutationStyle.Proportional
                    ? Mathf.Max(Mathf.Abs(parentValue), definition.stepScale)
                    : definition.stepScale;
                float childValue = definition.Clamp(parentValue + Random.Range(-magnitude, magnitude) * stepBase);

                child.genes[index] = childValue;
                mutated |= childValue != parentValue;
            }

            for (int site = 0; site < SiteCount; site++)
            {
                mutated |= child.MutateSite(site, bodyPlanMutation);
            }

            return child;
        }

        // Empty sites may sprout a stub. Existing parts may turn into another allowed part, grow or shrink,
        // and disappear once they shrink below the loss size. Returns true when the site changed.
        bool MutateSite(int site, BodyPlanMutation rules)
        {
            BodyPartType type = partTypes[site];
            if (type == BodyPartType.None)
            {
                if (Random.Range(0f, 100f) >= rules.sproutChance)
                {
                    return false;
                }

                BodyPartType sprouted = PickPart((BodySite)site, BodyPartType.None, rules.allowFins);
                if (sprouted == BodyPartType.None)
                {
                    return false;
                }

                partTypes[site] = sprouted;
                partSizes[site] = Mathf.Clamp01(rules.sproutSize);
                return true;
            }

            bool changed = false;
            if (Random.Range(0f, 100f) < rules.repurposeChance)
            {
                BodyPartType repurposed = PickPart((BodySite)site, type, rules.allowFins);
                if (repurposed != BodyPartType.None)
                {
                    partTypes[site] = repurposed;
                    changed = true;
                }
            }

            if (Random.Range(0f, 100f) < rules.growthChance)
            {
                float size = Mathf.Clamp01(partSizes[site] + Random.Range(-rules.growthStep, rules.growthStep));
                changed |= size != partSizes[site];
                partSizes[site] = size;
            }

            if (partSizes[site] < rules.lossSize)
            {
                partTypes[site] = BodyPartType.None;
                partSizes[site] = 0f;
                changed = true;
            }

            return changed;
        }

        // A random part allowed at the site, other than excluded; None when there is no choice.
        static BodyPartType PickPart(BodySite site, BodyPartType excluded, bool allowFins)
        {
            BodyPartType[] allowed = AllowedParts[(int)site];
            int choices = 0;
            foreach (BodyPartType type in allowed)
            {
                if (type != excluded && (allowFins || type != BodyPartType.Fins)) choices++;
            }

            if (choices == 0)
            {
                return BodyPartType.None;
            }

            int pick = Random.Range(0, choices);
            foreach (BodyPartType type in allowed)
            {
                if (type == excluded || (!allowFins && type == BodyPartType.Fins)) continue;
                if (pick-- == 0) return type;
            }

            return BodyPartType.None;
        }

        // Weighted average of how far apart each gene is relative to its range, together with how different
        // each body site is and which perks differ: 0 means identical, 1 means opposite ends of every gene's range,
        // completely different parts at every site and opposite perks.
        public static float Distance(AnimalGenome first, AnimalGenome second)
        {
            float total = 0f;
            for (int index = 0; index < GeneCount; index++)
            {
                total += Mathf.Abs(first.genes[index] - second.genes[index]) * DistanceScales[index];
            }

            for (int site = 0; site < SiteCount; site++)
            {
                total += BodySiteDistanceWeight * SiteDifference(first, second, site);
            }

            for (int perk = 0; perk < PerkCount; perk++)
            {
                if (CountsTowardsDistance((AnimalPerk)perk) &&
                    first.HasPerk((AnimalPerk)perk) != second.HasPerk((AnimalPerk)perk)) total += PerkDistanceWeight;
            }

            return total / TotalDistanceWeight;
        }

        // Every perk but the marker, which is only a yardstick.
        public static bool CountsTowardsDistance(AnimalPerk perk) => perk != AnimalPerk.Marker;

        static int CountedPerks()
        {
            int counted = 0;
            for (int perk = 0; perk < PerkCount; perk++)
            {
                if (CountsTowardsDistance((AnimalPerk)perk)) counted++;
            }
            return counted;
        }

        // 0 to 1. The same part differs by its size difference; different parts differ by their sizes combined,
        // so a tiny new stub barely counts while two different full-size parts count fully.
        static float SiteDifference(AnimalGenome first, AnimalGenome second, int site)
        {
            float firstSize = first.partTypes[site] == BodyPartType.None ? 0f : first.partSizes[site];
            float secondSize = second.partTypes[site] == BodyPartType.None ? 0f : second.partSizes[site];
            return first.partTypes[site] == second.partTypes[site]
                ? Mathf.Abs(firstSize - secondSize)
                : Mathf.Min(1f, firstSize + secondSize);
        }

        // Splits genomes into groups linked by chains of close relatives: two genomes share a group when a
        // chain of genomes connects them with every step closer than the threshold. Genomes in different
        // groups are therefore never that close. Returns indices into the input, largest group first.
        public static List<List<int>> GroupByDistance(IReadOnlyList<AnimalGenome> genomes, float threshold)
        {
            int count = genomes.Count;
            int[] groupRoot = new int[count];
            for (int index = 0; index < count; index++)
            {
                groupRoot[index] = index;
            }

            for (int first = 0; first < count; first++)
            {
                for (int second = first + 1; second < count; second++)
                {
                    int firstRoot = FindGroupRoot(groupRoot, first);
                    int secondRoot = FindGroupRoot(groupRoot, second);
                    if (firstRoot != secondRoot && Distance(genomes[first], genomes[second]) < threshold)
                    {
                        groupRoot[secondRoot] = firstRoot;
                    }
                }
            }

            Dictionary<int, List<int>> groupsByRoot = new Dictionary<int, List<int>>();
            for (int index = 0; index < count; index++)
            {
                int root = FindGroupRoot(groupRoot, index);
                if (!groupsByRoot.TryGetValue(root, out List<int> group))
                {
                    group = new List<int>();
                    groupsByRoot.Add(root, group);
                }

                group.Add(index);
            }

            List<List<int>> groups = new List<List<int>>(groupsByRoot.Values);
            groups.Sort((first, second) => second.Count.CompareTo(first.Count));
            return groups;
        }

        static int FindGroupRoot(int[] groupRoot, int index)
        {
            while (groupRoot[index] != index)
            {
                groupRoot[index] = groupRoot[groupRoot[index]];
                index = groupRoot[index];
            }

            return index;
        }

        static float[] BuildDistanceScales()
        {
            float[] scales = new float[GeneCount];
            for (int index = 0; index < GeneCount; index++)
            {
                AnimalGeneDefinition definition = GetDefinition((AnimalGene)index);
                scales[index] = definition.distanceWeight / (definition.maximum - definition.minimum);
            }

            return scales;
        }

        static float SumDistanceWeights()
        {
            float total = 0f;
            for (int index = 0; index < GeneCount; index++)
            {
                total += GetDefinition((AnimalGene)index).distanceWeight;
            }

            return total;
        }
    }
}
