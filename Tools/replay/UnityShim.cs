// The few parts of UnityEngine that AnimalGenome.cs and NeutralReplay.cs use, so they compile and run outside Unity
// (item 59). Random follows Unity's rules: value and Range(float, float) include both ends, Range(int, int) leaves out
// the top.
using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Pow(float value, float power) => (float)Math.Pow(value, power);
    }

    public static class Random
    {
        static System.Random generator = new System.Random(0);

        public static void InitState(int seed) => generator = new System.Random(seed);

        // [0, 1], as Unity's.
        public static float value => (float)(generator.Next(0, 16777217) / 16777216.0);

        public static float Range(float minimum, float maximum) => minimum + (maximum - minimum) * value;

        public static int Range(int minimum, int maximum) =>
            maximum <= minimum ? minimum : generator.Next(minimum, maximum);
    }
}
