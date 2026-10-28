using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // The sea's shorter waves as an FFT ocean on the GPU (OceanFFT.compute), for WaterSurface: the same wind and spectrum
    // as the Gerstner swells (WaveSpectrum), carrying on from the shortest swell down to ripples. Two tiling patches of
    // different sizes overlap so neither one's repeat shows, and the swells on top break up what is left of it. The larger
    // patch holds waves long enough for the water mesh to move with; the smaller one only shades the surface. Each patch
    // keeps its own foam where its crests fold over, fading slowly so breaking waves leave streaks.
    public sealed class OceanFFT
    {
        public const int Size = 256;
        public const int CascadeCount = 2;
        const float Gravity = 9.81f;
        const int Groups = Size / 8;

        static readonly int SpectrumId = Shader.PropertyToID("_Spectrum");
        static readonly int InitialSpectrumId = Shader.PropertyToID("_InitialSpectrum");
        static readonly int InAId = Shader.PropertyToID("_InA");
        static readonly int InBId = Shader.PropertyToID("_InB");
        static readonly int OutAId = Shader.PropertyToID("_OutA");
        static readonly int OutBId = Shader.PropertyToID("_OutB");
        static readonly int DisplacementId = Shader.PropertyToID("_Displacement");
        static readonly int SlopesId = Shader.PropertyToID("_Slopes");
        static readonly int FoamId = Shader.PropertyToID("_Foam");
        static readonly int TileSizeId = Shader.PropertyToID("_TileSize");
        static readonly int LowCutId = Shader.PropertyToID("_LowCut");
        static readonly int HighCutId = Shader.PropertyToID("_HighCut");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int PeakOmegaId = Shader.PropertyToID("_PeakOmega");
        static readonly int PeakSharpnessId = Shader.PropertyToID("_PeakSharpness");
        static readonly int PeakSpreadId = Shader.PropertyToID("_PeakSpread");
        static readonly int WindAngleId = Shader.PropertyToID("_WindAngle");
        static readonly int SpreadId = Shader.PropertyToID("_Spread");
        static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int SimulationTimeId = Shader.PropertyToID("_SimulationTime");
        static readonly int ChoppinessId = Shader.PropertyToID("_Choppiness");
        static readonly int FoamThresholdId = Shader.PropertyToID("_FoamThreshold");
        static readonly int FoamDecayId = Shader.PropertyToID("_FoamDecay");

        readonly ComputeShader shader;
        readonly int initKernel;
        readonly int evolveKernel;
        readonly int rowsKernel;
        readonly int columnsKernel;
        readonly int assembleKernel;
        readonly RenderTexture[] spectra = new RenderTexture[CascadeCount];
        readonly RenderTexture[] displacements = new RenderTexture[CascadeCount];
        readonly RenderTexture[] slopes = new RenderTexture[CascadeCount];
        readonly RenderTexture[] foam = new RenderTexture[CascadeCount];
        // Shared by both patches: the packed fields before and after each half of the transform.
        readonly RenderTexture fieldsA;
        readonly RenderTexture fieldsB;
        readonly RenderTexture rowsA;
        readonly RenderTexture rowsB;
        readonly float[] tileSizes = new float[CascadeCount];
        float lastTime = float.NaN;

        // The most the larger patch lifts or shifts the surface (m).
        public float Reach { get; private set; }

        public float TileSize(int cascade) => tileSizes[cascade];
        // xyz: how far each point moves, w: foam.
        public RenderTexture Displacement(int cascade) => displacements[cascade];
        // xy: the surface's slopes, z: the Jacobian minus 1.
        public RenderTexture Slopes(int cascade) => slopes[cascade];
        // xy: each wave's starting amplitude.
        public RenderTexture InitialSpectrum(int cascade) => spectra[cascade];

        public OceanFFT(ComputeShader shader)
        {
            this.shader = shader;
            initKernel = shader.FindKernel("InitSpectrum");
            evolveKernel = shader.FindKernel("EvolveSpectrum");
            rowsKernel = shader.FindKernel("FFTRows");
            columnsKernel = shader.FindKernel("FFTColumns");
            assembleKernel = shader.FindKernel("Assemble");

            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                spectra[cascade] = CreateTexture(RenderTextureFormat.ARGBFloat, false);
                displacements[cascade] = CreateTexture(RenderTextureFormat.ARGBHalf, true);
                slopes[cascade] = CreateTexture(RenderTextureFormat.ARGBHalf, true);
                foam[cascade] = CreateTexture(RenderTextureFormat.RFloat, false);
            }

            fieldsA = CreateTexture(RenderTextureFormat.ARGBFloat, false);
            fieldsB = CreateTexture(RenderTextureFormat.ARGBFloat, false);
            rowsA = CreateTexture(RenderTextureFormat.ARGBFloat, false);
            rowsB = CreateTexture(RenderTextureFormat.ARGBFloat, false);
        }

        // Draws the patches' waves for the sea settings describes, carrying on from the shortest of the Gerstner swells
        // (built from swells), and clears their foam. Waves longer than meshWaveLength go in the larger patch, shorter ones
        // in the smaller.
        public void SetSpectrum(WaveSpectrum.Settings settings, WaveSpectrum.Settings swells, float meshWaveLength)
        {
            WaveSpectrum.Shape(swells, out float alpha, out float peak, out float peakSpread, out _, out float swellTop);
            float lowCut = swellTop * swellTop / Gravity;
            float meshCut = 2f * Mathf.PI / meshWaveLength;
            float highCut = 2f * Mathf.PI / Mathf.Max(settings.shortestWave, 0.01f);

            // Sizes that don't divide each other, so the two patches' repeats never line up; the larger patch holds four of
            // its longest waves.
            tileSizes[1] = 2.9f * meshWaveLength;
            tileSizes[0] = Mathf.Max(4f * 2f * Mathf.PI / lowCut, 2f * tileSizes[1]);

            shader.SetFloat(AlphaId, alpha);
            shader.SetFloat(PeakOmegaId, peak);
            shader.SetFloat(PeakSharpnessId, Mathf.Max(settings.peakSharpness, 1f));
            shader.SetFloat(PeakSpreadId, peakSpread);
            shader.SetFloat(WindAngleId, settings.windDirection * Mathf.Deg2Rad);
            shader.SetFloat(SpreadId, Mathf.Max(settings.spread, 0f));
            shader.SetFloat(HeightScaleId, Mathf.Max(settings.heightScale, 0f));
            shader.SetFloat(ChoppinessId, Mathf.Clamp01(settings.choppiness));
            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                shader.SetFloat(TileSizeId, tileSizes[cascade]);
                shader.SetFloat(LowCutId, cascade == 0 ? lowCut : meshCut);
                shader.SetFloat(HighCutId, cascade == 0 ? Mathf.Min(meshCut, highCut) : highCut);
                shader.SetInt(SeedId, settings.seed * CascadeCount + cascade);
                shader.SetTexture(initKernel, SpectrumId, spectra[cascade]);
                shader.SetTexture(initKernel, FoamId, foam[cascade]);
                shader.Dispatch(initKernel, Groups, Groups, 1);
            }

            // Past the swells the spectrum holds a height variance of alpha g^2 / (4 omega^4), at most peakSharpness times
            // that near the peak; the surface rarely moves more than four times its square root.
            Reach = 4f * Mathf.Max(settings.heightScale, 0f) *
                    Mathf.Sqrt(Mathf.Max(settings.peakSharpness, 1f) * alpha * Gravity * Gravity / (4f * Mathf.Pow(swellTop, 4f)));
        }

        // Moves the patches on to time (seconds of wave travel) and builds up foam wherever their surface is squeezed below
        // foamThreshold; foam fades to about a third every foamLife seconds.
        public void Simulate(float time, float foamThreshold, float foamLife)
        {
            float deltaTime = float.IsNaN(lastTime) ? 0f : Mathf.Clamp(time - lastTime, 0f, 0.1f);
            lastTime = time;
            shader.SetFloat(SimulationTimeId, time);
            shader.SetFloat(FoamThresholdId, foamThreshold);
            shader.SetFloat(FoamDecayId, Mathf.Exp(-deltaTime / Mathf.Max(foamLife, 0.01f)));

            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                shader.SetFloat(TileSizeId, tileSizes[cascade]);
                shader.SetTexture(evolveKernel, InitialSpectrumId, spectra[cascade]);
                shader.SetTexture(evolveKernel, OutAId, fieldsA);
                shader.SetTexture(evolveKernel, OutBId, fieldsB);
                shader.Dispatch(evolveKernel, Groups, Groups, 1);

                Transform(rowsKernel, fieldsA, fieldsB, rowsA, rowsB, 1, Size);
                Transform(columnsKernel, rowsA, rowsB, fieldsA, fieldsB, Size, 1);

                shader.SetTexture(assembleKernel, InAId, fieldsA);
                shader.SetTexture(assembleKernel, InBId, fieldsB);
                shader.SetTexture(assembleKernel, DisplacementId, displacements[cascade]);
                shader.SetTexture(assembleKernel, SlopesId, slopes[cascade]);
                shader.SetTexture(assembleKernel, FoamId, foam[cascade]);
                shader.Dispatch(assembleKernel, Groups, Groups, 1);

                // Smaller copies, so far-off water averages its ripples instead of shimmering.
                displacements[cascade].GenerateMips();
                slopes[cascade].GenerateMips();
            }
        }

        public void Dispose()
        {
            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                Release(spectra[cascade]);
                Release(displacements[cascade]);
                Release(slopes[cascade]);
                Release(foam[cascade]);
            }

            Release(fieldsA);
            Release(fieldsB);
            Release(rowsA);
            Release(rowsB);
        }

        void Transform(int kernel, RenderTexture inA, RenderTexture inB, RenderTexture outA, RenderTexture outB,
                       int groupsX, int groupsY)
        {
            shader.SetTexture(kernel, InAId, inA);
            shader.SetTexture(kernel, InBId, inB);
            shader.SetTexture(kernel, OutAId, outA);
            shader.SetTexture(kernel, OutBId, outB);
            shader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        static RenderTexture CreateTexture(RenderTextureFormat format, bool mipmaps)
        {
            RenderTexture texture = new RenderTexture(Size, Size, 0, format, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                useMipMap = mipmaps,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Point,
                anisoLevel = mipmaps ? 4 : 0,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        static void Release(RenderTexture texture)
        {
            texture.Release();
            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
