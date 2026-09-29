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

// Everything an animal inherits. SeekFood copies these values into its trait fields at birth.
[System.Serializable]
public sealed class AnimalGenome
{
    public static readonly int GeneCount = System.Enum.GetValues(typeof(AnimalGene)).Length;

    // Per gene: distance weight divided by the gene's range, so Distance is one multiply per gene.
    // Declared after GeneCount because static fields initialise in order.
    static readonly float[] DistanceScales = BuildDistanceScales();
    static readonly float TotalDistanceWeight = SumDistanceWeights();

    [SerializeField] private float[] genes;

    // An unset genome has no genes. SeekFood replaces it with one captured from the
    // animal's inspector values, which is how founders get their genome.
    public bool IsValid => genes != null && genes.Length == GeneCount;

    public float this[AnimalGene gene]
    {
        get => genes[(int)gene];
        set => genes[(int)gene] = GetDefinition(gene).Clamp(value);
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

    // Creates a genome with every gene at its minimum, ready to be filled in.
    public static AnimalGenome Create()
    {
        AnimalGenome genome = new AnimalGenome { genes = new float[GeneCount] };
        for (int index = 0; index < GeneCount; index++)
        {
            genome.genes[index] = GetDefinition((AnimalGene)index).minimum;
        }

        return genome;
    }

    public AnimalGenome Clone()
    {
        return new AnimalGenome { genes = (float[])genes.Clone() };
    }

    // mutationChance is a percentage per gene. mutationMagnitude is the largest step as a
    // fraction, e.g. 0.1 moves a proportional gene by up to 10% of its value.
    public AnimalGenome CreateMutatedCopy(float mutationChance, float mutationMagnitude, out bool mutated)
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

        return child;
    }

    // Weighted average of how far apart each gene is relative to its range:
    // 0 means identical, 1 means opposite ends of every gene's range.
    public static float Distance(AnimalGenome first, AnimalGenome second)
    {
        float total = 0f;
        for (int index = 0; index < GeneCount; index++)
        {
            total += Mathf.Abs(first.genes[index] - second.genes[index]) * DistanceScales[index];
        }

        return total / TotalDistanceWeight;
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
