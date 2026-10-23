using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Grass and Norway's summer boreal flowers around the camera, drawn on the GPU. A map of the ground around the camera
// (height, how much grass the spot carries, tree cover, moisture) is built from the loaded terrain on a background
// thread whenever the camera has moved on. Grass thins towards the shore, under trees and around berry bushes. Every
// frame GrassField.compute places plants on a world-fixed grid within the radius and in view, in irregular clusters
// with bare or mossy ground between them, and instanced draws (grass, then one per flower kind) show them with
// Custom/Grass Field.
// It adds itself to the terrain generator when the game starts; Tools > Boreal Ecosystem > Apply Dense Grass puts it
// in the scene with a material asset for tuning.
public class GrassField : MonoBehaviour
{
    const int FieldResolution = 128;
    const int InstanceStride = 32;
    const string ShaderName = "Custom/Grass Field";
    static readonly int FlowerKinds = System.Enum.GetValues(typeof(BorealFlower)).Length;

    [Tooltip("Found automatically when empty.")]
    public TerrainGenerator terrain;
    [Tooltip("Loaded from Resources/GrassField when empty.")]
    public ComputeShader placement;
    [Tooltip("A material using Custom/Grass Field. A plain one is made when empty.")]
    public Material material;

    [Header("Grass")]
    [Tooltip("Plants are drawn to this distance from the camera, in metres.")]
    [Range(10f, 120f)] public float radius = 120f;
    [Tooltip("Metres between plants; smaller is denser and slower.")]
    [Range(0.3f, 2f)] public float spacing = 0.4f;
    [Range(0f, 1f)] public float grassDensity = 0.28f;
    [Tooltip("How tall the grass grows: 1 gives tufts of about 0.15-0.65 m, shorter at cluster edges, near the shore, " +
             "under trees and around bushes.")]
    [Range(0.3f, 10f)] public float bladeHeight = 4.76f;
    [Tooltip("Pale sunlit greens: the colour where blades meet the ground and at their tips.")]
    public Color rootColour = new Color(0.753f, 0.792f, 0.565f);
    public Color tipColour = new Color(0.992f, 1f, 0.722f);

    [Header("Flowers")]
    [Tooltip("How often a spot inside a flower patch holds a flower instead of grass.")]
    [Range(0f, 1f)] public float flowerChance = 0.04f;
    [Tooltip("How much larger than their meshes flowers are drawn, so they stand above the grass.")]
    [Range(1f, 4f)] public float flowerScale = 3f;
    [Tooltip("How much flower petals glow whatever the light, so they catch the eye even in shade.")]
    [Range(0f, 1f)] public float flowerGlow = 1f;

    static readonly int GrassId = Shader.PropertyToID("_Grass");
    static readonly int FieldId = Shader.PropertyToID("_Field");
    static readonly int FieldRectId = Shader.PropertyToID("_FieldRect");
    static readonly int CameraId = Shader.PropertyToID("_Camera");
    static readonly int GridId = Shader.PropertyToID("_Grid");
    static readonly int PlanesId = Shader.PropertyToID("_FrustumPlanes");
    static readonly int InstancesId = Shader.PropertyToID("_Instances");
    static readonly int IsFlowerId = Shader.PropertyToID("_IsFlower");
    static readonly int GrassHeightId = Shader.PropertyToID("_GrassHeight");
    static readonly int FlowerScaleId = Shader.PropertyToID("_FlowerScale");
    static readonly int FlowerGlowId = Shader.PropertyToID("_FlowerGlow");
    static readonly int RootColourId = Shader.PropertyToID("_RootColour");
    static readonly int TipColourId = Shader.PropertyToID("_TipColour");

    Texture2D field;
    Rect fieldRect;
    bool fieldRequested;
    float lastFieldTime = -100f;
    Vector2 fieldCentre = new Vector2(float.MaxValue, float.MaxValue);
    TerrainEnvironmentSampler fieldSampler;
    bool ownsMaterial;

    // Index 0 is grass; 1 onwards are the flower kinds in BorealFlower's order.
    GraphicsBuffer[] plants;
    GraphicsBuffer[] arguments;
    Mesh[] meshes;
    MaterialPropertyBlock[] properties;
    int[] bufferIds;
    int capacity;
    readonly Plane[] planes = new Plane[6];
    readonly Vector4[] planeVectors = new Vector4[6];

    sealed class FieldData
    {
        public float[] values;
        public Rect rect;
        public TerrainEnvironmentSampler sampler;
        public int missing;
    }

    readonly List<Vector2> bushPositions = new List<Vector2>();

    // Grass grows in every world without setting anything up; a GrassField already in the scene, even turned off,
    // is left as it is.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToTerrain()
    {
        TerrainGenerator generator = FindAnyObjectByType<TerrainGenerator>();
        if (generator != null && generator.GetComponent<GrassField>() == null)
        {
            generator.gameObject.AddComponent<GrassField>();
        }
    }

    void OnEnable()
    {
        if (terrain == null) terrain = FindAnyObjectByType<TerrainGenerator>();
        if (placement == null) placement = Resources.Load<ComputeShader>("GrassField");
        if (material == null)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                material = new Material(shader) { name = "Grass Field (runtime)" };
                ownsMaterial = true;
            }
        }

        int count = FlowerKinds + 1;
        meshes = new Mesh[count];
        properties = new MaterialPropertyBlock[count];
        bufferIds = new int[count];
        System.Random random = new System.Random(1729);
        meshes[0] = VegetationMeshBuilder.BuildGrassTuft(random);
        bufferIds[0] = GrassId;
        for (int kind = 0; kind < FlowerKinds; kind++)
        {
            meshes[kind + 1] = VegetationMeshBuilder.BuildFlower((BorealFlower)kind, random);
            bufferIds[kind + 1] = Shader.PropertyToID("_" + (BorealFlower)kind);
        }

        for (int i = 0; i < count; i++) properties[i] = new MaterialPropertyBlock();
    }

    void OnDisable()
    {
        ReleaseBuffers();
        if (field != null) Destroy(field);
        if (meshes != null)
        {
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null) Destroy(mesh);
            }
        }

        if (ownsMaterial && material != null) Destroy(material);
        if (ownsMaterial) material = null;
        ownsMaterial = false;
        field = null;
        fieldRequested = false;
        fieldCentre = new Vector2(float.MaxValue, float.MaxValue);
    }

    void LateUpdate()
    {
        Camera view = Camera.main;
        TerrainEnvironmentSampler sampler = terrain != null ? terrain.WorldEnvironmentSampler : null;
        if (view == null || sampler == null || placement == null || material == null || !SystemInfo.supportsComputeShaders)
        {
            return;
        }

        Vector3 cameraPosition = view.transform.position;
        RequestFieldIfNeeded(sampler, new Vector2(cameraPosition.x, cameraPosition.z));
        if (field == null) return;

        int cellsPerSide = Mathf.CeilToInt(radius * 2f / spacing) + 2;
        EnsureBuffers(cellsPerSide * cellsPerSide);
        int kernel = placement.FindKernel("Place");
        for (int i = 0; i < plants.Length; i++)
        {
            plants[i].SetCounterValue(0);
            placement.SetBuffer(kernel, bufferIds[i], plants[i]);
        }

        placement.SetTexture(kernel, FieldId, field);
        placement.SetVector(FieldRectId, new Vector4(fieldRect.xMin, fieldRect.yMin, fieldRect.width, fieldRect.height));
        placement.SetVector(CameraId, new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, radius));
        placement.SetVector(GridId, new Vector4(spacing, cellsPerSide, grassDensity, flowerChance));
        GeometryUtility.CalculateFrustumPlanes(view, planes);
        for (int i = 0; i < 6; i++) planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
        placement.SetVectorArray(PlanesId, planeVectors);
        int groups = Mathf.CeilToInt(cellsPerSide / 8f);
        placement.Dispatch(kernel, groups, groups, 1);

        Bounds bounds = new Bounds(cameraPosition, new Vector3(radius * 2f + 10f, 400f, radius * 2f + 10f));
        for (int i = 0; i < plants.Length; i++)
        {
            GraphicsBuffer.CopyCount(plants[i], arguments[i], sizeof(uint));
            MaterialPropertyBlock block = properties[i];
            block.SetBuffer(InstancesId, plants[i]);
            block.SetFloat(IsFlowerId, i == 0 ? 0f : 1f);
            block.SetFloat(GrassHeightId, bladeHeight);
            block.SetFloat(FlowerScaleId, flowerScale);
            block.SetFloat(FlowerGlowId, flowerGlow);
            block.SetColor(RootColourId, rootColour);
            block.SetColor(TipColourId, tipColour);

            Draw(meshes[i], arguments[i], block, bounds);
        }
    }

    void Draw(Mesh mesh, GraphicsBuffer drawArguments, MaterialPropertyBlock block, Bounds bounds)
    {
        RenderParams parameters = new RenderParams(material)
        {
            worldBounds = bounds,
            matProps = block,
            // Thousands of small plants: they take shadows from trees and hills but cast none of their own.
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            layer = gameObject.layer
        };
        Graphics.RenderMeshIndirect(parameters, mesh, drawArguments);
    }

    void EnsureBuffers(int count)
    {
        if (plants != null && capacity >= count) return;

        ReleaseBuffers();
        capacity = count;
        plants = new GraphicsBuffer[meshes.Length];
        arguments = new GraphicsBuffer[meshes.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            plants[i] = new GraphicsBuffer(GraphicsBuffer.Target.Append, count, InstanceStride);
            arguments[i] = Arguments(meshes[i]);
        }
    }

    static GraphicsBuffer Arguments(Mesh mesh)
    {
        GraphicsBuffer drawArguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                                                          GraphicsBuffer.IndirectDrawIndexedArgs.size);
        drawArguments.SetData(new[]
        {
            new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = mesh.GetIndexCount(0),
                startIndex = mesh.GetIndexStart(0),
                baseVertexIndex = mesh.GetBaseVertex(0),
                instanceCount = 0,
                startInstance = 0
            }
        });
        return drawArguments;
    }

    void ReleaseBuffers()
    {
        if (plants != null)
        {
            foreach (GraphicsBuffer buffer in plants) buffer?.Release();
        }

        if (arguments != null)
        {
            foreach (GraphicsBuffer buffer in arguments) buffer?.Release();
        }

        plants = null;
        arguments = null;
        capacity = 0;
    }

    // The map is rebuilt when the camera has moved a fifth of the way to its edge, when a new world is generated,
    // and every few seconds while some of it fell on terrain that had not loaded yet.
    void RequestFieldIfNeeded(TerrainEnvironmentSampler sampler, Vector2 cameraPosition)
    {
        if (fieldRequested) return;

        float moved = Vector2.Distance(cameraPosition, fieldCentre);
        bool stale = sampler != fieldSampler || moved > radius * 0.2f || Time.unscaledTime - lastFieldTime > 4f;
        if (!stale) return;

        fieldRequested = true;
        float size = (radius + 8f) * 2f;
        Rect rect = new Rect(cameraPosition.x - size * 0.5f, cameraPosition.y - size * 0.5f, size, size);

        // The berry bushes on the terrain under the map, copied here because chunks change on the main thread.
        bushPositions.Clear();
        foreach (TerrainChunk chunk in terrain.GetVisibleTerrainChunks())
        {
            chunk.CollectBushPositions(bushPositions);
        }

        Vector2[] bushes = bushPositions.FindAll(bush => rect.Contains(bush)).ToArray();
        // Grass stops where the terrain starts painting rock, read here because TextureData isn't safe off the main thread.
        TextureData texture = terrain.textureSettings;
        Vector2 rockSlope = texture != null ? new Vector2(texture.rockSlopeStart, texture.rockSlopeEnd) : new Vector2(0.15f, 0.3f);
        ThreadedDataRequester.RequestData(() => BuildField(sampler, rect, bushes, rockSlope), OnFieldBuilt);
        fieldCentre = cameraPosition;
    }

    static FieldData BuildField(TerrainEnvironmentSampler sampler, Rect rect, Vector2[] bushes, Vector2 rockSlope)
    {
        EnvironmentDefinitions definitions = sampler.EnvironmentDefinitions;
        float land = definitions != null ? definitions.LandThreshold : 0f;
        FieldData data = new FieldData
        {
            values = new float[FieldResolution * FieldResolution * 4],
            rect = rect,
            sampler = sampler
        };

        float texel = rect.width / FieldResolution;
        for (int y = 0; y < FieldResolution; y++)
        {
            for (int x = 0; x < FieldResolution; x++)
            {
                int index = (y * FieldResolution + x) * 4;
                float worldX = rect.xMin + (x + 0.5f) * texel;
                float worldZ = rect.yMin + (y + 0.5f) * texel;
                if (!sampler.TrySampleCached(worldX, worldZ, out EnvironmentSample ground))
                {
                    data.missing++;
                    continue;
                }

                data.values[index] = ground.height;
                if (!ground.isLand || ground.isShore || ground.isWater) continue;

                // How much grass the spot carries: none on steep ground, gone before the rock is half blended in, fading out over the last strip of land
                // before the shore, thinner on the mossy floor under trees, and cleared to a low ring around each
                // berry bush so the bushes stand out. Forest-floor flowers follow the tree cover instead.
                float flat = 1f - Mathf.InverseLerp(rockSlope.x, (rockSlope.x + rockSlope.y) * 0.5f, ground.slope);
                float inland = Mathf.InverseLerp(land, land + 0.05f, ground.normalizedHeight);
                float underTrees = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(ground.treeCover));
                float aroundBush = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 2.2f, NearestBush(bushes, worldX, worldZ)));
                float grassy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.4f, ground.grassBiomass));
                data.values[index + 1] = grassy * flat * inland * underTrees * aroundBush;
                data.values[index + 2] = Mathf.Clamp01(ground.treeCover) * flat;
                data.values[index + 3] = Mathf.Clamp01(ground.moisture);
            }
        }

        return data;
    }

    static float NearestBush(Vector2[] bushes, float x, float z)
    {
        float nearest = float.MaxValue;
        foreach (Vector2 bush in bushes)
        {
            float dx = bush.x - x;
            float dz = bush.y - z;
            nearest = Mathf.Min(nearest, dx * dx + dz * dz);
        }

        return Mathf.Sqrt(nearest);
    }

    void OnFieldBuilt(object result)
    {
        fieldRequested = false;
        lastFieldTime = Time.unscaledTime;
        if (!isActiveAndEnabled) return;

        FieldData data = (FieldData)result;
        if (field == null)
        {
            field = new Texture2D(FieldResolution, FieldResolution, TextureFormat.RGBAFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Grass Field Ground"
            };
        }

        field.SetPixelData(data.values, 0);
        field.Apply(false);
        fieldRect = data.rect;
        fieldSampler = data.sampler;
        // Retry soon while terrain under the map is still loading; otherwise only when the camera moves on.
        if (data.missing == 0) lastFieldTime = float.MaxValue;
    }
}
