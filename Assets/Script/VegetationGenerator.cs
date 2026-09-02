using System.Collections.Generic;
using UnityEngine;

public struct VegetationInstance
{
    public int variantIndex;
    public Vector3 position;
    public Quaternion rotation;
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
        VegetationSettings vegSettings, float moistureScale,
        int numVertsPerLine, float meshScale)
    {
        VegetationPlacementData data = new VegetationPlacementData();
        Vector2 chunkWorldCentre = (chunkWorldMin + chunkWorldMax) * 0.5f;

        GenerateType(vegSettings.trees, data.trees, TreeVariants, 1, chunkWorldMin, chunkWorldMax,
            heightMap, terrainMinHeight, terrainMaxHeight, vegSettings.minimumLandHeightPercent,
            vegSettings.seed, moistureScale, chunkWorldCentre, numVertsPerLine, meshScale);
        GenerateType(vegSettings.grass, data.grass, GrassVariants, 2, chunkWorldMin, chunkWorldMax,
            heightMap, terrainMinHeight, terrainMaxHeight, vegSettings.minimumLandHeightPercent,
            vegSettings.seed, moistureScale, chunkWorldCentre, numVertsPerLine, meshScale);
        GenerateType(vegSettings.rocks, data.rocks, RockVariants, 3, chunkWorldMin, chunkWorldMax,
            heightMap, terrainMinHeight, terrainMaxHeight, vegSettings.minimumLandHeightPercent,
            vegSettings.seed, moistureScale, chunkWorldCentre, numVertsPerLine, meshScale);

        return data;
    }

    static void GenerateType(VegetationTypeSettings settings, List<VegetationInstance> output, int variantCount, int typeSalt,
        Vector2 chunkWorldMin, Vector2 chunkWorldMax, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight, float minimumLandHeightPercent,
        int seed, float moistureScale, Vector2 chunkWorldCentre, int numVertsPerLine, float meshScale)
    {
        if (!settings.enabled) return;

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

                float densityRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 61.7f));
                if (densityRoll > settings.density) continue;

                if (!SampleTerrain(worldPos, heightMap, chunkWorldCentre, numVertsPerLine, meshScale,
                    out float worldY, out float slope, out Vector3 normal))
                {
                    continue;
                }

                float heightPercent = Mathf.InverseLerp(terrainMinHeight, terrainMaxHeight, worldY);
                float minimumAllowedHeight = Mathf.Max(minimumLandHeightPercent, settings.minHeightPercent);
                if (heightPercent < minimumAllowedHeight || heightPercent > settings.maxHeightPercent) continue;
                if (slope > settings.maxSlope) continue;

                float moisture = ValueNoise.Sample(worldPos / Mathf.Max(moistureScale, 0.01f));
                if (moisture < settings.minMoisture || moisture > settings.maxMoisture) continue;

                float variantRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 88.3f));
                int variant = Mathf.Clamp(Mathf.FloorToInt(variantRoll * variantCount), 0, variantCount - 1);
                float rotRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 5.5f));
                float scaleRoll = ValueNoise.Hash21(cellOrigin + new Vector2(saltBase, 44.4f));

                Quaternion spin = Quaternion.Euler(0, rotRoll * 360f, 0);
                Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, normal);
                Quaternion surfaceRotation = Quaternion.Slerp(Quaternion.identity, slopeAlign, settings.surfaceAlignment);

                output.Add(new VegetationInstance
                {
                    variantIndex = variant,
                    position = new Vector3(worldPos.x, worldY - settings.groundSink, worldPos.y),
                    rotation = surfaceRotation * spin,
                    scale = Mathf.Lerp(settings.minScale, settings.maxScale, scaleRoll),
                });
            }
        }
    }

    static bool SampleTerrain(Vector2 worldPos, HeightMap heightMap, Vector2 chunkWorldCentre,
        int numVertsPerLine, float meshScale, out float worldY, out float slope, out Vector3 normal)
    {

        float halfWorldSize = (numVertsPerLine - 3) * meshScale * 0.5f;
        float fi = (worldPos.x - chunkWorldCentre.x + halfWorldSize) / meshScale + 1f;
        float fj = (chunkWorldCentre.y + halfWorldSize - worldPos.y) / meshScale + 1f;

        int i0 = Mathf.FloorToInt(fi);
        int j0 = Mathf.FloorToInt(fj);

        if (i0 < 1 || i0 > numVertsPerLine - 3 || j0 < 1 || j0 > numVertsPerLine - 3)
        {
            worldY = 0f;
            slope = 1f;
            normal = Vector3.up;
            return false;
        }

        float tx = fi - i0;
        float tz = fj - j0;
        float h00 = heightMap.values[i0, j0];
        float h10 = heightMap.values[i0 + 1, j0];
        float h01 = heightMap.values[i0, j0 + 1];
        float h11 = heightMap.values[i0 + 1, j0 + 1];

        if (tz >= tx)
        {
            worldY = (1f - tz) * h00 + tx * h11 + (tz - tx) * h01;
        }
        else
        {
            worldY = (1f - tx) * h00 + (tx - tz) * h10 + tz * h11;
        }

        // Slope only needs to be roughly right, so the nearest whole cell is fine here.
        int i = Mathf.RoundToInt(fi);
        int j = Mathf.RoundToInt(fj);
        float hL = heightMap.values[i - 1, j];
        float hR = heightMap.values[i + 1, j];
        float hPositiveZ = heightMap.values[i, j - 1];
        float hNegativeZ = heightMap.values[i, j + 1];

        float heightDerivativeX = (hR - hL) / (2f * meshScale);
        float heightDerivativeZ = (hPositiveZ - hNegativeZ) / (2f * meshScale);
        float gradMag = Mathf.Sqrt(heightDerivativeX * heightDerivativeX + heightDerivativeZ * heightDerivativeZ);
        float normalY = 1f / Mathf.Sqrt(1f + gradMag * gradMag);
        slope = 1f - normalY;

        normal = new Vector3(-heightDerivativeX, 1f, -heightDerivativeZ).normalized;

        return true;
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
