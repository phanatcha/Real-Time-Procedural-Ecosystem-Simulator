using System.Collections.Generic;
using UnityEngine;

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
    HeatTolerance
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

// How the body plan changes at birth. Chances are percentages per birth.
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

// Everything an animal inherits: numeric trait genes plus a body plan of one part (type and size) per site.
// SeekFood copies the genes into its trait fields at birth and lets the body parts adjust them.
[System.Serializable]
public sealed class AnimalGenome
{
    public static readonly int GeneCount = System.Enum.GetValues(typeof(AnimalGene)).Length;
    public static readonly int SiteCount = System.Enum.GetValues(typeof(BodySite)).Length;

    // How much one body site counts towards genetic distance, next to the numeric genes' weights (16 in
    // total). At 0.5 a whole full-size part of difference is about one species threshold (0.03). In a
    // neutral-drift simulation (250 animals, default mutation rates) the first split still came after
    // about 45 generations, with about 10 species alive instead of 6 without body parts.
    public const float BodySiteDistanceWeight = 0.5f;

    // Per gene: distance weight divided by the gene's range, so Distance is one multiply per gene.
    // Declared after GeneCount because static fields initialise in order.
    static readonly float[] DistanceScales = BuildDistanceScales();
    static readonly float TotalDistanceWeight = SumDistanceWeights() + BodySiteDistanceWeight * SiteCount;

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
            partSizes = (float[])partSizes.Clone()
        };
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
    // each body site is: 0 means identical, 1 means opposite ends of every gene's range and completely
    // different parts at every site.
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

        return total / TotalDistanceWeight;
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
