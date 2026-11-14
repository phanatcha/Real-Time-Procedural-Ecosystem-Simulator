using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Eating: diet, which food it can reach and digest, giving up on food it can't reach, and the meal itself.
    public partial class SeekFood
    {
        [Header("Dietary Genetics")]
        [Tooltip("0 = herbivore specialist, 0.5 = omnivore, 1 = carnivore specialist.")]
        [Range(0f, 1f)] public float dietAffinity = 0.5f;
        [Tooltip("Minimum efficiency retained for the non-specialized food type.")]
        [Range(0f, 1f)] public float minimumDietEfficiency = 0.2f;
        public bool CanEatSpoiledFood => perkEffects.canEatSpoiledFood;
        // A neck adds reach on top of the body's own; legs add it by lifting the whole animal.
        public float FeedingReach => (Mathf.Max(0f, baseFeedingReach) * Mathf.Max(0.01f, bodyHeight) +
                                      bodyEffects.extraFeedingReach) * growth;
        public float CurrentFoodInteractionRange => foodInteractionRange * Mathf.Max(0.01f, bodyBulk);
        public string DietClassification => ClassifyDiet(dietAffinity);

        public static string ClassifyDiet(float affinity)
        {
            return affinity < 1f / 3f ? "Herbivore" :
                   affinity > 2f / 3f ? "Carnivore" : "Omnivore";
        }
        private float foodChaseTimer;
        // Food this animal recently failed to get to, ignored until the simulated time stored with it.
        private readonly List<UnreachableFood> unreachableFood = new List<UnreachableFood>();

        private struct UnreachableFood
        {
            public FoodItem food;
            public float retryTime;
        }

        bool IsCurrentFoodTargetAvailable()
        {
            return currentFoodTarget != null && currentFoodTarget.IsAvailable &&
                   (!currentFoodTarget.IsSpoiled || CanEatSpoiledFood);
        }

        // True once chasing the current food can't work: its route stops short of it (across water or up a cliff),
        // the animal has walked as far as the route goes and still can't eat it, or the chase is taking far longer
        // than crossing its whole field of view would. Without this, relatives that all pick the same unreachable
        // plant wait at the shore until they starve.
        bool IsCurrentFoodOutOfReach()
        {
            float speed = Mathf.Max(0.1f, GetCurrentMovementSpeed());
            float allowedChaseTime = Mathf.Max(maximumFoodChaseDuration, 3f * visionRadius / speed);
            if (foodChaseTimer >= allowedChaseTime)
            {
                return true;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending || RouteJustSet)
            {
                return false;
            }

            if (agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Vector3 shortfall = agent.pathEndPosition - currentFoodTarget.transform.position;
                shortfall.y = 0f;
                float range = CurrentFoodInteractionRange;
                if (shortfall.sqrMagnitude > range * range)
                {
                    return true;
                }
            }

            return !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.5f;
        }

        void GiveUpOnCurrentFood()
        {
            FoodItem abandonedFood = currentFoodTarget;
            RememberUnreachableFood(abandonedFood);
            ClearTargets();
            foodChaseTimer = 0f;
            currentState = State.Idling;
            timer = 0f;
            decisionTimer = 0f;
            TryResetPath();

            if (SpeciesManager.Instance != null && SpeciesManager.Instance.ShouldLogLifecycleEvents)
            {
                Debug.Log($"{gameObject.name} gave up on {abandonedFood.gameObject.name}, which it could not reach.");
            }
        }

        void RememberUnreachableFood(FoodItem food)
        {
            if (food == null)
            {
                return;
            }

            // A short memory is enough; the oldest entry makes room.
            if (unreachableFood.Count >= 16)
            {
                unreachableFood.RemoveAt(0);
            }

            unreachableFood.Add(new UnreachableFood
            {
                food = food,
                retryTime = Time.time + Mathf.Max(0.1f, unreachableFoodMemory)
            });
        }

        bool IsRememberedAsUnreachable(FoodItem food)
        {
            float now = Time.time;
            for (int index = unreachableFood.Count - 1; index >= 0; index--)
            {
                UnreachableFood entry = unreachableFood[index];
                if (entry.food == null || now >= entry.retryTime)
                {
                    unreachableFood.RemoveAt(index);
                }
                else if (entry.food == food)
                {
                    return true;
                }
            }

            return false;
        }

        // A scavenger gut digests plants worse.
        public float GetDigestionEfficiency(FoodType foodType)
        {
            float affinity = Mathf.Clamp01(dietAffinity);
            float minimum = Mathf.Clamp01(minimumDietEfficiency);
            return foodType == FoodType.Plant
                ? (minimum + (1f - minimum) * (1f - affinity)) * perkEffects.plantDigestionMultiplier
                : minimum + (1f - minimum) * affinity;
        }

        public bool CanReachFood(FoodItem food)
        {
            // Food out in deep water is only for animals that can swim there.
            if (food == null || (food.inDeepWater && !CanSwimDeepWater))
            {
                return false;
            }

            float verticalReachNeeded = Mathf.Max(0f, food.transform.position.y - transform.position.y);
            float requiredReach = Mathf.Max(verticalReachNeeded, Mathf.Max(0f, food.requiredFeedingReach));
            return FeedingReach >= requiredReach;
        }

        // Spoiled food is only for animals with a scavenger gut.
        public bool CanEat(FoodItem food)
        {
            return food != null && (!food.IsSpoiled || CanEatSpoiledFood) && CanReachFood(food);
        }

        public float EstimateDigestibleEnergy(FoodItem food)
        {
            return !CanEat(food)
                ? 0f
                : food.CurrentNutrition * GetDigestionEfficiency(food.foodType);
        }

        bool TryConsumeFood(FoodItem food)
        {
            // Courting animals don't stop to eat food they happen to touch, and a dying one can't eat its own carcass.
            if (food == null || isDying || currentState == State.Courting || !CanEat(food) || currentEnergy >= maxEnergy ||
                !food.TryConsume(out float rawNutrition, out FoodType foodType))
            {
                return false;
            }

            float digestibleEnergy = rawNutrition * GetDigestionEfficiency(foodType);
            float acceptedEnergy = Mathf.Min(maxEnergy - currentEnergy, digestibleEnergy);
            SetEnergy(currentEnergy + acceptedEnergy, foodType == FoodType.Plant ? EnergyFlow.Plants : EnergyFlow.Meat);
            EnergyLedger.CheckMeal(rawNutrition, acceptedEnergy, name);

            if (SpeciesManager.Instance != null)
            {
                SpeciesManager.Instance.RecordConsumption(this, foodType, rawNutrition, acceptedEnergy);
            }

            ClearTargets();
            currentState = State.Idling;
            timer = actionTimer;
            decisionTimer = 0f;
            TryResetPath();
            return true;
        }
    }
}
