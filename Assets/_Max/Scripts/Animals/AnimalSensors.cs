using UnityEngine;

public class AnimalSensors : MonoBehaviour
{
    private AnimalSpeciesData species;

    public LayerMask vegetationMask;
    public LayerMask animalMask;

    public void Initialize(
        AnimalSpeciesData speciesData)
    {
        species = speciesData;
    }

    public Collider FindNearestVegetation()
    {
        Collider[] results =
            Physics.OverlapSphere(
                transform.position,
                species.foodDetectionRadius,
                vegetationMask
            );

        Collider nearest = null;

        float distance =
            Mathf.Infinity;

        foreach (
            Collider result in results)
        {
            float newDistance =
                Vector3.SqrMagnitude(
                    result.transform.position -
                    transform.position
                );

            if (newDistance <
                distance)
            {
                distance =
                    newDistance;

                nearest =
                    result;
            }
        }

        return nearest;
    }

    public AnimalController FindNearestAnimal()
    {
        Collider[] results =
            Physics.OverlapSphere(
                transform.position,
                species.foodDetectionRadius,
                animalMask
            );

        AnimalController nearest = null;

        float distance =
            Mathf.Infinity;

        foreach (
            Collider result in results)
        {
            AnimalController animal =
                result.GetComponentInParent<
                    AnimalController
                >();

            if (animal == null)
                continue;

            if (animal ==
                GetComponent<AnimalController>())
            {
                continue;
            }

            float newDistance =
                Vector3.SqrMagnitude(
                    animal.transform.position -
                    transform.position
                );

            if (newDistance <
                distance)
            {
                distance =
                    newDistance;

                nearest =
                    animal;
            }
        }

        return nearest;
    }
}