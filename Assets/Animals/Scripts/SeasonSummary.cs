using Boreal.World;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using System.Collections.Generic;
using System.Text;

namespace Boreal.Animals
{
    // Totals across every species at one moment, from SpeciesManager's telemetry, so what happened over a season is the
    // change between its start and its end.
    public struct EcosystemTally
    {
        // Births by reproduction, so founders and animals returning from off-screen don't count.
        public int births;
        public int sexualBirths;
        public int deaths;
        public int starvationDeaths;
        public int predationDeaths;
        public int oldAgeDeaths;
        public int coldDeaths;
        public int heatDeaths;
        public int venomDeaths;
        public int plantMeals;
        public int meatMeals;
        public int speciesEver;

        // Deaths from causes without their own count, such as drowning out of bounds.
        public int OtherDeaths => deaths - starvationDeaths - predationDeaths - oldAgeDeaths - coldDeaths - heatDeaths -
                                  venomDeaths;

        public static EcosystemTally Of(IEnumerable<SpeciesTelemetryRecord> records)
        {
            EcosystemTally tally = default;
            foreach (SpeciesTelemetryRecord record in records)
            {
                tally.births += record.reproductionEvents;
                tally.sexualBirths += record.sexualOffspring;
                tally.deaths += record.deaths;
                tally.starvationDeaths += record.starvationDeaths;
                tally.predationDeaths += record.predationDeaths;
                tally.oldAgeDeaths += record.oldAgeDeaths;
                tally.coldDeaths += record.coldExposureDeaths;
                tally.heatDeaths += record.heatExposureDeaths;
                tally.venomDeaths += record.venomDeaths;
                tally.plantMeals += record.plantMeals;
                tally.meatMeals += record.meatMeals;
                tally.speciesEver++;
            }

            return tally;
        }

        public static EcosystemTally operator -(EcosystemTally later, EcosystemTally earlier)
        {
            return new EcosystemTally
            {
                births = later.births - earlier.births,
                sexualBirths = later.sexualBirths - earlier.sexualBirths,
                deaths = later.deaths - earlier.deaths,
                starvationDeaths = later.starvationDeaths - earlier.starvationDeaths,
                predationDeaths = later.predationDeaths - earlier.predationDeaths,
                oldAgeDeaths = later.oldAgeDeaths - earlier.oldAgeDeaths,
                coldDeaths = later.coldDeaths - earlier.coldDeaths,
                heatDeaths = later.heatDeaths - earlier.heatDeaths,
                venomDeaths = later.venomDeaths - earlier.venomDeaths,
                plantMeals = later.plantMeals - earlier.plantMeals,
                meatMeals = later.meatMeals - earlier.meatMeals,
                speciesEver = later.speciesEver - earlier.speciesEver
            };
        }
    }

    // The live animals' average inherited traits that matter for getting through winter, at one moment.
    public struct WinterTraits
    {
        public int animals;
        // Share of maximum energy needed before breeding.
        public float breedingThreshold;
        public float torpor;
        public float maxEnergy;
        // The coldest Celsius the animals are comfortable at, after body parts and perks.
        public float coldestComfort;
        // The share of the animals with each perk (0 to 1), indexed by AnimalPerk, or null when not counted.
        public float[] perkShares;
    }

    // What happens over one season, written as one line for the log so runs can be compared and tuned from Unity's
    // Editor log. SeasonClock starts one at the start of each season and writes it at the end, or earlier when every
    // animal has died, a new world begins or Play stops.
    public sealed class SeasonSummary
    {
        public const string Prefix = "Season summary";

        public readonly int year;
        public readonly BorealSeason season;
        // Simulated seconds since the world began.
        public readonly float startTime;
        readonly int startAnimals;
        readonly EcosystemTally startTally;

        int lowestAnimals;
        int highestAnimals;
        double sampledSeconds;
        double shownPlantSeconds;
        double seaPlantSeconds;
        double buriedPlantSeconds;
        double growthSeconds;
        double snowSeconds;
        double celsiusShiftSeconds;
        bool hasPlants = true;
        bool hasCelsiusShift = true;

        public SeasonSummary(int year, BorealSeason season, float startTime, int animals, EcosystemTally tally)
        {
            this.year = year;
            this.season = season;
            this.startTime = startTime;
            startAnimals = animals;
            startTally = tally;
            lowestAnimals = animals;
            highestAnimals = animals;
        }

        // Adds a frame of seconds. seaPlants are the shown plants in the sea, which only waders and swimmers reach. The
        // plant counts are -1 without a food spawner. plantGrowth is how fast plants regrow against the base climate,
        // snow the season's snow amount, and celsiusShift is NaN without the terrain's temperature.
        public void Sample(float seconds, int animals, int shownPlants, int seaPlants, int buriedPlants, float plantGrowth,
                           float snow, float celsiusShift)
        {
            if (animals < lowestAnimals) lowestAnimals = animals;
            if (animals > highestAnimals) highestAnimals = animals;
            if (!(seconds > 0f)) return;

            sampledSeconds += seconds;
            if (shownPlants < 0) hasPlants = false;
            shownPlantSeconds += seconds * shownPlants;
            seaPlantSeconds += seconds * seaPlants;
            buriedPlantSeconds += seconds * buriedPlants;
            growthSeconds += seconds * plantGrowth;
            snowSeconds += seconds * snow;
            if (float.IsNaN(celsiusShift)) hasCelsiusShift = false;
            else celsiusShiftSeconds += seconds * celsiusShift;
        }

        // averageEnergy is the live animals' average share of their maximum energy, or NaN with none alive. setting
        // says whether seasons were on; ending says why the season ended early, or is empty.
        public string Describe(float endTime, int animals, int offscreenAnimals, float averageEnergy, int speciesAlive,
                               EcosystemTally endTally, WinterTraits traits, string setting, string ending)
        {
            EcosystemTally change = endTally - startTally;
            StringBuilder line = new StringBuilder();
            line.Append($"{Prefix}: year {year} {season.ToString().ToLowerInvariant()}, " +
                        $"{startTime:0}-{endTime:0} s ({setting}");
            if (!string.IsNullOrEmpty(ending)) line.Append($", {ending}");
            line.Append($"): animals {startAnimals} -> {animals}, lowest {lowestAnimals}, highest {highestAnimals}, " +
                        $"off-screen {offscreenAnimals}");
            line.Append($" | births {change.births} ({change.births - change.sexualBirths} clones, " +
                        $"{change.sexualBirths} from two parents)");
            line.Append($" | deaths {change.deaths}: starvation {change.starvationDeaths}, " +
                        $"predation {change.predationDeaths}, old age {change.oldAgeDeaths}, cold {change.coldDeaths}, " +
                        $"heat {change.heatDeaths}, venom {change.venomDeaths}, other {change.OtherDeaths}");
            line.Append($" | meals: {change.plantMeals} plant, {change.meatMeals} meat");
            line.Append(float.IsNaN(averageEnergy)
                ? " | energy at end: no live animals"
                : $" | energy at end {100f * averageEnergy:0}% on average");
            line.Append(hasPlants && sampledSeconds > 0
                ? $" | plants on average: {shownPlantSeconds / sampledSeconds:0} shown " +
                  $"({seaPlantSeconds / sampledSeconds:0} in the sea), {buriedPlantSeconds / sampledSeconds:0} under snow"
                : " | plants: unknown");
            line.Append($" | species: {speciesAlive} alive, {change.speciesEver} new");
            if (traits.animals > 0)
            {
                line.Append($" | traits on average: breeds at {100f * traits.breedingThreshold:0}% energy, " +
                            $"torpor {traits.torpor:0.00}, energy store {traits.maxEnergy:0}, " +
                            $"comfortable down to {traits.coldestComfort:0} °C");
                if (traits.perkShares != null)
                {
                    // Every perk, even at 0%, so lines from different seasons and runs line up.
                    line.Append(" | perks:");
                    for (int perk = 0; perk < traits.perkShares.Length; perk++)
                    {
                        line.Append($"{(perk == 0 ? " " : ", ")}{AnimalPerks.PerkName((AnimalPerk)perk)} " +
                                    $"{100f * traits.perkShares[perk]:0}%");
                    }
                }
            }
            if (sampledSeconds > 0)
            {
                line.Append($" | conditions on average: plants regrow at {100.0 * growthSeconds / sampledSeconds:0}%, " +
                            $"snow {100.0 * snowSeconds / sampledSeconds:0}%");
                if (hasCelsiusShift)
                {
                    double shift = celsiusShiftSeconds / sampledSeconds;
                    line.Append(shift <= -0.5 ? $", {-shift:0} °C colder"
                        : shift >= 0.5 ? $", {shift:0} °C warmer"
                        : ", usual temperature");
                }
            }

            return line.ToString();
        }
    }
}
