using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// The FFT ocean on the GPU builds the same sea as adding its waves up one by one, and it carries on from where the
// Gerstner swells stop.
public class OceanFFTTests
{
    static WaveSpectrum.Settings Breeze() => new WaveSpectrum.Settings
    {
        windDirection = 35f, windSpeed = 7f, fetch = 40f, peakSharpness = 3.3f, spread = 1f, waveCount = 24,
        shortestWave = 1f, heightScale = 1f, choppiness = 0.8f, seed = 1
    };

    [Test]
    public void ThePatchIsItsWavesAddedUp()
    {
        Assume.That(SystemInfo.supportsComputeShaders, "This GPU can't run compute shaders.");
        ComputeShader shader = Resources.Load<ComputeShader>("OceanFFT");
        Assert.IsNotNull(shader);

        WaveSpectrum.Settings swells = Breeze();
        swells.shortestWave = 20f;
        OceanFFT ocean = new OceanFFT(shader);
        try
        {
            const float time = 1.3f;
            ocean.SetSpectrum(Breeze(), swells, 6f);
            ocean.Simulate(time, 0.8f, 3f);

            int n = OceanFFT.Size;
            float tileSize = ocean.TileSize(0);
            NativeArray<Vector4> spectrum = Read(ocean.InitialSpectrum(0));
            NativeArray<Vector4> displacement = Read(ocean.Displacement(0));

            float largest = 0f;
            foreach (Vector4 texel in displacement) largest = Mathf.Max(largest, Mathf.Abs(texel.y));
            Assert.Greater(largest, 0.01f, "The patch is flat.");

            foreach (Vector2Int point in new[] { new Vector2Int(0, 0), new Vector2Int(17, 200), new Vector2Int(131, 64) })
            {
                Vector2 position = (Vector2)point * (tileSize / n);
                double height = 0.0, imaginary = 0.0, moveX = 0.0;
                for (int y = 1; y < n; y++)
                {
                    for (int x = 1; x < n; x++)
                    {
                        Vector2 k = new Vector2(x - n / 2, y - n / 2) * (2f * Mathf.PI / tileSize);
                        if (k == Vector2.zero) continue;
                        Vector4 h0 = spectrum[y * n + x];
                        Vector4 opposite = spectrum[((n - y) % n) * n + (n - x) % n];
                        double omega = System.Math.Sqrt(9.81 * k.magnitude) * time;
                        // h = h0 e^(-i w t) + conj(h0(-k)) e^(i w t), then times e^(i k.x).
                        double hRe = h0.x * System.Math.Cos(omega) + h0.y * System.Math.Sin(omega) +
                                     opposite.x * System.Math.Cos(omega) + opposite.y * System.Math.Sin(omega);
                        double hIm = h0.y * System.Math.Cos(omega) - h0.x * System.Math.Sin(omega) -
                                     opposite.y * System.Math.Cos(omega) + opposite.x * System.Math.Sin(omega);
                        double phase = Vector2.Dot(k, position);
                        double re = hRe * System.Math.Cos(phase) - hIm * System.Math.Sin(phase);
                        double im = hRe * System.Math.Sin(phase) + hIm * System.Math.Cos(phase);
                        height += re;
                        imaginary += im;
                        // i k/|k| h e^(ikx): its real part is -im k/|k|.
                        moveX += -im * k.x / k.magnitude;
                    }
                }

                Vector4 texel = displacement[point.y * n + point.x];
                float tolerance = 0.01f * largest;
                Assert.AreEqual(0.0, imaginary, tolerance, "The sea isn't real at " + point);
                Assert.AreEqual(height, texel.y, tolerance, "Height at " + point);
                Assert.AreEqual(0.8 * moveX, texel.x, tolerance, "Sideways movement at " + point);
            }
        }
        finally
        {
            ocean.Dispose();
        }
    }

    [Test]
    public void TheFFTStartsWhereTheSwellsStop()
    {
        Assume.That(SystemInfo.supportsComputeShaders, "This GPU can't run compute shaders.");
        OceanFFT ocean = new OceanFFT(Resources.Load<ComputeShader>("OceanFFT"));
        try
        {
            WaveSpectrum.Settings swells = Breeze();
            swells.shortestWave = 20f;
            ocean.SetSpectrum(Breeze(), swells, 6f);

            int n = OceanFFT.Size;
            NativeArray<Vector4> spectrum = Read(ocean.InitialSpectrum(0));
            float longest = 0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i].x == 0f && spectrum[i].y == 0f) continue;
                Vector2 k = new Vector2(i % n - n / 2, i / n - n / 2) * (2f * Mathf.PI / ocean.TileSize(0));
                longest = Mathf.Max(longest, 2f * Mathf.PI / k.magnitude);
            }

            Assert.That(longest, Is.InRange(15f, 20f));
            Assert.Greater(ocean.Reach, 0f);
        }
        finally
        {
            ocean.Dispose();
        }
    }

    static NativeArray<Vector4> Read(RenderTexture texture)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
        request.WaitForCompletion();
        Assert.IsFalse(request.hasError, "Couldn't read " + texture.format + " back from the GPU.");
        return request.GetData<Vector4>();
    }
}
