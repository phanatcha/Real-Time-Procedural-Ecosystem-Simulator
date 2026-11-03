using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.TerrainGeneration.Tests
{
    // The seasons' look: birches and bushes in leaf from spring to fall and bare in winter, berries and flowers in
    // their seasons, and which parts of each plant the seasons change.
    public class SeasonalLookTests
    {
        const float Tolerance = 0.0001f;

        [Test]
        public void HighSummerLooksAsWithoutSeasons()
        {
            SeasonalLookState summer = SeasonalLook.At(0.25f, 0f, 0f);

            Assert.AreEqual(0f, summer.autumnColour, Tolerance);
            Assert.AreEqual(0f, summer.leafFall, Tolerance);
            Assert.AreEqual(0f, summer.springFresh, Tolerance);
            Assert.AreEqual(0f, summer.flowersGone, Tolerance, "flowers bloom until midsummer");
            Assert.AreEqual(0f, summer.grassDry, Tolerance);
            Assert.AreEqual(0f, summer.snow, Tolerance);
            Assert.AreEqual(0f, summer.ice, Tolerance);
        }

        [Test]
        public void LeavesColourInFallAndAreGoneInWinter()
        {
            SeasonalLookState fall = SeasonalLook.At(0.55f, 0.05f, 0f);
            Assert.AreEqual(1f, fall.autumnColour, Tolerance);
            Assert.Greater(fall.leafFall, 0.2f);
            Assert.Less(fall.leafFall, 0.8f, "halfway through the fall the trees are dropping their leaves");
            Assert.AreEqual(0f, fall.fruitGone, Tolerance, "berries are ripe in early fall");

            SeasonalLookState winter = SeasonalLook.At(0.75f, 0.7f, 1f);
            Assert.AreEqual(1f, winter.leafFall, Tolerance, "only twigs are left in winter");
            Assert.AreEqual(1f, winter.fruitGone, Tolerance);
            Assert.AreEqual(1f, winter.flowersGone, Tolerance);
            Assert.AreEqual(1f, winter.grassDry, Tolerance);
            Assert.AreEqual(0.7f, winter.snow, Tolerance);
            Assert.AreEqual(1f, winter.ice, Tolerance);
        }

        // Spring comes across the turn of the year: the leaves come out green and fresh after the bare winter.
        [Test]
        public void LeavesComeBackFreshInSpring()
        {
            SeasonalLookState lateWinter = SeasonalLook.At(0.9f, 0.5f, 0f);
            SeasonalLookState spring = SeasonalLook.At(0.05f, 0.5f, 0f);

            Assert.AreEqual(1f, lateWinter.leafFall, Tolerance);
            Assert.AreEqual(0f, spring.leafFall, Tolerance);
            Assert.Greater(spring.springFresh, 0.5f);
            Assert.AreEqual(0f, spring.autumnColour, Tolerance);
        }

        [Test]
        public void WindowsWrapRoundTheYear()
        {
            Assert.AreEqual(1f, SeasonalLook.Window(0.05f, 0.9f, 0.2f, 0.1f, 0.1f), Tolerance);
            Assert.AreEqual(0f, SeasonalLook.Window(0.5f, 0.9f, 0.2f, 0.1f, 0.1f), Tolerance);
            Assert.AreEqual(0.5f, SeasonalLook.Window(0.95f, 0.9f, 0.2f, 0.1f, 0.1f), Tolerance);
            Assert.AreEqual(0.5f, SeasonalLook.Window(0.25f, 0.9f, 0.2f, 0.1f, 0.1f), Tolerance);
        }

        // Birches lose their leaves and spruces keep their needles; the island's bushes drop leaves and berries, but
        // plants animals eat keep theirs, so food can still be seen in winter.
        [Test]
        public void EachPlantSaysWhatTheSeasonsDoToIt()
        {
            VegetationSettings settings = ScriptableObject.CreateInstance<VegetationSettings>();
            var meshes = new List<Mesh>();
            try
            {
                Mesh[] trees = VegetationTemplateCache.BuildTreeTemplates(settings, new System.Random(3), out Mesh[] far);
                meshes.AddRange(trees);
                meshes.AddRange(far);
                Mesh spruce = trees[0];
                Mesh birch = trees[VegetationGenerator.SpruceVariants];
                Mesh scenery = VegetationMeshBuilder.BuildBerryBush(settings.bushLeafColour, settings.bilberryColour, false,
                                                                    new System.Random(4));
                Mesh food = VegetationMeshBuilder.BuildForestFood(ForestFood.Bilberry, settings, new System.Random(4));
                meshes.Add(scenery);
                meshes.Add(food);

                CollectionAssert.Contains(Parts(spruce), SeasonalPart.Needles);
                CollectionAssert.DoesNotContain(Parts(spruce), SeasonalPart.Leaf);
                CollectionAssert.Contains(Parts(birch), SeasonalPart.Leaf);
                CollectionAssert.Contains(Parts(birch), SeasonalPart.Fixed, "the trunk and twigs stay all year");
                CollectionAssert.Contains(Parts(scenery), SeasonalPart.Leaf);
                CollectionAssert.Contains(Parts(scenery), SeasonalPart.Fruit);
                CollectionAssert.Contains(Parts(food), SeasonalPart.LastingLeaf);
                CollectionAssert.DoesNotContain(Parts(food), SeasonalPart.Leaf);
                CollectionAssert.DoesNotContain(Parts(food), SeasonalPart.Fruit);
            }
            finally
            {
                foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AnyLakeWaterIsFreshButARiverOnlyWhereItIsCutIn()
        {
            Assert.IsTrue(FreshWater.IsFresh(0f, 0.01f));
            Assert.IsTrue(FreshWater.IsFresh(0.06f, 0f));
            Assert.IsFalse(FreshWater.IsFresh(0.03f, 0f));
            Assert.IsFalse(FreshWater.IsFresh(0f, 0f), "the sea is salt");
        }

        static HashSet<SeasonalPart> Parts(Mesh mesh)
        {
            var uvs = new List<Vector3>();
            mesh.GetUVs(2, uvs);
            var parts = new HashSet<SeasonalPart>();
            foreach (Vector3 uv in uvs) parts.Add((SeasonalPart)Mathf.RoundToInt(uv.x));
            return parts;
        }
    }
}
