using UnityEngine;

public class AnimalMovement : MonoBehaviour
{
    private AnimalSpeciesData species;

    [Header("Terrain")]
    public LayerMask terrainMask;
    public LayerMask obstacleMask;

    public float groundCheckHeight = 4f;
    public float groundCheckDistance = 10f;

    public float forwardCheckDistance = 2f;

    private Vector3 destination;

    private bool hasDestination;

    public bool HasDestination =>
        hasDestination;

    public void Initialize(
        AnimalSpeciesData speciesData)
    {
        species = speciesData;
    }

    public void SetDestination(
        Vector3 target)
    {
        destination = target;

        hasDestination = true;
    }

    public void Stop()
    {
        hasDestination = false;
    }

    public void Walk()
    {
        Move(
            species.walkSpeed
        );
    }

    public void Run()
    {
        Move(
            species.runSpeed
        );
    }

    void Move(float speed)
    {
        if (!hasDestination)
            return;

        Vector3 direction =
            destination -
            transform.position;

        direction.y = 0;

        if (direction.magnitude < 1f)
        {
            hasDestination = false;

            return;
        }

        direction.Normalize();

        Vector3 safeDirection =
            FindSafeDirection(
                direction
            );

        if (safeDirection ==
            Vector3.zero)
        {
            return;
        }

        Quaternion targetRotation =
            Quaternion.LookRotation(
                safeDirection
            );

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                species.rotationSpeed *
                Time.deltaTime
            );

        transform.position +=
            transform.forward *
            speed *
            Time.deltaTime;

        SnapToGround();
    }

    Vector3 FindSafeDirection(
        Vector3 desired)
    {
        float[] angles =
        {
            0,
            -30,
            30,
            -60,
            60,
            -90,
            90,
            180
        };

        foreach (
            float angle in angles)
        {
            Vector3 direction =
                Quaternion.Euler(
                    0,
                    angle,
                    0
                ) *
                desired;

            if (IsDirectionSafe(
                direction))
            {
                return direction;
            }
        }

        return Vector3.zero;
    }

    bool IsDirectionSafe(
        Vector3 direction)
    {
        Vector3 position =
            transform.position +
            direction *
            forwardCheckDistance;

        Vector3 origin =
            position +
            Vector3.up *
            groundCheckHeight;

        if (!Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            groundCheckDistance,
            terrainMask))
        {
            return false;
        }

        float slope =
            Vector3.Angle(
                hit.normal,
                Vector3.up
            );

        if (slope >
            species.maxSlopeAngle)
        {
            return false;
        }

        return true;
    }

    void SnapToGround()
    {
        Vector3 origin =
            transform.position +
            Vector3.up *
            groundCheckHeight;

        if (Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            groundCheckDistance,
            terrainMask))
        {
            Vector3 position =
                transform.position;

            position.y =
                hit.point.y;

            transform.position =
                position;
        }
    }
}