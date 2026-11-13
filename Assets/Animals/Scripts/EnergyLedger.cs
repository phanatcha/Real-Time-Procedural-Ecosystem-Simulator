using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Boreal.Animals
{
    // Where an animal's stored energy comes from and goes to. Positive flows add to the animals' stores.
    public enum EnergyFlow
    {
        // What the animals held when the ledger began, and what founders and off-screen arrivals joined with.
        Start,
        Plants,
        Meat,
        // Handed over by parents at a birth (negative), and what the young started with (positive).
        GivenToYoung,
        BornWith,
        // A newborn handed more than it can store loses the rest.
        OverCapacity,
        Upkeep,
        Growth,
        Attacks,
        // The store an animal died with (part of it goes into its carcass), or left with for an off-screen population.
        LostAtDeath,
        // A change nothing accounted for: something wrote to an animal's energy directly.
        Untracked
    }

    // A test mode (item 61) that adds up every change to the animals' stored energy, so a rule that makes energy from
    // nothing shows up as a gap. Off unless Begin is called, and then it only counts: it never changes the simulation.
    //
    // Two checks. Every animal's store must equal what the ledger last saw plus the changes it recorded (anything else
    // is logged as Untracked), so the animals' stores always add up to the flows. And three rules are checked where
    // they happen: a litter never gets more than its parents handed over, a meal never gives more than the food held,
    // and a carcass never holds more than the body's meat and the store the animal died with.
    public static class EnergyLedger
    {
        public static bool Enabled { get; private set; }
        public static int Violations { get; private set; }
        public static IReadOnlyList<string> ViolationMessages => messages;

        static readonly int FlowCount = System.Enum.GetValues(typeof(EnergyFlow)).Length;
        static readonly double[] totals = new double[FlowCount];
        static readonly List<string> messages = new List<string>();
        const int MessagesKept = 20;

        // Starts counting from the animals alive now. Call it before any of them has a child on the way.
        public static void Begin(IEnumerable<SeekFood> living)
        {
            System.Array.Clear(totals, 0, totals.Length);
            messages.Clear();
            Violations = 0;
            Enabled = true;
            if (living == null) return;
            foreach (SeekFood animal in living)
            {
                if (animal == null) continue;
                Record(EnergyFlow.Start, animal.currentEnergy);
                animal.SyncEnergyLedger();
            }
        }

        public static void End() => Enabled = false;

        public static void Record(EnergyFlow flow, double amount)
        {
            if (Enabled) totals[(int)flow] += amount;
        }

        public static double Total(EnergyFlow flow) => totals[(int)flow];

        // What the living animals should hold between them: every flow added up.
        public static double Expected
        {
            get
            {
                double sum = 0;
                for (int flow = 0; flow < FlowCount; flow++) sum += totals[flow];
                return sum;
            }
        }

        // The animals' stores less what the ledger expects. Zero, but for rounding, when every change was recorded.
        public static double Gap(IEnumerable<SeekFood> living)
        {
            double held = 0;
            foreach (SeekFood animal in living)
                if (animal != null) held += animal.currentEnergy;
            return held - Expected;
        }

        public static void Violation(string message)
        {
            if (!Enabled) return;
            Violations++;
            if (messages.Count < MessagesKept)
            {
                messages.Add(message);
                Debug.LogWarning("Energy ledger: " + message);
            }
        }

        // A litter never gets more than its parents handed over.
        public static void CheckBirth(double handedOver, double givenToYoung, string parent)
        {
            if (Enabled && givenToYoung > handedOver + Tolerance(handedOver))
                Violation($"{parent}'s young were given {Number(givenToYoung)} but the parents handed over {Number(handedOver)}");
        }

        // A meal never gives more than the food held.
        public static void CheckMeal(double rawNutrition, double gained, string eater)
        {
            if (Enabled && gained > rawNutrition + Tolerance(rawNutrition))
                Violation($"{eater} gained {Number(gained)} from food that held {Number(rawNutrition)}");
        }

        // A carcass never holds more than the body's own meat and the store the animal died with.
        public static void CheckCarcass(double carcass, double bodyMeat, double store, string animal)
        {
            if (Enabled && carcass > bodyMeat + store + Tolerance(bodyMeat + store))
                Violation($"{animal}'s carcass holds {Number(carcass)}, more than its body's {Number(bodyMeat)} and its store of {Number(store)}");
        }

        public static string Report(IEnumerable<SeekFood> living)
        {
            var text = new StringBuilder("Energy ledger: ");
            if (!Enabled && Expected == 0) return text.Append("not running").ToString();
            for (int flow = 0; flow < FlowCount; flow++)
            {
                if (totals[flow] == 0) continue;
                text.Append(((EnergyFlow)flow).ToString()).Append(' ').Append(Number(totals[flow])).Append(", ");
            }
            text.Append("gap ").Append(Gap(living).ToString("0.###", CultureInfo.InvariantCulture))
                .Append(", violations ").Append(Violations);
            return text.ToString();
        }

        static double Tolerance(double scale) => 1e-4 * System.Math.Max(1.0, System.Math.Abs(scale));
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
