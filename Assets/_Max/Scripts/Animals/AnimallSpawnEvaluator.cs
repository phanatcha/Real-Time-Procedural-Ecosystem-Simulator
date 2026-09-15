using UnityEngine;

public class AnimalSpawnEvaluator : MonoBehaviour
{
    public float Evaluate(
        RaycastHit hit,
        AnimalSpeciesData species)
    {
        if (species == null)
        {
            return 0f;
        }



        int layer = hit.collider.gameObject.layer;

        bool allowedLayer =  (species.allowedSpawnLayers.value & (1 << layer)) != 0;

        if (!allowedLayer)
        {
            return 0f;
        }

      

        float slope =
            Vector3.Angle(
                hit.normal,
                Vector3.up
            );

        if (slope >  species.maxSpawnSlope)
        {
            return 0f;
        }

       

        float height =hit.point.y;

        return EvaluateHeight(
            height,
            species
        );
    }

    private float EvaluateHeight(float height,AnimalSpeciesData species)
    {
        if (height < species.minimumSpawnHeight)
        {
            return 0f;
        }

        if (height >species.maximumSpawnHeight)
        {
            return 0f;
        }

        // Ideal range
        if (height >=  species.idealMinSpawnHeight && height <=species.idealMaxSpawnHeight)
        {
            return 1f;
        }

        // Lower transition
        if (height < species.idealMinSpawnHeight)
        {
            return Mathf.InverseLerp(
                species.minimumSpawnHeight,
                species.idealMinSpawnHeight,
                height
            );
        }

        // Upper transition
        return 1f -  Mathf.InverseLerp( species.idealMaxSpawnHeight,species.maximumSpawnHeight, height);
    }
}