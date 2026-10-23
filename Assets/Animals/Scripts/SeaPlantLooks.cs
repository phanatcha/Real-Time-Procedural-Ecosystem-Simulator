using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// What sea plants look like (phase B, stage 4):
// - seagrass: a tuft of blades from the bed, with leaves lying on the water where they reach the surface;
// - kelp: a stalk from a holdfast on the bed, with blades along it and floats and fronds spread on the surface;
// - reef: a coral cluster on the bed, under a slick of pink coral spawn on the water;
// - open sea: specks of drifting plankton.
// Animals still eat by touching the floating food box, so its size and collider are untouched; only its own mesh
// is hidden. The shapes are shared meshes: a few variants per biome, and for plants reaching from the bed to the
// surface one mesh per half metre of depth.
public static class SeaPlantLooks
{
    const int Variants = 4;
    const float DepthStep = 0.5f;
    // How far above the water the floating parts sit, so the waves don't swallow them.
    const float FloatHeight = 0.06f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    static readonly Color SeagrassLeafColor = new Color(0.42f, 0.72f, 0.36f);
    static readonly Color SeagrassBladeColor = new Color(0.28f, 0.52f, 0.24f);
    static readonly Color KelpCanopyColor = new Color(0.6f, 0.48f, 0.2f);
    static readonly Color KelpStalkColor = new Color(0.4f, 0.31f, 0.13f);
    static readonly Color CoralSpawnColor = new Color(0.98f, 0.74f, 0.68f);
    static readonly Color[] CoralColors =
    {
        new Color(0.93f, 0.44f, 0.52f), // pink
        new Color(0.95f, 0.55f, 0.3f),  // orange
        new Color(0.97f, 0.7f, 0.5f),   // peach
        new Color(0.68f, 0.44f, 0.74f), // violet
    };
    static readonly Color PlanktonColor = new Color(0.72f, 0.92f, 0.82f);

    static readonly Dictionary<long, Mesh> meshes = new Dictionary<long, Mesh>();
    static readonly List<CombineInstance> pieces = new List<CombineInstance>();
    static Mesh cubeMesh;
    static Mesh cylinderMesh;
    static Mesh gemMesh;

    // Hides the food box's own mesh and hangs the biome's plant from it. depth is the water under the food, and
    // surfaceOffset how far the food floats above the water.
    public static void Dress(GameObject food, SeaBiome biome, float depth, float surfaceOffset)
    {
        if (!food.TryGetComponent(out MeshRenderer foodRenderer)) return;

        Material material = foodRenderer.sharedMaterial;
        foodRenderer.enabled = false;

        // Neighbouring plants differ in variant, turn and coral colour.
        Vector3 position = food.transform.position;
        int hash = Hash(position.x, position.z);
        int variant = hash & (Variants - 1);
        Quaternion turn = Quaternion.Euler(0f, (hash >> 2 & 1023) * (360f / 1024f), 0f);

        // The food box is scaled; this unrotated holder undoes that, so the parts below are in metres.
        Vector3 foodScale = food.transform.lossyScale;
        Transform holder = new GameObject("Look").transform;
        holder.gameObject.layer = food.layer;
        holder.SetParent(food.transform, false);
        holder.localScale = new Vector3(1f / foodScale.x, 1f / foodScale.y, 1f / foodScale.z);

        float surface = -surfaceOffset;
        float bed = surface - Mathf.Max(0f, depth);
        int depthSteps = Mathf.Max(1, Mathf.RoundToInt(depth / DepthStep));
        switch (biome)
        {
            case SeaBiome.SeagrassMeadow:
                AddPart(holder, "Leaves", SharedMesh(biome, 0, variant, 0), surface, turn, material, SeagrassLeafColor);
                AddPart(holder, "Blades", SharedMesh(biome, 1, variant, depthSteps), surface, turn, material,
                        SeagrassBladeColor);
                break;
            case SeaBiome.KelpForest:
                AddPart(holder, "Canopy", SharedMesh(biome, 0, variant, 0), surface, turn, material, KelpCanopyColor);
                AddPart(holder, "Stalk", SharedMesh(biome, 1, variant, depthSteps), surface, turn, material,
                        KelpStalkColor);
                break;
            case SeaBiome.ColdWaterReef:
                AddPart(holder, "Spawn", SharedMesh(biome, 0, variant, 0), surface, turn, material, CoralSpawnColor);
                AddPart(holder, "Coral", SharedMesh(biome, 1, variant, 0), bed, turn, material,
                        CoralColors[(hash >> 12) & 3]);
                break;
            default:
                AddPart(holder, "Plankton", SharedMesh(SeaBiome.OpenSea, 0, variant, 0), surface, turn, material,
                        PlanktonColor);
                break;
        }
    }

    static void AddPart(Transform holder, string partName, Mesh mesh, float height, Quaternion turn, Material material,
                        Color color)
    {
        GameObject part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
        part.layer = holder.gameObject.layer;
        part.transform.SetParent(holder, false);
        part.transform.localPosition = new Vector3(0f, height, 0f);
        part.transform.localRotation = turn;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // Small, many and mostly under water: shadows would cost more than they show.
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(block);
    }

    // Part 0 floats at the surface; part 1 is below it. Seagrass blades and kelp stalks hang from the surface down
    // to the bed, depthSteps half metres; corals stand on the bed.
    static Mesh SharedMesh(SeaBiome biome, int part, int variant, int depthSteps)
    {
        long key = (((long)biome * 2 + part) * Variants + variant) * 4096 + depthSteps;
        if (meshes.TryGetValue(key, out Mesh mesh) && mesh != null) return mesh;

        System.Random random = new System.Random((int)(key % 1000003) * 7919 + 17);
        float height = depthSteps * DepthStep;
        pieces.Clear();
        switch (biome)
        {
            case SeaBiome.SeagrassMeadow:
                if (part == 0) FloatingLeaves(random);
                else SeagrassBlades(random, height);
                break;
            case SeaBiome.KelpForest:
                if (part == 0) KelpCanopy(random);
                else KelpStalk(random, height);
                break;
            case SeaBiome.ColdWaterReef:
                if (part == 0) CoralSpawn(random);
                else CoralCluster(random);
                break;
            default:
                Plankton(random);
                break;
        }

        mesh = new Mesh { name = $"{SeaFoodRules.PlantName(biome)} {(part == 0 ? "surface" : "below")} {variant}" };
        mesh.CombineMeshes(pieces.ToArray(), true, true);
        mesh.RecalculateBounds();
        pieces.Clear();
        meshes[key] = mesh;
        return mesh;
    }

    // Seagrass leaves lying on the water, where the blades reach the surface.
    static void FloatingLeaves(System.Random random)
    {
        int leaves = 7 + random.Next(3);
        for (int index = 0; index < leaves; index++)
        {
            float angle = (index + Range(random, -0.3f, 0.3f)) * Mathf.PI * 2f / leaves;
            Vector3 root = Around(random, 0.35f, FloatHeight);
            Vector3 tip = root + Direction(angle) * Range(random, 0.7f, 1.3f);
            AddFlat(root, tip, Range(random, 0.08f, 0.12f), 0.025f);
        }
    }

    // A tuft of blades from the bed, height below the surface, up to the surface.
    static void SeagrassBlades(System.Random random, float height)
    {
        int blades = 9 + random.Next(4);
        for (int index = 0; index < blades; index++)
        {
            Vector3 root = Around(random, 0.6f, -height);
            Vector3 tip = new Vector3(root.x + Range(random, -0.35f, 0.35f), FloatHeight,
                                      root.z + Range(random, -0.35f, 0.35f));
            AddUpright(root, tip, Range(random, 0.06f, 0.09f), 0.02f, Range(random, 0f, 180f));
        }
    }

    // Floats bunched at the top of the stalk, with fronds spreading across the surface.
    static void KelpCanopy(System.Random random)
    {
        AddGem(new Vector3(0f, FloatHeight + 0.08f, 0f), new Vector3(0.34f, 0.26f, 0.34f));
        int floats = 1 + random.Next(2);
        for (int index = 0; index < floats; index++)
        {
            AddGem(Around(random, 0.4f, FloatHeight + 0.05f), Vector3.one * Range(random, 0.16f, 0.24f));
        }

        int fronds = 5 + random.Next(3);
        for (int index = 0; index < fronds; index++)
        {
            float angle = (index + Range(random, -0.25f, 0.25f)) * Mathf.PI * 2f / fronds;
            Vector3 root = new Vector3(0f, FloatHeight, 0f) + Direction(angle) * 0.15f;
            Vector3 tip = root + Direction(angle) * Range(random, 1.2f, 2f) + Vector3.up * 0.02f;
            AddFlat(root, tip, Range(random, 0.22f, 0.32f), 0.03f);
        }
    }

    // The stalk from a holdfast on the bed, height below the surface, up to the surface, with blades along it.
    static void KelpStalk(System.Random random, float height)
    {
        Vector3 bottom = new Vector3(Range(random, -0.4f, 0.4f), -height, Range(random, -0.4f, 0.4f));
        Vector3 top = new Vector3(0f, FloatHeight, 0f);
        AddRod(bottom, top, 0.1f);
        AddGem(bottom + Vector3.up * 0.08f, new Vector3(0.5f, 0.2f, 0.5f));

        int blades = Mathf.Clamp(Mathf.RoundToInt(height / 2f), 1, 6);
        for (int index = 0; index < blades; index++)
        {
            float along = (index + Range(random, 0.3f, 0.7f)) / (blades + 0.5f);
            Vector3 root = Vector3.Lerp(bottom, top, along);
            Vector3 tip = root + Direction(Range(random, 0f, Mathf.PI * 2f)) * Range(random, 0.5f, 0.8f) +
                          Vector3.up * Range(random, 0.8f, 1.2f);
            // Blades stay under the water; the canopy is what shows on top.
            tip.y = Mathf.Min(tip.y, -0.05f);
            AddUpright(root, tip, Range(random, 0.18f, 0.26f), 0.03f, Range(random, 0f, 180f));
        }
    }

    // A slick of coral spawn on the water, marking the reef's food.
    static void CoralSpawn(System.Random random)
    {
        int blobs = 4 + random.Next(3);
        for (int index = 0; index < blobs; index++)
        {
            float size = Range(random, 0.45f, 0.9f);
            AddGem(Around(random, 0.7f, FloatHeight), new Vector3(size, 0.07f, size * Range(random, 0.6f, 1f)));
        }
    }

    // A low mound on the bed with branching arms and round heads, about two metres across.
    static void CoralCluster(System.Random random)
    {
        AddGem(new Vector3(0f, 0.12f, 0f), new Vector3(1.5f, 0.5f, 1.3f));
        int branches = 5 + random.Next(4);
        for (int index = 0; index < branches; index++)
        {
            Vector3 root = Around(random, 0.5f, 0.25f);
            Vector3 tip = root + Direction(Range(random, 0f, Mathf.PI * 2f)) * Range(random, 0.2f, 0.6f) +
                          Vector3.up * Range(random, 0.6f, 1.3f);
            AddRod(root, tip, Range(random, 0.12f, 0.2f));
            AddGem(tip, Vector3.one * Range(random, 0.22f, 0.32f));

            // A shorter arm forks from halfway up.
            Vector3 fork = Vector3.Lerp(root, tip, 0.5f);
            Vector3 forkTip = fork + Direction(Range(random, 0f, Mathf.PI * 2f)) * 0.35f + Vector3.up * 0.4f;
            AddRod(fork, forkTip, 0.1f);
            AddGem(forkTip, Vector3.one * 0.18f);
        }

        int heads = 1 + random.Next(2);
        for (int index = 0; index < heads; index++)
        {
            AddGem(Around(random, 0.7f, 0.3f), new Vector3(0.75f, 0.5f, 0.7f));
        }
    }

    // Specks of plankton drifting on and just under the surface.
    static void Plankton(System.Random random)
    {
        int specks = 9 + random.Next(5);
        for (int index = 0; index < specks; index++)
        {
            AddGem(Around(random, 0.9f, Range(random, -0.1f, 0.12f)), Vector3.one * Range(random, 0.1f, 0.2f));
        }
    }

    static void AddGem(Vector3 centre, Vector3 size)
    {
        Add(Gem(), Matrix4x4.TRS(centre, Quaternion.identity, size));
    }

    // A cylinder between two points (Unity's cylinder is two units tall).
    static void AddRod(Vector3 from, Vector3 to, float thickness)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;
        if (length < 0.001f) return;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
        Add(Cylinder(), Matrix4x4.TRS((from + to) * 0.5f, rotation, new Vector3(thickness, length * 0.5f, thickness)));
    }

    // A thin strip standing between two points, its broad side turned by facing degrees.
    static void AddUpright(Vector3 from, Vector3 to, float width, float thickness, float facing)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;
        if (length < 0.001f) return;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction / length) * Quaternion.Euler(0f, facing, 0f);
        Add(Cube(), Matrix4x4.TRS((from + to) * 0.5f, rotation, new Vector3(width, length, thickness)));
    }

    // A thin strip lying between two points, broad side up.
    static void AddFlat(Vector3 from, Vector3 to, float width, float thickness)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;
        if (length < 0.001f) return;

        Quaternion rotation = Quaternion.LookRotation(direction / length, Vector3.up);
        Add(Cube(), Matrix4x4.TRS((from + to) * 0.5f, rotation, new Vector3(width, thickness, length)));
    }

    static void Add(Mesh mesh, Matrix4x4 transform)
    {
        pieces.Add(new CombineInstance { mesh = mesh, transform = transform });
    }

    static float Range(System.Random random, float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }

    static Vector3 Direction(float angle)
    {
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    // A random point within radius of the centre line, at the given height.
    static Vector3 Around(System.Random random, float radius, float height)
    {
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float distance = radius * Mathf.Sqrt((float)random.NextDouble());
        return new Vector3(Mathf.Cos(angle) * distance, height, Mathf.Sin(angle) * distance);
    }

    static int Hash(float x, float z)
    {
        unchecked
        {
            int hash = Mathf.FloorToInt(x * 4f) * 73856093 ^ Mathf.FloorToInt(z * 4f) * 19349663;
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            return (hash ^ hash >> 15) & int.MaxValue;
        }
    }

    static Mesh Cube()
    {
        if (cubeMesh == null) cubeMesh = BuiltInMesh(PrimitiveType.Cube);
        return cubeMesh;
    }

    static Mesh Cylinder()
    {
        if (cylinderMesh == null) cylinderMesh = BuiltInMesh(PrimitiveType.Cylinder);
        return cylinderMesh;
    }

    static Mesh BuiltInMesh(PrimitiveType shape)
    {
        GameObject temporary = GameObject.CreatePrimitive(shape);
        temporary.SetActive(false);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temporary);
        return mesh;
    }

    // A twenty-sided gem one unit across with flat faces, matching the faceted water and far lighter than a sphere.
    static Mesh Gem()
    {
        if (gemMesh != null) return gemMesh;

        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] corners =
        {
            new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
            new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
            new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f),
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        // Every face gets its own corners, so each is lit flat.
        Vector3[] vertices = new Vector3[faces.Length];
        int[] triangles = new int[faces.Length];
        for (int index = 0; index < faces.Length; index++)
        {
            vertices[index] = corners[faces[index]].normalized * 0.5f;
            triangles[index] = index;
        }

        gemMesh = new Mesh { name = "Gem", vertices = vertices, uv = new Vector2[faces.Length], triangles = triangles };
        gemMesh.RecalculateNormals();
        gemMesh.RecalculateBounds();
        return gemMesh;
    }
}
