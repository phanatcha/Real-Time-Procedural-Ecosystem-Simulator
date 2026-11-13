using System.Collections.Generic;
using System.IO;
using Boreal.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // The family lines as a Muller plot (item 65): M shows or hides a panel where each founder's line, and each branch
    // that grew big inside it, is a coloured band as tall as its share of the animals, nested inside the band it grew
    // from, from the world's start to now. Lines that began with a perk gain are striped. Below it, the biggest lines
    // with their share now, their peak and the perks most of them carried. It keeps every animal's life (ID, first
    // parent, birth, end and perks) from the Species Manager as they join and die, and redraws every couple of seconds
    // while shown. Adds itself to scenes with a Species Manager. Tools/muller_plot.py draws the same from a recording.
    [DisallowMultipleComponent]
    public sealed class FamilyLinesView : MonoBehaviour
    {
        public static FamilyLinesView Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<FamilyLinesView>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Family Lines").AddComponent<FamilyLinesView>();
            }
        }

        public bool visible;
        public FamilyLineRules rules = new FamilyLineRules();
        [Tooltip("Times the chart samples between the world's start and now.")]
        [Range(20, 400)] public int samples = 160;
        [Tooltip("Real seconds between redraws while the panel is shown.")]
        [Min(0.2f)] public float refreshSeconds = 2f;
        [Min(200)] public int chartWidth = 640;
        [Min(60)] public int chartHeight = 200;
        [Range(1, 16)] public int legendLines = 8;

        readonly List<LifeSpan> lives = new List<LifeSpan>();
        readonly Dictionary<int, int> byId = new Dictionary<int, int>();
        SpeciesManager watched;
        int worldRevision = -1;
        float scanTimer;
        float refreshTimer;
        Texture2D chart;
        Color32[] pixels;
        FamilyLineChart latest;
        Color[] colours = new Color[0];
        readonly List<string> legend = new List<string>();
        readonly List<Color> legendColours = new List<Color>();
        GUIStyle boxStyle, labelStyle;
        float drawnUntil;

        public IReadOnlyList<LifeSpan> Lives => lives;
        public FamilyLineChart Latest => latest;

        void OnEnable()
        {
            if (Active == null) Active = this;
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            Watch(null);
        }

        void OnDestroy()
        {
            if (chart != null) Destroy(chart);
        }

        void Update()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager != watched) Watch(manager);
            if (manager == null) return;
            if (manager.WorldRevision != worldRevision)
            {
                worldRevision = manager.WorldRevision;
                lives.Clear();
                byId.Clear();
                latest = null;
            }

            // Newborns and arrivals are noted every couple of simulated seconds; deaths as they happen.
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 2f;
                foreach (SeekFood animal in manager.ActiveAgents) Note(animal);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && !GodCamera.IsTypingInUI() && keyboard.mKey.wasPressedThisFrame)
            {
                visible = !visible;
                refreshTimer = 0f;
            }
            if (!visible) return;
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = refreshSeconds;
                Redraw(manager);
            }
        }

        void Watch(SpeciesManager manager)
        {
            if (watched != null)
            {
                watched.AnimalDied -= OnDied;
                watched.AnimalLeft -= OnLeft;
            }
            watched = manager;
            if (watched != null)
            {
                watched.AnimalDied += OnDied;
                watched.AnimalLeft += OnLeft;
            }
        }

        void OnDied(SeekFood animal, AgentDeathCause cause) => End(animal);
        void OnLeft(SeekFood animal) => End(animal);

        void End(SeekFood animal)
        {
            int index = Note(animal);
            if (index < 0) return;
            LifeSpan life = lives[index];
            life.ended = Now();
            lives[index] = life;
        }

        int Note(SeekFood animal)
        {
            if (animal == null || animal.AnimalId <= 0) return -1;
            if (byId.TryGetValue(animal.AnimalId, out int index)) return index;
            int perks = 0;
            AnimalGenome genome = animal.Genome;
            if (genome != null && genome.IsValid)
            {
                for (int p = 0; p < AnimalGenome.PerkCount; p++) if (genome.HasPerk((AnimalPerk)p)) perks |= 1 << p;
            }
            lives.Add(new LifeSpan
            {
                id = animal.AnimalId, parent = animal.FirstParentId, born = animal.BirthTime,
                ended = double.PositiveInfinity, perks = perks
            });
            byId[animal.AnimalId] = lives.Count - 1;
            return lives.Count - 1;
        }

        static float Now()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            return manager != null ? Mathf.Max(0f, Time.time - manager.WorldStartTime) : Time.time;
        }

        // Rebuilds the lines from the lives so far and paints the chart.
        public void Redraw(SpeciesManager manager)
        {
            if (manager != null)
            {
                foreach (SeekFood animal in manager.ActiveAgents) Note(animal);
            }
            float now = Now();
            if (now <= 0f || lives.Count == 0) return;
            double[] times = new double[Mathf.Max(2, samples)];
            for (int s = 0; s < times.Length; s++) times[s] = now * s / (times.Length - 1);
            latest = FamilyLineChart.Build(lives, times, rules);
            drawnUntil = now;
            Paint();
            BuildLegend();
        }

        void Paint()
        {
            int width = Mathf.Max(8, chartWidth), height = Mathf.Max(8, chartHeight);
            if (chart == null || chart.width != width || chart.height != height)
            {
                if (chart != null) Destroy(chart);
                chart = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Family Lines", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point
                };
                pixels = new Color32[width * height];
            }
            int lineCount = latest.lines.Count;
            colours = LineColours(latest);
            Color32 empty = new Color32(24, 28, 26, 230);
            float[] lower = new float[lineCount], upper = new float[lineCount];
            int sampleCount = latest.times.Length;
            for (int x = 0; x < width; x++)
            {
                int sample = Mathf.Clamp(Mathf.RoundToInt(x * (sampleCount - 1) / (float)(width - 1)), 0, sampleCount - 1);
                for (int y = 0; y < height; y++) pixels[y * width + x] = empty;
                if (latest.totals[sample] <= 0) continue;
                latest.Bands(sample, lower, upper);
                for (int i = 0; i < lineCount; i++)
                {
                    int from = Mathf.RoundToInt(lower[i] * height), to = Mathf.RoundToInt(upper[i] * height);
                    if (to <= from) continue;
                    Color colour = colours[i];
                    bool striped = latest.lines[i].perk >= 0;
                    for (int y = from; y < to && y < height; y++)
                    {
                        Color pixel = striped && (x + y) / 3 % 2 == 0 ? colour * 0.62f : colour;
                        pixel.a = 1f;
                        pixels[y * width + x] = pixel;
                    }
                }
            }
            chart.SetPixels32(pixels);
            chart.Apply(false);
        }

        // Founders' lines get well-separated hues; a branch shades gently away from its parent's colour (hue turned a
        // little per sibling, a little lighter or darker by depth), so a family reads as one family and a chain of
        // takeovers as one band. The same colours as Tools/muller_plot.py.
        static Color[] LineColours(FamilyLineChart chart)
        {
            int count = chart.lines.Count;
            Color[] result = new Color[count];
            float[] hues = new float[count], drift = new float[count], saturations = new float[count], values = new float[count];
            int founders = 0;
            for (int i = 0; i < count; i++)
            {
                FamilyLine line = chart.lines[i];
                if (line.parent < 0)
                {
                    hues[i] = Mathf.Repeat(0.58f + founders++ * 0.381966f, 1f);
                    saturations[i] = 0.55f;
                    values[i] = 0.8f;
                }
                else
                {
                    int p = line.parent;
                    int sibling = chart.lines[p].children.IndexOf(i);
                    // The turn from the founder's hue fades with depth, so a family never wanders more than about 0.12.
                    drift[i] = drift[p] * 0.6f + (sibling + 1) * 0.05f * (sibling % 2 == 0 ? 1f : -1f);
                    int founder = p;
                    while (chart.lines[founder].parent >= 0) founder = chart.lines[founder].parent;
                    hues[i] = Mathf.Repeat(hues[founder] + drift[i], 1f);
                    saturations[i] = Mathf.Clamp(saturations[p] + (line.depth % 3 == 1 ? 0.06f : -0.04f), 0.3f, 0.8f);
                    values[i] = Mathf.Clamp(values[p] + (line.depth % 3 != 2 ? 0.07f : -0.12f), 0.5f, 0.95f);
                }
                result[i] = Color.HSVToRGB(hues[i], saturations[i], values[i]);
            }
            return result;
        }

        // Whether a line had become (nearly) all of its parent line by the last sample the parent line had animals: its
        // parent's only branch line, with the parent keeping under 5% of its animals outside it. Such a line is listed
        // with its parent, as one chain. The same rule as Tools/muller_plot.py.
        static bool TookOver(FamilyLineChart chart, int index, int last)
        {
            FamilyLine line = chart.lines[index];
            if (line.parent < 0) return false;
            FamilyLine parent = chart.lines[line.parent];
            if (parent.children.Count != 1) return false;
            int alive = last;
            while (alive >= 0 && parent.clade[alive] <= 0) alive--;
            return alive >= 0 && line.clade[alive] >= 0.95f * parent.clade[alive];
        }

        void BuildLegend()
        {
            legend.Clear();
            legendColours.Clear();
            int last = latest.times.Length - 1;
            int total = Mathf.Max(1, latest.totals[last]);
            float year = YearLength();
            List<int> ranked = new List<int>();
            for (int i = 0; i < latest.lines.Count; i++) ranked.Add(i);
            ranked.Sort((a, b) =>
            {
                int byNow = latest.lines[b].clade[last].CompareTo(latest.lines[a].clade[last]);
                return byNow != 0 ? byNow : latest.lines[b].peakShare.CompareTo(latest.lines[a].peakShare);
            });
            foreach (int i in ranked)
            {
                if (legend.Count >= legendLines) break;
                if (TookOver(latest, i, last)) continue;
                FamilyLine line = latest.lines[i];
                string names = line.name;
                for (int j = i; latest.lines[j].children.Count == 1 && TookOver(latest, latest.lines[j].children[0], last);)
                {
                    j = latest.lines[j].children[0];
                    names += " → " + latest.lines[j].name;
                }
                string from = line.founder ? "founder" : $"from {latest.lines[line.parent].name}, year {line.born / year + 1:0.0}";
                string perk = line.perk >= 0 ? $", began with {AnimalPerks.PerkName((AnimalPerk)line.perk)}" : "";
                string carried = "";
                for (int p = 0; p < AnimalGenome.PerkCount; p++)
                {
                    if (line.perkShares[p] >= 0.5f) carried += $", {AnimalPerks.PerkName((AnimalPerk)p)} {line.perkShares[p]:P0}";
                }
                string peak = line.peakShare > 0f ? $"peak {100f * line.peakShare:0}%" : "died out while the animals were few";
                legend.Add($"{names} ({from}{perk}): {100f * line.clade[last] / total:0}% now, {peak}{carried}");
                legendColours.Add(colours[i]);
            }
        }

        static float YearLength()
        {
            SeasonClock clock = SeasonClock.Active;
            return clock != null ? Mathf.Max(1f, clock.yearLength) : 400f;
        }

        // Paints the chart as it is now and saves it as a PNG, with the legend in a text file beside it, for soak
        // runs. Returns false with no animals recorded.
        public bool SaveChart(string path)
        {
            Redraw(SpeciesManager.Instance);
            if (latest == null || chart == null) return false;
            File.WriteAllBytes(path, chart.EncodeToPNG());
            File.WriteAllLines(Path.ChangeExtension(path, ".txt"), legend);
            return true;
        }

        void OnGUI()
        {
            if (!visible || SpeciesManager.Instance == null) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, clipping = TextClipping.Clip };
                labelStyle.normal.textColor = Color.white;
            }
            int lines = latest != null ? Mathf.Min(legend.Count, legendLines) : 0;
            float width = chartWidth + 20f;
            float height = 30f + chartHeight + 20f + lines * 16f + 8f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 10f, width, height);
            string title = latest == null
                ? "<b>Family lines</b>   gathering animals…   M: hide"
                : $"<b>Family lines</b>   {latest.lines.Count} lines, Muller plot of each line's share of the animals   M: hide";
            GUI.Box(panel, title, boxStyle);
            if (latest == null || chart == null) return;

            Rect plot = new Rect(panel.x + 10f, panel.y + 28f, chartWidth, chartHeight);
            GUI.DrawTexture(plot, chart);
            float year = YearLength();
            Color previous = GUI.color;
            // A faint tick at the start of every simulated year, with the year below.
            int years = Mathf.FloorToInt(drawnUntil / year);
            for (int y = 1; y <= years; y++)
            {
                float x = plot.x + plot.width * (y * year / Mathf.Max(1f, drawnUntil));
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(x, plot.y, 1f, plot.height), Texture2D.whiteTexture);
                GUI.color = previous;
                if (years <= 30 || y % 5 == 0) GUI.Label(new Rect(x - 12f, plot.yMax + 1f, 40f, 16f), $"y{y + 1}", labelStyle);
            }
            GUI.Label(new Rect(plot.x, plot.yMax + 1f, 60f, 16f), "start", labelStyle);

            float rowY = plot.yMax + 20f;
            for (int i = 0; i < lines; i++)
            {
                GUI.color = legendColours[i];
                GUI.DrawTexture(new Rect(plot.x, rowY + i * 16f + 4f, 10f, 9f), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(new Rect(plot.x + 16f, rowY + i * 16f, plot.width - 16f, 16f), legend[i], labelStyle);
            }
        }
    }
}
