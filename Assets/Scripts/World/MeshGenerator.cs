using UnityEngine;

public static class MeshGenerator
{
    public static Mesh GenerateChunkMesh(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        float uvScale,
        int seed,
        TerrainColorSettings colorSettings
    )
    {
        int verticesPerLine = chunkSize + 1;

        Vector3[] vertices = new Vector3[verticesPerLine * verticesPerLine];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
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

                colors[vertexIndex] = GetTerrainColor(
                    worldData,
                    worldX,
                    worldZ,
                    seed,
                    colorSettings
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
        mesh.colors = colors;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Color GetTerrainColor(
        WorldData worldData,
        int worldX,
        int worldZ,
        int seed,
        TerrainColorSettings settings
    )
    {
        if (settings == null)
        {
            return Color.white;
        }

        float largeNoise = Mathf.PerlinNoise(
            (worldX + seed * 11.13f) / settings.largePatchScale,
            (worldZ + seed * 17.71f) / settings.largePatchScale
        );

        float mediumNoise = Mathf.PerlinNoise(
            (worldX - seed * 5.41f) / settings.mediumPatchScale,
            (worldZ + seed * 8.33f) / settings.mediumPatchScale
        );

        float smallNoise = Mathf.PerlinNoise(
            (worldX + seed * 2.19f) / settings.smallPatchScale,
            (worldZ - seed * 3.77f) / settings.smallPatchScale
        );

        float combinedNoise = largeNoise;

        combinedNoise = Mathf.Lerp(
            combinedNoise,
            mediumNoise,
            settings.mediumInfluence
        );

        combinedNoise = Mathf.Lerp(
            combinedNoise,
            smallNoise,
            settings.smallInfluence
        );

        Color baseColor;

        if (combinedNoise < 0.33f)
        {
            baseColor = Color.Lerp(
                settings.lightSand,
                settings.dryYellow,
                combinedNoise / 0.33f
            );
        }
        else if (combinedNoise < 0.66f)
        {
            baseColor = Color.Lerp(
                settings.dryYellow,
                settings.orangeDirt,
                (combinedNoise - 0.33f) / 0.33f
            );
        }
        else
        {
            baseColor = Color.Lerp(
                settings.orangeDirt,
                settings.paleGrass,
                (combinedNoise - 0.66f) / 0.34f
            );
        }

        TerrainZone zone = worldData.GetZone(worldX, worldZ);

        if (zone == TerrainZone.Arena)
        {
            baseColor = Color.Lerp(
                baseColor,
                settings.lightSand,
                settings.arenaPaleness
            );
        }
        else if (zone == TerrainZone.Resource)
        {
            baseColor = Color.Lerp(
                baseColor,
                settings.paleGrass,
                settings.resourceGreenness
            );
        }
        else if (zone == TerrainZone.Border)
        {
            baseColor *= 0.65f;
        }

        float height = worldData.GetHeight(worldX, worldZ);
        float height01 = Mathf.InverseLerp(0f, 30f, height);

        baseColor = Color.Lerp(
            baseColor,
            baseColor * (1f - settings.heightDarkening),
            height01
        );

        baseColor.a = 1f;
        return baseColor;
    }
}