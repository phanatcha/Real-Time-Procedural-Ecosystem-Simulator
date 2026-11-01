using Boreal.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // A family tree of the species (item 28), drawn from the Species Manager's records: each species is a bar from
    // when it appeared to when it died out, or to now, in its colour, under its parent, with its living members
    // after its name. Extinct species are greyed. T shows or hides it. Adds itself to scenes with a Species Manager.
    [DisallowMultipleComponent]
    public sealed class SpeciesTreeView : MonoBehaviour
    {
        public bool visible;
        [Min(200f)] public float width = 520f;
        [Min(10f)] public float rowHeight = 18f;

        GUIStyle labelStyle;
        GUIStyle boxStyle;
        readonly List<SpeciesTelemetryRecord> ordered = new List<SpeciesTelemetryRecord>();
        readonly Dictionary<string, List<SpeciesTelemetryRecord>> children = new Dictionary<string, List<SpeciesTelemetryRecord>>();
        readonly Dictionary<string, int> rows = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<SpeciesTreeView>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Species Tree").AddComponent<SpeciesTreeView>();
            }
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || GodCamera.IsTypingInUI()) return;
            if (keyboard.tKey.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (!visible || manager == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, clipping = TextClipping.Clip };
                labelStyle.normal.textColor = Color.white;
                boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };
            }

            Order(manager);
            float now = Mathf.Max(1f, Time.time - manager.WorldStartTime);
            float height = 30f + Mathf.Max(1, ordered.Count) * rowHeight + 22f;
            Rect panel = new Rect(10f, Screen.height - height - 10f, width, height);
            GUI.Box(panel, $"<b>Species tree</b>   {manager.SpeciesAliveCount} alive of {ordered.Count}   T: hide", boxStyle);
            if (ordered.Count == 0) return;

            float left = panel.x + 10f;
            float plotWidth = panel.width - 20f;
            float top = panel.y + 30f;
            Color previous = GUI.color;
            Texture2D white = Texture2D.whiteTexture;
            // A faint axis with the time span, so the bars read as a timeline.
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(left, top + ordered.Count * rowHeight + 2f, plotWidth, 1f), white);
            GUI.color = previous;
            GUI.Label(new Rect(left, top + ordered.Count * rowHeight + 4f, plotWidth, 16f),
                      $"0 s{new string(' ', 8)}…{new string(' ', 8)}{now:0} s simulated");

            foreach (SpeciesTelemetryRecord record in ordered)
            {
                int row = rows[record.speciesName];
                float y = top + row * rowHeight;
                float start = left + plotWidth * Mathf.Clamp01(record.originTime / now);
                float end = left + plotWidth * (record.extinctionTime >= 0f ? Mathf.Clamp01(record.extinctionTime / now) : 1f);
                bool extinct = record.extinctionTime >= 0f;
                Color colour = extinct ? new Color(0.55f, 0.55f, 0.55f, 0.9f) : record.color;

                // A connector down from the parent's bar to where this species split off.
                if (!string.IsNullOrEmpty(record.parentSpeciesName) && rows.TryGetValue(record.parentSpeciesName, out int parentRow))
                {
                    float parentY = top + parentRow * rowHeight + rowHeight * 0.5f;
                    GUI.color = new Color(colour.r, colour.g, colour.b, 0.6f);
                    GUI.DrawTexture(new Rect(start, Mathf.Min(parentY, y + rowHeight * 0.5f), 1f, Mathf.Abs(y + rowHeight * 0.5f - parentY)), white);
                }

                GUI.color = colour;
                GUI.DrawTexture(new Rect(start, y + rowHeight * 0.3f, Mathf.Max(2f, end - start), rowHeight * 0.4f), white);
                GUI.color = previous;

                manager.SpeciesPopulation.TryGetValue(record.speciesName, out int living);
                string label = extinct
                    ? $"{record.speciesName}  extinct at {record.extinctionTime:0} s"
                    : $"{record.speciesName}  {living} alive, {Lower(record.dietClassification)}";
                float labelX = end + 6f;
                float labelWidth = panel.xMax - 6f - labelX;
                if (labelWidth < 90f)
                {
                    labelWidth = 220f;
                    labelX = Mathf.Max(left, start - labelWidth - 6f);
                }
                GUI.Label(new Rect(labelX, y, labelWidth, rowHeight), label, labelStyle);
            }
        }

        // Species in family order: each parent followed by its children, oldest first, so the tree reads top-down.
        void Order(SpeciesManager manager)
        {
            ordered.Clear();
            rows.Clear();
            children.Clear();
            List<SpeciesTelemetryRecord> roots = new List<SpeciesTelemetryRecord>();
            foreach (SpeciesTelemetryRecord record in manager.Telemetry.Values)
            {
                string parent = record.parentSpeciesName;
                if (string.IsNullOrEmpty(parent) || !manager.Telemetry.ContainsKey(parent) || parent == record.speciesName)
                {
                    roots.Add(record);
                    continue;
                }
                if (!children.TryGetValue(parent, out List<SpeciesTelemetryRecord> list)) children[parent] = list = new List<SpeciesTelemetryRecord>();
                list.Add(record);
            }
            roots.Sort((a, b) => a.originTime.CompareTo(b.originTime));
            foreach (SpeciesTelemetryRecord root in roots) Visit(root, 0);
        }

        void Visit(SpeciesTelemetryRecord record, int depth)
        {
            if (rows.ContainsKey(record.speciesName) || depth > 64) return;
            rows[record.speciesName] = ordered.Count;
            ordered.Add(record);
            if (!children.TryGetValue(record.speciesName, out List<SpeciesTelemetryRecord> list)) return;
            list.Sort((a, b) => a.originTime.CompareTo(b.originTime));
            foreach (SpeciesTelemetryRecord child in list) Visit(child, depth + 1);
        }

        static string Lower(string value) => string.IsNullOrEmpty(value) ? "" : char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
