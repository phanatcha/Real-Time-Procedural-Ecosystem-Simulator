using System.Collections.Generic;
using System.Text;
using UnityEngine;

// What an animal's perks do to it. Perks are on/off inherited traits that flip rarely at birth (SeekFood's Perk
// Flip Chance), and each comes with a cost:
// - thick fur: more cold tolerance, as much less heat tolerance
// - venom: an attacker takes back part of the damage of every bite; costs energy
// - camouflage: other animals only spot it, as prey or as a hunter, within part of their vision, except while it
//   flees; costs energy
// - regeneration: slowly heals while the animal has energy; costs energy
// - scavenger gut: can eat spoiled carcasses, which nothing else can; digests plants worse
// Body plates already cover armour.
public readonly struct PerkEffects
{
    // Degrees Celsius added to the inherited tolerances.
    public readonly float coldToleranceChange;
    public readonly float heatToleranceChange;
    // Multiplies energy use: 1 plus the upkeep of each perk.
    public readonly float energyUseMultiplier;
    // Share of each bite's damage the attacker takes back.
    public readonly float venomDamageReturned;
    // How far other animals can spot it, as a fraction of their vision radius, while it isn't fleeing.
    public readonly float visibility;
    // Fraction of maximum health healed per simulated second while the animal has energy.
    public readonly float regenerationPerSecond;
    public readonly bool canEatSpoiledFood;
    // Multiplies how much of a plant's energy the animal digests.
    public readonly float plantDigestionMultiplier;

    public static readonly PerkEffects None = new PerkEffects(0f, 0f, 1f, 0f, 1f, 0f, false, 1f);

    public PerkEffects(float coldToleranceChange, float heatToleranceChange, float energyUseMultiplier,
                       float venomDamageReturned, float visibility, float regenerationPerSecond,
                       bool canEatSpoiledFood, float plantDigestionMultiplier)
    {
        this.coldToleranceChange = coldToleranceChange;
        this.heatToleranceChange = heatToleranceChange;
        this.energyUseMultiplier = energyUseMultiplier;
        this.venomDamageReturned = venomDamageReturned;
        this.visibility = visibility;
        this.regenerationPerSecond = regenerationPerSecond;
        this.canEatSpoiledFood = canEatSpoiledFood;
        this.plantDigestionMultiplier = plantDigestionMultiplier;
    }
}

public static class AnimalPerks
{
    public const float ThickFurToleranceShift = 8f;    // degrees C more cold tolerance, as much less heat tolerance
    public const float VenomDamageReturned = 0.5f;     // of each bite, before the bitten animal's plates
    public const float VenomEnergyCost = 0.08f;        // +8% energy use
    public const float CamouflageVisibility = 0.6f;    // spotted within 60% of the other animal's vision radius
    public const float CamouflageEnergyCost = 0.05f;   // +5% energy use
    public const float RegenerationPerSecond = 0.01f;  // 1% of maximum health per simulated second
    public const float RegenerationEnergyCost = 0.1f;  // +10% energy use
    public const float ScavengerPlantDigestion = 0.7f; // digests 30% less of a plant's energy

    public static PerkEffects Evaluate(AnimalGenome genome)
    {
        if (genome == null || !genome.IsValid)
        {
            return PerkEffects.None;
        }

        bool fur = genome.HasPerk(AnimalPerk.ThickFur);
        bool venom = genome.HasPerk(AnimalPerk.Venom);
        bool camouflage = genome.HasPerk(AnimalPerk.Camouflage);
        bool regeneration = genome.HasPerk(AnimalPerk.Regeneration);
        bool scavenger = genome.HasPerk(AnimalPerk.ScavengerGut);

        float upkeep = (venom ? VenomEnergyCost : 0f) + (camouflage ? CamouflageEnergyCost : 0f) +
                       (regeneration ? RegenerationEnergyCost : 0f);
        return new PerkEffects(
            fur ? ThickFurToleranceShift : 0f,
            fur ? -ThickFurToleranceShift : 0f,
            1f + upkeep,
            venom ? VenomDamageReturned : 0f,
            camouflage ? CamouflageVisibility : 1f,
            regeneration ? RegenerationPerSecond : 0f,
            scavenger,
            scavenger ? ScavengerPlantDigestion : 1f);
    }

    public static string PerkName(AnimalPerk perk) => perk switch
    {
        AnimalPerk.ThickFur => "thick fur",
        AnimalPerk.Venom => "venom",
        AnimalPerk.Camouflage => "camouflage",
        AnimalPerk.Regeneration => "regeneration",
        AnimalPerk.ScavengerGut => "scavenger gut",
        _ => perk.ToString()
    };

    // For the inspector, e.g. "thick fur, venom".
    public static string Describe(AnimalGenome genome)
    {
        if (genome == null || !genome.IsValid)
        {
            return "";
        }

        StringBuilder text = new StringBuilder();
        for (int index = 0; index < AnimalGenome.PerkCount; index++)
        {
            AnimalPerk perk = (AnimalPerk)index;
            if (!genome.HasPerk(perk)) continue;

            if (text.Length > 0) text.Append(", ");
            text.Append(PerkName(perk));
        }

        return text.Length == 0 ? "none" : text.ToString();
    }

    // The share of a group with each perk (indexed by AnimalPerk, 0 to 1), e.g. "thick fur 12%, venom 3%".
    // Perks nobody has are left out.
    public static string DescribeShares(IReadOnlyList<float> shares)
    {
        StringBuilder text = new StringBuilder();
        int count = shares == null ? 0 : Mathf.Min(shares.Count, AnimalGenome.PerkCount);
        for (int index = 0; index < count; index++)
        {
            if (shares[index] < 0.005f) continue;

            if (text.Length > 0) text.Append(", ");
            text.Append($"{PerkName((AnimalPerk)index)} {100f * shares[index]:0}%");
        }

        return text.Length == 0 ? "none yet" : text.ToString();
    }
}
