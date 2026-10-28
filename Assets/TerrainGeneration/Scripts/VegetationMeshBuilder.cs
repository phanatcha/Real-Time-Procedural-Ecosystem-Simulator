using System.Collections.Generic;
using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // Norway's summer boreal flowers that GrassField scatters: forest-floor ones first, then edge and meadow ones.
    public enum BorealFlower
    {
        Twinflower,
        ChickweedWintergreen,
        WoodCranesbill,
        Globeflower,
        Harebell,
        Fireweed
    }

    // The summer forest foods ground plants are drawn as (see ForestFoodLooks). Autumn and spring ones join with seasons.
    public enum ForestFood
    {
        Bilberry,
        Raspberry,
        Cloudberry,
        WildStrawberry,
        Chanterelle,
        WoodSorrel
    }

    // Low-poly trees, grass and rocks in a soft, painterly style (after Genshin Impact) for the Custom/Vegetation shader.
    // Vertex colours carry the look: rgb is the colour, already graded from dark insides to bright tips, and alpha is how
    // flexible the part is, which the shader uses for wind sway and for light shining through leaves (0 for rock and
    // trunks, up to 1 for treetops). Foliage normals point away from the middle of each clump rather than off each
    // face, so a whole tier or clump shades as one soft volume.
    public static class VegetationMeshBuilder
    {
        // Norway spruce: tall, narrow and dark, a short bare trunk under drooping, scalloped tiers that taper to a thin
        // spike. height 0 picks one between 13 and 17 m.
        // far: the same spruce for distant chunks, about a fifth of the triangles: plainer tiers and a four-sided trunk.
        public static Mesh BuildTree(Color trunkColour, Color foliageColour, System.Random rng, float height = 0f,
                                     bool far = false)
        {
            MeshParts mesh = new MeshParts();
            if (height <= 0f) height = SpruceHeight(rng);
            mesh.AddCylinder(Vector3.zero, height * 0.6f, 0.24f, 0.12f, far ? 4 : 8, trunkColour, 0.03f);

            const int tiers = 8;
            float tierHeight = 2.7f;
            float step = (height - SpruceSkirt - tierHeight * 0.85f) / (tiers - 1);
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float radius = SpruceRadius(t) * (0.92f + 0.16f * (float)rng.NextDouble());
                Vector3 centre = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.2f, SpruceSkirt + i * step,
                                             ((float)rng.NextDouble() - 0.5f) * 0.2f);
                // Lower tiers sit in the shade of the ones above; higher ones sway more.
                Color tierColour = foliageColour * Mathf.Lerp(0.82f, 1.08f, t);
                mesh.AddTier(centre, tierHeight, radius, tierColour, Mathf.Lerp(0.35f, 0.9f, t), rng, far);
            }

            mesh.AddTier(new Vector3(0f, height - tierHeight * 0.85f, 0f), tierHeight * 0.85f, 0.45f,
                         foliageColour * 1.08f, 1f, rng, far);
            return mesh.Build();
        }

        public static float SpruceHeight(System.Random rng) => 13f + (float)rng.NextDouble() * 4f;

        // Height of a spruce's lowest tier, and the radius of the tier at a fraction of the way up the tiers (0 is the
        // lowest, 1 the top). FoodSpawner uses these to hang fresh shoots on the outside of a tree.
        public const float SpruceSkirt = 1.4f;
        public static float SpruceRadius(float tierFraction) => Mathf.Lerp(2.6f, 0.6f, Mathf.Clamp01(tierFraction));

        // Bright new growth at the tips of a spruce's branches, centred on the origin: the leaves FoodSpawner's tall
        // food stands for.
        public static Mesh BuildShoots(Color colour, System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            int tufts = 5 + rng.Next(3);
            for (int i = 0; i < tufts; i++)
            {
                Vector3 centre = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.2f, ((float)rng.NextDouble() - 0.5f) * 0.8f,
                                             ((float)rng.NextDouble() - 0.5f) * 1.2f);
                float radius = 0.32f + 0.2f * (float)rng.NextDouble();
                mesh.AddClump(centre, new Vector3(radius, radius * 0.8f, radius), colour, 0.7f, 0, rng);
            }

            return mesh.Build();
        }

        // A summer forest food standing on the origin. Bilberries and raspberries are the same berry bushes the island
        // is scattered with (in the vegetation settings' colours), so edible ones look like the rest; the low plants are
        // about 0.6-0.9 m across. Berries and mushrooms are drawn larger than life so they read at the distance animals
        // are watched from, as collectibles are in Genshin Impact.
        public static Mesh BuildForestFood(ForestFood kind, VegetationSettings colours, System.Random rng)
        {
            switch (kind)
            {
                case ForestFood.Bilberry:
                    return BuildBerryBush(colours.bushLeafColour, colours.bilberryColour, false, rng);
                case ForestFood.Raspberry:
                    return BuildBerryBush(colours.bushLeafColour, colours.raspberryColour, true, rng);
            }

            MeshParts mesh = new MeshParts();
            switch (kind)
            {
                case ForestFood.Cloudberry: AddCloudberries(mesh, rng); break;
                case ForestFood.WildStrawberry: AddWildStrawberries(mesh, rng); break;
                case ForestFood.Chanterelle: AddChanterelles(mesh, rng); break;
                default: AddWoodSorrel(mesh, rng); break;
            }

            return mesh.Build();
        }

        // A rounded, leafy berry bush about 1.5 m across, studded with berries on its outside, as in Valheim: a low,
        // wide bilberry (blåbær) bush or, upright, a taller raspberry (bringebær) bush. BushSize scales the whole bush.
        public const float BushSize = 1.5f;

        public static Mesh BuildBerryBush(Color leafColour, Color berryColour, bool upright, System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            float spread = (upright ? 0.3f : 0.4f) * BushSize;
            float height = (upright ? 0.85f : 0.55f) * BushSize;

            // Woody stems rising from the ground into the leaves, so it reads as a shrub standing on the ground.
            Color wood = new Color(0.33f, 0.22f, 0.15f);
            int stems = 3 + rng.Next(3);
            for (int i = 0; i < stems; i++)
            {
                Vector3 root = Around(rng, spread * 0.35f, 0f);
                mesh.AddCylinder(root, height * 0.45f, 0.025f * BushSize, 0.015f * BushSize, 5, wood, 0.05f);
            }

            // Leaves in a broad, darker lower tier and a smaller, brighter crown, so the outline is a clear dome.
            int clumps = 8 + rng.Next(3);
            Vector3[] centres = new Vector3[clumps];
            Vector3[] radii = new Vector3[clumps];
            for (int i = 0; i < clumps; i++)
            {
                bool crown = i < 3;
                float radius = ((crown ? 0.17f : upright ? 0.19f : 0.23f) + 0.07f * Next(rng)) * BushSize;
                centres[i] = crown ? Around(rng, spread * 0.35f, height * (0.72f + 0.15f * Next(rng)))
                                   : Around(rng, spread, height * (0.4f + 0.2f * Next(rng)));
                radii[i] = new Vector3(radius, radius * 0.8f, radius);
                Color shade = leafColour * (crown ? 1.1f : 0.85f) * (0.92f + 0.16f * Next(rng));
                mesh.AddClump(centres[i], radii[i], shade, 0.3f, 0, rng);
            }

            // Leafy sprigs poking out of the lower tier break up the outline.
            int sprigs = 5 + rng.Next(3);
            for (int i = 0; i < sprigs; i++)
            {
                int clump = 3 + rng.Next(clumps - 3);
                float angle = Next(rng) * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                mesh.AddLeaf(centres[clump] + outward * (radii[clump].x * 0.8f), outward, 0.18f * BushSize, 0.07f * BushSize,
                             0.06f * BushSize, leafColour * 1.15f, 0.35f);
            }

            // Berries on the outer and upper faces, large and bright enough to read against the leaves.
            int berries = 30 + rng.Next(11);
            float berrySize = (upright ? 0.065f : 0.055f) * BushSize;
            for (int i = 0; i < berries; i++)
            {
                int clump = rng.Next(clumps);
                Vector3 outward = new Vector3(Next(rng) - 0.5f, Next(rng) * 0.8f + 0.1f, Next(rng) - 0.5f).normalized;
                Vector3 position = centres[clump] + Vector3.Scale(outward, radii[clump]) * 1.04f;
                float size = berrySize * (0.85f + 0.3f * Next(rng));
                mesh.AddBerry(position, size, upright ? 1.25f : 1f, berryColour * (0.9f + 0.2f * Next(rng)));
            }

            return mesh.Build();
        }

        // Cloudberry (multer): a few single stems, each with broad leaves near the ground and one amber berry on top.
        static void AddCloudberries(MeshParts mesh, System.Random rng)
        {
            Color leaves = new Color(0.27f, 0.45f, 0.17f);
            Color stem = new Color(0.3f, 0.4f, 0.16f);
            Color berry = new Color(0.98f, 0.6f, 0.16f);
            int plants = 2 + rng.Next(3);
            for (int i = 0; i < plants; i++)
            {
                Vector3 root = Around(rng, 0.25f, 0f);
                float height = 0.24f + 0.1f * Next(rng);
                mesh.AddCylinder(root, height, 0.012f, 0.01f, 4, stem, 0.3f);
                int leafCount = 2 + rng.Next(2);
                for (int j = 0; j < leafCount; j++)
                {
                    float angle = Next(rng) * Mathf.PI * 2f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    mesh.AddLeaf(root + Vector3.up * (0.05f + 0.05f * Next(rng)), direction, 0.17f, 0.15f, 0.03f,
                                 leaves * (0.9f + 0.2f * Next(rng)), 0.3f);
                }

                float radius = 0.06f + 0.015f * Next(rng);
                mesh.AddClump(root + Vector3.up * (height + radius * 0.7f), new Vector3(radius, radius * 1.1f, radius),
                              berry, 0.35f, 1, rng);
            }
        }

        // Wild strawberry (markjordbær): a rosette of three-part leaves, small red berries hanging low and a white flower.
        static void AddWildStrawberries(MeshParts mesh, System.Random rng)
        {
            Color leaves = new Color(0.24f, 0.44f, 0.17f);
            Color berry = new Color(0.86f, 0.1f, 0.1f);
            int leafCount = 5 + rng.Next(3);
            for (int i = 0; i < leafCount; i++)
            {
                float angle = (i + 0.3f * Next(rng)) / leafCount * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                mesh.AddTrefoil(direction * 0.12f + Vector3.up * (0.1f + 0.06f * Next(rng)), direction, 0.09f,
                                leaves * (0.9f + 0.2f * Next(rng)), 0.3f, rng);
            }

            int berries = 3 + rng.Next(3);
            for (int i = 0; i < berries; i++)
            {
                Vector3 centre = Around(rng, 0.2f, 0.05f + 0.04f * Next(rng));
                mesh.AddClump(centre, new Vector3(0.035f, 0.05f, 0.035f), berry * (0.9f + 0.2f * Next(rng)), 0.2f, 0, rng);
            }

            mesh.AddFlower(Around(rng, 0.15f, 0.16f), 0.04f, new Color(0.96f, 0.95f, 0.92f), new Color(0.95f, 0.8f, 0.2f));
        }

        // Chanterelles (kantarell): a cluster of golden, funnel-shaped mushrooms.
        static void AddChanterelles(MeshParts mesh, System.Random rng)
        {
            Color cap = new Color(0.98f, 0.66f, 0.16f);
            int mushrooms = 3 + rng.Next(4);
            for (int i = 0; i < mushrooms; i++)
            {
                Vector3 root = Around(rng, 0.2f, 0f);
                float size = 0.7f + 0.5f * Next(rng);
                mesh.AddMushroom(root, 0.16f * size, 0.1f * size, cap * (0.92f + 0.16f * Next(rng)), rng);
            }
        }

        // Wood sorrel (gjøksyre): a carpet of bright clover-like leaves on thin stalks, with a few small white flowers.
        static void AddWoodSorrel(MeshParts mesh, System.Random rng)
        {
            Color leaves = new Color(0.4f, 0.66f, 0.27f);
            int count = 10 + rng.Next(6);
            for (int i = 0; i < count; i++)
            {
                Vector3 root = Around(rng, 0.25f, 0f);
                float height = 0.08f + 0.08f * Next(rng);
                float angle = Next(rng) * Mathf.PI * 2f;
                mesh.AddCylinder(root, height, 0.006f, 0.005f, 3, leaves * 0.7f, 0.3f);
                mesh.AddTrefoil(root + Vector3.up * height, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.07f,
                                leaves * (0.9f + 0.2f * Next(rng)), 0.35f, rng);
            }

            int flowers = 2 + rng.Next(3);
            for (int i = 0; i < flowers; i++)
            {
                mesh.AddFlower(Around(rng, 0.22f, 0.12f + 0.05f * Next(rng)), 0.035f, new Color(0.96f, 0.95f, 0.98f),
                               new Color(0.93f, 0.85f, 0.4f));
            }
        }

        static float Next(System.Random rng) => (float)rng.NextDouble();

        // A random point within reach of the origin's vertical axis, at the given height.
        static Vector3 Around(System.Random rng, float reach, float height)
        {
            float angle = Next(rng) * Mathf.PI * 2f;
            float distance = Mathf.Sqrt(Next(rng)) * reach;
            return new Vector3(Mathf.Cos(angle) * distance, height, Mathf.Sin(angle) * distance);
        }

        // GrassField's grass tuft. Its vertex colours are codes, not colours: red is how far up the blade a vertex is (0
        // at the root, 1 at the tip), which the Custom/Grass Field shader turns into the grass colours. Alpha is
        // flexibility, as everywhere else.
        // A soft clump of a few wide blades fanning out from the middle: coarse enough to sit with the low-poly trees. Its blades are lit as if they
        // faced straight up, like the ground they grow from, so a meadow reads as one soft surface rather than as
        // separate dark blades.
        public static Mesh BuildGrassTuft(System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            int blades = 7 + rng.Next(3);
            for (int i = 0; i < blades; i++)
            {
                Vector3 root = Around(rng, 0.2f, 0f);
                Vector3 lean = root.sqrMagnitude > 0.0001f ? root.normalized : Vector3.forward;
                float twist = (Next(rng) - 0.5f) * 0.8f;
                lean = Quaternion.Euler(0f, twist * Mathf.Rad2Deg, 0f) * lean;
                mesh.AddBlade(root, lean, 0.3f + 0.2f * Next(rng), 0.16f, Color.black, Color.red, 1f);
            }

            return mesh.Build();
        }

        // One of Norway's summer boreal flowers, standing on the origin, in its own colours. Like the berries, they are
        // drawn about twice life size so they read from the camera.
        public static Mesh BuildFlower(BorealFlower kind, System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            Color stem = new Color(0.27f, 0.42f, 0.17f);
            Color leaf = new Color(0.25f, 0.45f, 0.17f);
            switch (kind)
            {
                // Twinflower (linnea): a forked stalk with a pair of small pink nodding bells over round leaves.
                case BorealFlower.Twinflower:
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = (i + 0.4f * Next(rng)) * Mathf.PI * 0.5f;
                        mesh.AddLeaf(Vector3.up * 0.01f, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.06f, 0.05f,
                                     0.01f, leaf * 0.85f, 0.2f);
                    }

                    float height = 0.17f + 0.04f * Next(rng);
                    mesh.AddCylinder(Vector3.zero, height, 0.004f, 0.004f, 3, stem, 0.4f);
                    Color pink = new Color(0.98f, 0.68f, 0.8f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 fork = Vector3.up * height + new Vector3(side * 0.025f, 0f, 0f);
                        mesh.AddBell(fork, new Vector3(side * 0.35f, -1f, 0f), 0.045f, 0.028f, pink, 0.5f);
                    }

                    break;
                }
                // Chickweed wintergreen (skogstjerne): a white seven-pointed star above a ring of glossy leaves.
                case BorealFlower.ChickweedWintergreen:
                {
                    float height = 0.2f + 0.05f * Next(rng);
                    mesh.AddCylinder(Vector3.zero, height, 0.004f, 0.004f, 3, stem, 0.35f);
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i / 6f * Mathf.PI * 2f;
                        mesh.AddLeaf(Vector3.up * (height * 0.6f), new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.09f,
                                     0.035f, 0.015f, leaf, 0.3f);
                    }

                    mesh.AddFlower(Vector3.up * height, 0.045f, new Color(0.98f, 0.98f, 0.96f), new Color(0.95f, 0.88f, 0.45f), 7);
                    break;
                }
                // Wood cranesbill (skogstorkenebb): a leafy plant whose stem branches into several violet flowers.
                case BorealFlower.WoodCranesbill:
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = (i + 0.3f * Next(rng)) / 5f * Mathf.PI * 2f;
                        mesh.AddLeaf(Vector3.up * 0.08f, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.12f, 0.09f,
                                     0.03f, leaf, 0.3f);
                    }

                    Color violet = new Color(0.56f, 0.36f, 0.86f);
                    int flowers = 3 + rng.Next(2);
                    for (int i = 0; i < flowers; i++)
                    {
                        Vector3 head = Around(rng, 0.09f, 0.34f + 0.12f * Next(rng));
                        mesh.AddCylinder(Vector3.zero, head.y, 0.005f, 0.004f, 3, stem, 0.4f);
                        mesh.AddCylinder(new Vector3(0f, head.y * 0.7f, 0f), head.y * 0.3f, 0.004f, 0.004f, 3, stem, 0.5f);
                        mesh.AddFlower(head, 0.05f, violet * (0.92f + 0.16f * Next(rng)), new Color(0.92f, 0.86f, 0.96f));
                    }

                    break;
                }
                // Globeflower (ballblom): a round yellow head on a tall stem over deeply cut leaves.
                case BorealFlower.Globeflower:
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = (i + 0.3f * Next(rng)) * Mathf.PI * 0.5f;
                        mesh.AddLeaf(Vector3.up * 0.06f, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.13f, 0.1f,
                                     0.04f, leaf, 0.3f);
                    }

                    float height = 0.5f + 0.1f * Next(rng);
                    mesh.AddCylinder(Vector3.zero, height, 0.007f, 0.006f, 4, stem, 0.45f);
                    mesh.AddClump(Vector3.up * (height + 0.035f), new Vector3(0.05f, 0.055f, 0.05f),
                                  new Color(1f, 0.8f, 0.16f), 0.5f, 1, rng);
                    break;
                }
                // Harebell (blåklokke): thin stems with blue bells nodding at their tips.
                case BorealFlower.Harebell:
                {
                    Color blue = new Color(0.45f, 0.5f, 0.96f);
                    int stems = 2 + rng.Next(2);
                    for (int i = 0; i < stems; i++)
                    {
                        Vector3 root = Around(rng, 0.05f, 0f);
                        float height = 0.3f + 0.12f * Next(rng);
                        float angle = Next(rng) * Mathf.PI * 2f;
                        Vector3 lean = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        mesh.AddBlade(root, lean, height * 0.6f, 0.012f, leaf * 0.8f, leaf);
                        mesh.AddCylinder(root, height, 0.003f, 0.003f, 3, stem, 0.5f);
                        mesh.AddBell(root + Vector3.up * height, lean * 0.6f + Vector3.down, 0.05f, 0.035f, blue, 0.6f);
                    }

                    break;
                }
                // Fireweed (geitrams): a tall stem with narrow leaves and a spike of magenta flowers, buds at the tip.
                default:
                {
                    float height = 0.85f + 0.2f * Next(rng);
                    mesh.AddCylinder(Vector3.zero, height, 0.009f, 0.006f, 4, stem * 0.9f + new Color(0.1f, 0f, 0f), 0.4f);
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i * 2.4f;
                        mesh.AddLeaf(Vector3.up * (height * (0.15f + i * 0.08f)), new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                                     0.13f, 0.03f, 0.03f, leaf, 0.35f);
                    }

                    Color magenta = new Color(0.86f, 0.3f, 0.68f);
                    int florets = 9 + rng.Next(4);
                    for (int i = 0; i < florets; i++)
                    {
                        float t = i / (float)florets;
                        float angle = i * 2.4f;
                        Vector3 position = new Vector3(Mathf.Cos(angle) * 0.04f, height * (0.62f + 0.33f * t), Mathf.Sin(angle) * 0.04f);
                        if (t < 0.7f)
                        {
                            mesh.AddFlower(position, 0.035f, magenta * (0.92f + 0.16f * Next(rng)), new Color(0.95f, 0.85f, 0.92f), 4);
                        }
                        else
                        {
                            mesh.AddBerry(position, 0.012f, 1.6f, magenta * 0.7f);
                        }
                    }

                    break;
                }
            }

            return mesh.Build();
        }

        // A clump of curved, tapering blades, dark at the root where it meets the ground and bright at the tips.
        public static Mesh BuildGrassClump(Color colourA, Color colourB, System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            int blades = 9 + rng.Next(5);
            for (int i = 0; i < blades; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float spread = (float)rng.NextDouble() * 0.18f;
                Vector3 root = new Vector3(Mathf.Cos(angle) * spread, 0f, Mathf.Sin(angle) * spread);
                float facing = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 lean = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
                float height = 0.5f + (float)rng.NextDouble() * 0.55f;
                Color tip = Color.Lerp(colourA, colourB, 0.5f + 0.5f * (float)rng.NextDouble()) * 1.35f
                            + new Color(0.05f, 0.05f, 0f);
                mesh.AddBlade(root, lean, height, 0.075f, colourA * 0.7f, tip);
            }

            return mesh.Build();
        }

        // A chunky, rounded boulder with flat facets and moss on its upper faces.
        public static Mesh BuildRock(Color colourA, Color colourB, Color mossColour, System.Random rng)
        {
            MeshParts mesh = new MeshParts();
            Vector3 size = new Vector3(0.55f + 0.15f * (float)rng.NextDouble(), 0.38f + 0.12f * (float)rng.NextDouble(),
                                       0.45f + 0.15f * (float)rng.NextDouble());
            mesh.AddBoulder(size, colourA, colourB, mossColour, rng);
            return mesh.Build();
        }

        sealed class MeshParts
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<Color> colours = new List<Color>();
            // Per vertex: xyz is the point a soft normal points away from, w how much of it replaces the face's normal.
            readonly List<Vector4> softNormals = new List<Vector4>();

            int Add(Vector3 position, Color colour, float flexibility, Vector3 pivot, float soft)
            {
                vertices.Add(position);
                colour.a = flexibility;
                colours.Add(colour);
                softNormals.Add(new Vector4(pivot.x, pivot.y, pivot.z, soft));
                return vertices.Count - 1;
            }

            // Wound so the face's normal points away from inside, which makes that the side Unity draws.
            void Triangle(int a, int b, int c, Vector3 inside)
            {
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Vector3 centre = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                if (Vector3.Dot(normal, centre - inside) < 0f) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public void AddCylinder(Vector3 baseCentre, float height, float bottomRadius, float topRadius, int sides,
                                    Color colour, float flexibility)
            {
                int start = vertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i / (float)sides * Mathf.PI * 2f;
                    Vector3 around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Add(baseCentre + around * bottomRadius, colour * 0.85f, 0f, Vector3.zero, 0f);
                    Add(baseCentre + around * topRadius + Vector3.up * height, colour, flexibility, Vector3.zero, 0f);
                }

                for (int i = 0; i < sides; i++)
                {
                    int a = start + i * 2;
                    int b = start + (i + 1) % sides * 2;
                    Vector3 axis = baseCentre + Vector3.up * height * 0.5f;
                    Triangle(a, a + 1, b + 1, axis);
                    Triangle(a, b + 1, b, axis);
                }
            }

            // One spruce tier: a drooping skirt whose rim is scalloped into long and short points, rising to a peak.
            public void AddTier(Vector3 baseCentre, float height, float radius, Color colour, float flexibility,
                                System.Random rng, bool far = false)
            {
                int points = far ? 6 : 10;
                int rimCount = points * 2;
                Vector3 pivot = baseCentre + Vector3.up * (height * 0.2f - 1.2f);
                Color rimColour = colour * 1.3f + new Color(0.03f, 0.04f, 0f);
                Color innerColour = colour * 0.8f;

                int rimStart = vertices.Count;
                for (int i = 0; i < rimCount; i++)
                {
                    float angle = i / (float)rimCount * Mathf.PI * 2f;
                    bool longPoint = i % 2 == 0;
                    float reach = radius * (longPoint ? 1f + 0.12f * (float)rng.NextDouble() : 0.78f);
                    float droop = longPoint ? -0.35f : 0.1f;
                    Vector3 position = baseCentre + new Vector3(Mathf.Cos(angle) * reach, droop, Mathf.Sin(angle) * reach);
                    Add(position, longPoint ? rimColour : colour, flexibility, pivot, 0.7f);
                }

                // Far away the skirt runs straight from the rim to the peak, darkened to the shade of the inner band it skips.
                if (far)
                {
                    int peak = Add(baseCentre + Vector3.up * height, colour * 0.9f, flexibility, pivot, 0.7f);
                    Vector3 within = baseCentre + Vector3.up * height * 0.3f;
                    for (int i = 0; i < rimCount; i++)
                    {
                        Triangle(rimStart + i, peak, rimStart + (i + 1) % rimCount, within);
                    }

                    return;
                }

                int middleStart = vertices.Count;
                for (int i = 0; i < rimCount; i++)
                {
                    float angle = i / (float)rimCount * Mathf.PI * 2f;
                    Vector3 position = baseCentre + new Vector3(Mathf.Cos(angle) * radius * 0.5f, height * 0.42f,
                                                                Mathf.Sin(angle) * radius * 0.5f);
                    Add(position, innerColour, flexibility * 0.7f, pivot, 0.7f);
                }

                int apex = Add(baseCentre + Vector3.up * height, colour * 1.05f, flexibility, pivot, 0.7f);
                Vector3 inside = baseCentre + Vector3.up * height * 0.3f;
                for (int i = 0; i < rimCount; i++)
                {
                    int next = (i + 1) % rimCount;
                    Triangle(rimStart + i, middleStart + i, middleStart + next, inside);
                    Triangle(rimStart + i, middleStart + next, rimStart + next, inside);
                    Triangle(middleStart + i, apex, middleStart + next, inside);
                }
            }

            // A lumpy ball of leaves, bright on top and darker below; subdivisions 1 makes it rounder.
            public void AddClump(Vector3 centre, Vector3 radii, Color colour, float flexibility, int subdivisions,
                                 System.Random rng)
            {
                Sphere(subdivisions, out List<Vector3> corners, out List<int> faces);
                int start = vertices.Count;
                foreach (Vector3 corner in corners)
                {
                    float lump = 0.85f + 0.3f * (float)rng.NextDouble();
                    Vector3 position = centre + Vector3.Scale(corner * lump, radii);
                    float shade = Mathf.Lerp(0.75f, 1.3f, corner.y * 0.5f + 0.5f);
                    Color graded = colour * shade + (corner.y > 0.3f ? new Color(0.03f, 0.04f, 0f) : Color.clear);
                    Add(position, graded, flexibility, centre, 0.8f);
                }

                for (int i = 0; i < faces.Count; i += 3)
                {
                    Triangle(start + faces[i], start + faces[i + 1], start + faces[i + 2], centre);
                }
            }

            // A berry: a small, soft eight-sided bead, stretched upright by elongation (raspberries are taller than wide).
            public void AddBerry(Vector3 centre, float radius, float elongation, Color colour)
            {
                Color shine = colour * 1.25f + new Color(0.05f, 0.05f, 0.05f);
                int top = Add(centre + Vector3.up * (radius * elongation), shine, 0.25f, centre, 0.9f);
                int bottom = Add(centre - Vector3.up * (radius * elongation), colour * 0.7f, 0.25f, centre, 0.9f);
                int start = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI * 0.5f;
                    Add(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, colour, 0.25f, centre, 0.9f);
                }

                for (int i = 0; i < 4; i++)
                {
                    int a = start + i;
                    int b = start + (i + 1) % 4;
                    Triangle(top, a, b, centre);
                    Triangle(bottom, b, a, centre);
                }
            }

            // A flat leaf pointing along direction from its base, widest a third of the way out, with its tip lifted.
            public void AddLeaf(Vector3 baseCentre, Vector3 direction, float length, float width, float lift, Color colour,
                                float flexibility)
            {
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * (width * 0.5f);
                Vector3 pivot = baseCentre - Vector3.up * 2f;
                Vector3 middle = baseCentre + direction * (length * 0.4f) + Vector3.up * (lift * 0.3f);
                int root = Add(baseCentre, colour * 0.85f, flexibility * 0.5f, pivot, 0.7f);
                int left = Add(middle - side, colour, flexibility, pivot, 0.7f);
                int right = Add(middle + side, colour, flexibility, pivot, 0.7f);
                int vein = Add(middle + Vector3.up * (width * 0.08f), colour * 1.1f, flexibility, pivot, 0.7f);
                int tip = Add(baseCentre + direction * length + Vector3.up * lift, colour * 1.15f, flexibility, pivot, 0.7f);
                Vector3 below = middle - Vector3.up;
                Triangle(root, left, vein, below);
                Triangle(root, vein, right, below);
                Triangle(left, tip, vein, below);
                Triangle(vein, tip, right, below);
            }

            // Three rounded leaflets from one point, as on clover-like wood sorrel and strawberry leaves.
            public void AddTrefoil(Vector3 centre, Vector3 direction, float size, Color colour, float flexibility,
                                   System.Random rng)
            {
                float facing = Mathf.Atan2(direction.z, direction.x);
                for (int i = 0; i < 3; i++)
                {
                    float angle = facing + i * Mathf.PI * 2f / 3f + ((float)rng.NextDouble() - 0.5f) * 0.3f;
                    Vector3 leaflet = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    AddLeaf(centre, leaflet, size, size * 0.9f, size * 0.15f, colour, flexibility);
                }
            }

            // A small flat flower facing up: petals (five unless told otherwise) around a coloured centre.
            public void AddFlower(Vector3 centre, float radius, Color petals, Color middle, int petalCount = 5)
            {
                Vector3 pivot = centre - Vector3.up * 2f;
                int heart = Add(centre + Vector3.up * (radius * 0.1f), middle, 0.3f, pivot, 0.8f);
                int start = vertices.Count;
                int points = petalCount * 2;
                for (int i = 0; i < points; i++)
                {
                    float angle = i / (float)points * Mathf.PI * 2f;
                    float reach = i % 2 == 0 ? radius : radius * 0.55f;
                    Add(centre + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach), petals, 0.3f, pivot, 0.8f);
                }

                for (int i = 0; i < points; i++)
                {
                    Triangle(heart, start + i, start + (i + 1) % points, centre - Vector3.up);
                }
            }

            // A bell flower hanging from top along direction: a cone that widens to a mouth with five pointed lobes,
            // paler inside.
            public void AddBell(Vector3 top, Vector3 direction, float length, float mouthRadius, Color colour, float flexibility)
            {
                Vector3 axis = direction.normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 across = Vector3.Cross(axis, side);
                Vector3 middle = top + axis * (length * 0.5f);
                int apex = Add(top, colour * 0.8f, flexibility, middle, 0.6f);
                int start = vertices.Count;
                const int points = 10;
                for (int i = 0; i < points; i++)
                {
                    float angle = i / (float)points * Mathf.PI * 2f;
                    float reach = i % 2 == 0 ? mouthRadius : mouthRadius * 0.8f;
                    float flare = i % 2 == 0 ? length * 1.1f : length;
                    Vector3 position = top + axis * flare + (side * Mathf.Cos(angle) + across * Mathf.Sin(angle)) * reach;
                    Add(position, i % 2 == 0 ? colour * 1.15f : colour, flexibility, middle, 0.6f);
                }

                for (int i = 0; i < points; i++)
                {
                    Triangle(apex, start + i, start + (i + 1) % points, middle - axis * length);
                }
            }

            // A chanterelle: a stem that flares into a funnel-shaped cap with a wavy rim and a hollow in the middle,
            // with paler ridges underneath.
            public void AddMushroom(Vector3 root, float height, float capRadius, Color capColour, System.Random rng)
            {
                Color stem = capColour * 0.95f + new Color(0.03f, 0.05f, 0.08f);
                Color under = Color.Lerp(capColour, Color.white, 0.3f);
                Vector3 stemTop = root + Vector3.up * (height * 0.7f);
                AddCylinder(root, height * 0.7f, capRadius * 0.22f, capRadius * 0.35f, 6, stem, 0f);

                const int points = 9;
                Vector3 hollow = root + Vector3.up * (height * 0.88f);
                int centre = Add(hollow, capColour * 0.85f, 0f, hollow - Vector3.up, 0.5f);
                int stemRing = vertices.Count;
                for (int i = 0; i < points; i++)
                {
                    float angle = i / (float)points * Mathf.PI * 2f;
                    Add(stemTop + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (capRadius * 0.35f), under, 0f,
                        stemTop - Vector3.up, 0.4f);
                }

                int rim = vertices.Count;
                for (int i = 0; i < points; i++)
                {
                    float angle = i / (float)points * Mathf.PI * 2f;
                    float wave = 1f + 0.12f * ((float)rng.NextDouble() - 0.5f);
                    Vector3 edge = root + new Vector3(Mathf.Cos(angle) * capRadius * wave, height * (0.95f + 0.08f * (float)rng.NextDouble()),
                                                      Mathf.Sin(angle) * capRadius * wave);
                    Add(edge, capColour * 1.08f, 0f, hollow - Vector3.up * 0.5f, 0.5f);
                }

                for (int i = 0; i < points; i++)
                {
                    int next = (i + 1) % points;
                    // The hollow top, and the ridged underside from the rim down to the stem.
                    Triangle(centre, rim + i, rim + next, hollow - Vector3.up);
                    Triangle(stemRing + i, rim + next, rim + i, hollow + Vector3.up * height);
                    Triangle(stemRing + i, stemRing + next, rim + next, hollow + Vector3.up * height);
                }
            }

            // A blade of grass in three segments, curving away from its root and narrowing to a point.
            public void AddBlade(Vector3 root, Vector3 lean, float height, float width, Color rootColour, Color tipColour,
                                 float softness = 0.85f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, lean).normalized * (width * 0.5f);
                Vector3 pivot = root - Vector3.up * 3f;
                int previousLeft = -1;
                int previousRight = -1;
                const int segments = 3;
                for (int row = 0; row <= segments; row++)
                {
                    float t = row / (float)segments;
                    Vector3 centre = root + Vector3.up * (height * t) + lean * (height * 0.35f * t * t);
                    Color colour = Color.Lerp(rootColour, tipColour, t);
                    float flexibility = 0.6f * t;
                    if (row == segments)
                    {
                        int tip = Add(centre, colour, flexibility, pivot, softness);
                        Triangle(previousLeft, previousRight, tip, centre - lean);
                        break;
                    }

                    float taper = 1f - t * 0.8f;
                    int left = Add(centre - side * taper, colour, flexibility, pivot, softness);
                    int right = Add(centre + side * taper, colour, flexibility, pivot, softness);
                    if (previousLeft >= 0)
                    {
                        Triangle(previousLeft, previousRight, right, centre - lean);
                        Triangle(previousLeft, right, left, centre - lean);
                    }

                    previousLeft = left;
                    previousRight = right;
                }
            }

            // A rounded boulder with a flat base, built from flat facets (each face has its own corners), softened a
            // little towards the middle. Faces turned to the sky are mossy, faces near the ground darker.
            public void AddBoulder(Vector3 size, Color colourA, Color colourB, Color mossColour, System.Random rng)
            {
                Sphere(1, out List<Vector3> corners, out List<int> faces);
                Vector3[] shaped = new Vector3[corners.Count];
                float lowest = float.MaxValue;
                float highest = float.MinValue;
                for (int i = 0; i < corners.Count; i++)
                {
                    float lump = 0.82f + 0.36f * (float)rng.NextDouble();
                    Vector3 position = Vector3.Scale(corners[i] * lump, size);
                    position.y = Mathf.Max(position.y, -size.y * 0.35f);
                    shaped[i] = position;
                    lowest = Mathf.Min(lowest, position.y);
                    highest = Mathf.Max(highest, position.y);
                }

                // The boulder is moved up to stand on its flat base; this is its middle after the move.
                Vector3 middle = new Vector3(0f, (highest - lowest) * 0.5f, 0f);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    Vector3 a = shaped[faces[i]] - Vector3.up * lowest;
                    Vector3 b = shaped[faces[i + 1]] - Vector3.up * lowest;
                    Vector3 c = shaped[faces[i + 2]] - Vector3.up * lowest;
                    Vector3 facing = Vector3.Cross(b - a, c - a).normalized;
                    if (Vector3.Dot(facing, (a + b + c) / 3f - middle) < 0f) facing = -facing;

                    Color colour = Color.Lerp(colourA, colourB, (float)rng.NextDouble());
                    colour = Color.Lerp(colour, mossColour, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.85f, facing.y)) * 0.85f);
                    colour *= Mathf.Lerp(0.78f, 1f, Mathf.InverseLerp(-0.3f, 0.3f, facing.y));
                    int ia = Add(a, colour, 0f, middle, 0.25f);
                    int ib = Add(b, colour, 0f, middle, 0.25f);
                    int ic = Add(c, colour, 0f, middle, 0.25f);
                    Triangle(ia, ib, ic, middle);
                }
            }

            public Mesh Build()
            {
                Mesh mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.SetColors(colours);
                mesh.RecalculateNormals();

                Vector3[] normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector4 soft = softNormals[i];
                    if (soft.w <= 0f) continue;
                    Vector3 away = (vertices[i] - (Vector3)soft).normalized;
                    normals[i] = Vector3.Lerp(normals[i], away, soft.w).normalized;
                }

                mesh.normals = normals;
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // A unit icosahedron, with each triangle split into four subdivisions times and pushed back onto the sphere.
        static void Sphere(int subdivisions, out List<Vector3> corners, out List<int> faces)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            corners = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < corners.Count; i++) corners[i] = corners[i].normalized;
            faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            for (int level = 0; level < subdivisions; level++)
            {
                Dictionary<long, int> midpoints = new Dictionary<long, int>();
                List<int> finer = new List<int>(faces.Count * 4);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i];
                    int b = faces[i + 1];
                    int c = faces[i + 2];
                    int ab = Midpoint(a, b, corners, midpoints);
                    int bc = Midpoint(b, c, corners, midpoints);
                    int ca = Midpoint(c, a, corners, midpoints);
                    finer.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                faces = finer;
            }
        }

        static int Midpoint(int a, int b, List<Vector3> corners, Dictionary<long, int> midpoints)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midpoints.TryGetValue(key, out int index)) return index;
            corners.Add(((corners[a] + corners[b]) * 0.5f).normalized);
            midpoints[key] = corners.Count - 1;
            return corners.Count - 1;
        }
    }
}
