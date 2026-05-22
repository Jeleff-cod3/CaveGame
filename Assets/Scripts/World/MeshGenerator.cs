using UnityEngine;

public static class MeshGenerator
{
    public static Mesh GenerateChunkMesh(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        float uvScale
    )
    {
        int verticesPerLine = chunkSize + 1;

        Vector3[] vertices = new Vector3[verticesPerLine * verticesPerLine];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[chunkSize * chunkSize * 6];

        int vertexIndex = 0;

        for (int z = 0; z < verticesPerLine; z++)
        {
            for (int x = 0; x < verticesPerLine; x++)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                float y = worldData.GetHeight(worldX, worldZ);

                vertices[vertexIndex] = new Vector3(x, y, z);

                uvs[vertexIndex] = new Vector2(
                    worldX / uvScale,
                    worldZ / uvScale
                );

                vertexIndex++;
            }
        }

        int triangleIndex = 0;

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int bottomLeft = z * verticesPerLine + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + verticesPerLine;
                int topRight = topLeft + 1;

                triangles[triangleIndex] = bottomLeft;
                triangles[triangleIndex + 1] = topLeft;
                triangles[triangleIndex + 2] = topRight;

                triangles[triangleIndex + 3] = bottomLeft;
                triangles[triangleIndex + 4] = topRight;
                triangles[triangleIndex + 5] = bottomRight;

                triangleIndex += 6;
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