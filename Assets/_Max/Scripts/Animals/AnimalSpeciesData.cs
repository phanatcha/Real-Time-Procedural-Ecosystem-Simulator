using UnityEngine;

public enum AnimalDiet
{
    Herbivore,
    Carnivore,
    Omnivore
}

[CreateAssetMenu(
    fileName = "AnimalSpecies",
    menuName = "Ecosystem/Animal Species"
)]
public class AnimalSpeciesData : ScriptableObject
{



    [Header("Spawn Habitat")]
    public LayerMask allowedSpawnLayers;

    public float minimumSpawnHeight = 5f;
    public float idealMinSpawnHeight = 15f;
    public float idealMaxSpawnHeight = 60f;
    public float maximumSpawnHeight = 100f;

    public float maxSpawnSlope = 35f;

    [Header("Identity")]
    public string speciesName;

    public AnimalDiet diet;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float rotationSpeed = 5f;

    public float maxSlopeAngle = 35f;

    [Header("Detection")]
    public float foodDetectionRadius = 20f;
    public float predatorDetectionRadius = 25f;

    [Header("Needs")]
    public float maxHealth = 100f;

    public float hungerRate = 1f;

    public float hungryThreshold = 60f;

    public float starvationThreshold = 95f;

    [Header("Energy")]
    public float maxEnergy = 100f;

    public float energyDrainRate = 0.5f;

    [Header("Behavior")]
    public float wanderRadius = 15f;

    public float fleeDistance = 20f;
}