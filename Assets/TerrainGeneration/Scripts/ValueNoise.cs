using UnityEngine;

namespace Boreal.TerrainGeneration
{
    public static class ValueNoise
    {
        // Integer cell hashing avoids spacing resonances and preserves every bit of the world seed.
        // Each purpose gets an independent stream. Top 24 bits map exactly to floats in [0, 1).
        public static float CellHash(int x, int z, int seed, int type, int stream)
        {
            unchecked
            {
                uint h = Mix((uint)seed ^ 0x9e3779b9u);
                h = Mix(h ^ (uint)x);
                h = Mix(h ^ (uint)z);
                h = Mix(h ^ (uint)type);
                h = Mix(h ^ (uint)stream);
                return (h >> 8) * (1f / 16777216f);
            }
        }

        static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x7feb352du;
                h ^= h >> 15; h *= 0x846ca68bu;
                return h ^ (h >> 16);
            }
        }

        static float Frac(float x)
        {
            return x - Mathf.Floor(x);
        }

        public static float Hash21(Vector2 p)
        {
            p = new Vector2(Frac(p.x * 123.34f), Frac(p.y * 456.21f));
            float d = Vector2.Dot(p, p + new Vector2(45.32f, 45.32f));
            p += new Vector2(d, d);
            return Frac(p.x * p.y);
        }

        public static float Sample(Vector2 p)
        {
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = new Vector2(p.x - i.x, p.y - i.y);

            float a = Hash21(i);
            float b = Hash21(i + new Vector2(1, 0));
            float c = Hash21(i + new Vector2(0, 1));
            float d = Hash21(i + new Vector2(1, 1));

            Vector2 u = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));
            return Mathf.Lerp(a, b, u.x) + (c - a) * u.y * (1f - u.x) + (d - b) * u.x * u.y;
        }
    }
}
