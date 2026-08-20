/**
 * 2D-only port of K.jpg's OpenSimplex 2, "faster" variant.
 * Trimmed from the official reference (https://github.com/KdotJPG/OpenSimplex2,
 * csharp/OpenSimplex2.cs) to just the Noise2 path used for heightmap generation;
 * constants, gradient table, and math are unmodified from the original.
 */

using System.Runtime.CompilerServices;

public static class OpenSimplex2
{
    private const long PRIME_X = 0x5205402B9270C86FL;
    private const long PRIME_Y = 0x598CD327003817B5L;
    private const long HASH_MULTIPLIER = 0x53A3F72DEEC546F5L;

    private const double SKEW_2D = 0.366025403784439;
    private const double UNSKEW_2D = -0.21132486540518713;

    private const int N_GRADS_2D_EXPONENT = 7;
    private const int N_GRADS_2D = 1 << N_GRADS_2D_EXPONENT;

    private const double NORMALIZER_2D = 0.01001634121365712;

    private const float RSQUARED_2D = 0.5f;

    /**
     * 2D Simplex noise, standard lattice orientation.
     */
    public static float Noise2(long seed, double x, double y)
    {
        // Get points for A2* lattice
        double s = SKEW_2D * (x + y);
        double xs = x + s, ys = y + s;

        return Noise2_UnskewedBase(seed, xs, ys);
    }

    /**
     * 2D Simplex noise base.
     */
    private static float Noise2_UnskewedBase(long seed, double xs, double ys)
    {
        // Get base points and offsets.
        int xsb = FastFloor(xs), ysb = FastFloor(ys);
        float xi = (float)(xs - xsb), yi = (float)(ys - ysb);

        // Prime pre-multiplication for hash.
        long xsbp = xsb * PRIME_X, ysbp = ysb * PRIME_Y;

        // Unskew.
        float t = (xi + yi) * (float)UNSKEW_2D;
        float dx0 = xi + t, dy0 = yi + t;

        // First vertex.
        float value = 0;
        float a0 = RSQUARED_2D - dx0 * dx0 - dy0 * dy0;
        if (a0 > 0)
        {
            value = (a0 * a0) * (a0 * a0) * Grad(seed, xsbp, ysbp, dx0, dy0);
        }

        // Second vertex.
        float a1 = (float)(2 * (1 + 2 * UNSKEW_2D) * (1 / UNSKEW_2D + 2)) * t + ((float)(-2 * (1 + 2 * UNSKEW_2D) * (1 + 2 * UNSKEW_2D)) + a0);
        if (a1 > 0)
        {
            float dx1 = dx0 - (float)(1 + 2 * UNSKEW_2D);
            float dy1 = dy0 - (float)(1 + 2 * UNSKEW_2D);
            value += (a1 * a1) * (a1 * a1) * Grad(seed, xsbp + PRIME_X, ysbp + PRIME_Y, dx1, dy1);
        }

        // Third vertex.
        if (dy0 > dx0)
        {
            float dx2 = dx0 - (float)UNSKEW_2D;
            float dy2 = dy0 - (float)(UNSKEW_2D + 1);
            float a2 = RSQUARED_2D - dx2 * dx2 - dy2 * dy2;
            if (a2 > 0)
            {
                value += (a2 * a2) * (a2 * a2) * Grad(seed, xsbp, ysbp + PRIME_Y, dx2, dy2);
            }
        }
        else
        {
            float dx2 = dx0 - (float)(UNSKEW_2D + 1);
            float dy2 = dy0 - (float)UNSKEW_2D;
            float a2 = RSQUARED_2D - dx2 * dx2 - dy2 * dy2;
            if (a2 > 0)
            {
                value += (a2 * a2) * (a2 * a2) * Grad(seed, xsbp + PRIME_X, ysbp, dx2, dy2);
            }
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Grad(long seed, long xsvp, long ysvp, float dx, float dy)
    {
        long hash = seed ^ xsvp ^ ysvp;
        hash *= HASH_MULTIPLIER;
        hash ^= hash >> (64 - N_GRADS_2D_EXPONENT + 1);
        int gi = (int)hash & ((N_GRADS_2D - 1) << 1);
        return GRADIENTS_2D[gi | 0] * dx + GRADIENTS_2D[gi | 1] * dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(double x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }

    private static readonly float[] GRADIENTS_2D;
    static OpenSimplex2()
    {
        GRADIENTS_2D = new float[N_GRADS_2D * 2];
        float[] grad2 = {
             0.38268343236509f,   0.923879532511287f,
             0.923879532511287f,  0.38268343236509f,
             0.923879532511287f, -0.38268343236509f,
             0.38268343236509f,  -0.923879532511287f,
            -0.38268343236509f,  -0.923879532511287f,
            -0.923879532511287f, -0.38268343236509f,
            -0.923879532511287f,  0.38268343236509f,
            -0.38268343236509f,   0.923879532511287f,
            //-------------------------------------//
             0.130526192220052f,  0.99144486137381f,
             0.608761429008721f,  0.793353340291235f,
             0.793353340291235f,  0.608761429008721f,
             0.99144486137381f,   0.130526192220051f,
             0.99144486137381f,  -0.130526192220051f,
             0.793353340291235f, -0.60876142900872f,
             0.608761429008721f, -0.793353340291235f,
             0.130526192220052f, -0.99144486137381f,
            -0.130526192220052f, -0.99144486137381f,
            -0.608761429008721f, -0.793353340291235f,
            -0.793353340291235f, -0.608761429008721f,
            -0.99144486137381f,  -0.130526192220052f,
            -0.99144486137381f,   0.130526192220051f,
            -0.793353340291235f,  0.608761429008721f,
            -0.608761429008721f,  0.793353340291235f,
            -0.130526192220052f,  0.99144486137381f,
        };
        for (int i = 0; i < grad2.Length; i++)
        {
            grad2[i] = (float)(grad2[i] / NORMALIZER_2D);
        }
        for (int i = 0, j = 0; i < GRADIENTS_2D.Length; i++, j++)
        {
            if (j == grad2.Length) j = 0;
            GRADIENTS_2D[i] = grad2[j];
        }
    }
}
