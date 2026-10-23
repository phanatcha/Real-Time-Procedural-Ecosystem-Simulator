using System.Text;
using UnityEngine;

// What an animal's body parts do to it. Parts adjust existing traits instead of adding new ones:
// - legs make it faster (and lift it higher, so it reaches higher food)
// - a neck reaches higher food and sees a little further
// - horns hit harder, plates absorb damage, eye stalks see further
// - fins (and a little, long legs) give swimming ability (see WaterMovement)
// Every part costs something: its weight raises energy use (SeekFood's metabolism already charges for mass,
// speed, strength and vision), plates and fins slow the animal on land, and long limbs shed heat.
public readonly struct BodyPlanEffects
{
    public readonly float speedMultiplier;
    public readonly float visionMultiplier;
    public readonly float strengthMultiplier;
    // Metres of extra feeding reach.
    public readonly float extraFeedingReach;
    // 1 = full damage from attacks.
    public readonly float damageTakenMultiplier;
    // Extra body mass as a fraction of the plain capsule body.
    public readonly float partsMass;
    // Degrees Celsius added to the inherited tolerances.
    public readonly float coldToleranceChange;
    public readonly float heatToleranceChange;
    // Size (0-1) of the largest leg pair, which sets how far the body stands off the ground.
    public readonly float longestLegs;
    // 0 = can only wade, 1 = the best swimmer. Mostly from fins, a little from long legs.
    public readonly float swimmingAbility;

    public static readonly BodyPlanEffects None = new BodyPlanEffects(1f, 1f, 1f, 0f, 1f, 0f, 0f, 0f, 0f);

    public BodyPlanEffects(float speedMultiplier, float visionMultiplier, float strengthMultiplier,
                           float extraFeedingReach, float damageTakenMultiplier, float partsMass,
                           float coldToleranceChange, float heatToleranceChange, float longestLegs,
                           float swimmingAbility = 0f)
    {
        this.speedMultiplier = speedMultiplier;
        this.visionMultiplier = visionMultiplier;
        this.strengthMultiplier = strengthMultiplier;
        this.extraFeedingReach = extraFeedingReach;
        this.damageTakenMultiplier = damageTakenMultiplier;
        this.partsMass = partsMass;
        this.coldToleranceChange = coldToleranceChange;
        this.heatToleranceChange = heatToleranceChange;
        this.longestLegs = longestLegs;
        this.swimmingAbility = swimmingAbility;
    }
}

public static class AnimalBodyPlan
{
    // Effects of a full-size part; a part at size s gives s times as much.
    public const float LegPairSpeedBonus = 0.5f;        // each of the first two leg pairs
    public const float ExtraLegPairSpeedBonus = 0.25f;  // leg pairs beyond two add less
    public const float NeckReach = 10f;                 // metres
    public const float NeckVisionBonus = 0.1f;
    public const float HornStrengthBonus = 1f;          // +100%
    public const float EyeStalkVisionBonus = 0.6f;
    public const float PlateDamageReduction = 0.2f;     // per site
    public const float MaximumDamageReduction = 0.6f;
    public const float PlateSpeedPenalty = 0.08f;       // per site
    public const float FinLandSpeedPenalty = 0.05f;     // per site
    // Swimming ability from a full-size fin at each kind of site. A full tail fin alone reaches deep water (0.5).
    public const float TailFinSwimming = 0.5f;
    public const float BackFinSwimming = 0.2f;
    public const float PairedFinSwimming = 0.25f;       // per pair
    // Long legs help wading, but never enough on their own to reach deep water.
    public const float LegWadingSwimming = 0.1f;
    public const float MinimumSpeedMultiplier = 0.3f;
    // Allen's rule: long legs, necks and fins shed heat, so they suit warmth and cost cold tolerance.
    public const float AppendageToleranceShift = 3f;    // degrees C per unit of exposure
    // Bergmann's rule: a bulkier body keeps its heat, so it suits cold and costs heat tolerance.
    public const float BulkToleranceShift = 6f;         // degrees C per unit of bulk above 1

    public static BodyPlanEffects Evaluate(AnimalGenome genome)
    {
        if (genome == null || !genome.IsValid)
        {
            return BodyPlanEffects.None;
        }

        float legPairs = 0f;
        float longestLegs = 0f;
        float neck = 0f;
        float horn = 0f;
        float eyeStalks = 0f;
        float plates = 0f;
        float fins = 0f;
        float finSwimming = 0f;
        float mass = 0f;
        float exposure = 0f;

        for (int index = 0; index < AnimalGenome.SiteCount; index++)
        {
            BodySite site = (BodySite)index;
            BodyPartType type = genome.GetPartType(site);
            float size = genome.GetPartSize(site);
            if (type == BodyPartType.None || size <= 0f)
            {
                continue;
            }

            int instances = AnimalGenome.IsPaired(site) ? 2 : 1;
            mass += PartMass(type) * size * instances;
            exposure += HeatExposure(type) * size;

            switch (type)
            {
                case BodyPartType.Legs:
                    legPairs += size;
                    longestLegs = Mathf.Max(longestLegs, size);
                    break;
                case BodyPartType.Neck: neck += size; break;
                case BodyPartType.Horn: horn += size; break;
                case BodyPartType.EyeStalks: eyeStalks += size; break;
                case BodyPartType.Plates: plates += size; break;
                case BodyPartType.Fins:
                    fins += size;
                    finSwimming += FinSwimming(site) * size;
                    break;
            }
        }

        float legBonus = LegPairSpeedBonus * Mathf.Min(legPairs, 2f) +
                         ExtraLegPairSpeedBonus * Mathf.Max(0f, legPairs - 2f);
        float speedMultiplier = (1f + legBonus) *
                                Mathf.Max(0f, 1f - PlateSpeedPenalty * plates) *
                                Mathf.Max(0f, 1f - FinLandSpeedPenalty * fins);
        float bulk = genome[AnimalGene.BodyBulk];
        float coldChange = -AppendageToleranceShift * exposure + BulkToleranceShift * (bulk - 1f);

        return new BodyPlanEffects(
            Mathf.Max(MinimumSpeedMultiplier, speedMultiplier),
            1f + EyeStalkVisionBonus * eyeStalks + NeckVisionBonus * neck,
            1f + HornStrengthBonus * horn,
            NeckReach * neck,
            1f - Mathf.Min(MaximumDamageReduction, PlateDamageReduction * plates),
            mass,
            coldChange,
            -coldChange,
            longestLegs,
            Mathf.Clamp01(finSwimming + LegWadingSwimming * longestLegs));
    }

    // Swimming ability a full-size fin gives at a site: a tail fin drives the animal forward, side fins steer
    // and paddle, and a back fin keeps it steady.
    public static float FinSwimming(BodySite site) => site switch
    {
        BodySite.Tail => TailFinSwimming,
        BodySite.Back => BackFinSwimming,
        BodySite.FrontPair or BodySite.MiddlePair or BodySite.RearPair => PairedFinSwimming,
        _ => 0f
    };

    // Mass of one full-size part as a fraction of the plain body. Paired sites carry two of them.
    public static float PartMass(BodyPartType type) => type switch
    {
        BodyPartType.Legs => 0.075f,
        BodyPartType.Neck => 0.12f,
        BodyPartType.Horn => 0.06f,
        BodyPartType.Plates => 0.12f,
        BodyPartType.EyeStalks => 0.02f,
        BodyPartType.Fins => 0.03f,
        _ => 0f
    };

    // How much a full-size part exposes to the air and sheds heat. A leg pair counts once.
    public static float HeatExposure(BodyPartType type) => type switch
    {
        BodyPartType.Legs => 1f,
        BodyPartType.Neck => 0.8f,
        BodyPartType.Fins => 0.5f,
        BodyPartType.EyeStalks => 0.2f,
        _ => 0f
    };

    public static string PartName(BodyPartType type) => type switch
    {
        BodyPartType.Legs => "legs",
        BodyPartType.Neck => "neck",
        BodyPartType.Horn => "horns",
        BodyPartType.Plates => "plates",
        BodyPartType.EyeStalks => "eye stalks",
        BodyPartType.Fins => "fins",
        _ => "none"
    };

    // For the inspector, e.g. "Head: neck 0.40, FrontPair: legs 0.25".
    public static string Describe(AnimalGenome genome)
    {
        if (genome == null || !genome.IsValid)
        {
            return "";
        }

        StringBuilder text = new StringBuilder();
        for (int index = 0; index < AnimalGenome.SiteCount; index++)
        {
            BodySite site = (BodySite)index;
            BodyPartType type = genome.GetPartType(site);
            if (type == BodyPartType.None)
            {
                continue;
            }

            if (text.Length > 0) text.Append(", ");
            text.Append($"{site}: {PartName(type)} {genome.GetPartSize(site):0.00}");
        }

        return text.Length == 0 ? "plain capsule" : text.ToString();
    }
}
