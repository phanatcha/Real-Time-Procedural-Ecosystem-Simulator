using UnityEngine;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    public class UtilityDecisionPolicy : AnimalDecisionPolicy
    {
        [Header("Utility Weights")]
        [Min(0f)] public float travelEnergyWeight = 1f;
        [Min(0f)] public float attackEnergyWeight = 1f;
        [Min(0f)] public float staminaShortfallWeight = 0.25f;
        [Min(0f)] public float injuryRiskWeight = 0.1f;
        public float minimumFoodUtility = 0.1f;
        [Tooltip("What reaching a fully fertile partner is worth to an animal looking for a mate, as a fraction of " +
                 "its maximum energy, weighed against the energy food would give it.")]
        [Min(0f)] public float mateValue = 0.5f;
        [Tooltip("How much of its maximum energy a drink is worth to an animal with no water left, scaled down " +
                 "towards the thirst threshold, where it is worth nothing.")]
        [Min(0f)] public float thirstValue = 1f;
        [Tooltip("What resting is worth deep in an animal's off hours, as a fraction of its maximum energy, weighed " +
                 "against food, water and mates; less towards its active hours, and nothing once it is hungry or " +
                 "thirsty (SeekFood.RestUrgency). A partner or rich food close by can still wake it.")]
        [Min(0f)] public float restValue = 0.5f;

        [Header("Wildfire")]
        [Tooltip("What getting away from a wildfire is worth at full danger, as a fraction of maximum energy, weighed " +
                 "against food, water and mates (item 79).")]
        [Min(0f)] public float fireEscapeValue = 2f;
        [Tooltip("Fire danger (0-1) at which an animal drops everything, predators included, and runs from the fire.")]
        [Range(0f, 1f)] public float firePanicDanger = 0.45f;
        [Tooltip("Metres from a fire's nearest burning block within which it is a danger; nearer is worse.")]
        [Min(1f)] public float fireDangerDistance = 120f;
        [Tooltip("How far an animal downwind of a fire turns across the wind as it runs: the fire runs with the " +
                 "wind, so the way out is to the side.")]
        [Range(0f, 2f)] public float crosswindEscape = 0.6f;

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
            // A close fire comes before everything, predators included (item 79).
            float fireDanger = FireDanger(self, perception);
            if (fireDanger > 0f && fireDanger >= firePanicDanger)
            {
                ResetThreatDebug();
                return FleeFire(perception);
            }

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

            float drinkUtility = ScoreDrink(self, perception);
            if (drinkUtility > bestUtility)
            {
                bestUtility = drinkUtility;
                bestDecision = new AnimalDecision(AgentIntent.Drink, drinkingSpot: perception.drinkingSpot);
            }

            float restUtility = restValue * self.maxEnergy * self.RestUrgency;
            if (restUtility > bestUtility)
            {
                bestUtility = restUtility;
                bestDecision = new AnimalDecision(AgentIntent.Rest);
            }

            float mateUtility = ScoreMate(self, perception.mate, perception.mateDistance, perception.mateFertility);
            if (mateUtility > bestUtility)
            {
                bestUtility = mateUtility;
                bestDecision = new AnimalDecision(AgentIntent.SeekMate, mateTarget: perception.mate);
            }

            float huntUtility = ScoreHunt(self, perception.nearestPrey, perception.nearestPreyDistance);
            if (huntUtility > bestUtility)
            {
                bestUtility = huntUtility;
                bestDecision = new AnimalDecision(AgentIntent.HuntPrey, preyTarget: perception.nearestPrey);
            }

            // A fire further off is weighed against everything else: worth more the nearer it is.
            float fireUtility = fireDanger > 0f ? fireEscapeValue * self.maxEnergy * fireDanger : float.NegativeInfinity;
            if (fireUtility > bestUtility)
            {
                bestDecision = FleeFire(perception);
            }

            return bestDecision;
        }

        // 0 to 1: nearer fires are worse, and worse still downwind of them, where the fire is heading; heat and smoke
        // the animal already feels add to it. An animal in water is safe from all but the worst of it.
        public float FireDanger(SeekFood self, AnimalPerception perception)
        {
            if (!perception.fireSensed && perception.fireHeat <= 0f) return 0f;
            float danger = 0f;
            if (perception.fireSensed)
            {
                danger = Mathf.Clamp01(1f - perception.fireDistance / Mathf.Max(1f, fireDangerDistance));
                NaturalEventDirector director = NaturalEventDirector.Active;
                if (director != null && danger > 0f)
                {
                    NaturalConditions conditions = director.Conditions;
                    Vector3 wind = new Vector3(conditions.windDirection.x, 0f, conditions.windDirection.y);
                    float downwind = Mathf.Clamp01(Vector3.Dot(wind, perception.fireAway));
                    danger *= 1f + 0.5f * downwind * Mathf.Clamp01(conditions.windSpeed / 6f);
                }
            }
            danger = Mathf.Max(danger, 1.5f * perception.fireHeat) + 0.3f * perception.smoke;
            if (self.IsInWater) danger *= 0.3f;
            return Mathf.Clamp01(danger);
        }

        // Away from the fires, turning across the wind when downwind of them. With nothing to go by (in the middle of
        // the fire), upwind, onto ground it has already burned.
        AnimalDecision FleeFire(AnimalPerception perception)
        {
            Vector3 away = perception.fireAway;
            away.y = 0f;
            NaturalEventDirector director = NaturalEventDirector.Active;
            Vector3 wind = Vector3.zero;
            if (director != null)
            {
                Vector2 flat = director.Conditions.windDirection;
                wind = new Vector3(flat.x, 0f, flat.y);
            }
            if (away.sqrMagnitude < 0.01f)
            {
                away = wind.sqrMagnitude > 0.01f ? -wind : Vector3.zero;
            }
            else if (wind.sqrMagnitude > 0.01f)
            {
                away.Normalize();
                float downwind = Vector3.Dot(away, wind);
                if (downwind > 0f)
                {
                    Vector3 across = new Vector3(-wind.z, 0f, wind.x);
                    if (Vector3.Dot(across, away) < 0f) across = -across;
                    away += across * (crosswindEscape * downwind);
                }
            }
            if (away.sqrMagnitude > 0.0001f) away.Normalize();
            return new AnimalDecision(AgentIntent.Flee, fleeDirection: away, fromFire: true);
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
                if (!self.IsOtherLivingAnimal(attacker))
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
            return UsableEnergy(self, self.EstimateDigestibleEnergy(food)) - travelCost;
        }

        // Water is worth more the thirstier the animal is: nothing at the thirst threshold, its whole energy store
        // (times Thirst Value) when it has none left, so a dying animal goes to water before food.
        float ScoreDrink(SeekFood self, AnimalPerception perception)
        {
            if (!perception.hasDrinkingSpot)
            {
                return float.NegativeInfinity;
            }

            float travelTime = perception.drinkingSpotDistance / self.GetCurrentMovementSpeed();
            float travelCost = travelTime * self.CurrentEnergyDrainPerSecond * travelEnergyWeight;
            return thirstValue * self.maxEnergy * self.ThirstUrgency - travelCost;
        }

        // Energy only counts up to what still fits under the animal's maximum, so a full animal gains nothing
        // from food and leaves it for hungrier ones (it can't eat while full anyway).
        static float UsableEnergy(SeekFood self, float digestibleEnergy)
        {
            return Mathf.Min(digestibleEnergy, Mathf.Max(0f, self.maxEnergy - self.currentEnergy));
        }

        // An animal only sees a mate while it is ready to breed and looking for one.
        float ScoreMate(SeekFood self, SeekFood mate, float distance, float fertility)
        {
            if (mate == null)
            {
                return float.NegativeInfinity;
            }

            float travelTime = distance / self.GetCurrentMovementSpeed();
            float travelCost = travelTime * self.CurrentEnergyDrainPerSecond * travelEnergyWeight;
            return mateValue * self.maxEnergy * fertility - travelCost;
        }

        float ScoreHunt(SeekFood self, SeekFood prey, float distance)
        {
            if (!self.IsViablePrey(prey) || self.strength <= 0f)
            {
                return float.NegativeInfinity;
            }

            float digestibleCarcassEnergy = UsableEnergy(self, prey.EstimatedCarcassRawEnergy *
                                                               self.GetDigestionEfficiency(FoodType.Meat));
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
            return GetPerceivedSize(other) / Mathf.Max(0.01f, GetPerceivedSize(observer));
        }

        // Body build, made larger by the weight of horns, plates and other body parts.
        float GetPerceivedSize(SeekFood animal)
        {
            float build = Mathf.Lerp(Mathf.Max(0.01f, animal.bodyBulk), Mathf.Max(0.01f, animal.bodyHeight),
                                     Mathf.Clamp01(heightIntimidationContribution));
            return build * (1f + 0.5f * animal.BodyPartsMass);
        }

        float CalculateSizeIntimidationMultiplier(float relativeSizeRatio)
        {
            float minimumMultiplier = Mathf.Max(0.01f, minimumSizeIntimidationMultiplier);
            float maximumMultiplier = Mathf.Max(minimumMultiplier, maximumSizeIntimidationMultiplier);
            float rawMultiplier = 1f + sizeIntimidationWeight * (relativeSizeRatio - 1f);
            return Mathf.Clamp(rawMultiplier, minimumMultiplier, maximumMultiplier);
        }
    }
}
