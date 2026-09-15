using UnityEngine;

public class AnimalNeeds : MonoBehaviour
{
    private AnimalSpeciesData species;

    public float Health { get; private set; }

    public float Hunger { get; private set; }

    public float Energy { get; private set; }

    public bool IsHungry =>
        Hunger >= species.hungryThreshold;

    public bool IsStarving =>
        Hunger >= species.starvationThreshold;

    public bool IsDead =>
        Health <= 0;

    public void Initialize(
        AnimalSpeciesData speciesData)
    {
        species = speciesData;

        Health =
            species.maxHealth;

        Hunger = 0;

        Energy =
            species.maxEnergy;
    }

    void Update()
    {
        if (species == null)
            return;

        if (IsDead)
            return;

        Hunger +=
            species.hungerRate *
            Time.deltaTime;

        Energy -=
            species.energyDrainRate *
            Time.deltaTime;

        Hunger =
            Mathf.Clamp(
                Hunger,
                0,
                100
            );

        Energy =
            Mathf.Clamp(
                Energy,
                0,
                species.maxEnergy
            );

        if (IsStarving)
        {
            Health -=
                5f *
                Time.deltaTime;
        }
    }

    public void Eat(float amount)
    {
        Hunger -= amount;

        Hunger =
            Mathf.Max(
                Hunger,
                0
            );
    }

    public void Rest(float amount)
    {
        Energy += amount;

        Energy =
            Mathf.Min(
                Energy,
                species.maxEnergy
            );
    }

    public void TakeDamage(float damage)
    {
        Health -= damage;

        Health =
            Mathf.Max(
                Health,
                0
            );
    }
}