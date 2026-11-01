using System.Collections.Generic;
using NUnit.Framework;

namespace Boreal.TerrainGeneration.Tests
{
    public class VegetationHashTests
    {
        [TestCase(0, 0)]
        [TestCase(-100000, 100000)]
        [TestCase(int.MinValue, int.MaxValue - 4096)]
        public void NeighboringCellsKeepDistinctOffsetsAtAnyDistance(int x, int z)
        {
            var offsets = new HashSet<float>();
            int closeNeighbors = 0;
            float previous = -1;
            for (int i = 0; i < 4096; i++)
            {
                float value = ValueNoise.CellHash(x, z + i, int.MinValue, 1, 0);
                Assert.That(value, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
                Assert.AreEqual(value, ValueNoise.CellHash(x, z + i, int.MinValue, 1, 0));
                offsets.Add(value);
                if (System.Math.Abs(value - previous) < 0.01) closeNeighbors++;
                previous = value;
            }
            Assert.Greater(offsets.Count, 4000);
            Assert.Less(closeNeighbors, 150, "Neighboring z cells must not form straight runs of trees.");
        }

        [Test]
        public void HighSeedBitsAndPurposeStreamsAffectPlacement()
        {
            float original = ValueNoise.CellHash(-51, 72, 1, 1, 0);
            Assert.AreNotEqual(original, ValueNoise.CellHash(-51, 72, 1025, 1, 0));
            Assert.AreNotEqual(original, ValueNoise.CellHash(-51, 72, 1, 2, 0));
            Assert.AreNotEqual(original, ValueNoise.CellHash(-51, 72, 1, 1, 1));
        }
    }
}
