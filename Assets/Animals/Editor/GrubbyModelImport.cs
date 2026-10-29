using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Boreal.Animals.Editor
{
    // Import settings for Grubby's model. It gets a Generic rig without root motion (the NavMeshAgent moves the
    // animal) and keeps its socket bones as transforms, so body parts can hang off them. Each Blender action
    // becomes a clip named after it. Once the model has imported, GrubbyAssets sets up its materials and builds
    // the Animator Controller and prefab if they are missing.
    public sealed class GrubbyModelImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Animals/Models/Grubby";
        public const string ModelPath = Folder + "/Grubby.fbx";

        private static readonly HashSet<string> LoopingClips = new HashSet<string> { "Idle", "Crawl" };

        private void OnPreprocessModel()
        {
            if (assetPath != ModelPath)
                return;

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.motionNodeName = string.Empty;
            importer.optimizeGameObjects = false;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != ModelPath)
                return;

            ModelImporter importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = ClipName(clip.takeName);
                clip.loopTime = LoopingClips.Contains(clip.name);
            }
            importer.clipAnimations = clips;
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                                                   string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (System.Array.IndexOf(importedAssets, ModelPath) >= 0)
                EditorApplication.delayCall += () => GrubbyAssets.Build(false);
        }

        // Blender names each take "<armature>|<action>". The clip keeps the action name without its "Grubby_" prefix.
        public static string ClipName(string takeName)
        {
            string name = takeName.Substring(takeName.LastIndexOf('|') + 1);
            return name.StartsWith("Grubby_") ? name.Substring("Grubby_".Length) : name;
        }
    }
}
