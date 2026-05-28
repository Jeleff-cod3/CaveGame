using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        List<Color> colors = new List<Color>();

        System.Random random = new System.Random(seed ^ startX * 73856093 ^ startZ * 19349663);

        for (int z = 0; z <= chunkSize; z += settings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += settings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ)) continue;

                TerrainZone zone = worldData.GetZone(worldX, worldZ);

                float zoneDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise((worldX + seed * 13.37f) / settings.largePatchScale,
                                                     (worldZ + seed * 7.91f) / settings.largePatchScale);
                float smallPatch = Mathf.PerlinNoise((worldX - seed * 5.33f) / settings.smallPatchScale,
                                                     (worldZ + seed * 2.17f) / settings.smallPatchScale);

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.35f);
                float density = Mathf.Clamp01(zoneDensity * Mathf.Lerp(1f - settings.patchContrast, 1f, patchValue));

                if ((float)random.NextDouble() > density) continue;

                if (IsTooSteep(worldData, worldX, worldZ, settings.maxSlopeAngle)) continue;

                float y = worldData.GetHeight(worldX, worldZ) + settings.yOffset;

                Vector3 localPos = new Vector3(worldX - startX, y, worldZ - startZ);

                // Determine if tuft is tall or normal
                bool isTall = random.NextDouble() < 0.25; // 25% tall
                int blades = isTall ? random.Next(6, 12) : random.Next(3, 6);
                float minH = isTall ? settings.minHeight * 1.5f : settings.minHeight;
                float maxH = isTall ? settings.maxHeight * 1.5f : settings.maxHeight;

                for (int i = 0; i < blades; i++)
                {
                    Vector2 offset = RandomInsideCircle(random) * 0.25f;
                    Vector3 bladePos = localPos + new Vector3(offset.x, 0f, offset.y);
                    float height = Mathf.Lerp(minH, maxH, (float)random.NextDouble());
                    float width = Mathf.Lerp(settings.minWidth, settings.maxWidth, (float)random.NextDouble());
                    float rotation = (float)random.NextDouble() * 360f;

                    Color color = GetGrassColor(patchValue, settings, random, worldX, worldZ);

                    AddGrassBlade(vertices, triangles, colors, bladePos, width, height, rotation, color);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static float GetZoneDensity(TerrainZone zone, GrassSettings settings)
    {
        switch (zone)
        {
            case TerrainZone.Arena: return settings.arenaDensity;
            case TerrainZone.Transition: return settings.transitionDensity;
            case TerrainZone.Resource: return settings.resourceDensity;
            case TerrainZone.Border: return settings.borderDensity;
            default: return 0f;
        }
    }

    private static bool IsTooSteep(WorldData worldData, int x, int z, float maxSlopeAngle)
    {
        float center = worldData.GetHeight(x, z);
        float right = worldData.GetHeight(x + 1, z);
        float forward = worldData.GetHeight(x, z + 1);
        Vector3 dx = new Vector3(1f, right - center, 0f);
        Vector3 dz = new Vector3(0f, forward - center, 1f);
        return Vector3.Angle(Vector3.Cross(dz, dx).normalized, Vector3.up) > maxSlopeAngle;
    }

    private static Vector2 RandomInsideCircle(System.Random random)
    {
        float t = (float)random.NextDouble() * Mathf.PI * 2f;
        float u = (float)random.NextDouble() + (float)random.NextDouble();
        float r = u > 1 ? 2 - u : u;
        return new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
    }

    private static Color GetGrassColor(float patchValue, GrassSettings settings, System.Random random, float worldX, float worldZ)
    {
        Color baseColor;
        if (patchValue < 0.45f)
            baseColor = Color.Lerp(settings.dryGrass, settings.oliveGrass, patchValue / 0.45f);
        else
            baseColor = Color.Lerp(settings.oliveGrass, settings.greenGrass, (patchValue - 0.45f) / 0.55f);

        // Micro Perlin noise for individual blade variation
        float microNoise = Mathf.PerlinNoise((worldX + 345.13f) * 0.13f, (worldZ - 217.42f) * 0.13f);
        baseColor = Color.Lerp(baseColor, settings.greenGrass, microNoise * 0.3f);

        // Random tint
        float tint = ((float)random.NextDouble() - 0.5f) * 0.15f;
        baseColor.r = Mathf.Clamp01(baseColor.r + tint);
        baseColor.g = Mathf.Clamp01(baseColor.g + tint);
        baseColor.b = Mathf.Clamp01(baseColor.b + tint);

        return baseColor;
    }

    private static void AddGrassBlade(List<Vector3> verts, List<int> tris, List<Color> colors,
                                      Vector3 pos, float width, float height, float rotationDegrees, Color color)
    {
        int start = verts.Count;
        Quaternion rot = Quaternion.Euler(0f, rotationDegrees, 0f);
        Vector3 right = rot * Vector3.right * width * 0.5f;
        Vector3 top = Vector3.up * height;

        verts.Add(pos - right);
        verts.Add(pos + right);
        verts.Add(pos + right + top);
        verts.Add(pos - right + top);

        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);

        tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
        tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
    }
}