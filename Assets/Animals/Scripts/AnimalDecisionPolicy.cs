using System.Collections.Generic;
using UnityEngine;

namespace Boreal.Animals
{
    public enum AgentIntent
    {
        Wander,
        SeekPlant,
        SeekMeat,
        HuntPrey,
        FightThreat,
        Flee,
        SeekMate,
        // Walk to fresh water and drink (item 27).
        Drink,
        // Settle down at the animal's off hours (item 74).
        Rest
    }

    public struct AnimalThreat
    {
        public SeekFood attacker;
        public float distance;
        public bool isActivelyHunting;
        public bool recentlyAttacked;

        public AnimalThreat(SeekFood attacker, float distance, bool isActivelyHunting, bool recentlyAttacked)
        {
            this.attacker = attacker;
            this.distance = distance;
            this.isActivelyHunting = isActivelyHunting;
            this.recentlyAttacked = recentlyAttacked;
        }
    }

    public struct AnimalPerception
    {
        public FoodItem nearestPlant;
        public float nearestPlantDistance;
        public FoodItem nearestMeat;
        public float nearestMeatDistance;
        public SeekFood nearestPrey;
        public float nearestPreyDistance;
        // The most promising partner in view, only looked for while the animal is looking for a mate.
        public SeekFood mate;
        public float mateDistance;
        // The chance a courtship with the mate ends in a child.
        public float mateFertility;
        // The nearest place to drink, only looked for while the animal is thirsty.
        public bool hasDrinkingSpot;
        public Vector3 drinkingSpot;
        public float drinkingSpotDistance;
        // A wildfire within the animal's fire sense radius (item 79): the distance to the nearest burning block's centre
        // of heat, a direction away from the fires (unit, flat), and the heat and smoke the animal feels now.
        public bool fireSensed;
        public float fireDistance;
        public Vector3 fireAway;
        public float fireHeat;
        public float smoke;
        public List<AnimalThreat> threats;

        public bool HasThreats => threats != null && threats.Count > 0;
    }

    public struct AnimalDecision
    {
        public AgentIntent intent;
        public FoodItem foodTarget;
        public SeekFood preyTarget;
        public Vector3 fleeDirection;
        public SeekFood mateTarget;
        public Vector3 drinkingSpot;
        // A flight from a wildfire, with no animal to run from (item 79).
        public bool fromFire;

        public AnimalDecision(AgentIntent intent, FoodItem foodTarget = null, SeekFood preyTarget = null,
                              Vector3 fleeDirection = default, SeekFood mateTarget = null, Vector3 drinkingSpot = default,
                              bool fromFire = false)
        {
            this.intent = intent;
            this.foodTarget = foodTarget;
            this.preyTarget = preyTarget;
            this.fleeDirection = fleeDirection;
            this.mateTarget = mateTarget;
            this.drinkingSpot = drinkingSpot;
            this.fromFire = fromFire;
        }
    }

    public abstract class AnimalDecisionPolicy : MonoBehaviour
    {
        public abstract AnimalDecision Decide(SeekFood self, AnimalPerception perception);
    }
}
