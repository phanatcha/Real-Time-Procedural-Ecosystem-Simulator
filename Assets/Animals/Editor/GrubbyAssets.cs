using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boreal.Animals.Editor
{
    // Builds Grubby's assets from the imported model: one solid material per material slot (no textures), the
    // Animator Controller from the animation plan and the visual prefab. The materials are set from the colours
    // below on every import, so change them here. A missing controller or prefab is built after the model
    // imports. The menu item rebuilds both, replacing them.
    public static class GrubbyAssets
    {
        private const string RebuildMenu = "Tools/Ecosystem/Rebuild Grubby Assets";
        private const string ControllerPath = GrubbyModelImport.Folder + "/Grubby.controller";
        private const string PrefabPath = GrubbyModelImport.Folder + "/Grubby.prefab";

        // The design sheet's colours, one material per slot in the model's slot order. At runtime the species
        // colour replaces the body's (slot 0); the mouth, teeth and tongue keep theirs.
        private static readonly (string name, Color color, float smoothness)[] Materials =
        {
            ("Grubby_Body", new Color(30f / 255f, 189f / 255f, 245f / 255f), 0.15f),
            ("Grubby_Mouth", new Color(199f / 255f, 0f, 1f / 255f), 0.3f),
            ("Grubby_Teeth", new Color(249f / 255f, 251f / 255f, 250f / 255f), 0.45f),
            ("Grubby_Tongue", new Color(252f / 255f, 115f / 255f, 114f / 255f), 0.35f),
        };

        // Values of SeekFood's movement states, which the State parameter carries.
        private const int Idling = 0;
        private const int Fighting = 4;

        private static readonly string[] Actions = { "Bite", "Eat", "Hurt" };
        private static readonly string[] Triggers = { "Bite", "Eat", "Hurt", "Split", "Die" };
        private static readonly string[] Weights = { "Hunt", "Flee", "Cold", "Heat", "Hunger" };

        [MenuItem(RebuildMenu)]
        private static void Rebuild()
        {
            Build(true);
        }

        public static void Build(bool rebuild)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess())
                return;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(GrubbyModelImport.ModelPath);
            if (model == null)
            {
                Debug.LogWarning($"Grubby's model isn't at {GrubbyModelImport.ModelPath}, so its assets weren't built.");
                return;
            }

            List<Material> materials = Materials.Select(m => BuildMaterial(m.name, m.color, m.smoothness)).ToList();
            AssetDatabase.SaveAssets();

            // Reimporting with the new materials runs this again, which then builds the controller and prefab.
            if (RemapMaterials(materials))
                return;

            Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(GrubbyModelImport.ModelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .ToDictionary(clip => clip.name);
            string[] missing = new[] { "Idle", "Crawl" }.Concat(Actions).Append("Die")
                .Where(name => !clips.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError($"Grubby's model has no clip named {string.Join(", ", missing)} " +
                               $"(found {string.Join(", ", clips.Keys)}), so the controller wasn't built.");
                return;
            }

            bool controllerExists = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null;
            AnimatorController controller = BuildController(clips, rebuild);
            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject prefab = BuildPrefab(model, controller, rebuild || !controllerExists);
            AssetDatabase.SaveAssets();

            string built = rebuild || !controllerExists || !prefabExists ? "Built" : "Updated";
            Debug.Log($"{built} Grubby's assets: {materials.Count} solid materials " +
                      $"({string.Join(", ", materials.Select(material => material.name))}), " +
                      $"{clips.Count} clips ({string.Join(", ", clips.Keys.OrderBy(name => name))}), " +
                      $"{controller.parameters.Length} Animator parameters on {controller.layers.Length} layers, " +
                      $"and the prefab at {PrefabPath}.", prefab);
        }

        private static Material BuildMaterial(string materialName, Color color, float smoothness)
        {
            string path = $"{GrubbyModelImport.Folder}/{materialName}.mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != lit)
            {
                material.shader = lit;
            }

            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Points each of the model's Blender materials at the project material of the same name.
        private static bool RemapMaterials(IEnumerable<Material> materials)
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(GrubbyModelImport.ModelPath);
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> current = importer.GetExternalObjectMap();
            bool changed = false;
            foreach (Material material in materials)
                changed |= Remap(importer, current, material.name, material);
            if (changed)
                importer.SaveAndReimport();
            return changed;
        }

        private static bool Remap(ModelImporter importer, Dictionary<AssetImporter.SourceAssetIdentifier, Object> current,
                                  string materialName, Material material)
        {
            AssetImporter.SourceAssetIdentifier id = new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName);
            if (current.TryGetValue(id, out Object existing) && existing == material)
                return false;

            importer.AddRemap(id, material);
            return true;
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, bool rebuild)
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !rebuild)
                return existing;
            if (existing != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
            });
            controller.AddParameter("State", AnimatorControllerParameterType.Int);
            controller.AddParameter("HasLegs", AnimatorControllerParameterType.Bool);
            controller.AddParameter("InWater", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Swimmer", AnimatorControllerParameterType.Bool);
            foreach (string trigger in Triggers)
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DeathCause", AnimatorControllerParameterType.Int);
            foreach (string weight in Weights)
                controller.AddParameter(weight, AnimatorControllerParameterType.Float);

            // Movement. Until Walk and Fight stance exist, every animal crawls and a fight idles.
            AnimatorStateMachine movement = controller.layers[0].stateMachine;
            AnimatorState idle = AddState(movement, "Idle", clips["Idle"]);
            AnimatorState crawl = AddState(movement, "Crawl", clips["Crawl"]);
            crawl.speedParameterActive = true;
            crawl.speedParameter = "Speed";
            movement.defaultState = idle;
            AnimatorStateTransition startCrawling = Transition(idle, crawl, 0.15f);
            startCrawling.AddCondition(AnimatorConditionMode.Greater, Idling, "State");
            startCrawling.AddCondition(AnimatorConditionMode.NotEqual, Fighting, "State");
            Transition(crawl, idle, 0.2f).AddCondition(AnimatorConditionMode.Equals, Idling, "State");
            Transition(crawl, idle, 0.2f).AddCondition(AnimatorConditionMode.Equals, Fighting, "State");

            // Actions play once over whatever movement is running, then hand back.
            AnimatorStateMachine actions = AddLayer(controller, "Actions");
            AnimatorState resting = AddState(actions, "None", null);
            actions.defaultState = resting;
            foreach (string action in Actions)
            {
                AnimatorState state = AddState(actions, action, clips[action]);
                AnimatorStateTransition start = actions.AddAnyStateTransition(state);
                start.AddCondition(AnimatorConditionMode.If, 0f, action);
                start.hasExitTime = false;
                start.duration = 0.05f;
                AnimatorStateTransition finish = state.AddTransition(resting);
                finish.hasExitTime = true;
                finish.exitTime = 0.9f;
                finish.duration = 0.1f;
            }

            // Death sits on top of everything, never exits and holds its last frame.
            AnimatorStateMachine death = AddLayer(controller, "Death");
            AnimatorState alive = AddState(death, "Alive", null);
            death.defaultState = alive;
            AnimatorState die = AddState(death, "Die", clips["Die"]);
            AnimatorStateTransition dying = death.AddAnyStateTransition(die);
            dying.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            dying.hasExitTime = false;
            dying.duration = 0.05f;
            dying.canTransitionToSelf = false;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorStateMachine AddLayer(AnimatorController controller, string layerName)
        {
            controller.AddLayer(layerName);
            AnimatorControllerLayer[] layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        // Write Defaults stays off, so an empty state leaves the layers below it alone.
        private static AnimatorState AddState(AnimatorStateMachine machine, string stateName, AnimationClip clip)
        {
            AnimatorState state = machine.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = false;
            return state;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            return transition;
        }

        // A prefab variant of the model with the Animator set up. It is 1 m long; animals scale it to their body.
        private static GameObject BuildPrefab(GameObject model, AnimatorController controller, bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && !rebuild)
                return existing;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                instance.name = "Grubby";
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

                foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.quality = SkinQuality.Bone2;
                    skin.updateWhenOffscreen = false;
                }

                return PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
