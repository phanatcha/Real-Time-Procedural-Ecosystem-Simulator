using UnityEngine;
using UnityEngine.Serialization;

public class UtilityDecisionPolicy : AnimalDecisionPolicy
{
    [Header("Utility Weights")]
    [Min(0f)] public float travelEnergyWeight = 1f;
    [Min(0f)] public float attackEnergyWeight = 1f;
    [Min(0f)] public float staminaShortfallWeight = 0.25f;
    [Min(0f)] public float injuryRiskWeight = 0.1f;
    public float minimumFoodUtility = 0.1f;

    [Header("Threat Response")]
    [FormerlySerializedAs("fleeAfterBeingAttacked")]
    public bool enableFightOrFlight = true;
    [Tooltip("Fight normally when self combat power is at least this fraction of combined threat power.")]
    [Range(0.1f, 2f)] public float fightPowerRatio = 0.9f;
    [Tooltip("Amount subtracted from the fight threshold while already fighting the same threat. This prevents rapid fight/flee oscillation near an even matchup.")]
    [Range(0f, 0.5f)] public float fightContinuationHysteresis = 0.15f;
    [Tooltip("Fight as a last resort when escape looks poor but this minimum power ratio is still met.")]
    [Range(0f, 1f)] public float desperateFightPowerRatio = 0.45f;
    [Tooltip("Contribution of every attacker after the strongest. Lower values model crowding and diminishing returns.")]
    [Range(0f, 1f)] public float additionalAttackerContribution = 0.65f;
    [Tooltip("Power multiplier for a remembered attacker that is no longer visibly hunting this animal.")]
    [Range(0f, 1f)] public float rememberedThreatPowerMultiplier = 0.75f;
    [Tooltip("Minimum threat weight at the edge of perception.")]
    [Range(0f, 1f)] public float minimumDistantThreatWeight = 0.25f;
    [Tooltip("Required escape score before an outmatched animal chooses to flee instead of making a last stand.")]
    [Range(0f, 1f)] public float minimumEscapeFeasibility = 0.55f;

    [Header("Size Intimidation")]
    [Tooltip("How strongly relative body size changes perceived danger. A value of 0 disables size intimidation.")]
    [Range(0f, 2f)] public float sizeIntimidationWeight = 0.5f;
    [Tooltip("Share of perceived size contributed by height. The remainder comes from body bulk.")]
    [Range(0f, 1f)] public float heightIntimidationContribution = 0.3f;
    [Tooltip("Lowest multiplier that relative size can apply to perceived danger.")]
    [Range(0.1f, 1f)] public float minimumSizeIntimidationMultiplier = 0.6f;
    [Tooltip("Highest multiplier that relative size can apply to perceived danger.")]
    [Min(1f)] public float maximumSizeIntimidationMultiplier = 1.8f;

    [Header("Runtime Threat Debug")]
    [SerializeField] private int lastThreatCount;
    [SerializeField] private float lastSelfCombatPower;
    [SerializeField] private float lastCombinedThreatPower;
    [SerializeField] private float lastFightPowerRatio;
    [SerializeField] private float lastRequiredFightPowerRatio;
    [SerializeField] private float lastEscapeFeasibility;
    [SerializeField] private float lastPrimaryThreatSizeRatio = 1f;
    [SerializeField] private float lastPrimaryThreatSizeMultiplier = 1f;

    public override AnimalDecision Decide(SeekFood self, AnimalPerception perception)
    {
        if (enableFightOrFlight && perception.HasThreats)
        {
            return DecideThreatResponse(self, perception);
        }

        ResetThreatDebug();

        float bestUtility = minimumFoodUtility;
        AnimalDecision bestDecision = new AnimalDecision(AgentIntent.Wander);

        float plantUtility = ScoreFood(self, perception.nearestPlant, perception.nearestPlantDistance);
        if (plantUtility > bestUtility)
        {
            bestUtility = plantUtility;
            bestDecision = new AnimalDecision(AgentIntent.SeekPlant, perception.nearestPlant);
        }

        float meatUtility = ScoreFood(self, perception.nearestMeat, perception.nearestMeatDistance);
        if (meatUtility > bestUtility)
        {
            bestUtility = meatUtility;
            bestDecision = new AnimalDecision(AgentIntent.SeekMeat, perception.nearestMeat);
        }

        float huntUtility = ScoreHunt(self, perception.nearestPrey, perception.nearestPreyDistance);
        if (huntUtility > bestUtility)
        {
            bestDecision = new AnimalDecision(AgentIntent.HuntPrey, preyTarget: perception.nearestPrey);
        }

        return bestDecision;
    }

    AnimalDecision DecideThreatResponse(SeekFood self, AnimalPerception perception)
    {
        float strongestThreatPower = 0f;
        float totalThreatPower = 0f;
        float fastestThreatSpeed = 0f;
        SeekFood primaryThreat = null;
        Vector3 fleeDirection = Vector3.zero;
        float primaryThreatSizeRatio = 1f;
        float primaryThreatSizeMultiplier = 1f;

        foreach (AnimalThreat threat in perception.threats)
        {
            SeekFood attacker = threat.attacker;
            if (!self.IsViablePrey(attacker))
            {
                continue;
            }

            float distanceFraction = Mathf.Clamp01(threat.distance / Mathf.Max(0.01f, self.visionRadius));
            float distanceWeight = Mathf.Lerp(1f, minimumDistantThreatWeight, distanceFraction);
            float intentWeight = threat.isActivelyHunting ? 1f : rememberedThreatPowerMultiplier;
            float sizeRatio = GetRelativeSizeRatio(self, attacker);
            float sizeMultiplier = CalculateSizeIntimidationMultiplier(sizeRatio);
            float effectivePower = attacker.GetCombatPower() * sizeMultiplier *
                                   distanceWeight * intentWeight;

            totalThreatPower += effectivePower;
            fastestThreatSpeed = Mathf.Max(fastestThreatSpeed, attacker.GetCurrentMovementSpeed());
            if (effectivePower > strongestThreatPower)
            {
                strongestThreatPower = effectivePower;
                primaryThreat = attacker;
                primaryThreatSizeRatio = sizeRatio;
                primaryThreatSizeMultiplier = sizeMultiplier;
            }

            Vector3 away = self.transform.position - attacker.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
            {
                fleeDirection += away.normalized * effectivePower / Mathf.Max(1f, threat.distance);
            }
        }

        if (primaryThreat == null)
        {
            ResetThreatDebug();
            return new AnimalDecision(AgentIntent.Wander);
        }

        float combinedThreatPower = strongestThreatPower +
                                    Mathf.Max(0f, totalThreatPower - strongestThreatPower) *
                                    additionalAttackerContribution;
        float selfCombatPower = self.GetCombatPower();
        float powerRatio = selfCombatPower / Mathf.Max(0.01f, combinedThreatPower);
        float requiredFightPowerRatio = self.IsFightingThreat(primaryThreat)
            ? Mathf.Max(0f, fightPowerRatio - fightContinuationHysteresis)
            : fightPowerRatio;

        float speedAdvantage = self.GetCurrentMovementSpeed() /
                               Mathf.Max(0.01f, self.GetCurrentMovementSpeed() + fastestThreatSpeed);
        float escapeFeasibility = speedAdvantage * 0.5f +
                                  self.StaminaFraction * 0.35f +
                                  self.HealthFraction * 0.15f;

        lastThreatCount = perception.threats.Count;
        lastSelfCombatPower = selfCombatPower;
        lastCombinedThreatPower = combinedThreatPower;
        lastFightPowerRatio = powerRatio;
        lastRequiredFightPowerRatio = requiredFightPowerRatio;
        lastEscapeFeasibility = escapeFeasibility;
        lastPrimaryThreatSizeRatio = primaryThreatSizeRatio;
        lastPrimaryThreatSizeMultiplier = primaryThreatSizeMultiplier;

        if (powerRatio >= requiredFightPowerRatio)
        {
            return new AnimalDecision(AgentIntent.FightThreat, preyTarget: primaryThreat);
        }

        if (escapeFeasibility >= minimumEscapeFeasibility)
        {
            return new AnimalDecision(AgentIntent.Flee, preyTarget: primaryThreat,
                                      fleeDirection: fleeDirection.normalized);
        }

        if (powerRatio >= desperateFightPowerRatio)
        {
            return new AnimalDecision(AgentIntent.FightThreat, preyTarget: primaryThreat);
        }

        return new AnimalDecision(AgentIntent.Flee, preyTarget: primaryThreat,
                                  fleeDirection: fleeDirection.normalized);
    }

    void ResetThreatDebug()
    {
        lastThreatCount = 0;
        lastSelfCombatPower = 0f;
        lastCombinedThreatPower = 0f;
        lastFightPowerRatio = 0f;
        lastRequiredFightPowerRatio = 0f;
        lastEscapeFeasibility = 0f;
        lastPrimaryThreatSizeRatio = 1f;
        lastPrimaryThreatSizeMultiplier = 1f;
    }

    float ScoreFood(SeekFood self, FoodItem food, float distance)
    {
        if (food == null || !food.IsAvailable)
        {
            return float.NegativeInfinity;
        }

        float travelTime = distance / self.GetCurrentMovementSpeed();
        float travelCost = travelTime * self.CurrentEnergyDrainPerSecond * travelEnergyWeight;
        return self.EstimateDigestibleEnergy(food) - travelCost;
    }

    float ScoreHunt(SeekFood self, SeekFood prey, float distance)
    {
        if (!self.IsViablePrey(prey) || self.strength <= 0f)
        {
            return float.NegativeInfinity;
        }

        float digestibleCarcassEnergy = prey.EstimatedCarcassRawEnergy *
                                         self.GetDigestionEfficiency(FoodType.Meat);
        float attacksRequired = Mathf.Ceil(prey.currentHealth / self.strength);
        float attackCost = attacksRequired * self.attackEnergyCost * attackEnergyWeight;
        float staminaDemand = attacksRequired * self.attackStaminaCost;
        float staminaShortfall = Mathf.Max(0f, staminaDemand - self.currentStamina) *
                                 staminaShortfallWeight;
        float travelTime = distance / self.GetCurrentMovementSpeed();
        float travelCost = travelTime * self.CurrentEnergyDrainPerSecond * travelEnergyWeight;
        float preySizeMultiplier = CalculateSizeIntimidationMultiplier(GetRelativeSizeRatio(self, prey));
        float injuryRisk = prey.strength * preySizeMultiplier * injuryRiskWeight *
                           Mathf.Clamp01(prey.currentHealth / Mathf.Max(0.01f, prey.maxHealth));

        return digestibleCarcassEnergy - attackCost - staminaShortfall - travelCost - injuryRisk;
    }

    float GetRelativeSizeRatio(SeekFood observer, SeekFood other)
    {
        float heightContribution = Mathf.Clamp01(heightIntimidationContribution);
        float observerPerceivedSize = Mathf.Lerp(Mathf.Max(0.01f, observer.bodyBulk),
                                                 Mathf.Max(0.01f, observer.bodyHeight),
                                                 heightContribution);
        float otherPerceivedSize = Mathf.Lerp(Mathf.Max(0.01f, other.bodyBulk),
                                              Mathf.Max(0.01f, other.bodyHeight),
                                              heightContribution);
        return otherPerceivedSize / Mathf.Max(0.01f, observerPerceivedSize);
    }

    float CalculateSizeIntimidationMultiplier(float relativeSizeRatio)
    {
        float minimumMultiplier = Mathf.Max(0.01f, minimumSizeIntimidationMultiplier);
        float maximumMultiplier = Mathf.Max(minimumMultiplier, maximumSizeIntimidationMultiplier);
        float rawMultiplier = 1f + sizeIntimidationWeight * (relativeSizeRatio - 1f);
        return Mathf.Clamp(rawMultiplier, minimumMultiplier, maximumMultiplier);
    }
}
