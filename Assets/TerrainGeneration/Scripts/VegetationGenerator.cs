using System.Collections.Generic;
using UnityEngine;

public struct VegetationInstance
{
    public int variantIndex;
    public Vector3 position;
    public Quaternion rotation;
    public float yawDegrees;
    public float scale;
}

public class VegetationPlacementData
{
    public List<VegetationInstance> trees = new List<VegetationInstance>();
    public List<VegetationInstance> grass = new List<VegetationInstance>();
    public List<VegetationInstance> rocks = new List<VegetationInstance>();
}

public static class VegetationGenerator
{
    const int TreeVariants = 3;
    const int GrassVariants = 3;
    const int RockVariants = 3;

    public static VegetationPlacementData GeneratePlacements(
        Vector2 chunkWorldMin, Vector2 chunkWorldMax,
        HeightMap heightMap, float terrainMinHeight, float terrainMaxHeight,
        VegetationSettings vegSettings, EnvironmentDefinitions environmentDefinitions,
        int numVertsPerLine, float meshScale)
    {
        VegetationPlacementData data = new VegetationPlacementData();
        if (vegSettings == null || environmentDefinitions == null) return data;

        Vector2 chunkWorldCentre = (chunkWorldMin + chunkWorldMax) * 0.5f;

        GenerateType(vegSettings.trees, EnvironmentResourceType.Tree, data.trees, TreeVariants, 1,
            chunkWorldMin, chunkWorldMax, heightMap, terrainMinHeight, terrainMaxHeight,
            environmentDefinitions, vegSettings.seed, chunkWorldCentre, numVertsPerLine, meshScale);
        GenerateType(vegSettings.grass, EnvironmentResourceType.Grass, data.grass, GrassVariants, 2,
            chunkWorldMin, chunkWorldMax, heightMap, terrainMinHeight, terrainMaxHeight,
            environmentDefinitions, vegSettings.seed, chunkWorldCentre, numVertsPerLine, meshScale);
        GenerateType(vegSettings.rocks, EnvironmentResourceType.Rock, data.rocks, RockVariants, 3,
            chunkWorldMin, chunkWorldMax, heightMap, terrainMinHeight, terrainMaxHeight,
            environmentDefinitions, vegSettings.seed, chunkWorldCentre, numVertsPerLine, meshScale);

        return data;
    }

    static void GenerateType(VegetationTypeSettings settings, EnvironmentResourceType resourceType,
        List<VegetationInstance> output, int variantCount, int typeSalt,
        Vector2 chunkWorldMin, Vector2 chunkWorldMax, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight, EnvironmentDefinitions environmentDefinitions,
        int seed, Vector2 chunkWorldCentre, int numVertsPerLine, float meshScale)
    {
        if (settings == null || !settings.enabled) return;

        float cellSize = Mathf.Max(settings.cellSize, 0.5f);
        int gxMin = Mathf.FloorToInt(chunkWorldMin.x / cellSize) - 1;
        int gxMax = Mathf.CeilToInt(chunkWorldMax.x / cellSize) + 1;
        int gzMin = Mathf.FloorToInt(chunkWorldMin.y / cellSize) - 1;
        int gzMax = Mathf.CeilToInt(chunkWorldMax.y / cellSize) + 1;

        float saltBase = seed * 13.7f + typeSalt * 91.3f;

        for (int gx = gxMin; gx <= gxMax; gx++)
        {
            for (int gz = gzMin; gz <= gzMax; gz++)
            {
                Vector2 cellOrigin = new Vector2(gx * cellSize, gz * cellSize);

                float hx = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 7.11f));
                float hz = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 33.9f));
                Vector2 worldPos = cellOrigin + new Vector2(hx, hz) * cellSize;

                if (worldPos.x < chunkWorldMin.x || worldPos.x >= chunkWorldMax.x ||
                    worldPos.y < chunkWorldMin.y || worldPos.y >= chunkWorldMax.y)
                {
                    continue;
                }

                if (!EnvironmentSampler.TrySampleBase(worldPos, heightMap, terrainMinHeight, terrainMaxHeight,
                    environmentDefinitions, chunkWorldCentre, numVertsPerLine, meshScale,
                    out EnvironmentSample environmentSample))
                {
                    continue;
                }

                float localDensity = EnvironmentSampler.GetResourceDensity(
                    environmentSample, environmentDefinitions, settings, resourceType);
                float densityRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 61.7f));
                if (localDensity <= 0f || densityRoll >= localDensity) continue;

                float variantRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 88.3f));
                int variant = Mathf.Clamp(Mathf.FloorToInt(variantRoll * variantCount), 0, variantCount - 1);
                float rotRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 5.5f));
                float scaleRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 44.4f));

                Quaternion spin = Quaternion.Euler(0, rotRoll * 360f, 0);
                Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, environmentSample.surfaceNormal);
                Quaternion surfaceRotation = Quaternion.Slerp(Quaternion.identity, slopeAlign, settings.surfaceAlignment);

                output.Add(new VegetationInstance
                {
                    variantIndex = variant,
                    position = new Vector3(worldPos.x, environmentSample.height - settings.groundSink, worldPos.y),
                    rotation = surfaceRotation * spin,
                    yawDegrees = rotRoll * 360f,
                    scale = Mathf.Lerp(settings.minScale, settings.maxScale, scaleRoll),
                });
            }
        }
    }

    public static VegetationPlacementData ProjectPlacementsToLOD(
        VegetationPlacementData source, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight,
        VegetationSettings vegSettings, EnvironmentDefinitions environmentDefinitions,
        Vector2 chunkWorldCentre,
        int numVertsPerLine, float meshScale, int levelOfDetail)
    {
        VegetationPlacementData projected = new VegetationPlacementData();
        if (vegSettings == null || environmentDefinitions == null) return projected;

        ProjectTypeToLOD(source.trees, projected.trees, vegSettings.trees, heightMap,
            terrainMinHeight, terrainMaxHeight, environmentDefinitions,
            chunkWorldCentre, numVertsPerLine, meshScale, levelOfDetail);
        ProjectTypeToLOD(source.grass, projected.grass, vegSettings.grass, heightMap,
            terrainMinHeight, terrainMaxHeight, environmentDefinitions,
            chunkWorldCentre, numVertsPerLine, meshScale, levelOfDetail);
        ProjectTypeToLOD(source.rocks, projected.rocks, vegSettings.rocks, heightMap,
            terrainMinHeight, terrainMaxHeight, environmentDefinitions,
            chunkWorldCentre, numVertsPerLine, meshScale, levelOfDetail);

        return projected;
    }

    static void ProjectTypeToLOD(
        List<VegetationInstance> source, List<VegetationInstance> output,
        VegetationTypeSettings settings, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight, EnvironmentDefinitions environmentDefinitions,
        Vector2 chunkWorldCentre, int numVertsPerLine, float meshScale, int levelOfDetail)
    {
        for (int i = 0; i < source.Count; i++)
        {
            VegetationInstance instance = source[i];
            Vector2 worldPos = new Vector2(instance.position.x, instance.position.z);
            if (!SampleTerrainLOD(worldPos, heightMap, chunkWorldCentre, numVertsPerLine,
                meshScale, levelOfDetail, out float worldY, out Vector3 normal))
            {
                continue;
            }

            float heightPercent = Mathf.InverseLerp(terrainMinHeight, terrainMaxHeight, worldY);
            if (!environmentDefinitions.SupportsTerrestrialVegetation(heightPercent)) continue;
            if (heightPercent + 0.0001f < settings.minHeightPercent) continue;

            Quaternion spin = Quaternion.Euler(0f, instance.yawDegrees, 0f);
            Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, normal);
            Quaternion surfaceRotation = Quaternion.Slerp(Quaternion.identity, slopeAlign, settings.surfaceAlignment);

            instance.position.y = worldY - settings.groundSink;
            instance.rotation = surfaceRotation * spin;
            output.Add(instance);
        }
    }

    static bool SampleTerrainLOD(Vector2 worldPos, HeightMap heightMap, Vector2 chunkWorldCentre,
        int numVertsPerLine, float meshScale, int levelOfDetail,
        out float worldY, out Vector3 normal)
    {
        float halfWorldSize = (numVertsPerLine - 3) * meshScale * 0.5f;
        float fi = (worldPos.x - chunkWorldCentre.x + halfWorldSize) / meshScale + 1f;
        float fj = (chunkWorldCentre.y + halfWorldSize - worldPos.y) / meshScale + 1f;
        int lastMainIndex = numVertsPerLine - 3;

        if (fi < 1f || fi > numVertsPerLine - 2f || fj < 1f || fj > numVertsPerLine - 2f)
        {
            worldY = 0f;
            normal = Vector3.up;
            return false;
        }

        int skipIncrement = levelOfDetail == 0 ? 1 : levelOfDetail * 2;
        bool edgeCell = fi < 2f || fi >= lastMainIndex || fj < 2f || fj >= lastMainIndex;
        int x0;
        int y0;
        int increment;

        if (edgeCell)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt(fi), 1, lastMainIndex);
            y0 = Mathf.Clamp(Mathf.FloorToInt(fj), 1, lastMainIndex);
            increment = 1;
        }
        else
        {
            x0 = 2 + Mathf.FloorToInt((fi - 2f) / skipIncrement) * skipIncrement;
            y0 = 2 + Mathf.FloorToInt((fj - 2f) / skipIncrement) * skipIncrement;
            x0 = Mathf.Min(x0, lastMainIndex - skipIncrement);
            y0 = Mathf.Min(y0, lastMainIndex - skipIncrement);
            increment = skipIncrement;
        }

        int x1 = x0 + increment;
        int y1 = y0 + increment;
        float tx = Mathf.Clamp01((fi - x0) / increment);
        float tz = Mathf.Clamp01((fj - y0) / increment);
        float h00 = GetLODVertexHeight(heightMap.values, x0, y0, numVertsPerLine, skipIncrement);
        float h10 = GetLODVertexHeight(heightMap.values, x1, y0, numVertsPerLine, skipIncrement);
        float h01 = GetLODVertexHeight(heightMap.values, x0, y1, numVertsPerLine, skipIncrement);
        float h11 = GetLODVertexHeight(heightMap.values, x1, y1, numVertsPerLine, skipIncrement);
        float span = increment * meshScale;
        Vector3 a = new Vector3(0f, h00, 0f);

        if (tz >= tx)
        {
            worldY = (1f - tz) * h00 + tx * h11 + (tz - tx) * h01;
            Vector3 d = new Vector3(span, h11, -span);
            Vector3 c = new Vector3(0f, h01, -span);
            normal = Vector3.Cross(d - a, c - a).normalized;
        }
        else
        {
            worldY = (1f - tx) * h00 + (tx - tz) * h10 + tz * h11;
            Vector3 b = new Vector3(span, h10, 0f);
            Vector3 d = new Vector3(span, h11, -span);
            normal = Vector3.Cross(b - a, d - a).normalized;
        }

        return true;
    }

    static float GetLODVertexHeight(float[,] heights, int x, int y, int numVertsPerLine, int skipIncrement)
    {
        bool isMeshEdgeVertex = y == 1 || y == numVertsPerLine - 2 || x == 1 || x == numVertsPerLine - 2;
        bool isMainVertex = (x - 2) % skipIncrement == 0 && (y - 2) % skipIncrement == 0 && !isMeshEdgeVertex;
        bool isEdgeConnectionVertex = (y == 2 || y == numVertsPerLine - 3 || x == 2 || x == numVertsPerLine - 3)
            && !isMeshEdgeVertex && !isMainVertex;

        if (!isEdgeConnectionVertex) return heights[x, y];

        bool isVertical = x == 2 || x == numVertsPerLine - 3;
        int distanceFromA = ((isVertical ? y : x) - 2) % skipIncrement;
        int distanceFromB = skipIncrement - distanceFromA;
        float percent = distanceFromA / (float)skipIncrement;
        float heightA = heights[isVertical ? x : x - distanceFromA, isVertical ? y - distanceFromA : y];
        float heightB = heights[isVertical ? x : x + distanceFromB, isVertical ? y + distanceFromB : y];
        return Mathf.Lerp(heightA, heightB, percent);
    }

    public static void BuildCombinedMeshes(VegetationPlacementData data, Vector3 chunkOrigin,
        out Mesh treesMesh, out Mesh grassMesh, out Mesh rocksMesh)
    {
        treesMesh = CombineType(data.trees, VegetationTemplateCache.Trees, chunkOrigin);
        grassMesh = CombineType(data.grass, VegetationTemplateCache.Grass, chunkOrigin);
        rocksMesh = CombineType(data.rocks, VegetationTemplateCache.Rocks, chunkOrigin);
    }

    static Mesh CombineType(List<VegetationInstance> instances, Mesh[] templates, Vector3 chunkOrigin)
    {
        if (instances.Count == 0 || templates == null) return null;

        CombineInstance[] combines = new CombineInstance[instances.Count];
        for (int i = 0; i < instances.Count; i++)
        {
            VegetationInstance inst = instances[i];
            combines[i].mesh = templates[inst.variantIndex];
            combines[i].transform = Matrix4x4.TRS(
                inst.position - chunkOrigin,
                inst.rotation,
                Vector3.one * inst.scale);
        }

        int totalVertexCount = 0;
        for (int i = 0; i < combines.Length; i++)
        {
            totalVertexCount += combines[i].mesh.vertexCount;
        }

        Mesh combined = new Mesh();
        combined.indexFormat = totalVertexCount > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        combined.CombineMeshes(combines, true, true);
        combined.RecalculateBounds();
        return combined;
    }
}

public static class VegetationTemplateCache
{
    static Mesh[] treeTemplates;
    static Mesh[] grassTemplates;
    static Mesh[] rockTemplates;
    static VegetationSettings cachedFor;

    public static Mesh[] Trees => treeTemplates;
    public static Mesh[] Grass => grassTemplates;
    public static Mesh[] Rocks => rockTemplates;

    public static void EnsureBuilt(VegetationSettings settings)
    {
        if (treeTemplates != null && cachedFor == settings) return;

        System.Random rng = new System.Random(settings.seed);

        treeTemplates = new Mesh[3];
        for (int i = 0; i < treeTemplates.Length; i++)
        {
            Color foliage = i % 2 == 0 ? settings.foliageColourA : settings.foliageColourB;
            treeTemplates[i] = VegetationMeshBuilder.BuildTree(settings.trunkColour, foliage, rng);
        }

        grassTemplates = new Mesh[3];
        for (int i = 0; i < grassTemplates.Length; i++)
        {
            grassTemplates[i] = VegetationMeshBuilder.BuildGrassClump(settings.grassColourA, settings.grassColourB, rng);
        }

        rockTemplates = new Mesh[3];
        for (int i = 0; i < rockTemplates.Length; i++)
        {
            rockTemplates[i] = VegetationMeshBuilder.BuildRock(settings.rockColourA, settings.rockColourB, rng);
        }

        cachedFor = settings;
    }
}
