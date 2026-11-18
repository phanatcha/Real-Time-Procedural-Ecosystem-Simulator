using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Boreal.Animals.Editor
{
    // Builds Assets/Animals/Resources/GrubbyLook.asset from Grubby.fbx (the body) and GrubbyParts.fbx (every body part,
    // exported from Blender already in place on the body, with a "Grown" blend shape). The asset holds its own copies
    // of the meshes, so the game needs nothing but that one file. Runs when GrubbyParts.fbx imports and from
    // Tools > Ecosystem > Rebuild Grubby Look.
    public sealed class GrubbyLookBuilder : AssetPostprocessor
    {
        public const string PartsPath = GrubbyModelImport.Folder + "/GrubbyParts.fbx";
        public const string LookPath = "Assets/Animals/Resources/" + GrubbyLook.ResourceName + ".asset";

        void OnPreprocessModel()
        {
            if (assetPath != PartsPath) return;
            ModelImporter importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = true;
            // Smooth the rounded parts, keeping hard edges only where faces meet at a sharp angle (blade rims).
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = 70f;
            importer.importBlendShapeNormals = ModelImporterNormals.None;
            importer.weldVertices = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
                                           string[] movedFromAssetPaths)
        {
            if (System.Array.IndexOf(importedAssets, PartsPath) >= 0) EditorApplication.delayCall += () => Build();
        }

        [MenuItem("Tools/Ecosystem/Rebuild Grubby Look")]
        static void Rebuild() => Build();

        // For the command line: Unity -batchmode -executeMethod Boreal.Animals.Editor.GrubbyLookBuilder.BuildFromCommandLine
        public static void BuildFromCommandLine()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            bool built = Build();
            EditorApplication.Exit(built ? 0 : 1);
        }

        public static bool Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return false;
            GameObject bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(GrubbyModelImport.ModelPath);
            GameObject partsModel = AssetDatabase.LoadAssetAtPath<GameObject>(PartsPath);
            if (bodyModel == null || partsModel == null)
            {
                Debug.LogWarning($"Grubby's look wasn't built: {GrubbyModelImport.ModelPath} or {PartsPath} is missing.");
                return false;
            }

            GameObject bodyInstance = Object.Instantiate(bodyModel);
            GameObject partsInstance = Object.Instantiate(partsModel);
            try
            {
                bodyInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                partsInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                SkinnedMeshRenderer bodyRenderer = bodyInstance.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
                if (bodyRenderer == null || bodyRenderer.sharedMesh == null)
                {
                    Debug.LogError("Grubby's look wasn't built: Grubby.fbx has no skinned body mesh.");
                    return false;
                }

                Mesh body = Bake(bodyRenderer, 0f, "Grubby Body");
                // The mouth must face +Z, as the animals walk. If the export ever turns the model round, turn it back.
                bool turn = MouthFacesBackwards(body, bodyRenderer.sharedMaterials);
                if (turn) Turn(body);

                List<GrubbyLook.Part> parts = new List<GrubbyLook.Part>();
                List<Mesh> meshes = new List<Mesh> { body };
                Dictionary<string, Vector3> sockets = new Dictionary<string, Vector3>();
                foreach (Transform child in partsInstance.GetComponentsInChildren<Transform>())
                {
                    if (child.name.StartsWith("Socket__"))
                        sockets[child.name.Substring("Socket__".Length)] = turn ? Turned(child.position) : child.position;
                }

                foreach (Renderer renderer in partsInstance.GetComponentsInChildren<Renderer>())
                {
                    Mesh source = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                    if (source == null) continue;
                    string[] name = renderer.name.Split(new[] { "__" }, System.StringSplitOptions.None);
                    if (name.Length != 4 || name[0] != "Part") continue;
                    if (!System.Enum.TryParse(name[1], out BodySite site) || !System.Enum.TryParse(name[2], out BodyPartType type))
                    {
                        Debug.LogWarning($"Grubby's look: skipped {renderer.name}, which names no body site or part.");
                        continue;
                    }

                    int grownShape = source.GetBlendShapeIndex("Grown");
                    if (grownShape < 0)
                    {
                        Debug.LogWarning($"Grubby's look: skipped {renderer.name}, which has no Grown blend shape.");
                        continue;
                    }

                    Mesh knub = Plain(source, renderer.transform.localToWorldMatrix, -1, renderer.name + " Knub");
                    Mesh grown = Plain(source, renderer.transform.localToWorldMatrix, grownShape, renderer.name + " Grown");
                    if (turn)
                    {
                        Turn(knub);
                        Turn(grown);
                    }

                    sockets.TryGetValue(name[1] + "__" + name[3], out Vector3 socket);
                    // Blender's left is +X there and -X here after the axis change; the side only has to tell the two apart.
                    parts.Add(new GrubbyLook.Part
                    {
                        site = site, type = type, side = name[3] == "L" ? -1 : name[3] == "R" ? 1 : 0,
                        knub = knub, grown = grown, socket = socket,
                        knubBottom = knub.bounds.min.y, grownBottom = grown.bounds.min.y,
                        palettes = renderer.sharedMaterials.Select(PaletteOf).ToArray()
                    });
                    meshes.Add(knub);
                    meshes.Add(grown);
                }

                if (parts.Count == 0)
                {
                    Debug.LogError("Grubby's look wasn't built: GrubbyParts.fbx has no parts named Part__Site__Type__Side. It holds: " +
                                   string.Join(", ", partsInstance.GetComponentsInChildren<Transform>().Take(12).Select(t =>
                                       $"{t.name} [{string.Join("/", t.GetComponents<Component>().Select(c => c.GetType().Name))}" +
                                       $"{(t.TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? " shapes " + f.sharedMesh.blendShapeCount : "")}]")));
                    return false;
                }

                System.IO.Directory.CreateDirectory("Assets/Animals/Resources");
                AssetDatabase.DeleteAsset(LookPath);
                GrubbyLook look = ScriptableObject.CreateInstance<GrubbyLook>();
                look.body = body;
                look.bodyMaterials = bodyRenderer.sharedMaterials;
                // The skin is the material named Body, or else the biggest submesh.
                look.skinSubmesh = System.Array.FindIndex(look.bodyMaterials, m => m != null && m.name.Contains("Body"));
                if (look.skinSubmesh < 0)
                {
                    int most = -1;
                    for (int sub = 0; sub < body.subMeshCount; sub++)
                    {
                        int count = (int)body.GetIndexCount(sub);
                        if (count > most)
                        {
                            most = count;
                            look.skinSubmesh = sub;
                        }
                    }
                }
                look.parts = parts.OrderBy(p => p.site).ThenBy(p => p.type).ThenBy(p => p.side).ToArray();
                // The rig, and its socket bones' rest poses (the stored pose of an instance the Animator hasn't moved).
                look.turned = turn;
                look.rig = AssetDatabase.LoadAssetAtPath<GameObject>(GrubbyModelImport.Folder + "/Grubby.prefab");
                if (look.rig != null)
                {
                    GameObject rigInstance = Object.Instantiate(look.rig);
                    try
                    {
                        rigInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        rigInstance.transform.localScale = Vector3.one;
                        List<string> names = new List<string>();
                        List<Matrix4x4> rests = new List<Matrix4x4>();
                        foreach (Transform bone in rigInstance.GetComponentsInChildren<Transform>(true))
                        {
                            if (!bone.name.StartsWith("socket_")) continue;
                            names.Add(bone.name);
                            rests.Add(rigInstance.transform.worldToLocalMatrix * bone.localToWorldMatrix);
                        }
                        look.socketNames = names.ToArray();
                        look.socketRest = rests.ToArray();
                    }
                    finally
                    {
                        Object.DestroyImmediate(rigInstance);
                    }
                }

                AssetDatabase.CreateAsset(look, LookPath);
                foreach (Mesh mesh in meshes) AssetDatabase.AddObjectToAsset(mesh, look);
                EditorUtility.SetDirty(look);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(LookPath);

                Bounds b = body.bounds;
                sockets.TryGetValue("Head__C", out Vector3 head);
                Debug.Log($"Built Grubby's look at {LookPath}: body {body.vertexCount} vertices, skin in submesh {look.skinSubmesh} of {body.subMeshCount}, {b.size.x:0.00} x {b.size.y:0.00} x " +
                          $"{b.size.z:0.00} m, base at y {b.min.y:0.00}{(turn ? ", turned to face +Z" : "")}; {parts.Count} parts " +
                          $"({string.Join(", ", parts.GroupBy(p => p.type).Select(g => $"{g.Key} x{g.Count()}"))}); " +
                          $"head socket at {head.x:0.00}, {head.y:0.00}, {head.z:0.00}; " +
                          (look.rig != null ? $"rig {look.rig.name} with {look.socketNames.Length} sockets." : "no rig (Grubby.prefab missing), so it stays still."));
                return true;
            }
            finally
            {
                Object.DestroyImmediate(bodyInstance);
                Object.DestroyImmediate(partsInstance);
            }
        }

        // The renderer's mesh as it stands in the model's space, with one blend shape set to a weight.
        static Mesh Bake(SkinnedMeshRenderer renderer, float weight, string name, int shape = -1)
        {
            if (shape >= 0) renderer.SetBlendShapeWeight(shape, weight);
            Mesh baked = new Mesh { name = name };
            renderer.BakeMesh(baked, true);
            Matrix4x4 toModel = renderer.transform.localToWorldMatrix;
            Vector3[] vertices = baked.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = toModel.MultiplyPoint3x4(vertices[i]);

            // A plain mesh: positions, triangles by submesh and fresh normals, without skin or blend shapes.
            Mesh mesh = new Mesh { name = name, vertices = vertices, subMeshCount = baked.subMeshCount };
            bool mirrored = toModel.determinant < 0f;
            for (int sub = 0; sub < baked.subMeshCount; sub++)
            {
                int[] triangles = baked.GetTriangles(sub);
                if (mirrored) System.Array.Reverse(triangles);
                mesh.SetTriangles(triangles, sub);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Object.DestroyImmediate(baked);
            return mesh;
        }

        // A mesh's vertices in the model's space, moved by one of its blend shapes at full weight (or none), as a plain
        // mesh with the same triangles by submesh.
        static Mesh Plain(Mesh source, Matrix4x4 toModel, int shape, string name)
        {
            Vector3[] vertices = source.vertices;
            if (shape >= 0)
            {
                Vector3[] delta = new Vector3[vertices.Length];
                source.GetBlendShapeFrameVertices(shape, source.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                for (int i = 0; i < vertices.Length; i++) vertices[i] += delta[i];
            }
            for (int i = 0; i < vertices.Length; i++) vertices[i] = toModel.MultiplyPoint3x4(vertices[i]);

            Mesh mesh = new Mesh { name = name, vertices = vertices, subMeshCount = source.subMeshCount };
            bool mirrored = toModel.determinant < 0f;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] triangles = source.GetTriangles(sub);
                if (mirrored) System.Array.Reverse(triangles);
                mesh.SetTriangles(triangles, sub);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool MouthFacesBackwards(Mesh body, Material[] materials)
        {
            for (int sub = 0; sub < body.subMeshCount && sub < materials.Length; sub++)
            {
                if (materials[sub] == null || !materials[sub].name.Contains("Mouth")) continue;
                int[] triangles = body.GetTriangles(sub);
                Vector3[] vertices = body.vertices;
                float z = 0f;
                foreach (int index in triangles) z += vertices[index].z;
                return triangles.Length > 0 && z / triangles.Length < body.bounds.center.z;
            }
            return false;
        }

        static Vector3 Turned(Vector3 point) => new Vector3(-point.x, point.y, -point.z);

        static void Turn(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Turned(vertices[i]);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static GrubbyLook.Palette PaletteOf(Material material)
        {
            string name = material != null ? material.name : "";
            if (name.Contains("Hard")) return GrubbyLook.Palette.Hard;
            if (name.Contains("EyeWhite")) return GrubbyLook.Palette.EyeWhite;
            if (name.Contains("Pupil")) return GrubbyLook.Palette.Pupil;
            if (name.Contains("Mouth")) return GrubbyLook.Palette.Mouth;
            if (name.Contains("Teeth")) return GrubbyLook.Palette.Teeth;
            return GrubbyLook.Palette.Body;
        }
    }
}
