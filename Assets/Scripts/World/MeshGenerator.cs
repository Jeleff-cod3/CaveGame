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

        TerrainZone zone = worldData.GetZone(worldX, worldZ);
        Color arenaColor = SamplePalette(
            combinedNoise,
            settings.arenaDust,
            settings.arenaGold,
            settings.arenaStraw
        );
        Color resourceColor = SamplePalette(
            combinedNoise,
            settings.resourceMoss,
            settings.resourceGrass,
            settings.resourceBrightGrass
        );
        Color transitionColor = SamplePalette(
            combinedNoise,
            settings.transitionEarth,
            settings.transitionBrush,
            settings.transitionOlive
        );

        Color baseColor;

        if (zone == TerrainZone.Arena)
        {
            baseColor = Color.Lerp(
                transitionColor,
                arenaColor,
                settings.arenaTintStrength
            );
        }
        else if (zone == TerrainZone.Resource)
        {
            baseColor = Color.Lerp(
                transitionColor,
                resourceColor,
                settings.resourceTintStrength
            );
        }
        else if (zone == TerrainZone.Transition)
        {
            baseColor = Color.Lerp(
                arenaColor,
                resourceColor,
                Mathf.Lerp(
                    settings.transitionBlendStrength * 0.55f,
                    settings.transitionBlendStrength,
                    mediumNoise
                )
            );
        }
        else if (zone == TerrainZone.Border)
        {
            baseColor = Color.Lerp(
                transitionColor,
                settings.borderColor,
                settings.borderTintStrength
            );
        }
        else
        {
            baseColor = transitionColor;
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

    private static Color SamplePalette(
        float value,
        Color low,
        Color mid,
        Color high
    )
    {
        if (value < 0.5f)
        {
            return Color.Lerp(low, mid, value / 0.5f);
        }

        return Color.Lerp(mid, high, (value - 0.5f) / 0.5f);
    }
}
