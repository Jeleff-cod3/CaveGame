using UnityEngine;

public static class MeshGenerator
{
    public static Mesh GenerateTerrainMesh(
        float[,] heightMap,
        float heightMultiplier,
        AnimationCurve heightCurve,
        float uvScale = 20f
    )
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        if (heightCurve == null || heightCurve.length == 0)
        {
            heightCurve = AnimationCurve.Linear(0, 0, 1, 1);
        }

        AnimationCurve curve = new AnimationCurve(heightCurve.keys);

        Vector3[] vertices = new Vector3[width * height];
        Vector2[] uvs = new Vector2[width * height];
        int[] triangles = new int[(width - 1) * (height - 1) * 6];

        int vertexIndex = 0;
        int triangleIndex = 0;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float y = curve.Evaluate(heightMap[x, z]) * heightMultiplier;

                vertices[vertexIndex] = new Vector3(x, y, z);

                uvs[vertexIndex] = new Vector2(
                    x / uvScale,
                    z / uvScale
                );

                if (x < width - 1 && z < height - 1)
                {
                    triangles[triangleIndex] = vertexIndex;
                    triangles[triangleIndex + 1] = vertexIndex + width + 1;
                    triangles[triangleIndex + 2] = vertexIndex + width;

                    triangles[triangleIndex + 3] = vertexIndex;
                    triangles[triangleIndex + 4] = vertexIndex + 1;
                    triangles[triangleIndex + 5] = vertexIndex + width + 1;

                    triangleIndex += 6;
                }

                vertexIndex++;
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}