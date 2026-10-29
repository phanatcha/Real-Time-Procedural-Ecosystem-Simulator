using System;
using System.Globalization;

namespace Boreal.Animals
{
    public static class CsvEncoding
    {
        public static string Cell(object value)
        {
            string text = value is IFormattable number
                ? number.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

    }
    // Welford's algorithm, with population standard deviation (the entire living population is sampled).
    public struct RunningStatistics
    {
        public int Count { get; private set; }
        public double Mean { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        double squaredDeviation;
        public double StandardDeviation => Count == 0 ? 0 : Math.Sqrt(Math.Max(0, squaredDeviation / Count));
        public void Add(double value)
        {
            Count++;
            if (Count == 1) Minimum = Maximum = value;
            Minimum = Math.Min(Minimum, value); Maximum = Math.Max(Maximum, value);
            double delta = value - Mean;
            Mean += delta / Count;
            squaredDeviation += delta * (value - Mean);
        }
    }

    public sealed class SimulationSampleSchedule
    {
        double next;
        public void Reset() => next = 0;
        public bool Take(double time, double interval)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < next) return false;
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval));
            next = (Math.Floor(time / interval) + 1) * interval;
            return true;
        }
    }
}
