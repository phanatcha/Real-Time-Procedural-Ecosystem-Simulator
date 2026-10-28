using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // A directional wave spectrum for the sea: the idea behind FFT ocean water (as in Acerola's Water project). The long
    // swells are summed as a few dozen Gerstner waves, so they never repeat across WaterSurface's tiles; OceanFFT draws the
    // shorter waves from the same spectrum. How much energy waves of each
    // length carry follows the JONSWAP spectrum for the wind speed and the open water upwind (the fetch). Each wave gets
    // its own direction, drawn around the wind: waves near the spectrum's peak follow the wind closely, and shorter and
    // longer ones spread wider (Mitsuyasu's cos-2s spreading).
    public static class WaveSpectrum
    {
        public const int MaxWaves = 32;
        const float Gravity = 9.81f;

        public struct Settings
        {
            public float windDirection;   // degrees the waves travel towards: 0 is +x, 90 is +z
            public float windSpeed;       // m/s
            public float fetch;           // km of open water upwind
            public float peakSharpness;   // JONSWAP's peak enhancement (gamma)
            public float spread;          // 0 sends every wave along the wind, 1 is the natural spread
            public int waveCount;
            public float shortestWave;    // m
            public float heightScale;
            public float choppiness;      // 0-1, how far crests lean and sharpen
            public int seed;
        }

        // Fills waves[i] with (direction x, direction z, wavenumber in rad/m, amplitude in m) and phases[i] in radians, and
        // returns how many waves there are. chop is the sideways movement, capped so crests never fold through themselves;
        // significantHeight is the usual measure of a sea (the average height of the highest third of waves); reach is the
        // most the waves can ever lift the surface.
        public static int Build(Settings settings, Vector4[] waves, float[] phases,
                                out float chop, out float significantHeight, out float reach)
        {
            int count = Mathf.Clamp(settings.waveCount, 1, Mathf.Min(MaxWaves, Mathf.Min(waves.Length, phases.Length)));
            Shape(settings, out float alpha, out float peak, out float peakSpread, out float lowest, out float highest);
            float logRange = Mathf.Log(highest / lowest);

            System.Random random = new System.Random(settings.seed);
            float windAngle = settings.windDirection * Mathf.Deg2Rad;
            float steepness = 0f;
            float variance = 0f;
            reach = 0f;
            for (int i = 0; i < count; i++)
            {
                // One wave per band of frequencies, the bands evenly spaced on a log scale, each wave somewhere in its band
                // so the waves don't fall into step.
                float omega = lowest * Mathf.Exp(logRange * (i + 0.25f + 0.5f * (float)random.NextDouble()) / count);
                float bandWidth = omega * logRange / count;
                float amplitude = Mathf.Max(settings.heightScale, 0f) *
                                  Mathf.Sqrt(2f * Jonswap(omega, alpha, peak, Mathf.Max(settings.peakSharpness, 1f)) * bandWidth);

                // cos-2s spreading: the larger s, the closer the wave follows the wind. A cos-2s lobe is close to a bell
                // curve about sqrt(2 / (s + 1)) radians wide.
                float s = peakSpread * (omega < peak ? Mathf.Pow(omega / peak, 5f) : Mathf.Pow(omega / peak, -2.5f));
                float angle = windAngle + Gaussian(random) * Mathf.Sqrt(2f / (s + 1f)) * Mathf.Max(settings.spread, 0f);
                float wavenumber = omega * omega / Gravity;

                waves[i] = new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), wavenumber, amplitude);
                phases[i] = (float)random.NextDouble() * 2f * Mathf.PI;
                steepness += wavenumber * amplitude;
                variance += amplitude * amplitude * 0.5f;
                reach += amplitude;
            }

            chop = Mathf.Min(Mathf.Clamp01(settings.choppiness), 0.9f / Mathf.Max(steepness, 0.0001f));
            significantHeight = 4f * Mathf.Sqrt(variance);
            return count;
        }

        // JONSWAP's energy level and peak frequency (rad/s) for the wind and the fetch (Hasselmann et al., 1973), the cos-2s
        // spreading's s at the peak (Mitsuyasu), and the band of frequencies Build's waves cover: from a little below the
        // peak, where the spectrum starts, up to the shortest wave wanted.
        public static void Shape(Settings settings, out float alpha, out float peak, out float peakSpread,
                                 out float lowest, out float highest)
        {
            float wind = Mathf.Max(settings.windSpeed, 0.5f);
            float fetch = Mathf.Max(settings.fetch, 0.1f) * 1000f;
            alpha = 0.076f * Mathf.Pow(Gravity * fetch / (wind * wind), -0.22f);
            peak = 22f * Mathf.Pow(Gravity * Gravity / (wind * fetch), 1f / 3f);
            peakSpread = 11.5f * Mathf.Pow(peak * wind / Gravity, -2.5f);
            lowest = peak * 0.6f;
            highest = Mathf.Max(Mathf.Sqrt(Gravity * 2f * Mathf.PI / Mathf.Max(settings.shortestWave, 0.1f)), lowest * 1.5f);
        }

        // Energy per unit of angular frequency (m^2 s) for a sea that is still growing under the wind.
        static float Jonswap(float omega, float alpha, float peak, float peakSharpness)
        {
            float sigma = omega <= peak ? 0.07f : 0.09f;
            float r = Mathf.Exp(-(omega - peak) * (omega - peak) / (2f * sigma * sigma * peak * peak));
            return alpha * Gravity * Gravity / Mathf.Pow(omega, 5f) * Mathf.Exp(-1.25f * Mathf.Pow(peak / omega, 4f)) *
                   Mathf.Pow(peakSharpness, r);
        }

        // A normally distributed random number (Box-Muller).
        static float Gaussian(System.Random random)
        {
            double u = 1.0 - random.NextDouble();
            double v = random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
        }
    }
}
