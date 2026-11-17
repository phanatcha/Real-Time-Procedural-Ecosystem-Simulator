using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Boreal.Animals
{
    // Draws an animal's body parts on its capsule body, sized by the genome: legs, a neck with a head, horns,
    // eye stalks, armour plates and fins. The parts are simple shapes merged into one mesh per animal, built once
    // at birth, in a shade darker than the species colour. Parts have no colliders; only the body does.
    [DisallowMultipleComponent]
    public class AnimalBodyView : MonoBehaviour
    {
        const string PartsObjectName = "Body Parts";
        const string EyeshineObjectName = "Eyeshine";
        // The yellow-green of a deer's or cat's eyes in torchlight.
        static readonly Color EyeshineColor = new Color(0.75f, 0.95f, 0.45f);
        const string GlowObjectName = "Glow";
        // Bioluminescent blue-green, as most living light is (item 76).
        static readonly Color GlowColor = new Color(0.18f, 0.62f, 0.56f);
        // The glow shell is the body's own mesh, this much larger, so the light spills a little past the outline.
        const float GlowShellScale = 1.08f;
        const float PartShade = 0.7f;
        const int FramesToCheckLift = 30;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Dictionary<Color32, Material> materialsByColor = new Dictionary<Color32, Material>();
        static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshes = new Dictionary<PrimitiveType, Mesh>();
        static Material eyeshineMaterial;
        static Material glowMaterial;
        // One coloured copy of each body mesh for the glow shell, shared by every glowing animal.
        static readonly Dictionary<Mesh, Mesh> glowMeshes = new Dictionary<Mesh, Mesh>();

        readonly List<CombineInstance> pieces = new List<CombineInstance>();
        AnimalGenome genome;
        float fullLegReach;
        float? measuredGround;
        int liftCheckFrames;
        Mesh partsMesh;
        Mesh eyeshineMesh;
        MeshFilter partsFilter;
        MeshRenderer partsRenderer;

        // True once the legs have been fitted to the height the agent holds the body at. Until then nothing else
        // should move the body up or down (SeekFood waits before floating it in water).
        public bool HasMeasuredLift => !enabled || genome == null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCaches()
        {
            materialsByColor.Clear();
            primitiveMeshes.Clear();
            eyeshineMaterial = null;
            glowMaterial = null;
            glowMeshes.Clear();
            fittedBodies.Clear();
        }

        // fullLegReach: how far below the body a full-size leg reaches, in world units.
        public void Build(AnimalGenome bodyGenome, float fullLegReach, Color speciesColor)
        {
            genome = bodyGenome;
            this.fullLegReach = Mathf.Max(0f, fullLegReach);
            measuredGround = null;
            liftCheckFrames = 0;
            enabled = true;
            this.speciesColor = speciesColor;
            grubby = GrubbyLook.Current;
            if (grubby != null) UseGrubbyBody();
            PreparePartsObject();
            Rebuild();
            SetColor(speciesColor);
        }

        // ---- Grubby (item 33): the modelled body and parts, when Resources holds GrubbyLook ----

        GrubbyLook grubby;
        Color speciesColor = Color.white;
        // How the model sits in the body's own mesh space: scaled to the length of the body it replaces, standing on
        // the same base.
        float grubbyScale = 1f;
        Vector3 grubbyOffset;
        readonly List<GrubbyLook.Palette> partPalettes = new List<GrubbyLook.Palette>();

        struct FittedBody
        {
            public Mesh mesh;
            public float scale;
            public Vector3 offset;
        }
        // One fitted copy of Grubby's body for each mesh it replaces, and the same lookup from the copy itself, since
        // the young are copies of their parents and already carry it.
        static readonly Dictionary<Mesh, FittedBody> fittedBodies = new Dictionary<Mesh, FittedBody>();

        static readonly Color EyeWhiteColor = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color PupilColor = new Color(0.02f, 0.02f, 0.025f);
        static readonly Color PartMouthColor = new Color(199f / 255f, 0f, 1f / 255f);
        static readonly Color PartTeethColor = new Color(249f / 255f, 251f / 255f, 250f / 255f);
        // Horns, fins and plates take a dark shade of the species colour.
        const float HardShade = 0.4f;

        // Swaps the capsule's mesh for Grubby's body, as long as the body it replaces and standing on the same base, and
        // gives the renderer the mouth, teeth and tongue materials after the body's own.
        void UseGrubbyBody()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            MeshRenderer body = GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null || body == null)
            {
                grubby = null;
                return;
            }

            if (!fittedBodies.TryGetValue(filter.sharedMesh, out FittedBody fitted) || fitted.mesh == null)
            {
                Bounds replaced = filter.sharedMesh.bounds, model = grubby.body.bounds;
                float scale = replaced.size.z / Mathf.Max(0.0001f, model.size.z);
                Vector3 offset = new Vector3(replaced.center.x - scale * model.center.x,
                                             replaced.min.y - scale * model.min.y,
                                             replaced.center.z - scale * model.center.z);
                Vector3[] vertices = grubby.body.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = vertices[i] * scale + offset;
                Mesh mesh = Instantiate(grubby.body);
                mesh.name = "Grubby Body (fitted)";
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                fitted = new FittedBody { mesh = mesh, scale = scale, offset = offset };
                fittedBodies[filter.sharedMesh] = fitted;
                fittedBodies[mesh] = fitted;
            }

            filter.sharedMesh = fitted.mesh;
            grubbyScale = fitted.scale;
            grubbyOffset = fitted.offset;

            Material skin = body.sharedMaterial;
            int count = Mathf.Max(1, fitted.mesh.subMeshCount);
            if (body.sharedMaterials.Length != count)
            {
                Material[] materials = new Material[count];
                for (int i = 0; i < count; i++)
                {
                    materials[i] = i == grubby.skinSubmesh || grubby.bodyMaterials == null ||
                                   i >= grubby.bodyMaterials.Length || grubby.bodyMaterials[i] == null
                        ? skin
                        : grubby.bodyMaterials[i];
                }
                body.sharedMaterials = materials;
            }
        }

        // A point of the model, in the unscaled metres the parts are laid out in.
        Vector3 GrubbyPoint(Vector3 model, Vector3 bodyScale) =>
            Vector3.Scale(model * grubbyScale + grubbyOffset, bodyScale);

        // Every part the genome has, blended between its knub and its full-grown shape by its size and merged into one
        // mesh with a submesh per colour. Legs are fitted to the ground instead: they grow until the foot reaches it,
        // and stretch for the rest if the body is held higher than a full-grown leg is long.
        void BuildGrubbyParts(BodyFrame frame, Vector3 bodyScale)
        {
            List<Vector3> vertices = new List<Vector3>();
            Dictionary<GrubbyLook.Palette, List<int>> triangles = new Dictionary<GrubbyLook.Palette, List<int>>();
            float longestLegs = frame.LongestLegs(genome);
            for (int index = 0; index < AnimalGenome.SiteCount; index++)
            {
                BodySite site = (BodySite)index;
                float size = genome.GetPartSize(site);
                BodyPartType type = genome.GetPartType(site);
                if (size <= 0f || type == BodyPartType.None) continue;

                foreach (GrubbyLook.Part part in grubby.parts)
                {
                    if (part.site != site || part.type != type || part.knub == null || part.grown == null) continue;

                    float blend = size, stretch = 1f;
                    float socketY = GrubbyPoint(part.socket, bodyScale).y;
                    if (type == BodyPartType.Legs)
                    {
                        float footY = frame.Bottom - (frame.Bottom - frame.Ground) * size / Mathf.Max(0.0001f, longestLegs);
                        float reach = socketY - footY;
                        float knubReach = socketY - GrubbyPoint(new Vector3(0f, part.knubBottom, 0f), bodyScale).y;
                        float grownReach = socketY - GrubbyPoint(new Vector3(0f, part.grownBottom, 0f), bodyScale).y;
                        blend = Mathf.Clamp01((reach - knubReach) / Mathf.Max(0.0001f, grownReach - knubReach));
                        if (blend >= 1f) stretch = reach / Mathf.Max(0.0001f, grownReach);
                    }

                    Vector3[] knub = part.knub.vertices, grown = part.grown.vertices;
                    int first = vertices.Count;
                    for (int i = 0; i < knub.Length; i++)
                    {
                        Vector3 point = GrubbyPoint(Vector3.LerpUnclamped(knub[i], grown[i], blend), bodyScale);
                        if (stretch != 1f && point.y < socketY) point.y = socketY - (socketY - point.y) * stretch;
                        vertices.Add(point);
                    }

                    for (int sub = 0; sub < part.knub.subMeshCount; sub++)
                    {
                        GrubbyLook.Palette palette = part.palettes != null && sub < part.palettes.Length
                            ? part.palettes[sub]
                            : GrubbyLook.Palette.Body;
                        if (!triangles.TryGetValue(palette, out List<int> list)) triangles[palette] = list = new List<int>();
                        foreach (int vertex in part.knub.GetTriangles(sub)) list.Add(first + vertex);
                    }
                }
            }

            partPalettes.Clear();
            if (partsFilter == null) return;
            if (vertices.Count == 0)
            {
                partsFilter.sharedMesh = null;
                return;
            }

            if (partsMesh == null) partsMesh = new Mesh { name = "Animal Body Parts" };
            else partsMesh.Clear();
            partsMesh.SetVertices(vertices);
            partsMesh.subMeshCount = triangles.Count;
            int submesh = 0;
            foreach (KeyValuePair<GrubbyLook.Palette, List<int>> entry in triangles)
            {
                partsMesh.SetTriangles(entry.Value, submesh++);
                partPalettes.Add(entry.Key);
            }
            partsMesh.RecalculateNormals();
            partsMesh.RecalculateBounds();
            partsFilter.sharedMesh = partsMesh;
        }

        Color PaletteColor(GrubbyLook.Palette palette, Color species)
        {
            switch (palette)
            {
                case GrubbyLook.Palette.Hard:
                    Color hard = species * HardShade;
                    hard.a = 1f;
                    return hard;
                case GrubbyLook.Palette.EyeWhite: return EyeWhiteColor;
                case GrubbyLook.Palette.Pupil: return PupilColor;
                case GrubbyLook.Palette.Mouth: return PartMouthColor;
                case GrubbyLook.Palette.Teeth: return PartTeethColor;
                default: return species;
            }
        }

        public void SetColor(Color speciesColor)
        {
            this.speciesColor = speciesColor;
            MeshRenderer body = GetComponent<MeshRenderer>();
            if (partsRenderer == null || body == null || body.sharedMaterial == null)
            {
                return;
            }

            if (grubby != null)
            {
                // The species colour goes on the skin only; the mouth, teeth and tongue keep their own.
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, speciesColor);
                block.SetColor(ColorId, speciesColor);
                body.SetPropertyBlock(null);
                body.SetPropertyBlock(block, Mathf.Clamp(grubby.skinSubmesh, 0, body.sharedMaterials.Length - 1));

                Material[] materials = new Material[partPalettes.Count];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = PartMaterial(body.sharedMaterial, PaletteColor(partPalettes[i], speciesColor));
                partsRenderer.sharedMaterials = materials;
                return;
            }

            Color shade = speciesColor * PartShade;
            shade.a = 1f;
            partsRenderer.sharedMaterial = PartMaterial(body.sharedMaterial, shade);
        }

        // Legs are sized to reach the ground from the height the navigation actually holds the body at, which is
        // only known once the agent has placed it.
        void LateUpdate()
        {
            if (genome == null)
            {
                return;
            }

            if (++liftCheckFrames > FramesToCheckLift)
            {
                enabled = false;
                return;
            }

            if (!TryGetComponent(out NavMeshAgent agent) || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            float ground = agent.nextPosition.y - transform.position.y;
            enabled = false;
            if (Mathf.Abs(ground - EstimatedGround()) > 0.05f)
            {
                measuredGround = ground;
                Rebuild();
            }
        }

        void OnDestroy()
        {
            if (partsMesh != null)
            {
                Destroy(partsMesh);
            }

            if (eyeshineMesh != null)
            {
                Destroy(eyeshineMesh);
            }
        }

        void PreparePartsObject()
        {
            Transform parts = transform.Find(PartsObjectName);
            if (parts == null)
            {
                parts = new GameObject(PartsObjectName).transform;
                parts.SetParent(transform, false);
                // Drawn with the body, so camera settings for the animal's layer apply to its parts too.
                parts.gameObject.layer = gameObject.layer;
            }

            // Undo the body's uneven scale, so parts are laid out in metres and never skew.
            Vector3 scale = transform.lossyScale;
            parts.localPosition = Vector3.zero;
            parts.localRotation = Quaternion.identity;
            parts.localScale = new Vector3(1f / Mathf.Max(0.0001f, scale.x),
                                           1f / Mathf.Max(0.0001f, scale.y),
                                           1f / Mathf.Max(0.0001f, scale.z));

            if (!parts.TryGetComponent(out partsFilter)) partsFilter = parts.gameObject.AddComponent<MeshFilter>();
            if (!parts.TryGetComponent(out partsRenderer)) partsRenderer = parts.gameObject.AddComponent<MeshRenderer>();
        }

        void Rebuild()
        {
            pieces.Clear();
            MeshFilter bodyFilter = GetComponent<MeshFilter>();
            if (bodyFilter != null && bodyFilter.sharedMesh != null && genome != null && genome.IsValid)
            {
                Vector3 scale = transform.lossyScale;
                Bounds bounds = bodyFilter.sharedMesh.bounds;
                BodyFrame frame = new BodyFrame(Vector3.Scale(bounds.min, scale), Vector3.Scale(bounds.max, scale),
                                                measuredGround ?? EstimatedGround(bounds.min.y * scale.y));
                if (grubby != null)
                {
                    BuildGrubbyParts(frame, scale);
                    BuildEyeshine(frame);
                    BuildGlow();
                    SetColor(speciesColor);
                    return;
                }

                for (int index = 0; index < AnimalGenome.SiteCount; index++)
                {
                    BodySite site = (BodySite)index;
                    float size = genome.GetPartSize(site);
                    if (size > 0f)
                    {
                        AddPart(frame, site, genome.GetPartType(site), size);
                    }
                }

                BuildEyeshine(frame);
            }

            BuildGlow();

            if (partsFilter == null)
            {
                return;
            }

            // An offspring starts with its parent's part object; never reuse or destroy the parent's mesh.
            if (pieces.Count == 0)
            {
                partsFilter.sharedMesh = null;
                return;
            }

            if (partsMesh == null)
            {
                partsMesh = new Mesh { name = "Animal Body Parts" };
            }
            else
            {
                partsMesh.Clear();
            }

            partsMesh.CombineMeshes(pieces.ToArray(), true, true);
            partsFilter.sharedMesh = partsMesh;
        }

        // How strongly an animal's eyes shine in the dark (item 75): not at all with ordinary eyes, fully from night
        // vision 0.8.
        public static float EyeshineStrength(float nightVision) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, nightVision));

        // Two small dots high on the front of the body that glow in the dark for animals with night eyes, so the
        // trait shows at a glance. Their brightness is in the vertex colour; the dark is a shader global DayClock
        // sets, so nothing here runs per frame. An offspring starts with its parent's object; it gets its own mesh.
        void BuildEyeshine(BodyFrame frame)
        {
            Transform parts = partsFilter != null ? partsFilter.transform : null;
            Transform eyes = parts != null ? parts.Find(EyeshineObjectName) : null;
            float strength = EyeshineStrength(genome[AnimalGene.NightVision]);
            if (strength <= 0f || parts == null || EyeshineMaterial() == null)
            {
                if (eyes != null) eyes.gameObject.SetActive(false);
                return;
            }

            if (eyes == null)
            {
                eyes = new GameObject(EyeshineObjectName).transform;
                eyes.SetParent(parts, false);
                eyes.gameObject.layer = gameObject.layer;
                eyes.gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = eyes.gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            eyes.gameObject.SetActive(true);
            eyes.GetComponent<MeshRenderer>().sharedMaterial = EyeshineMaterial();

            // On the front of the body's straight part, a little apart, sunk a little into the round surface.
            float radius = 0.06f * frame.Unit;
            float apart = 0.22f;
            float forward = 0.5f * frame.Length * Mathf.Sqrt(1f - 4f * apart * apart) - 0.3f * radius;
            CombineInstance[] dots = new CombineInstance[2];
            for (int side = 0; side < 2; side++)
            {
                Vector3 position = new Vector3(frame.CentreX + (side == 0 ? -apart : apart) * frame.Width,
                                               frame.Bottom + 0.68f * frame.Height, frame.CentreZ + forward);
                dots[side] = new CombineInstance
                {
                    mesh = PrimitiveMesh(PrimitiveType.Sphere),
                    transform = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * 2f * radius)
                };
            }

            if (eyeshineMesh == null) eyeshineMesh = new Mesh { name = "Animal Eyeshine" };
            else eyeshineMesh.Clear();
            eyeshineMesh.CombineMeshes(dots, true, true);
            Color32[] colours = new Color32[eyeshineMesh.vertexCount];
            Color32 colour = EyeshineColor * strength;
            for (int index = 0; index < colours.Length; index++) colours[index] = colour;
            eyeshineMesh.colors32 = colours;
            eyes.GetComponent<MeshFilter>().sharedMesh = eyeshineMesh;
        }

        // An animal with the glow perk (item 76) gives off a soft blue-green light in the dark: a shell over its body,
        // drawn added on top and faded in by the dark (Hidden/Boreal/Glow), so nothing runs per frame. The shell is a
        // child of the body, so it grows and turns with it. An offspring starts with its parent's shell and hides it
        // when it doesn't glow.
        void BuildGlow()
        {
            Transform shell = transform.Find(GlowObjectName);
            MeshFilter bodyFilter = GetComponent<MeshFilter>();
            bool glows = genome != null && genome.IsValid && genome.HasPerk(AnimalPerk.Glow);
            if (!glows || bodyFilter == null || bodyFilter.sharedMesh == null || GlowMaterial() == null)
            {
                if (shell != null) shell.gameObject.SetActive(false);
                return;
            }

            if (shell == null)
            {
                shell = new GameObject(GlowObjectName).transform;
                shell.SetParent(transform, false);
                shell.localScale = Vector3.one * GlowShellScale;
                shell.gameObject.layer = gameObject.layer;
                shell.gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = shell.gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            shell.gameObject.SetActive(true);
            shell.GetComponent<MeshFilter>().sharedMesh = GlowMesh(bodyFilter.sharedMesh);
            shell.GetComponent<MeshRenderer>().sharedMaterial = GlowMaterial();
        }

        static Mesh GlowMesh(Mesh body)
        {
            if (glowMeshes.TryGetValue(body, out Mesh mesh) && mesh != null) return mesh;
            mesh = Instantiate(body);
            mesh.name = body.name + " Glow";
            Color32[] colours = new Color32[mesh.vertexCount];
            Color32 colour = GlowColor;
            for (int index = 0; index < colours.Length; index++) colours[index] = colour;
            mesh.colors32 = colours;
            glowMeshes[body] = mesh;
            return mesh;
        }

        static Material GlowMaterial()
        {
            if (glowMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Boreal/Glow");
                if (shader != null) glowMaterial = new Material(shader) { name = "Glow" };
            }

            return glowMaterial;
        }

        static Material EyeshineMaterial()
        {
            if (eyeshineMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Boreal/Eyeshine");
                if (shader != null) eyeshineMaterial = new Material(shader) { name = "Eyeshine" };
            }

            return eyeshineMaterial;
        }

        float EstimatedGround()
        {
            MeshFilter bodyFilter = GetComponent<MeshFilter>();
            float bottom = bodyFilter != null && bodyFilter.sharedMesh != null
                ? bodyFilter.sharedMesh.bounds.min.y * transform.lossyScale.y
                : 0f;
            return EstimatedGround(bottom);
        }

        // The ground sits below the body's underside by the lift of the longest legs.
        float EstimatedGround(float bodyBottom)
        {
            float longestLegs = 0f;
            for (int index = 0; index < AnimalGenome.SiteCount; index++)
            {
                BodySite site = (BodySite)index;
                if (genome != null && genome.IsValid && genome.GetPartType(site) == BodyPartType.Legs)
                {
                    longestLegs = Mathf.Max(longestLegs, genome.GetPartSize(site));
                }
            }

            return bodyBottom - fullLegReach * longestLegs;
        }

        void AddPart(BodyFrame frame, BodySite site, BodyPartType type, float size)
        {
            float unit = frame.Unit;
            if (AnimalGenome.IsPaired(site))
            {
                AddPairedPart(frame, site, type, size, -1f);
                AddPairedPart(frame, site, type, size, 1f);
                return;
            }

            Vector3 anchor = frame.Anchor(site, 0f);
            switch (type)
            {
                case BodyPartType.Neck:
                {
                    float length = unit * 0.2f + AnimalBodyPlan.NeckReach * 0.8f * size;
                    Vector3 top = anchor + new Vector3(0f, 0.87f, 0.5f) * length;
                    AddLimb(PrimitiveType.Capsule, anchor, top, unit * (0.12f + 0.05f * size));
                    AddPiece(PrimitiveType.Sphere, top, Quaternion.identity, Vector3.one * unit * 0.28f);
                    break;
                }
                case BodyPartType.Horn:
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 root = anchor + new Vector3(side * frame.Width * 0.22f, 0f, 0f);
                        float length = unit * (0.15f + 0.55f * size);
                        AddLimb(PrimitiveType.Capsule, root, root + new Vector3(side * 0.15f, 0.5f, 0.85f) * length,
                                unit * (0.05f + 0.05f * size));
                    }
                    break;
                case BodyPartType.EyeStalks:
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 root = anchor + new Vector3(side * frame.Width * 0.18f, 0f, 0f);
                        Vector3 tip = root + new Vector3(side * 0.25f, 1f, 0.2f).normalized * unit * (0.12f + 0.6f * size);
                        AddLimb(PrimitiveType.Cylinder, root, tip, unit * 0.035f);
                        AddPiece(PrimitiveType.Sphere, tip, Quaternion.identity, Vector3.one * unit * (0.1f + 0.06f * size));
                    }
                    break;
                case BodyPartType.Plates when site == BodySite.Back:
                    foreach (float offset in new[] { -0.25f, 0f, 0.25f })
                    {
                        float height = unit * (0.12f + 0.4f * size);
                        AddPiece(PrimitiveType.Cube, new Vector3(frame.CentreX, frame.Top + height * 0.4f, frame.CentreZ + offset * frame.Length),
                                 Quaternion.identity, new Vector3(unit * 0.05f, height, frame.Length * 0.18f));
                    }
                    break;
                case BodyPartType.Plates:
                {
                    float length = frame.Length * (0.1f + 0.2f * size);
                    AddPiece(PrimitiveType.Cube, anchor + new Vector3(0f, 0f, -length * 0.5f), Quaternion.identity,
                             new Vector3(unit * 0.05f, frame.Height * (0.3f + 0.4f * size), length));
                    break;
                }
                case BodyPartType.Fins when site == BodySite.Back:
                {
                    float height = unit * (0.1f + 0.45f * size);
                    AddPiece(PrimitiveType.Cube,
                             new Vector3(frame.CentreX, frame.Top + height * 0.4f, frame.CentreZ - 0.1f * frame.Length),
                             Quaternion.Euler(-20f, 0f, 0f),
                             new Vector3(unit * 0.04f, height, frame.Length * (0.15f + 0.15f * size)));
                    break;
                }
                case BodyPartType.Fins:
                {
                    float length = frame.Length * (0.1f + 0.3f * size);
                    AddPiece(PrimitiveType.Cube, anchor + new Vector3(0f, 0f, -length * 0.5f), Quaternion.identity,
                             new Vector3(unit * 0.04f, frame.Height * (0.4f + 0.5f * size), length));
                    break;
                }
            }
        }

        void AddPairedPart(BodyFrame frame, BodySite site, BodyPartType type, float size, float side)
        {
            float unit = frame.Unit;
            Vector3 anchor = frame.Anchor(site, side);
            switch (type)
            {
                case BodyPartType.Legs:
                {
                    // Reaches below the body by the leg's own length; the longest legs meet the ground.
                    float footY = frame.Bottom - (frame.Bottom - frame.Ground) * size / Mathf.Max(0.0001f, frame.LongestLegs(genome));
                    Vector3 foot = new Vector3(anchor.x + side * 0.15f * (anchor.y - footY), footY, anchor.z);
                    AddLimb(PrimitiveType.Cylinder, anchor, foot, unit * (0.06f + 0.08f * size));
                    break;
                }
                case BodyPartType.Plates:
                    AddPiece(PrimitiveType.Cube,
                             new Vector3(anchor.x + side * unit * 0.03f, frame.Bottom + frame.Height * 0.55f, anchor.z),
                             Quaternion.identity,
                             new Vector3(unit * 0.05f, frame.Height * (0.25f + 0.35f * size), frame.Length * (0.18f + 0.1f * size)));
                    break;
                case BodyPartType.Fins:
                {
                    float span = unit * (0.12f + 0.5f * size);
                    AddPiece(PrimitiveType.Cube, anchor + new Vector3(side * span * 0.5f, 0f, 0f),
                             Quaternion.Euler(0f, 0f, side * -15f),
                             new Vector3(span, unit * 0.04f, frame.Length * 0.14f));
                    break;
                }
            }
        }

        // A cylinder or capsule stretched between two points (both primitives are 2 units tall along Y).
        void AddLimb(PrimitiveType shape, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < 0.001f)
            {
                return;
            }

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
            AddPiece(shape, (from + to) * 0.5f, rotation, new Vector3(thickness, length * 0.5f, thickness));
        }

        void AddPiece(PrimitiveType shape, Vector3 position, Quaternion rotation, Vector3 size)
        {
            pieces.Add(new CombineInstance
            {
                mesh = PrimitiveMesh(shape),
                transform = Matrix4x4.TRS(position, rotation, size)
            });
        }

        static Mesh PrimitiveMesh(PrimitiveType shape)
        {
            if (primitiveMeshes.TryGetValue(shape, out Mesh mesh) && mesh != null)
            {
                return mesh;
            }

            GameObject temporary = GameObject.CreatePrimitive(shape);
            temporary.SetActive(false);
            mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temporary);
            primitiveMeshes[shape] = mesh;
            return mesh;
        }

        // One material per colour keeps every animal of a species drawable in the same batch.
        static Material PartMaterial(Material template, Color color)
        {
            Color32 key = color;
            if (materialsByColor.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            material = new Material(template) { name = $"Body Parts {ColorUtility.ToHtmlStringRGB(color)}" };
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
            materialsByColor[key] = material;
            return material;
        }

        // The body's box in the animal's own unscaled space: +Z is forward, and the ground is below the body
        // by the leg lift.
        readonly struct BodyFrame
        {
            readonly Vector3 minimum;
            readonly Vector3 maximum;
            public readonly float Ground;

            public BodyFrame(Vector3 minimum, Vector3 maximum, float ground)
            {
                this.minimum = minimum;
                this.maximum = maximum;
                Ground = ground;
            }

            public float Width => maximum.x - minimum.x;
            public float Height => maximum.y - minimum.y;
            public float Length => maximum.z - minimum.z;
            public float CentreX => (minimum.x + maximum.x) * 0.5f;
            public float CentreZ => (minimum.z + maximum.z) * 0.5f;
            public float Bottom => minimum.y;
            public float Top => maximum.y;
            // A typical body dimension that part thickness and length scale with.
            public float Unit => (Width + Height + Length) / 3f;

            public Vector3 Anchor(BodySite site, float side) => site switch
            {
                BodySite.Head => new Vector3(CentreX, Bottom + 0.65f * Height, maximum.z - 0.08f * Length),
                BodySite.Back => new Vector3(CentreX, Top, CentreZ),
                BodySite.Tail => new Vector3(CentreX, Bottom + 0.5f * Height, minimum.z + 0.05f * Length),
                BodySite.FrontPair => new Vector3(CentreX + side * 0.45f * Width, Bottom + 0.35f * Height, CentreZ + 0.28f * Length),
                BodySite.MiddlePair => new Vector3(CentreX + side * 0.45f * Width, Bottom + 0.35f * Height, CentreZ),
                _ => new Vector3(CentreX + side * 0.45f * Width, Bottom + 0.35f * Height, CentreZ - 0.28f * Length)
            };

            public float LongestLegs(AnimalGenome genome)
            {
                float longest = 0f;
                for (int index = 0; index < AnimalGenome.SiteCount; index++)
                {
                    BodySite site = (BodySite)index;
                    if (genome.GetPartType(site) == BodyPartType.Legs)
                    {
                        longest = Mathf.Max(longest, genome.GetPartSize(site));
                    }
                }

                return longest;
            }
        }
    }
}
