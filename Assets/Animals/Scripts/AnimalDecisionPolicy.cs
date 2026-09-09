using System.Collections.Generic;
using UnityEngine;

public enum AgentIntent
{
    Wander,
    SeekPlant,
    SeekMeat,
    HuntPrey,
    FightThreat,
    Flee
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
    public List<AnimalThreat> threats;

    public bool HasThreats => threats != null && threats.Count > 0;
}

public struct AnimalDecision
{
    public AgentIntent intent;
    public FoodItem foodTarget;
    public SeekFood preyTarget;
    public Vector3 fleeDirection;

    public AnimalDecision(AgentIntent intent, FoodItem foodTarget = null, SeekFood preyTarget = null,
                          Vector3 fleeDirection = default)
    {
        this.intent = intent;
        this.foodTarget = foodTarget;
        this.preyTarget = preyTarget;
        this.fleeDirection = fleeDirection;
    }
}

public abstract class AnimalDecisionPolicy : MonoBehaviour
{
    public abstract AnimalDecision Decide(SeekFood self, AnimalPerception perception);
}
