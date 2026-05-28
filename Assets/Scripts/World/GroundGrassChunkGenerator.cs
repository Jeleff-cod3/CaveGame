using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class GroundGrassChunkGenerator
{
    public static Mesh GenerateGroundGrassMesh(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        GroundGrassSettings settings
    )
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector2> uv2s = new List<Vector2>();
        List<Color> colors = new List<Color>();

        System.Random random = new System.Random(seed ^ startX * 92837111 ^ startZ * 689287499);

        for (int z = 0; z <= chunkSize; z += settings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += settings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ)) continue;

                TerrainZone zone = worldData.GetZone(worldX, worldZ);

                if (IsTooSteep(worldData, worldX, worldZ, settings.maxSlopeAngle)) continue;

                float zoneDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise((worldX + seed * 3.17f) / settings.largePatchScale, (worldZ - seed * 2.41f) / settings.largePatchScale);
                float mediumPatch = Mathf.PerlinNoise((worldX - seed * 5.83f) / settings.mediumPatchScale, (worldZ + seed * 4.91f) / settings.mediumPatchScale);
                float smallPatch = Mathf.PerlinNoise((worldX + seed * 1.77f) / settings.smallPatchScale, (worldZ - seed * 7.33f) / settings.smallPatchScale);

                float coverageNoise = Mathf.Lerp(largePatch, mediumPatch, 0.45f);
                coverageNoise = Mathf.Lerp(coverageNoise, smallPatch, 0.25f);

                float barePatchNoise = Mathf.PerlinNoise((worldX - seed * 0.77f) / (settings.largePatchScale * 0.5f), (worldZ + seed * 1.29f) / (settings.largePatchScale * 0.5f));
                float densityMultiplier = Mathf.Lerp(1f - settings.patchContrast, 1f + settings.patchContrast, coverageNoise);

                float finalDensity = Mathf.Clamp01(zoneDensity * densityMultiplier);
                if (barePatchNoise < settings.barePatchChance) finalDensity *= 0.18f;

                if ((float)random.NextDouble() > finalDensity) continue;

                float jitterRange = settings.spacing * settings.pointJitter;
                float tuftX = worldX + Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());
                float tuftZ = worldZ + Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());
                float tuftY = worldData.GetHeight(Mathf.RoundToInt(tuftX), Mathf.RoundToInt(tuftZ)) + settings.yOffset;

                int bladeCount = random.Next(Mathf.Max(5, settings.minBladesPerTuft), Mathf.Max(settings.minBladesPerTuft, settings.maxBladesPerTuft) + 1);
                float tuftRadius = Mathf.Lerp(settings.minTuftRadius, settings.maxTuftRadius, (float)random.NextDouble());
                float baseAngle = (float)random.NextDouble() * 360f;

                for (int i = 0; i < bladeCount; i++)
                {
                    Vector2 offset = RandomInsideCircle(random) * tuftRadius;
                    float bladeX = tuftX + offset.x;
                    float bladeZ = tuftZ + offset.y;
                    float bladeY = worldData.GetHeight(Mathf.RoundToInt(bladeX), Mathf.RoundToInt(bladeZ)) + settings.yOffset;

                    Vector3 bladePos = new Vector3(bladeX - startX, bladeY, bladeZ - startZ);
                    float bladeHeight = Mathf.Lerp(settings.minBladeHeight, settings.maxBladeHeight, (float)random.NextDouble()) * Mathf.Lerp(0.85f, 1f, coverageNoise);
                    float bladeWidth = Mathf.Lerp(settings.minBladeWidth, settings.maxBladeWidth, (float)random.NextDouble());
                    float bladeRotation = baseAngle + (360f / bladeCount) * i + Mathf.Lerp(-25f, 25f, (float)random.NextDouble());

                    Color bladeColor = GetGrassColor(bladeX, bladeZ, seed, coverageNoise, zone, settings, (float)random.NextDouble());

                    AddBladeQuad(vertices, triangles, uvs, uv2s, colors, bladePos, bladeWidth, bladeHeight, bladeRotation, bladeColor);
                }
            }
        }

        Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Vector2 RandomInsideCircle(System.Random r)
    {
        float t = (float)r.NextDouble() * Mathf.PI * 2f;
        float u = (float)r.NextDouble() + (float)r.NextDouble();
        float radius = u > 1 ? 2 - u : u;
        return new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius;
    }

    private static float GetZoneDensity(TerrainZone zone, GroundGrassSettings settings)
    {
        switch (zone)
        {
            case TerrainZone.Resource: return settings.resourceDensity * 1.2f; // denser in resource area
            case TerrainZone.Transition: return settings.transitionDensity;
            case TerrainZone.Arena: return settings.arenaDensity * 0.8f; // slightly sparser
            case TerrainZone.Border: return settings.borderDensity;
            default: return 0f;
        }
    }

    private static bool IsTooSteep(WorldData worldData, int x, int z, float maxSlope)
    {
        float center = worldData.GetHeight(x, z);
        float right = worldData.GetHeight(x + 1, z);
        float forward = worldData.GetHeight(x, z + 1);
        Vector3 dx = new Vector3(1f, right - center, 0f);
        Vector3 dz = new Vector3(0f, forward - center, 1f);
        float angle = Vector3.Angle(Vector3.Cross(dz, dx).normalized, Vector3.up);
        return angle > maxSlope;
    }

    private static Color GetGrassColor(float x, float z, int seed, float coverageNoise, TerrainZone zone, GroundGrassSettings settings, float rnd)
    {
        float noise = Mathf.PerlinNoise((x + seed * 6.11f) / settings.colorNoiseScale, (z - seed * 3.07f) / settings.colorNoiseScale);
        float dryVsGreen = Mathf.Lerp(coverageNoise, noise, 0.55f);

        Color c;
        if (dryVsGreen < 0.28f) c = Color.Lerp(settings.dryGrass, settings.warmGrass, dryVsGreen / 0.28f);
        else if (dryVsGreen < 0.65f) c = Color.Lerp(settings.warmGrass, settings.oliveGrass, (dryVsGreen - 0.28f) / 0.37f);
        else c = Color.Lerp(settings.oliveGrass, settings.lushGrass, (dryVsGreen - 0.65f) / 0.35f);

        // blend slightly with terrain color
        if (zone == TerrainZone.Resource) c = Color.Lerp(c, settings.lushGrass, 0.15f);

        return c;
    }

    private static void AddBladeQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, Vector3 pos, float width, float height, float rotation, Color color)
    {
        int start = verts.Count;
        Quaternion rot = Quaternion.Euler(0f, rotation, 0f);
        Vector3 w = rot * Vector3.right * width * 0.5f;
        Vector3 h = Vector3.up * height;

        verts.Add(pos - w);
        verts.Add(pos + w);
        verts.Add(pos + w + h);
        verts.Add(pos - w + h);

        tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
        tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);

        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
        uv2s.Add(Vector2.zero); uv2s.Add(Vector2.zero); uv2s.Add(Vector2.zero); uv2s.Add(Vector2.zero);
        cols.Add(color); cols.Add(color); cols.Add(color); cols.Add(color);
    }
}