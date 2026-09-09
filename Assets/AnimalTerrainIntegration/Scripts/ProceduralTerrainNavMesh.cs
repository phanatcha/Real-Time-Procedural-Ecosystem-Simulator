using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-700)]
[DisallowMultipleComponent]
public sealed class ProceduralTerrainNavMesh : MonoBehaviour
{
    public AnimalTerrainWorld terrainWorld;
    public Transform focus;

    [Header("Local Navigation Region")]
    [Min(50f)] public float buildRadius = 600f;
    [Min(2f)] public float sampleSpacing = 8f;
    [Min(10f)] public float rebuildDistance = 140f;
    [Min(10f)] public float verticalPadding = 100f;
    public int agentTypeId;
    public bool buildOnStart = true;

    NavMeshData navMeshData;
    NavMeshDataInstance navMeshInstance;
    Mesh sourceMesh;
    Vector2 lastBuildCenter;
    float nextBuildAttemptTime;

    public bool IsReady { get; private set; }
    public Vector2 LastBuildCenter => lastBuildCenter;
    public event Action NavigationReady;

    void Start()
    {
        ResolveReferences();
        if (buildOnStart) Rebuild();
    }

    void Update()
    {
        if (Time.unscaledTime < nextBuildAttemptTime) return;
        if (!IsReady)
        {
            if (buildOnStart) Rebuild();
            return;
        }
        if (focus == null) return;

        Vector2 position = new Vector2(focus.position.x, focus.position.z);
        if ((position - lastBuildCenter).sqrMagnitude >= rebuildDistance * rebuildDistance)
        {
            Rebuild();
        }
    }

    [ContextMenu("Rebuild Procedural NavMesh")]
    public bool Rebuild()
    {
        ResolveReferences();
        if (terrainWorld == null || !terrainWorld.IsConfigured)
        {
            ScheduleRetry();
            return false;
        }

        NavMeshBuildSettings buildSettings = ResolveBuildSettings();
        if (buildSettings.agentTypeID < 0)
        {
            Debug.LogError("No NavMesh agent type is available for procedural animal navigation.", this);
            ScheduleRetry();
            return false;
        }

        Vector2 center = GetSnappedFocusPosition();
        Mesh generatedSource = BuildWalkableMesh(center);
        if (generatedSource == null || generatedSource.triangles.Length == 0)
        {
            if (generatedSource != null) Destroy(generatedSource);
            Debug.LogWarning("No walkable land was found inside the procedural NavMesh region.", this);
            ScheduleRetry();
            return false;
        }

        var source = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Mesh,
            sourceObject = generatedSource,
            transform = Matrix4x4.identity,
            area = 0
        };
        var sources = new List<NavMeshBuildSource> { source };

        HeightMapSettings heightSettings = terrainWorld.terrainGenerator.heightMapSettings;
        float minimumHeight = heightSettings.minHeight - verticalPadding;
        float maximumHeight = heightSettings.maxHeight + verticalPadding;
        Bounds bounds = new Bounds(
            new Vector3(center.x, (minimumHeight + maximumHeight) * 0.5f, center.y),
            new Vector3(buildRadius * 2f, maximumHeight - minimumHeight, buildRadius * 2f));

        NavMeshData generatedData = NavMeshBuilder.BuildNavMeshData(
            buildSettings,
            sources,
            bounds,
            Vector3.zero,
            Quaternion.identity);
        if (generatedData == null)
        {
            Destroy(generatedSource);
            Debug.LogError("Unity could not build the procedural animal NavMesh.", this);
            ScheduleRetry();
            return false;
        }

        NavMeshDataInstance generatedInstance = NavMesh.AddNavMeshData(generatedData);
        if (!generatedInstance.valid)
        {
            Destroy(generatedSource);
            Destroy(generatedData);
            Debug.LogError("Unity built the procedural NavMesh but could not activate it.", this);
            ScheduleRetry();
            return false;
        }

        RemoveCurrentData();
        navMeshData = generatedData;
        navMeshInstance = generatedInstance;
        sourceMesh = generatedSource;
        lastBuildCenter = center;
        nextBuildAttemptTime = 0f;
        IsReady = true;
        PlaceExistingAgentsOnSurface();
        NavigationReady?.Invoke();
        return true;
    }

    public bool TryProjectToNavigation(Vector3 candidate, float maximumDistance,
        out Vector3 navigationPosition)
    {
        navigationPosition = default;
        if (!IsReady || terrainWorld == null ||
            !terrainWorld.TryFindWalkableGround(candidate, maximumDistance, out Vector3 ground))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(ground, out NavMeshHit hit,
            Mathf.Max(0.1f, maximumDistance), NavMesh.AllAreas))
        {
            return false;
        }

        if (!terrainWorld.TryGetWalkableSample(hit.position, out _)) return false;
        navigationPosition = hit.position;
        return true;
    }

    Mesh BuildWalkableMesh(Vector2 center)
    {
        float spacing = Mathf.Max(2f, sampleSpacing);
        int pointsPerAxis = Mathf.Max(3, Mathf.CeilToInt(buildRadius * 2f / spacing) + 1);
        if (pointsPerAxis % 2 == 0) pointsPerAxis++;
        spacing = buildRadius * 2f / (pointsPerAxis - 1);
        int vertexCount = pointsPerAxis * pointsPerAxis;
        Vector3[] vertices = new Vector3[vertexCount];
        bool[] walkable = new bool[vertexCount];
        float minimumX = center.x - buildRadius;
        float minimumZ = center.y - buildRadius;

        for (int z = 0; z < pointsPerAxis; z++)
        {
            for (int x = 0; x < pointsPerAxis; x++)
            {
                int index = z * pointsPerAxis + x;
                Vector2 worldPosition = new Vector2(minimumX + x * spacing, minimumZ + z * spacing);
                if (terrainWorld.TryGetWalkableSample(worldPosition, out EnvironmentSample sample))
                {
                    vertices[index] = sample.position;
                    walkable[index] = true;
                }
                else
                {
                    vertices[index] = new Vector3(worldPosition.x, 0f, worldPosition.y);
                }
            }
        }

        var triangles = new List<int>((pointsPerAxis - 1) * (pointsPerAxis - 1) * 6);
        for (int z = 0; z < pointsPerAxis - 1; z++)
        {
            for (int x = 0; x < pointsPerAxis - 1; x++)
            {
                int a = z * pointsPerAxis + x;
                int b = a + 1;
                int c = a + pointsPerAxis;
                int d = c + 1;
                Vector2 quadCenter = new Vector2(minimumX + (x + 0.5f) * spacing,
                    minimumZ + (z + 0.5f) * spacing);
                bool centerWalkable = terrainWorld.TryGetWalkableSample(quadCenter, out _);
                if (!centerWalkable) continue;

                AddTriangleIfWalkable(a, c, d, vertices, walkable, triangles);
                AddTriangleIfWalkable(a, d, b, vertices, walkable, triangles);
            }
        }

        Mesh mesh = new Mesh { name = "Procedural Animal Navigation Source" };
        if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void AddTriangleIfWalkable(int a, int b, int c, Vector3[] vertices,
        bool[] walkable, List<int> triangles)
    {
        if (!walkable[a] || !walkable[b] || !walkable[c]) return;

        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
        float slope = Vector3.Angle(normal, Vector3.up);
        if (slope > terrainWorld.maximumWalkableSlopeDegrees) return;

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    NavMeshBuildSettings ResolveBuildSettings()
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeId);
        if (settings.agentTypeID < 0 && NavMesh.GetSettingsCount() > 0)
        {
            settings = NavMesh.GetSettingsByIndex(0);
            agentTypeId = settings.agentTypeID;
        }
        return settings;
    }

    Vector2 GetSnappedFocusPosition()
    {
        Vector3 position = focus == null ? Vector3.zero : focus.position;
        float spacing = Mathf.Max(2f, sampleSpacing);
        return new Vector2(
            Mathf.Round(position.x / spacing) * spacing,
            Mathf.Round(position.z / spacing) * spacing);
    }

    void PlaceExistingAgentsOnSurface()
    {
        NavMeshAgent[] agents = FindObjectsByType<NavMeshAgent>();
        for (int index = 0; index < agents.Length; index++)
        {
            NavMeshAgent agent = agents[index];
            if (agent == null || !agent.isActiveAndEnabled) continue;
            if (!TryProjectToNavigation(agent.transform.position, sampleSpacing * 3f, out Vector3 position)) continue;

            if (agent.isOnNavMesh)
            {
                agent.Warp(position);
            }
            else
            {
                agent.enabled = false;
                agent.transform.position = position;
                agent.enabled = true;
            }
        }
    }

    void ResolveReferences()
    {
        if (terrainWorld == null) terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld == null) terrainWorld = FindAnyObjectByType<AnimalTerrainWorld>();
        if (focus == null && terrainWorld != null && terrainWorld.terrainGenerator != null)
        {
            focus = terrainWorld.terrainGenerator.viewer;
        }
    }

    void ScheduleRetry()
    {
        IsReady = false;
        nextBuildAttemptTime = Time.unscaledTime + 2f;
    }

    void RemoveCurrentData()
    {
        if (navMeshInstance.valid) navMeshInstance.Remove();
        if (navMeshData != null) Destroy(navMeshData);
        if (sourceMesh != null) Destroy(sourceMesh);
        navMeshData = null;
        sourceMesh = null;
        navMeshInstance = default;
    }

    void OnDisable()
    {
        RemoveCurrentData();
        IsReady = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        buildRadius = Mathf.Max(50f, buildRadius);
        sampleSpacing = Mathf.Max(2f, sampleSpacing);
        rebuildDistance = Mathf.Max(10f, rebuildDistance);
        verticalPadding = Mathf.Max(10f, verticalPadding);
    }
#endif
}
