using System;
using System.Collections.Generic;
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

    // Counts the genomes sitting at a gene's lowest or highest allowed value (item 54). A gene with many animals at
    // a limit can't move further that way, so its average understates the pressure on it.
    public static class GeneLimits
    {
        public static void Count(IEnumerable<AnimalGenome> genomes, AnimalGene gene, out int atMinimum, out int atMaximum)
        {
            atMinimum = atMaximum = 0;
            if (genomes == null) return;
            AnimalGeneDefinition definition = AnimalGenome.GetDefinition(gene);
            foreach (AnimalGenome genome in genomes)
            {
                // Mutation clamps to the limits exactly, so no tolerance is needed.
                float value = genome[gene];
                if (value <= definition.minimum) atMinimum++;
                else if (value >= definition.maximum) atMaximum++;
            }
        }
    }

    // One row of lives.csv per animal (item 57): who its parents were, when and where it lived, how it ended, how
    // many young it had and the genome it was born with. Parent IDs link the rows into the run's full family tree.
    public static class LifeRecords
    {
        public const string StillAlive = "alive";
        public const string LeftForOffscreen = "left";

        public static string Header()
        {
            var header = new System.Text.StringBuilder(
                "id,first_parent,second_parent,species,generation,origin,born,ended,age,cause," +
                "birth_x,birth_z,end_x,end_z,young,harmful_mutations");
            for (int perk = 0; perk < AnimalGenome.PerkCount; perk++) header.Append(",Perk_").Append((AnimalPerk)perk);
            for (int gene = 0; gene < AnimalGenome.GeneCount; gene++) header.Append(',').Append((AnimalGene)gene);
            for (int site = 0; site < AnimalGenome.SiteCount; site++) header.Append(",Part_").Append((BodySite)site);
            return header.ToString();
        }

        // ended is blank while the animal lives; a parent ID of 0 (none) is written blank. Positions are world x and z.
        public static List<object> Cells(int id, int firstParent, int secondParent, string species, int generation,
                                         string origin, double born, double? ended, double age, string cause,
                                         double birthX, double birthZ, double endX, double endZ, int young,
                                         AnimalGenome genome)
        {
            var cells = new List<object>
            {
                id, firstParent > 0 ? (object)firstParent : "", secondParent > 0 ? (object)secondParent : "", species,
                generation, origin, born, ended.HasValue ? (object)ended.Value : "", age, cause,
                birthX, birthZ, endX, endZ, young
            };
            bool valid = genome != null && genome.IsValid;
            cells.Add(valid ? (object)genome.HarmfulMutations : "");
            for (int perk = 0; perk < AnimalGenome.PerkCount; perk++)
                cells.Add(valid ? (object)(genome.HasPerk((AnimalPerk)perk) ? 1 : 0) : "");
            for (int gene = 0; gene < AnimalGenome.GeneCount; gene++)
                cells.Add(valid ? (object)genome[(AnimalGene)gene] : "");
            for (int site = 0; site < AnimalGenome.SiteCount; site++)
            {
                BodyPartType part = valid ? genome.GetPartType((BodySite)site) : BodyPartType.None;
                // "Legs:0.35"; blank where nothing grows.
                cells.Add(part == BodyPartType.None ? "" : part + ":" +
                          genome.GetPartSize((BodySite)site).ToString("0.###", CultureInfo.InvariantCulture));
            }
            return cells;
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
