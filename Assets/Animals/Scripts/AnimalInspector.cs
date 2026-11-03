using Boreal.Core;
using Boreal.World;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // Click an animal to inspect it (item 29): its genes beside its current values, body parts, perks, energy, what it
    // is doing, its parents and species. F follows it with the camera (item 32), until the camera is moved by hand
    // or the animal dies; Escape lets go. Adds itself to scenes with a Species Manager.
    [DisallowMultipleComponent]
    public sealed class AnimalInspector : MonoBehaviour
    {
        public bool visible = true;
        [Tooltip("Metres behind the followed animal the camera sits.")]
        [Min(2f)] public float followDistance = 16f;
        [Tooltip("Metres above the followed animal the camera sits.")]
        [Min(0f)] public float followHeight = 8f;

        public SeekFood Selected => selected;
        public bool IsFollowing => following && selected != null;

        SeekFood selected;
        bool following;
        Camera viewCamera;
        GUIStyle style;
        readonly StringBuilder text = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<AnimalInspector>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Animal Inspector").AddComponent<AnimalInspector>();
            }
        }

        public void Select(SeekFood animal)
        {
            selected = animal;
            if (animal == null) following = false;
        }

        public void Follow(SeekFood animal)
        {
            Select(animal);
            following = animal != null;
        }

        void Update()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (selected != null && !selected.IsAlive) Select(null);

            Mouse mouse = Mouse.current;
            if (mouse != null && viewCamera != null && mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed &&
                !PointerOverUi())
            {
                Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 5000f, 1 << WildlifeLayer.Index))
                {
                    SeekFood hitAnimal = hit.collider.GetComponentInParent<SeekFood>();
                    if (hitAnimal != null) Select(hitAnimal);
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || GodCamera.IsTypingInUI()) return;
            if (keyboard.escapeKey.wasPressedThisFrame) Select(null);
            if (keyboard.fKey.wasPressedThisFrame && selected != null) following = !following;
            // Moving the camera by hand ends the follow.
            if (following && (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed ||
                              keyboard.dKey.isPressed || keyboard.qKey.isPressed || keyboard.eKey.isPressed ||
                              keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed ||
                              keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed))
            {
                following = false;
            }
        }

        // After the god camera has moved, so this wins while following.
        void LateUpdate()
        {
            if (!following || selected == null || viewCamera == null) return;

            Vector3 target = selected.transform.position + Vector3.up;
            Vector3 back = viewCamera.transform.position - target;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = -selected.transform.forward;
            Vector3 wanted = target + back.normalized * followDistance + Vector3.up * followHeight;
            float blend = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            viewCamera.transform.position = Vector3.Lerp(viewCamera.transform.position, wanted, blend);
            viewCamera.transform.rotation = Quaternion.Slerp(viewCamera.transform.rotation,
                Quaternion.LookRotation(target - viewCamera.transform.position), blend);
        }

        static bool PointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        void OnGUI()
        {
            if (!visible || selected == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12,
                    richText = true,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }

            string content = Describe(selected);
            Vector2 size = style.CalcSize(new GUIContent(content));
            GUI.Box(new Rect(Screen.width - size.x - 10f, 10f, size.x, size.y), content, style);
        }

        string Describe(SeekFood animal)
        {
            text.Clear();
            AnimalGenome genome = animal.Genome;
            NavMeshAgent agent = animal.GetComponent<NavMeshAgent>();
            text.Append($"<b>{animal.name}</b>   species {animal.speciesName}, generation {animal.Generation}   " +
                        $"{(following ? "following (F stops)" : "F: follow")}   Esc: close\n");
            string youth = animal.IsMature ? "adult"
                : $"young, {animal.Growth:P0} grown, mature at {animal.maturityTime:0} s";
            text.Append($"{Lower(animal.Activity)}{(animal.IsInWater ? ", in water" : "")}   age {animal.CurrentAge:0} of " +
                        $"{animal.maxLifespan:0} s, {youth}\n");
            text.Append($"energy {animal.currentEnergy:0} of {animal.maxEnergy:0} ({animal.CurrentEnergyDrainPerSecond:0.00}/s)   " +
                        (animal.enableThirst ? $"water {animal.currentHydration:0} of {animal.maxHydration:0}{(animal.IsThirsty ? ", thirsty" : "")}   " : "") +
                        $"health {animal.currentHealth:0} of {animal.maxHealth:0}   stamina {animal.currentStamina:0} of {animal.maxStamina:0}\n");
            AnimalTemperature thermal = animal.ThermalResponse;
            if (thermal != null && thermal.HasTemperatureSample)
            {
                text.Append($"ambient {thermal.AmbientTemperature:0.#} °C, comfortable {thermal.MinimumComfortTemperature:0.#} to " +
                            $"{thermal.MaximumComfortTemperature:0.#} °C" +
                            (thermal.ColdStress > 0f ? $", cold stress {thermal.ColdStress:0.00}" : "") +
                            (thermal.HeatStress > 0f ? $", heat stress {thermal.HeatStress:0.00}" : "") + "\n");
            }

            if (genome != null && genome.IsValid)
            {
                text.Append("\n<b>Genes</b> (inherited → now)\n");
                text.Append($"speed {genome[AnimalGene.MoveSpeed]:0.0} → {(agent != null ? agent.speed : animal.moveSpeed):0.0}   " +
                            $"vision {genome[AnimalGene.VisionRadius]:0} → {animal.visionRadius:0} m   " +
                            $"reach {animal.FeedingReach:0.0} m   strength {animal.strength:0}\n");
                text.Append($"bulk {genome[AnimalGene.BodyBulk]:0.00} → {animal.bodyBulk * animal.Growth:0.00}   " +
                            $"height {genome[AnimalGene.BodyHeight]:0.00} → {animal.bodyHeight * animal.Growth:0.00}   " +
                            $"energy store {genome[AnimalGene.MaxEnergy]:0}   lifespan {genome[AnimalGene.MaxLifespan]:0} s   " +
                            $"maturity {genome[AnimalGene.MaturityTime]:0} s\n");
                text.Append($"diet {genome[AnimalGene.DietAffinity]:0.00} ({SeekFood.ClassifyDiet(animal.dietAffinity)}" +
                            $"{(animal.HuntsOwnKind ? ", hunts its own kind" : "")})   " +
                            $"prefers {genome[AnimalGene.PreferredTemperature]:0.#} °C, −{genome[AnimalGene.ColdTolerance]:0} / " +
                            $"+{genome[AnimalGene.HeatTolerance]:0}   torpor {genome[AnimalGene.Torpor]:0.00}   " +
                            $"active {SeekFood.DescribeChronotype(genome[AnimalGene.Nocturnality])} " +
                            $"({genome[AnimalGene.Nocturnality]:0.00})\n");
                text.Append($"sexual drive {genome[AnimalGene.SexualDrive]:0.00}   breeds at {100f * genome[AnimalGene.BreedingThreshold]:0}% energy   " +
                            $"litter {genome[AnimalGene.LitterSize]:0.00}   mutation rate {genome[AnimalGene.MutationRate]:0.00}×   " +
                            $"harmful mutations {genome.HarmfulMutations}\n");
            }

            text.Append($"\n<b>Body parts</b> {Blank(animal.BodyPlanDescription)}\n");
            text.Append($"<b>Perks</b> {Blank(animal.PerkDescription)}\n");
            text.Append($"<b>Parents</b> {DescribeParent(animal.FirstParent)}" +
                        (animal.SecondParent != null || animal.FirstParent != null
                            ? $" and {(animal.SecondParent != null ? DescribeParent(animal.SecondParent) : "none (a clone)")}"
                            : ""));
            return text.ToString().TrimEnd();
        }

        static string DescribeParent(SeekFood parent)
        {
            if (ReferenceEquals(parent, null)) return "none (a founder or from off-screen numbers)";
            return parent == null ? "dead" : $"{parent.name} ({parent.speciesName}), alive";
        }

        static string Blank(string value) => string.IsNullOrEmpty(value) ? "none" : value;

        static string Lower(string value) => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
