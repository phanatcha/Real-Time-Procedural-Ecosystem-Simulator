using NUnit.Framework;
using UnityEngine;

namespace Boreal.TerrainGeneration.Tests
{
    // Distant chunks draw simpler spruces and birches, which must stand exactly as tall as the near ones so trees don't
    // jump when a chunk changes detail.
    public class FarTreeTests
    {
        [Test]
        public void FarTreesAreAsTallWithUnderAQuarterOfTheTriangles()
        {
            VegetationSettings settings = ScriptableObject.CreateInstance<VegetationSettings>();
            Mesh[] near = VegetationTemplateCache.BuildTreeTemplates(settings, new System.Random(1), out Mesh[] far);

            Assert.AreEqual(VegetationGenerator.SpruceVariants + VegetationGenerator.BirchVariants, near.Length);
            for (int i = 0; i < near.Length; i++)
            {
                Assert.AreEqual(near[i].bounds.max.y, far[i].bounds.max.y, 0.01f, $"Tree {i} changes height far away.");
                Assert.Less(far[i].triangles.Length * 4, near[i].triangles.Length, $"Far tree {i} isn't much simpler.");
                Object.DestroyImmediate(near[i]);
                Object.DestroyImmediate(far[i]);
            }

            Object.DestroyImmediate(settings);
        }
    }
}
