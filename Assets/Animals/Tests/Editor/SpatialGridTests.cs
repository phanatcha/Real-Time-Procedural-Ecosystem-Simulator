using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class SpatialGridTests
{
    const float CellSize = 64f;

    [Test]
    public void AnEmptyGridFindsNothing()
    {
        SpatialGrid<int> grid = new SpatialGrid<int>(CellSize);
        List<int> results = new List<int> { 99 };

        grid.Query(Vector3.zero, 500f, results);

        Assert.AreEqual(0, results.Count);
    }

    [Test]
    public void ASearchFindsEverythingWithinItsRadiusOnceAndLittleBeyond()
    {
        System.Random random = new System.Random(1234);
        SpatialGrid<int> grid = new SpatialGrid<int>(CellSize);
        Vector3[] positions = new Vector3[500];
        for (int index = 0; index < positions.Length; index++)
        {
            positions[index] = new Vector3(RandomRange(random, -1000f, 1000f), RandomRange(random, 0f, 50f),
                                           RandomRange(random, -1000f, 1000f));
            grid.Add(index, positions[index]);
        }

        List<int> results = new List<int>();
        for (int search = 0; search < 50; search++)
        {
            Vector3 centre = new Vector3(RandomRange(random, -1100f, 1100f), 0f, RandomRange(random, -1100f, 1100f));
            float radius = RandomRange(random, 0f, 300f);
            grid.Query(centre, radius, results);

            HashSet<int> found = new HashSet<int>(results);
            Assert.AreEqual(results.Count, found.Count, "nothing is found twice");
            for (int index = 0; index < positions.Length; index++)
            {
                float distance = GroundDistance(centre, positions[index]);
                if (distance <= radius) Assert.IsTrue(found.Contains(index), $"{index} is {distance} away");
            }

            // Only cells the search square overlaps are read, so nothing found is more than a cell beyond it.
            foreach (int index in results)
            {
                Assert.LessOrEqual(GroundDistance(centre, positions[index]), (radius + CellSize) * 1.415f);
            }
        }
    }

    [Test]
    public void AHugeRadiusFindsEverything()
    {
        SpatialGrid<int> grid = new SpatialGrid<int>(CellSize);
        for (int index = 0; index < 20; index++)
        {
            grid.Add(index, new Vector3(index * 500f, 0f, -index * 300f));
        }

        List<int> results = new List<int>();
        grid.Query(Vector3.zero, 1e9f, results);

        Assert.AreEqual(20, results.Count);
    }

    [Test]
    public void ThingsAtNegativeCoordinatesAreInTheirOwnCells()
    {
        SpatialGrid<int> grid = new SpatialGrid<int>(10f);
        grid.Add(1, new Vector3(-1f, 0f, -1f));
        List<int> results = new List<int>();

        grid.Query(new Vector3(-2f, 0f, -2f), 0f, results);
        CollectionAssert.AreEqual(new[] { 1 }, results);

        grid.Query(new Vector3(2f, 0f, 2f), 0f, results);
        Assert.AreEqual(0, results.Count);
    }

    [Test]
    public void ClearingEmptiesTheGridAndLetsGoOfCellsNoLongerUsed()
    {
        SpatialGrid<int> grid = new SpatialGrid<int>(CellSize);
        grid.Add(1, new Vector3(0f, 0f, 0f));
        grid.Add(2, new Vector3(200f, 0f, 0f));
        grid.Add(3, new Vector3(0f, 0f, 200f));
        Assert.AreEqual(3, grid.Count);

        grid.Clear();
        List<int> results = new List<int>();
        grid.Query(Vector3.zero, 1000f, results);
        Assert.AreEqual(0, grid.Count);
        Assert.AreEqual(0, results.Count);

        // Refilled after a clear, a cell in use again is kept and the others are let go at the next clear.
        grid.Add(4, new Vector3(1f, 0f, 1f));
        grid.Clear();
        Assert.AreEqual(1, grid.CellCount);
    }

    static float RandomRange(System.Random random, float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }

    static float GroundDistance(Vector3 first, Vector3 second)
    {
        float x = first.x - second.x;
        float z = first.z - second.z;
        return Mathf.Sqrt(x * x + z * z);
    }
}
