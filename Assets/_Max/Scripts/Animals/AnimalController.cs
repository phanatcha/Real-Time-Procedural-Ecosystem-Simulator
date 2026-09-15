using UnityEngine;

public class AnimalController : MonoBehaviour
{
    [Header("Species")]
    public AnimalSpeciesData species;

    [Header("Components")]
    public AnimalMovement movement;
    public AnimalNeeds needs;
    public AnimalSensors sensors;

    public AnimalState CurrentState
    {
        get;
        private set;
    }

    private System.Random random;

    private float decisionTimer;

    private Vector3 fleeTarget;

    private Collider foodTarget;

    void Awake()
    {
        if (movement == null)
            movement =
                GetComponent<AnimalMovement>();

        if (needs == null)
            needs =
                GetComponent<AnimalNeeds>();

        if (sensors == null)
            sensors =
                GetComponent<AnimalSensors>();
    }

    void Start()
    {
        Initialize(
            species,
            GetInstanceID()
        );
    }

    public void Initialize(
        AnimalSpeciesData speciesData,
        int seed)
    {
        species =
            speciesData;

        random =
            new System.Random(
                seed
            );

        movement.Initialize(
            species
        );

        needs.Initialize(
            species
        );

        sensors.Initialize(
            species
        );

        CurrentState =
            AnimalState.Wander;
    }

    void Update()
    {
        if (species == null)
            return;

        if (needs.IsDead)
        {
            CurrentState =
                AnimalState.Dead;

            movement.Stop();

            return;
        }

        decisionTimer -=
            Time.deltaTime;

        if (decisionTimer <= 0)
        {
            MakeDecision();

            decisionTimer =
                0.5f;
        }

        RunState();
    }

    void MakeDecision()
    {
        if (species.diet ==
            AnimalDiet.Herbivore)
        {
            HerbivoreDecision();
        }

        if (species.diet ==
            AnimalDiet.Carnivore)
        {
            CarnivoreDecision();
        }
    }

    void HerbivoreDecision()
    {
        if (needs.IsHungry)
        {
            foodTarget =
                sensors.FindNearestVegetation();

            if (foodTarget != null)
            {
                CurrentState =
                    AnimalState.SearchFood;

                return;
            }
        }

        if (!movement.HasDestination)
        {
            CurrentState =
                AnimalState.Wander;
        }
    }

    void CarnivoreDecision()
    {
        if (needs.IsHungry)
        {
            AnimalController prey =
                sensors.FindNearestAnimal();

            if (prey != null)
            {
                movement.SetDestination(
                    prey.transform.position
                );

                CurrentState =
                    AnimalState.Chase;

                return;
            }
        }

        if (!movement.HasDestination)
        {
            CurrentState =
                AnimalState.Wander;
        }
    }

    void RunState()
    {
        switch (CurrentState)
        {
            case AnimalState.Wander:
                Wander();
                break;

            case AnimalState.SearchFood:
                SearchFood();
                break;

            case AnimalState.Chase:
                Chase();
                break;

            case AnimalState.Flee:
                Flee();
                break;

            case AnimalState.Rest:
                Rest();
                break;
        }
    }

    void Wander()
    {
        if (!movement.HasDestination)
        {
            movement.SetDestination(
                GetRandomWanderPosition()
            );
        }

        movement.Walk();
    }

    void SearchFood()
    {
        if (foodTarget == null)
        {
            CurrentState =
                AnimalState.Wander;

            return;
        }

        movement.SetDestination(
            foodTarget.transform.position
        );

        movement.Walk();
    }

    void Chase()
    {
        movement.Run();
    }

    void Flee()
    {
        movement.SetDestination(
            fleeTarget
        );

        movement.Run();
    }

    void Rest()
    {
        movement.Stop();

        needs.Rest(
            10f *
            Time.deltaTime
        );
    }

    Vector3 GetRandomWanderPosition()
    {
        float angle =
            (float)
            random.NextDouble() *
            360f;

        float distance =
            (float)
            random.NextDouble() *
            species.wanderRadius;

        Vector3 direction =
            Quaternion.Euler(
                0,
                angle,
                0
            ) *
            Vector3.forward;

        return transform.position +
            direction *
            distance;
    }
}