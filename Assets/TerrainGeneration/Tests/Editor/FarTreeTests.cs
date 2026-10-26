using NUnit.Framework;
using UnityEngine;

// Distant chunks draw simpler spruces, which must stand exactly as tall as the near ones so trees don't jump when a
// chunk changes detail.
public class FarTreeTests
{
    [Test]
    public void FarSprucesAreAsTallWithUnderAQuarterOfTheTriangles()
    {
        VegetationSettings settings = ScriptableObject.CreateInstance<VegetationSettings>();
        Mesh[] near = VegetationTemplateCache.BuildTreeTemplates(settings, new System.Random(1), out Mesh[] far);

        for (int i = 0; i < near.Length; i++)
        {
            Assert.AreEqual(near[i].bounds.max.y, far[i].bounds.max.y, 0.01f, $"Spruce {i} changes height far away.");
            Assert.Less(far[i].triangles.Length * 4, near[i].triangles.Length, $"Far spruce {i} isn't much simpler.");
            Object.DestroyImmediate(near[i]);
            Object.DestroyImmediate(far[i]);
        }

        Object.DestroyImmediate(settings);
    }
}
