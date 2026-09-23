using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class TerrainReportDataTests
{
    HeightMapSettings heights;
    MeshSettings meshes;

    [SetUp]
    public void SetUp()
    {
        heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>("Assets/TerrainGeneration/Settings/HeightMapSettings.asset"));
        meshes = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MeshSettings>("Assets/TerrainGeneration/Settings/MeshSettings.asset"));
        heights.noiseSettings.normalizeMode = Noise.NormalizeMode.Global;
        meshes.chunkSizeIndex = 0;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(heights);
        Object.DestroyImmediate(meshes);
    }

    [TestCase(0, 0)]
    [TestCase(-3, -2)]
    [TestCase(13, 0)]
    public void FinalMapAndCarvingMasksMatchRuntimeAcrossEntireRegion(int centreX, int centreZ)
    {
        Vector2Int centre = new Vector2Int(centreX, centreZ);
        int n = meshes.numVertsPerLine;
        int stride = n - 3;
        using (TerrainReportData data = TerrainReportData.Generate(heights, meshes, null, centre, 3, 0))
        {
            Assert.AreEqual(3 * stride + 1, data.Size);
            foreach (TerrainReportData.Chunk chunk in data.Chunks)
            {
                Vector2 world = TerrainGrid.ChunkCoordinateToWorldPosition(chunk.coordinate, meshes);
                HeightMap expected = HeightMapGenerator.GenerateHeightMap(n, n, heights, world / meshes.meshScale);
                int column = chunk.coordinate.x - centre.x + 1;
                int row = centre.y + 1 - chunk.coordinate.y;
                for (int y = 1; y < n - 1; y++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        int atlasX = column * stride + x - 1;
                        int atlasY = row * stride + y - 1;
                        Assert.That(data.Maps[(int)TerrainReportStage.FinalHeight][atlasX, atlasY], Is.EqualTo(expected.values[x, y]).Within(0.0001f));
                        Assert.That(data.Maps[(int)TerrainReportStage.RiverMask][atlasX, atlasY], Is.EqualTo(expected.riverStrengthValues[x, y]).Within(0.0001f));
                        Assert.That(data.Maps[(int)TerrainReportStage.LakeMask][atlasX, atlasY], Is.EqualTo(expected.lakeStrengthValues[x, y]).Within(0.0001f));
                    }
            }
        }
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(4)]
    public void PreviewMeshExactlyMatchesProductionMeshAtSelectedLod(int lod)
    {
        Vector2Int centre = new Vector2Int(-4, 7);
        using (TerrainReportData data = TerrainReportData.Generate(heights, meshes, null, centre, 1, lod))
        {
            Vector2 sampleCentre = TerrainGrid.ChunkCoordinateToWorldPosition(centre, meshes) / meshes.meshScale;
            HeightMap map = HeightMapGenerator.GenerateHeightMap(meshes.numVertsPerLine, meshes.numVertsPerLine, heights, sampleCentre);
            Mesh expected = MeshGenerator.GenerateTerrainMesh(map.values, meshes, lod).CreateMesh();
            try
            {
                CollectionAssert.AreEqual(expected.vertices, data.Chunks[0].mesh.vertices);
                CollectionAssert.AreEqual(expected.triangles, data.Chunks[0].mesh.triangles);
                CollectionAssert.AreEqual(expected.normals, data.Chunks[0].mesh.normals);
            }
            finally { Object.DestroyImmediate(expected); }
        }
    }

    [Test]
    public void DisabledEffectsProduceBlackMasksAndUnchangedNoise()
    {
        heights.noiseSettings.domainWarpStrength = 0f;
        heights.ridgeSettings.strength = 0f;
        heights.riverSettings.enabled = false;
        heights.lakeSettings.enabled = false;
        heights.useFalloff = false;
        using (TerrainReportData data = TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 1, 0))
        {
            for (int y = 0; y < data.Size; y++)
                for (int x = 0; x < data.Size; x++)
                {
                    float expected = data.Maps[(int)TerrainReportStage.BaseNoise][x, y];
                    Assert.AreEqual(expected, data.Maps[(int)TerrainReportStage.WarpedNoise][x, y]);
                    Assert.AreEqual(expected, data.Maps[(int)TerrainReportStage.RidgeBlend][x, y]);
                    Assert.AreEqual(expected, data.Maps[(int)TerrainReportStage.ShapedHeight][x, y]);
                    Assert.AreEqual(0f, data.Maps[(int)TerrainReportStage.RiverMask][x, y]);
                    Assert.AreEqual(0f, data.Maps[(int)TerrainReportStage.LakeMask][x, y]);
                    Assert.AreEqual(0f, data.Maps[(int)TerrainReportStage.Falloff][x, y]);
                }
        }
    }

    [Test]
    public void TextureUsesNorthUpOrientationAndSuppliedFixedRange()
    {
        float[,] values = { { 10f, 20f }, { 30f, 40f } };
        Texture2D texture = TerrainReportData.CreateTexture(values, 0f, 100f);
        try
        {
            Assert.That(texture.GetPixel(0, 1).r, Is.EqualTo(0.1f).Within(1f / 255f));
            Assert.That(texture.GetPixel(1, 1).r, Is.EqualTo(0.3f).Within(1f / 255f));
            Assert.That(texture.GetPixel(0, 0).r, Is.EqualTo(0.2f).Within(1f / 255f));
            Assert.That(texture.GetPixel(1, 0).r, Is.EqualTo(0.4f).Within(1f / 255f));
        }
        finally { Object.DestroyImmediate(texture); }
    }

    [Test]
    public void RepeatedGenerationIsDeterministicAndDoesNotMutateSettings()
    {
        string beforeHeights = EditorJsonUtility.ToJson(heights);
        string beforeMeshes = EditorJsonUtility.ToJson(meshes);
        using (TerrainReportData first = TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 1, 0))
        using (TerrainReportData second = TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 1, 0))
        {
            for (int stage = 0; stage < first.Maps.Length; stage++)
                CollectionAssert.AreEqual(first.Maps[stage], second.Maps[stage]);
        }
        Assert.AreEqual(beforeHeights, EditorJsonUtility.ToJson(heights));
        Assert.AreEqual(beforeMeshes, EditorJsonUtility.ToJson(meshes));
    }

    [Test]
    public void CancellingGenerationStopsBeforeProducingARegion()
    {
        Assert.Throws<OperationCanceledException>(() => TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 3, 0, _ => true));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(17)]
    public void InvalidRegionSizesAreRejected(int width)
    {
        Assert.Throws<ArgumentException>(() => TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, width, 0));
    }

    [Test]
    public void OversizedAtlasIsRejectedBeforeAllocation()
    {
        meshes.chunkSizeIndex = MeshSettings.supportedChunkSizes.Length - 1;
        Assert.Throws<ArgumentException>(() => TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 15, 0));
    }
}
