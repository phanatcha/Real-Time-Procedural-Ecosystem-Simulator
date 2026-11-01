using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // The scheduling specs the soak harness and experiments pass to the god tools (item 31).
    public class GodToolsTests
    {
        [Test]
        public void ParsesEveryKindWithItsDefaults()
        {
            Assert.IsTrue(GodTools.TryParse("drought@800", out GodTools.Intervention drought));
            Assert.AreEqual(GodTools.Kind.Drought, drought.kind);
            Assert.AreEqual(800f, drought.time);
            Assert.AreEqual(200f, drought.duration);
            Assert.AreEqual(0.2f, drought.amount);

            Assert.IsTrue(GodTools.TryParse("food@600", out GodTools.Intervention food));
            Assert.AreEqual(30f, food.amount);
            Assert.AreEqual(0f, food.duration);

            Assert.IsTrue(GodTools.TryParse("extinction@1200", out GodTools.Intervention extinction));
            Assert.AreEqual(0.5f, extinction.amount);

            Assert.IsTrue(GodTools.TryParse("mutation@400", out GodTools.Intervention mutation));
            Assert.AreEqual(4f, mutation.amount);
            Assert.AreEqual(300f, mutation.duration);
        }

        [Test]
        public void ParsesDurationAndAmount()
        {
            Assert.IsTrue(GodTools.TryParse(" Drought@800:150x0.35 ", out GodTools.Intervention drought));
            Assert.AreEqual(150f, drought.duration);
            Assert.AreEqual(0.35f, drought.amount, 1e-6f);

            Assert.IsTrue(GodTools.TryParse("extinction@100x2", out GodTools.Intervention extinction));
            Assert.AreEqual(1f, extinction.amount, "A share above 1 means everyone.");
            Assert.AreEqual("mass extinction: 100% of the animals at 100 s", extinction.ToString());
        }

        [TestCase("")]
        [TestCase("drought")]
        [TestCase("flood@100")]
        [TestCase("drought@-5")]
        [TestCase("drought@100:abc")]
        [TestCase("drought@100xabc")]
        [TestCase("drought@100:1:2")]
        public void RejectsMalformedSpecs(string spec)
        {
            Assert.IsFalse(GodTools.TryParse(spec, out _));
        }
    }
}
