using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    public class BreedingWaitTests
    {
        [TestCase(0.02f, 500)]
        [TestCase(1f, 10)]
        public void OneContinuousWaitIsOneEpisodeRegardlessOfFrameStep(float step, int frames)
        {
            var tracker = new BreedingWaitTracker();
            int episodes = 0;
            for (int i = 0; i < frames; i++) if (tracker.Observe(true, step, 0)) episodes++;
            Assert.AreEqual(1, episodes);
            Assert.IsFalse(tracker.Observe(true, 0, 0));
            Assert.IsFalse(tracker.Observe(false, step, 0));
            Assert.IsTrue(tracker.Observe(true, step, 0));
            Assert.IsTrue(tracker.Observe(true, step, 1));
        }

        [Test]
        public void AutomaticRevisionPreservesExplicitExperimentLabels()
        {
            var previous = SimulationRunRecorder.RevisionProvider;
            try
            {
                SimulationRunRecorder.RevisionProvider = () => "abc123+dirty";
                Assert.AreEqual("abc123+dirty", SimulationRunRecorder.ResolveRevision("unrecorded"));
                Assert.AreEqual("frozen-experiment", SimulationRunRecorder.ResolveRevision("frozen-experiment"));
                SimulationRunRecorder.RevisionProvider = null;
                Assert.AreEqual("unrecorded", SimulationRunRecorder.ResolveRevision(""));
            }
            finally { SimulationRunRecorder.RevisionProvider = previous; }
        }
    }
}
