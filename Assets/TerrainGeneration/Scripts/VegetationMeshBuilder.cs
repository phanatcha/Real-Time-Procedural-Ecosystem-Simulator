using System.Collections.Generic;
using UnityEngine;

public static class VegetationMeshBuilder
{
    public static Mesh BuildTree(Color trunkColour, Color foliageColour, System.Random rng)
    {
        const int sides = 6;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Color> colors = new List<Color>();

        float trunkHeight = 5f + (float)rng.NextDouble() * 3f;
        AddCylinder(verts, tris, colors, Vector3.zero, trunkHeight, 0.35f, sides, trunkColour);

        int layers = 4;
        float foliageBase = trunkHeight * 0.4f;
        float totalFoliageHeight = trunkHeight * 1.3f + 7f;
        float layerHeight = (totalFoliageHeight - foliageBase) / layers * 1.3f;
        float baseRadius = 3.2f;

        for (int i = 0; i < layers; i++)
        {
            float t = i / (float)(layers - 1);
            float radius = Mathf.Lerp(baseRadius, baseRadius * 0.3f, t);
            float y = foliageBase + i * layerHeight * 0.7f;
            AddCone(verts, tris, colors, new Vector3(0, y, 0), layerHeight, radius, sides, foliageColour);
        }

        return BuildMesh(verts, tris, colors);
    }

    public static Mesh BuildGrassClump(Color colourA, Color colourB, System.Random rng)
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Color> colors = new List<Color>();

        int bladeCount = 4 + rng.Next(0, 3);
        for (int i = 0; i < bladeCount; i++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = (float)rng.NextDouble() * 0.15f;
            Vector3 basePos = new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
            float height = 0.22f + (float)rng.NextDouble() * 0.28f;
            float width = 0.045f;
            float lean = ((float)rng.NextDouble() - 0.5f) * 0.18f;

            Vector3 bladeDir = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * width;
            Vector3 tip = basePos + new Vector3(Mathf.Cos(angle) * lean, height, Mathf.Sin(angle) * lean);

            int vi = verts.Count;
            verts.Add(basePos - bladeDir);
            verts.Add(basePos + bladeDir);
            verts.Add(tip);

            Color c = Color.Lerp(colourA, colourB, (float)rng.NextDouble());
            colors.Add(c); colors.Add(c); colors.Add(c);

            tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 1);
        }

        return BuildMesh(verts, tris, colors);
    }

    public static Mesh BuildRock(Color colourA, Color colourB, System.Random rng)
    {
        Vector3[] baseVerts =
        {
            new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f),
            new Vector3(-0.4f, 0.6f, -0.4f), new Vector3(0.4f, 0.7f, -0.4f), new Vector3(0.4f, 0.6f, 0.4f), new Vector3(-0.4f, 0.65f, 0.4f),
        };

        Vector3[] verts = new Vector3[baseVerts.Length];
        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 jitter = new Vector3(
                ((float)rng.NextDouble() - 0.5f) * 0.25f,
                ((float)rng.NextDouble() - 0.5f) * 0.15f,
                ((float)rng.NextDouble() - 0.5f) * 0.25f);
            verts[i] = baseVerts[i] + jitter;
        }

        int[] tris =
        {
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            1, 5, 6, 1, 6, 2,
            2, 6, 7, 2, 7, 3,
            3, 7, 4, 3, 4, 0,
        };

        Color[] colors = new Color[verts.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = Color.Lerp(colourA, colourB, (float)rng.NextDouble());
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddCylinder(List<Vector3> verts, List<int> tris, List<Color> colors, Vector3 baseCentre, float height, float radius, int sides, Color colour)
    {
        int start = verts.Count;
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            verts.Add(baseCentre + new Vector3(x, 0, z));
            verts.Add(baseCentre + new Vector3(x, height, z));
            colors.Add(colour);
            colors.Add(colour);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = start + i * 2;
            int b = start + ((i + 1) % sides) * 2;
            tris.Add(a); tris.Add(a + 1); tris.Add(b + 1);
            tris.Add(a); tris.Add(b + 1); tris.Add(b);
        }
    }

    static void AddCone(List<Vector3> verts, List<int> tris, List<Color> colors, Vector3 baseCentre, float height, float radius, int sides, Color colour)
    {
        int start = verts.Count;
        int apex = start + sides;
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            verts.Add(baseCentre + new Vector3(x, 0, z));
            colors.Add(colour);
        }
        verts.Add(baseCentre + new Vector3(0, height, 0));
        colors.Add(colour);

        for (int i = 0; i < sides; i++)
        {
            int a = start + i;
            int b = start + (i + 1) % sides;
            tris.Add(a); tris.Add(apex); tris.Add(b);
        }
    }

    static Mesh BuildMesh(List<Vector3> verts, List<int> tris, List<Color> colors)
    {
        Mesh mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
