using System.Collections.Generic;
using UnityEngine;

public static class GrassChunkGenerator
{
    public static Mesh GenerateGrassMesh(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        GrassSettings settings
    )
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector2> uv2s = new List<Vector2>();
        List<Color> colors = new List<Color>();

        System.Random random = new System.Random(
            seed ^
            startX * 73856093 ^
            startZ * 19349663
        );

        for (int z = 0; z <= chunkSize; z += settings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += settings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ))
                {
                    continue;
                }

                TerrainZone zone = worldData.GetZone(worldX, worldZ);

                //if (zone == TerrainZone.Border)
                //{
                //    continue;
                //}

                float zoneDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 13.37f) / settings.largePatchScale,
                    (worldZ + seed * 7.91f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 5.33f) / settings.smallPatchScale,
                    (worldZ + seed * 2.17f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.35f);
                float density = zoneDensity * Mathf.Lerp(
                    1f - settings.patchContrast,
                    1f,
                    patchValue
                );

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                if (IsTooSteep(worldData, worldX, worldZ, settings.maxSlopeAngle))
                {
                    continue;
                }

                float height = Mathf.Lerp(
                    settings.minHeight,
                    settings.maxHeight,
                    (float)random.NextDouble()
                );

                float width = Mathf.Lerp(
                    settings.minWidth,
                    settings.maxWidth,
                    (float)random.NextDouble()
                );

                float y = worldData.GetHeight(worldX, worldZ) + settings.yOffset;

                Vector3 localPosition = new Vector3(
                    worldX - startX,
                    y,
                    worldZ - startZ
                );

                float randomRotation = (float)random.NextDouble() * 360f;

                Color grassColor = GetGrassColor(patchValue, settings);
                AddGrassTuft(
                    vertices,
                    triangles,
                    uvs,
                    uv2s,
                    colors,
                    localPosition,
                    width,
                    height,
                    randomRotation,
                    grassColor
                );
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetColors(colors);

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        return mesh;
    }

    private static float GetZoneDensity(TerrainZone zone, GrassSettings settings)
    {
        switch (zone)
        {
            case TerrainZone.Arena:
                return settings.arenaDensity;

            case TerrainZone.Transition:
                return settings.transitionDensity;

            case TerrainZone.Resource:
                return settings.resourceDensity;

            case TerrainZone.Border:
                return settings.borderDensity;

            default:
                return 0f;
        }
    }

    private static Color GetGrassColor(float noise, GrassSettings settings)
    {
        if (noise < 0.45f)
        {
            return Color.Lerp(settings.dryGrass, settings.oliveGrass, noise / 0.45f);
        }

        return Color.Lerp(settings.oliveGrass, settings.greenGrass, (noise - 0.45f) / 0.55f);
    }

    private static bool IsTooSteep(
        WorldData worldData,
        int x,
        int z,
        float maxSlopeAngle
    )
    {
        float center = worldData.GetHeight(x, z);
        float right = worldData.GetHeight(x + 1, z);
        float forward = worldData.GetHeight(x, z + 1);

        Vector3 dx = new Vector3(1f, right - center, 0f);
        Vector3 dz = new Vector3(0f, forward - center, 1f);

        Vector3 normal = Vector3.Cross(dz, dx).normalized;

        float angle = Vector3.Angle(normal, Vector3.up);

        return angle > maxSlopeAngle;
    }

    private static void AddGrassTuft(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        List<Vector2> uv2s,
        List<Color> colors,
        Vector3 position,
        float width,
        float height,
        float rotationDegrees,
        Color color
    )
    {
        AddGrassQuad(
            vertices,
            triangles,
            uvs,
            uv2s,
            colors,
            position,
            width,
            height,
            rotationDegrees,
            color
        );

        AddGrassQuad(
            vertices,
            triangles,
            uvs,
            uv2s,
            colors,
            position,
            width,
            height,
            rotationDegrees + 90f,
            color
        );
    }

    private static void AddGrassQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        List<Vector2> uv2s,
        List<Color> colors,
        Vector3 position,
        float width,
        float height,
        float rotationDegrees,
        Color color
    )
    {
        int startIndex = vertices.Count;

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
        Vector3 right = rotation * Vector3.right;

        Vector3 bottomLeft = position - right * width * 0.5f;
        Vector3 bottomRight = position + right * width * 0.5f;
        Vector3 topLeft = bottomLeft + Vector3.up * height;
        Vector3 topRight = bottomRight + Vector3.up * height;

        vertices.Add(bottomLeft);
        vertices.Add(bottomRight);
        vertices.Add(topRight);
        vertices.Add(topLeft);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        // uv2.x controls wind strength.
        // bottom = 0, top = 1
        uv2s.Add(new Vector2(0f, 0f));
        uv2s.Add(new Vector2(0f, 0f));
        uv2s.Add(new Vector2(1f, 0f));
        uv2s.Add(new Vector2(1f, 0f));

        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 1);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 3);
        triangles.Add(startIndex + 2);
    }
}