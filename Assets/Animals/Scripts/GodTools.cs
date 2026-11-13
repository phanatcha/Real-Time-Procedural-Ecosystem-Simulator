using Boreal.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // God tools (item 31): a drought, dropped food, a mass extinction, a burst of mutation and a wildfire (item 79), from
    // buttons on a panel (K shows or hides it) or scheduled at a simulated time for experiments. Every use is recorded
    // as an event with the Species Manager, which the run recorder saves. Adds itself to scenes with a Species Manager.
    [DisallowMultipleComponent]
    public sealed class GodTools : MonoBehaviour
    {
        public enum Kind { Drought, Food, Extinction, Mutation, Fire }

        // One scheduled or running intervention. Duration and amount mean different things per kind: a drought
        // multiplies plant regrowth by amount for duration seconds; food drops amount plants; an extinction kills
        // the share amount of the animals; mutation multiplies every mutation chance by amount for duration seconds; a
        // fire is a wildfire with amount ignition points that burns out by itself, or is put out after duration seconds
        // if that is above 0 (item 79).
        [Serializable]
        public struct Intervention
        {
            public Kind kind;
            public float time;
            public float duration;
            public float amount;

            public override string ToString()
            {
                string what = kind switch
                {
                    Kind.Drought => $"drought: plants regrow at {amount * 100f:0}% for {duration:0} s",
                    Kind.Food => $"{amount:0} plants dropped",
                    Kind.Extinction => $"mass extinction: {amount * 100f:0}% of the animals",
                    Kind.Fire => $"wildfire with {amount:0} ignition point{(amount >= 1.5f ? "s" : "")}" +
                                 (duration > 0f ? $", put out after {duration:0} s" : ""),
                    _ => $"mutation chances x{amount:0.#} for {duration:0} s"
                };
                return $"{what} at {time:0} s";
            }
        }

        public bool visible;
        [Tooltip("Metres around the point in front of the camera that dropped food lands in.")]
        [Min(5f)] public float foodDropRadius = 40f;
        [Tooltip("Metres in front of the camera where the panel's wildfire button lights a fire.")]
        [Min(5f)] public float fireDistance = 80f;

        readonly List<Intervention> scheduled = new List<Intervention>();
        float droughtEndsAt = -1f;
        float mutationEndsAt = -1f;
        int lastWorldRevision = -1;
        GUIStyle boxStyle;
        FoodSpawner foodSpawner;

        public IReadOnlyList<Intervention> Scheduled => scheduled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<GodTools>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("God Tools").AddComponent<GodTools>();
            }
        }

        // "drought@800:200x0.2" is a drought at 800 s lasting 200 s with regrowth at 20%; "food@600x30" drops 30 plants
        // at 600 s; "extinction@1200x0.5" kills half the animals at 1200 s; "mutation@400:300x4" quadruples mutation
        // chances from 400 s for 300 s; "fire@900" (or "wildfire@900") lights a wildfire at 900 s among the animals that
        // burns out by itself, "fire@900:120x2" one with two ignition points put out after 120 s (item 79). Durations
        // and amounts have defaults. Returns false for anything else.
        public static bool TryParse(string spec, out Intervention intervention)
        {
            intervention = default;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            string[] atParts = spec.Trim().Split('@');
            if (atParts.Length != 2) return false;
            string name = atParts[0].Trim();
            if (string.Equals(name, "wildfire", StringComparison.OrdinalIgnoreCase)) name = nameof(Kind.Fire);
            if (int.TryParse(name, out _) || !Enum.TryParse(name, true, out Kind kind) || !Enum.IsDefined(typeof(Kind), kind))
                return false;

            string rest = atParts[1];
            float amount = kind switch
            {
                Kind.Drought => 0.2f, Kind.Food => 30f, Kind.Extinction => 0.5f, Kind.Fire => 1f, _ => 4f
            };
            float duration = kind == Kind.Drought ? 200f : kind == Kind.Mutation ? 300f : 0f;
            int x = rest.IndexOf('x');
            if (x >= 0)
            {
                if (!float.TryParse(rest.Substring(x + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return false;
                rest = rest.Substring(0, x);
            }
            string[] timeParts = rest.Split(':');
            if (timeParts.Length > 2 ||
                !float.TryParse(timeParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float time) || time < 0f) return false;
            if (timeParts.Length == 2 &&
                !float.TryParse(timeParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out duration)) return false;
            if (amount < 0f || duration < 0f) return false;
            if (kind == Kind.Extinction) amount = Mathf.Clamp01(amount);
            if (kind == Kind.Fire) amount = Mathf.Clamp(Mathf.Round(amount), 1f, 20f);

            intervention = new Intervention { kind = kind, time = time, duration = duration, amount = amount };
            return true;
        }

        // Schedules every comma-separated spec; returns how many were understood.
        public int ScheduleAll(string specs)
        {
            int count = 0;
            foreach (string spec in (specs ?? "").Split(','))
            {
                if (!TryParse(spec, out Intervention intervention)) continue;
                scheduled.Add(intervention);
                count++;
            }
            scheduled.Sort((a, b) => a.time.CompareTo(b.time));
            return count;
        }

        public void Apply(Kind kind, float duration, float amount)
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == null) return;
            float now = Mathf.Max(0f, Time.time - manager.WorldStartTime);
            Apply(new Intervention { kind = kind, time = now, duration = duration, amount = amount }, manager);
        }

        void Apply(Intervention intervention, SpeciesManager manager)
        {
            if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
            string detail = intervention.ToString();
            switch (intervention.kind)
            {
                case Kind.Drought:
                    if (foodSpawner == null) return;
                    foodSpawner.growthMultiplier = Mathf.Max(0f, intervention.amount);
                    droughtEndsAt = Time.time + intervention.duration;
                    break;
                case Kind.Food:
                    if (foodSpawner == null) return;
                    Camera camera = Camera.main;
                    Vector3 centre = camera != null
                        ? camera.transform.position + Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized * foodDropRadius
                        : Vector3.zero;
                    int placed = foodSpawner.DropPlants(centre, foodDropRadius, Mathf.RoundToInt(intervention.amount));
                    detail = $"{placed} plants dropped at x {centre.x:0} z {centre.z:0} at {intervention.time:0} s";
                    break;
                case Kind.Extinction:
                    List<SeekFood> animals = new List<SeekFood>(manager.ActiveAgents);
                    int toKill = Mathf.RoundToInt(animals.Count * Mathf.Clamp01(intervention.amount));
                    // A random share, so no species or place is spared on purpose.
                    for (int index = 0; index < toKill && animals.Count > 0; index++)
                    {
                        int pick = UnityEngine.Random.Range(0, animals.Count);
                        if (animals[pick] != null) animals[pick].Kill(AgentDeathCause.Other);
                        animals.RemoveAt(pick);
                    }
                    detail = $"mass extinction: {toKill} of {toKill + animals.Count} animals killed at {intervention.time:0} s";
                    break;
                case Kind.Mutation:
                    manager.mutationMultiplier = Mathf.Max(0f, intervention.amount);
                    mutationEndsAt = Time.time + intervention.duration;
                    break;
                case Kind.Fire:
                    // The natural event director records the fire itself, with where and how it started.
                    NaturalEventDirector director = NaturalEventDirector.Active;
                    Vector3 at = fireAtCamera ? FireInFrontOfCamera() : FireAmongTheAnimals(manager);
                    fireAtCamera = false;
                    NaturalEvent fire = director != null
                        ? director.Force("wildfire", at, intervention.amount, intervention.duration, "god tool")
                        : null;
                    if (fire == null)
                    {
                        manager.RecordEvent("fire", $"nothing caught fire at x {at.x:0} z {at.z:0} at {intervention.time:0} s");
                    }
                    return;
            }
            manager.RecordEvent(intervention.kind.ToString().ToLowerInvariant(), detail);
        }

        bool fireAtCamera;

        // The point the panel's wildfire button aims at.
        Vector3 FireInFrontOfCamera()
        {
            Camera camera = Camera.main;
            return camera != null
                ? camera.transform.position + Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized * fireDistance
                : Vector3.zero;
        }

        // A scheduled fire starts beside the live animal nearest the animals' average position, so it reaches them in
        // every replicate of an experiment.
        static Vector3 FireAmongTheAnimals(SpeciesManager manager)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.IsInWater) continue;
                sum += animal.transform.position;
                count++;
            }
            if (count == 0)
            {
                Camera camera = Camera.main;
                return camera != null ? camera.transform.position : Vector3.zero;
            }
            Vector3 middle = sum / count;
            Vector3 best = middle;
            float bestDistance = float.PositiveInfinity;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.IsInWater) continue;
                float distance = (animal.transform.position - middle).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = animal.transform.position;
                }
            }
            return best;
        }

        // Puts out every burning wildfire.
        public void PutOutFires()
        {
            NaturalEventDirector director = NaturalEventDirector.Active;
            if (director == null) return;
            int stopped = director.StopAll("put out by god tools");
            if (stopped > 0 && SpeciesManager.Instance != null)
                SpeciesManager.Instance.RecordEvent("end", $"{stopped} natural event{(stopped == 1 ? "" : "s")} ended early");
        }

        public void EndEffects()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
            bool ended = false;
            if (droughtEndsAt >= 0f && foodSpawner != null) { foodSpawner.growthMultiplier = 1f; ended = true; }
            if (mutationEndsAt >= 0f && manager != null) { manager.mutationMultiplier = 1f; ended = true; }
            droughtEndsAt = mutationEndsAt = -1f;
            if (ended && manager != null) manager.RecordEvent("end", "drought and mutation effects ended early");
        }

        void Update()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == null) return;
            if (manager.WorldRevision != lastWorldRevision)
            {
                // A new world starts clean: the manager and spawner reset their multipliers themselves.
                lastWorldRevision = manager.WorldRevision;
                droughtEndsAt = mutationEndsAt = -1f;
            }

            float now = Mathf.Max(0f, Time.time - manager.WorldStartTime);
            while (scheduled.Count > 0 && scheduled[0].time <= now)
            {
                Intervention next = scheduled[0];
                scheduled.RemoveAt(0);
                Apply(next, manager);
            }

            if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
            if (droughtEndsAt >= 0f && Time.time >= droughtEndsAt)
            {
                droughtEndsAt = -1f;
                if (foodSpawner != null) foodSpawner.growthMultiplier = 1f;
                manager.RecordEvent("drought-end", $"plants regrow normally again at {now:0} s");
            }
            if (mutationEndsAt >= 0f && Time.time >= mutationEndsAt)
            {
                mutationEndsAt = -1f;
                manager.mutationMultiplier = 1f;
                manager.RecordEvent("mutation-end", $"mutation chances back to normal at {now:0} s");
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || GodCamera.IsTypingInUI()) return;
            if (keyboard.kKey.wasPressedThisFrame) visible = !visible;
        }

        public static string DescribeFireWeather(NaturalEventDirector director)
        {
            NaturalConditions conditions = director.Conditions;
            WildfireKind fires = director.Wildfire;
            string burning = fires.BurningFires > 0
                ? $", {fires.BurningFires} burning ({fires.BurningHectares:0.0} ha alight)"
                : "";
            return $"fire danger {fires.Danger(conditions):0.00} (dryness {conditions.dryness:0.00}, wind " +
                   $"{conditions.windSpeed:0} m/s to {conditions.WindTowards}){burning}";
        }

        void OnGUI()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (!visible || manager == null) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };
            }

            const float Width = 300f;
            float height = 268f + 18f * Mathf.Min(scheduled.Count, 4);
            Rect panel = new Rect(Screen.width - Width - 10f, Screen.height - height - 10f, Width, height);
            GUI.Box(panel, "<b>God tools</b>   K: hide", boxStyle);
            GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 26f, panel.width - 16f, panel.height - 30f));
            if (GUILayout.Button("Drought: plants regrow at 20% for 200 s")) Apply(Kind.Drought, 200f, 0.2f);
            if (GUILayout.Button("Drop 30 plants in front of the camera")) Apply(Kind.Food, 0f, 30f);
            if (GUILayout.Button("Mass extinction: half the animals")) Apply(Kind.Extinction, 0f, 0.5f);
            if (GUILayout.Button("Mutation chances x4 for 300 s")) Apply(Kind.Mutation, 300f, 4f);
            if (GUILayout.Button("End the drought and mutation effects")) EndEffects();
            if (GUILayout.Button($"Wildfire {fireDistance:0} m in front of the camera"))
            {
                fireAtCamera = true;
                Apply(Kind.Fire, 0f, 1f);
            }
            if (GUILayout.Button("Put out every wildfire")) PutOutFires();
            string status = "";
            if (droughtEndsAt >= 0f) status += $"drought, {Mathf.Max(0f, droughtEndsAt - Time.time):0} s left   ";
            if (mutationEndsAt >= 0f) status += $"mutation x{manager.mutationMultiplier:0.#}, {Mathf.Max(0f, mutationEndsAt - Time.time):0} s left";
            GUILayout.Label(status.Length > 0 ? status : "no effect running", boxStyle.fontSize > 0 ? GUI.skin.label : GUI.skin.label);
            NaturalEventDirector natural = NaturalEventDirector.Active;
            if (natural != null) GUILayout.Label(DescribeFireWeather(natural));
            for (int index = 0; index < Mathf.Min(scheduled.Count, 4); index++) GUILayout.Label("scheduled: " + scheduled[index]);
            GUILayout.EndArea();
        }
    }
}
