using System.Collections.Generic;
using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(float[,] heightMap, float heightMultiplier, AnimationCurve _heightCurve, int levelOfDetail)
    {
        AnimationCurve heightCurve = new AnimationCurve(_heightCurve.keys);

        int meshSimplificationIncrement =(levelOfDetail == 0) ? 1 : levelOfDetail * 2;

        int borderedSize = heightMap.GetLength(0);
        int meshSize = borderedSize - 2 * meshSimplificationIncrement;
        int meshSizeUnsimplified = borderedSize -2;

        float topLeftX = (meshSizeUnsimplified - 1) / -2f;
        float topLeftZ = (meshSizeUnsimplified - 1) / 2f;

        int verticesPerLine = (meshSize-1)/ meshSimplificationIncrement + 1;

        MeshData meshData = new MeshData(verticesPerLine);

        int[, ] vertexIndicesMap = new int[borderedSize, borderedSize];
        int meshVertexIndex = 0;
        int borderVertexIndex = -1;

        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement)
        {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement)
            {
                bool isBorderVertex = y == 0 || y == borderedSize - 1 || x == 0 || x == borderedSize -1;

                if (isBorderVertex)
                {
                    vertexIndicesMap[x,y] = borderVertexIndex;
                    borderVertexIndex--;
                }

                else
                {
                    vertexIndicesMap[x, y] = meshVertexIndex;
                    meshVertexIndex++;
                }
            }
        }

        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement)
        {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement)
            {
                int vertexIndex = vertexIndicesMap[x,y];
                Vector2 percent = new Vector2((x - meshSimplificationIncrement)/(float)meshSize, (y-meshSimplificationIncrement)/(float)meshSize);
                float height = heightCurve.Evaluate(heightMap[x,y]) * heightMultiplier;
                Vector3 vertexPosition = new Vector3(topLeftX + percent.x * meshSizeUnsimplified, height, topLeftZ - percent.y * meshSizeUnsimplified);

                meshData.AddVertex(vertexPosition, percent, vertexIndex);

                if (x < borderedSize - 1 && y < borderedSize -1)
                {
                    int a = vertexIndicesMap[x,y];
                    int b = vertexIndicesMap[x+meshSimplificationIncrement,y];
                    int c = vertexIndicesMap[x,y+meshSimplificationIncrement];
                    int d = vertexIndicesMap[x+meshSimplificationIncrement,y+meshSimplificationIncrement];
                    meshData.AddTriangle(a, d, c);
                    meshData.AddTriangle(d, a, b);
                }
                vertexIndex++;
            }
        }

        int minCoord = meshSimplificationIncrement;
        int maxCoord = borderedSize - 1 - meshSimplificationIncrement;
        List<int> edgeIndices = new List<int>();

        for (int x = minCoord; x <= maxCoord; x += meshSimplificationIncrement)
        {
            edgeIndices.Add(vertexIndicesMap[x, minCoord]);
        }
        for (int y = minCoord + meshSimplificationIncrement; y <= maxCoord; y += meshSimplificationIncrement)
        {
            edgeIndices.Add(vertexIndicesMap[maxCoord, y]);
        }
        for (int x = maxCoord - meshSimplificationIncrement; x >= minCoord; x -= meshSimplificationIncrement)
        {
            edgeIndices.Add(vertexIndicesMap[x, maxCoord]);
        }
        for (int y = maxCoord - meshSimplificationIncrement; y >= minCoord + meshSimplificationIncrement; y -= meshSimplificationIncrement)
        {
            edgeIndices.Add(vertexIndicesMap[minCoord, y]);
        }

        float skirtDepth = heightMultiplier * 0.5f + 2f;
        meshData.AddSkirt(edgeIndices, skirtDepth);

        return meshData;
    }
}

public class MeshData
{
    Vector3[] vertices;
    int[] triangles;
    Vector2[] uvs;

    Vector3[] borderVertices;
    int[] borderTriangles;

    List<Vector3> skirtVertices = new List<Vector3>();
    List<Vector2> skirtUVs = new List<Vector2>();
    List<int> skirtTriangles = new List<int>();
    List<int> skirtParentIndex = new List<int>();

    int triangleIndex;
    int borderTriangleIndex;

    public MeshData(int verticesPerLine)
    {
        vertices = new Vector3[verticesPerLine * verticesPerLine];
        uvs = new Vector2[verticesPerLine * verticesPerLine];
        triangles = new int[(verticesPerLine-1) * (verticesPerLine-1)*6];

        borderVertices = new Vector3[verticesPerLine * 4 + 4];
        borderTriangles = new int[24 * verticesPerLine];
    }


    public void AddVertex(Vector3 vertexPosition, Vector2 uv, int vertexIndex)
    {
        if (vertexIndex < 0)
        {
            borderVertices[-vertexIndex-1] = vertexPosition;
        }
        else
        {
            vertices[vertexIndex] = vertexPosition;
            uvs[vertexIndex] = uv;
        }
    }
    public void AddTriangle(int a, int b, int c)
    {
        if (a < 0 || b < 0 || c < 0)
        {
            borderTriangles [borderTriangleIndex] = a;
            borderTriangles [borderTriangleIndex + 1] = b;
            borderTriangles[borderTriangleIndex+2] = c;
            borderTriangleIndex+= 3;
        }
        else
        {
            triangles [triangleIndex] = a;
            triangles[triangleIndex + 1] = b;
            triangles[triangleIndex+2] = c;
            triangleIndex += 3;
        }

    }

    public void AddSkirt(List<int> edgeVertexIndices, float skirtDepth)
    {
        int n = edgeVertexIndices.Count;
        int[] skirtIndexFor = new int[n];

        for (int i = 0; i < n; i++)
        {
            int mainIndex = edgeVertexIndices[i];
            Vector3 pos = vertices[mainIndex];
            skirtVertices.Add(new Vector3(pos.x, pos.y - skirtDepth, pos.z));
            skirtUVs.Add(uvs[mainIndex]);
            skirtParentIndex.Add(mainIndex);
            skirtIndexFor[i] = vertices.Length + skirtVertices.Count - 1;
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int mainA = edgeVertexIndices[i];
            int mainB = edgeVertexIndices[next];
            int skirtA = skirtIndexFor[i];
            int skirtB = skirtIndexFor[next];

            skirtTriangles.Add(mainA);
            skirtTriangles.Add(mainB);
            skirtTriangles.Add(skirtA);

            skirtTriangles.Add(mainB);
            skirtTriangles.Add(skirtB);
            skirtTriangles.Add(skirtA);
        }
    }

    Vector3[] CalculateNormals()
    {
        Vector3[] vertexNormals = new Vector3[vertices.Length];
        int triangleCount = triangles.Length/3;
        for (int i = 0; i < triangleCount; i++)
        {
            int normalTriangleIndex = i * 3;
            int vertexIndexA = triangles[normalTriangleIndex];
            int vertexIndexB = triangles[normalTriangleIndex + 1];
            int vertexIndexC = triangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            vertexNormals[vertexIndexA] += triangleNormal;
            vertexNormals[vertexIndexB] += triangleNormal;
            vertexNormals[vertexIndexC] += triangleNormal;
        }

        int bordertTriangleCount = borderTriangles.Length/3;
        for (int i = 0; i < bordertTriangleCount; i++)
        {
            int normalTriangleIndex = i * 3;
            int vertexIndexA = borderTriangles[normalTriangleIndex];
            int vertexIndexB = borderTriangles[normalTriangleIndex + 1];
            int vertexIndexC = borderTriangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            if (vertexIndexA >= 0)
            {
                vertexNormals[vertexIndexA] += triangleNormal;
            }
            if (vertexIndexB >= 0)
            {
                vertexNormals[vertexIndexB] += triangleNormal;
            }
            if (vertexIndexC >= 0)
            {
                vertexNormals[vertexIndexC] += triangleNormal;
            }

        }

        for(int i = 0; i < vertexNormals.Length; i++)
        {
            vertexNormals[i].Normalize();
        }

        return vertexNormals;
    }

    Vector3 SurfaceNormalFromIndices(int indexA, int indexB, int indexC)
    {
        Vector3 pointA = (indexA < 0) ? borderVertices[-indexA-1] : vertices[indexA];
        Vector3 pointB = (indexB < 0) ? borderVertices[-indexB-1] : vertices[indexB];
        Vector3 pointC = (indexC < 0) ? borderVertices[-indexC-1] : vertices[indexC];

        Vector3 sideAB = pointB - pointA;
        Vector3 sideAC = pointC - pointA;
        return Vector3.Cross(sideAB, sideAC).normalized;

    }

    public Mesh CreateMesh()
    {
        Vector3[] terrainNormals = CalculateNormals();

        Vector3[] allVertices = new Vector3[vertices.Length + skirtVertices.Count];
        vertices.CopyTo(allVertices, 0);
        skirtVertices.CopyTo(allVertices, vertices.Length);

        Vector2[] allUVs = new Vector2[uvs.Length + skirtUVs.Count];
        uvs.CopyTo(allUVs, 0);
        skirtUVs.CopyTo(allUVs, uvs.Length);

        int[] allTriangles = new int[triangles.Length + skirtTriangles.Count];
        triangles.CopyTo(allTriangles, 0);
        skirtTriangles.CopyTo(allTriangles, triangles.Length);

        Vector3[] allNormals = new Vector3[allVertices.Length];
        terrainNormals.CopyTo(allNormals, 0);
        for (int i = 0; i < skirtParentIndex.Count; i++)
        {
            allNormals[vertices.Length + i] = terrainNormals[skirtParentIndex[i]];
        }

        Mesh mesh = new Mesh();
        mesh.vertices = allVertices;
        mesh.triangles = allTriangles;
        mesh.uv = allUVs;
        mesh.normals = allNormals;
        return mesh;
    }

}
