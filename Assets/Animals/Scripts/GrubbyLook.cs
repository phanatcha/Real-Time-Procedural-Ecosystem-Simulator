using System;
using UnityEngine;

namespace Boreal.Animals
{
    // Grubby's look, ready for the game: the body mesh and every body part as modelled in Blender, in one asset under
    // Resources so AnimalBodyView can load it without anything set up in a scene. Each part comes as two meshes with
    // the same vertices in different places, the knub it starts as and the full-grown part; the body view blends
    // between them by the genome's part size. Everything is in the model's own space: metres on a body 1 m long, +Z
    // towards the mouth, the ground at y = 0.
    //
    // Built by the editor's GrubbyLookBuilder from Grubby.fbx and GrubbyParts.fbx (Tools > Ecosystem > Rebuild
    // Grubby Look, and whenever GrubbyParts.fbx imports). Without the asset, animals stay capsules with plain shapes.
    public sealed class GrubbyLook : ScriptableObject
    {
        public const string ResourceName = "GrubbyLook";

        // Which colour a piece of a part takes.
        public enum Palette
        {
            // The species colour, as the body.
            Body,
            // A dark shade of the species colour: horns, fins and plates.
            Hard,
            EyeWhite,
            Pupil,
            Mouth,
            Teeth
        }

        [Serializable]
        public sealed class Part
        {
            public BodySite site;
            public BodyPartType type;
            // -1 and 1 for the two sides of a paired site, 0 on the centre line.
            public int side;
            public Mesh knub;
            public Mesh grown;
            // One per submesh of the two meshes.
            public Palette[] palettes;
            // Where the part joins the body, and the lowest point of each shape (legs are fitted to the ground by them).
            public Vector3 socket;
            public float knubBottom;
            public float grownBottom;
        }

        public Mesh body;
        // One per submesh of the body: the skin, mouth, teeth and tongue, in the model's own order.
        public Material[] bodyMaterials;
        // The submesh that is skin, which takes the species colour.
        public int skinSubmesh;
        public Part[] parts;

        // The rigged, animated Grubby (Grubby.prefab, with its Animator and clips), and whether the body above was
        // turned round from it to face +Z. Without a rig the body is drawn still.
        public GameObject rig;
        public bool turned;
        // Each socket bone's rest pose in the rig's own space, by name, so parts can be laid out on the moving bones.
        public string[] socketNames;
        public Matrix4x4[] socketRest;

        public bool TryGetSocketRest(string socketName, out Matrix4x4 rest)
        {
            int index = socketNames != null ? System.Array.IndexOf(socketNames, socketName) : -1;
            rest = index >= 0 && socketRest != null && index < socketRest.Length ? socketRest[index] : Matrix4x4.identity;
            return index >= 0;
        }

        // The rig's socket bone a part grows from: socket_head, socket_back, socket_tail, or socket_front, _middle and
        // _rear with .L (side -1) or .R (side 1).
        public static string SocketName(BodySite site, int side)
        {
            string pair = side < 0 ? ".L" : ".R";
            switch (site)
            {
                case BodySite.Head: return "socket_head";
                case BodySite.Back: return "socket_back";
                case BodySite.Tail: return "socket_tail";
                case BodySite.FrontPair: return "socket_front" + pair;
                case BodySite.MiddlePair: return "socket_middle" + pair;
                default: return "socket_rear" + pair;
            }
        }

        // Off for a test or a scene that wants the plain capsules.
        public static bool Enabled = true;

        static GrubbyLook loaded;
        static bool looked;

        public static GrubbyLook Current
        {
            get
            {
                if (!Enabled) return null;
                if (!looked)
                {
                    looked = true;
                    loaded = Resources.Load<GrubbyLook>(ResourceName);
                }
                return loaded != null && loaded.body != null ? loaded : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded = null;
            looked = false;
            Enabled = true;
        }
    }
}
