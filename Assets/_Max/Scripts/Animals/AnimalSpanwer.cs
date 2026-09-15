using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimalSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainGenerator terrainGenerator;

    [Header("Animal")]
    public GameObject animalPrefab;
    public int spawnCount = 10;

    [Header("Height Rules")]
    public float minimumSpawnHeight = 5f;
    public float idealMinimumHeight = 15f;
    public float idealMaximumHeight = 60f;
    public float maximumSpawnHeight = 100f;

    [Header("Slope Rules")]
    public float maxSlopeAngle = 35f;

    [Header("Spawn Settings")]
    public float spawnHeightOffset = 0.1f;

    [Range(0f, 1f)]
    public float minimumSuitability = 0.1f;

    [Header("Generation")]
    public int attemptsPerFrame = 20;
    public int maxFramesToWait = 600;
    public int maxAttemptsPerAnimal = 100;

    private readonly List<GameObject> spawnedAnimals = new List<GameObject>();

    private Coroutine spawnCoroutine;

    public void SpawnAnimals(int seed)
    {
        if (animalPrefab == null)
        {
            Debug.LogError("AnimalSpawner: Animal prefab is not assigned!");
            return;
        }

        if (terrainGenerator == null)
        {
            Debug.LogError("AnimalSpawner: TerrainGenerator is not assigned!");
            return;
        }

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
        }

        spawnCoroutine = StartCoroutine(SpawnAnimalsRoutine(seed));
    }

    private IEnumerator SpawnAnimalsRoutine(int seed)
    {
        Debug.Log($"AnimalSpawner: Waiting for AnimalSpawn terrain. Seed: {seed}");

        // Give the old terrain one frame to be destroyed.
        yield return null;

        int framesWaited = 0;

        while (!terrainGenerator.HasSpawnableTerrain())
        {
            framesWaited++;

            if (framesWaited >= maxFramesToWait)
            {
                Debug.LogError("AnimalSpawner: Timed out waiting for AnimalSpawn terrain.");
                spawnCoroutine = null;
                yield break;
            }

            yield return null;
        }

        // Allow Unity Physics to register the terrain colliders.
        yield return new WaitForFixedUpdate();

        Physics.SyncTransforms();

        List<MeshCollider> terrainColliders = terrainGenerator.GetSpawnableTerrainColliders();

        if (terrainColliders == null || terrainColliders.Count == 0)
        {
            Debug.LogError("AnimalSpawner: No AnimalSpawn terrain colliders were found.");
            spawnCoroutine = null;
            yield break;
        }

        Debug.Log($"AnimalSpawner: Found {terrainColliders.Count} AnimalSpawn chunks.");

        System.Random random = new System.Random(seed);

        int spawned = 0;
        int totalAttempts = 0;
        int maxAttempts = Mathf.Max(spawnCount * maxAttemptsPerAnimal, maxAttemptsPerAnimal);

        while (spawned < spawnCount && totalAttempts < maxAttempts)
        {
            int attemptsThisFrame = 0;

            while (attemptsThisFrame < attemptsPerFrame &&
                   spawned < spawnCount &&
                   totalAttempts < maxAttempts)
            {
                attemptsThisFrame++;
                totalAttempts++;

                if (TrySpawnAnimal(random, terrainColliders))
                {
                    spawned++;
                }
            }

            if (spawned < spawnCount)
            {
                yield return null;
            }
        }

        if (spawned == spawnCount)
        {
            Debug.Log(
                $"AnimalSpawner: Successfully spawned {spawned}/{spawnCount} animals " +
                $"after {totalAttempts} attempts."
            );
        }
        else
        {
            Debug.LogWarning(
                $"AnimalSpawner: Spawned only {spawned}/{spawnCount} animals " +
                $"after {totalAttempts} attempts."
            );
        }

        spawnCoroutine = null;
    }

    private bool TrySpawnAnimal(System.Random random, List<MeshCollider> terrainColliders)
    {
        if (terrainColliders == null || terrainColliders.Count == 0)
        {
            return false;
        }

        int colliderIndex = random.Next(terrainColliders.Count);
        MeshCollider terrainCollider = terrainColliders[colliderIndex];

        if (terrainCollider == null)
        {
            return false;
        }

        if (!terrainCollider.enabled)
        {
            return false;
        }

        if (!terrainCollider.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (terrainCollider.sharedMesh == null)
        {
            return false;
        }

        Bounds bounds = terrainCollider.bounds;

        // Keep spawn attempts slightly away from chunk borders.
        float normalizedX = Mathf.Lerp(0.05f, 0.95f, (float)random.NextDouble());
        float normalizedZ = Mathf.Lerp(0.05f, 0.95f, (float)random.NextDouble());

        float x = Mathf.Lerp(bounds.min.x, bounds.max.x, normalizedX);
        float z = Mathf.Lerp(bounds.min.z, bounds.max.z, normalizedZ);

        Vector3 rayOrigin = new Vector3(x, bounds.max.y + 100f, z);
        Ray ray = new Ray(rayOrigin, Vector3.down);

        float rayDistance = bounds.size.y + 200f;

        bool foundGround = terrainCollider.Raycast(
            ray,
            out RaycastHit hit,
            rayDistance
        );

        if (!foundGround)
        {
            return false;
        }

        float suitability = GetSpawnSuitability(hit);

        if (suitability < minimumSuitability)
        {
            return false;
        }

        float randomRoll = (float)random.NextDouble();

        if (randomRoll > suitability)
        {
            return false;
        }

        Vector3 spawnPosition = hit.point + Vector3.up * spawnHeightOffset;
        float rotationY = (float)random.NextDouble() * 360f;
        Quaternion spawnRotation = Quaternion.Euler(0f, rotationY, 0f);

        GameObject animal = Instantiate(animalPrefab, spawnPosition, spawnRotation);

        spawnedAnimals.Add(animal);

        AnimalController controller = animal.GetComponent<AnimalController>();

        if (controller != null)
        {
            int individualSeed = random.Next();
            controller.Initialize(controller.species, individualSeed);
        }

        return true;
    }

    // Used by the normal animal spawning system.
    public float GetSpawnSuitability(RaycastHit hit)
    {
        return GetSpawnSuitability(hit.point.y, hit.normal);
    }

    // Used by both the spawner and the mesh-based heatmap.
    public float GetSpawnSuitability(float height, Vector3 normal)
    {
        if (height < minimumSpawnHeight || height > maximumSpawnHeight)
        {
            return 0f;
        }

        float slopeAngle = Vector3.Angle(normal, Vector3.up);

        if (slopeAngle > maxSlopeAngle)
        {
            return 0f;
        }

        float heightScore = GetHeightScore(height);
        float slopeScore = 1f - Mathf.InverseLerp(0f, maxSlopeAngle, slopeAngle);

        float suitability = heightScore * slopeScore;

        return Mathf.Clamp01(suitability);
    }

    private float GetHeightScore(float height)
    {
        if (height >= idealMinimumHeight && height <= idealMaximumHeight)
        {
            return 1f;
        }

        if (height < idealMinimumHeight)
        {
            return Mathf.InverseLerp(
                minimumSpawnHeight,
                idealMinimumHeight,
                height
            );
        }

        return 1f - Mathf.InverseLerp(
            idealMaximumHeight,
            maximumSpawnHeight,
            height
        );
    }

    public void ClearAnimals()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        foreach (GameObject animal in spawnedAnimals)
        {
            if (animal != null)
            {
                Destroy(animal);
            }
        }

        spawnedAnimals.Clear();
    }
}