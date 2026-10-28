using NUnit.Framework;
using UnityEngine;

namespace Boreal.TerrainGeneration.Tests
{
    // The sea's waves move open water fully, rivers and lakes not at all, and the shallows by the shore only a little. The
    // waves come from a directional spectrum: spread around the wind, never folding over, as high as the wind makes them.
    public class WaterWaveTests
    {
        const float WaterHeight = 10f;

        static WaveSpectrum.Settings Breeze(float spread = 1f) => new WaveSpectrum.Settings
        {
            windDirection = 35f, windSpeed = 7f, fetch = 40f, peakSharpness = 3.3f, spread = spread, waveCount = 24,
            shortestWave = 1f, heightScale = 1f, choppiness = 1f, seed = 1
        };

        [Test]
        public void WavesSpreadAroundTheWindUnlessSpreadIsZero()
        {
            Vector4[] waves = new Vector4[WaveSpectrum.MaxWaves];
            float[] phases = new float[WaveSpectrum.MaxWaves];
            Vector2 wind = new Vector2(Mathf.Cos(35f * Mathf.Deg2Rad), Mathf.Sin(35f * Mathf.Deg2Rad));

            int count = WaveSpectrum.Build(Breeze(), waves, phases, out _, out _, out _);
            float widest = 0f;
            Vector2 mean = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = new Vector2(waves[i].x, waves[i].y);
                widest = Mathf.Max(widest, Vector2.Angle(direction, wind));
                mean += direction * waves[i].w;
            }

            Assert.Greater(widest, 30f, "Every wave travels the same way.");
            Assert.Less(Vector2.Angle(mean, wind), 20f, "The biggest waves don't follow the wind.");

            count = WaveSpectrum.Build(Breeze(0f), waves, phases, out _, out _, out _);
            for (int i = 0; i < count; i++)
            {
                Assert.Less(Vector2.Angle(new Vector2(waves[i].x, waves[i].y), wind), 0.01f);
            }
        }

        [Test]
        public void CrestsNeverFoldOverAndTheSeaIsAsHighAsTheWindMakesIt()
        {
            Vector4[] waves = new Vector4[WaveSpectrum.MaxWaves];
            float[] phases = new float[WaveSpectrum.MaxWaves];
            int count = WaveSpectrum.Build(Breeze(), waves, phases, out float chop, out float significantHeight, out _);

            float steepness = 0f;
            for (int i = 0; i < count; i++) steepness += waves[i].z * waves[i].w;
            Assert.LessOrEqual(chop * steepness, 0.9f + 0.0001f);
            // A 7 m/s breeze over 40 km of open water raises a sea of roughly 0.5-1.5 m.
            Assert.That(significantHeight, Is.InRange(0.5f, 1.5f));
        }

        [Test]
        public void TheSameSeedGivesTheSameSea()
        {
            Vector4[] first = new Vector4[WaveSpectrum.MaxWaves];
            Vector4[] second = new Vector4[WaveSpectrum.MaxWaves];
            float[] phases = new float[WaveSpectrum.MaxWaves];
            WaveSpectrum.Build(Breeze(), first, phases, out _, out _, out _);
            WaveSpectrum.Build(Breeze(), second, phases, out _, out _, out _);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void DeepSeaGetsFullWaves()
        {
            Assert.AreEqual(1f, WaterSurface.WaveStrength(new TerrainSurfaceSample { height = WaterHeight - 20f }, WaterHeight), 0.001f);
        }

        [Test]
        public void RiversAndLakesStayCalm()
        {
            Assert.AreEqual(0f, WaterSurface.WaveStrength(new TerrainSurfaceSample { height = WaterHeight - 20f, riverStrength = 1f }, WaterHeight), 0.001f);
            Assert.AreEqual(0f, WaterSurface.WaveStrength(new TerrainSurfaceSample { height = WaterHeight - 20f, lakeStrength = 1f }, WaterHeight), 0.001f);
        }

        [Test]
        public void ShallowsGetSmallerWaves()
        {
            float shallow = WaterSurface.WaveStrength(new TerrainSurfaceSample { height = WaterHeight - 0.5f }, WaterHeight);
            Assert.AreEqual(0.3f, shallow, 0.001f);
        }
    }
}
