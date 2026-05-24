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

        System.Random random = new System.Random(
            seed ^
            startX * 92837111 ^
            startZ * 689287499
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

                if (IsTooSteep(worldData, worldX, worldZ, settings.maxSlopeAngle))
                {
                    continue;
                }

                float zoneDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 3.17f) / settings.largePatchScale,
                    (worldZ - seed * 2.41f) / settings.largePatchScale
                );

                float mediumPatch = Mathf.PerlinNoise(
                    (worldX - seed * 5.83f) / settings.mediumPatchScale,
                    (worldZ + seed * 4.91f) / settings.mediumPatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX + seed * 1.77f) / settings.smallPatchScale,
                    (worldZ - seed * 7.33f) / settings.smallPatchScale
                );

                float coverageNoise = largePatch;
                coverageNoise = Mathf.Lerp(coverageNoise, mediumPatch, 0.45f);
                coverageNoise = Mathf.Lerp(coverageNoise, smallPatch, 0.20f);

                float barePatchNoise = Mathf.PerlinNoise(
                    (worldX - seed * 0.77f) / (settings.largePatchScale * 0.5f),
                    (worldZ + seed * 1.29f) / (settings.largePatchScale * 0.5f)
                );

                float densityMultiplier = Mathf.Lerp(
                    1f - settings.patchContrast,
                    1f + settings.patchContrast,
                    coverageNoise
                );

                float finalDensity = Mathf.Clamp01(zoneDensity * densityMultiplier);

                if (barePatchNoise < settings.barePatchChance)
                {
                    finalDensity *= 0.18f;
                }

                if ((float)random.NextDouble() > finalDensity)
                {
                    continue;
                }

                float jitterRange = settings.spacing * settings.pointJitter;
                float tuftCenterWorldX = worldX + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );
                float tuftCenterWorldZ = worldZ + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                int centerSampleX = Mathf.RoundToInt(tuftCenterWorldX);
                int centerSampleZ = Mathf.RoundToInt(tuftCenterWorldZ);

                if (!worldData.IsInsideMap(centerSampleX, centerSampleZ))
                {
                    continue;
                }

                float tuftCenterY = worldData.GetHeight(centerSampleX, centerSampleZ) + settings.yOffset;
                Vector3 tuftCenter = new Vector3(
                    tuftCenterWorldX - startX,
                    tuftCenterY,
                    tuftCenterWorldZ - startZ
                );

                int bladeCount = random.Next(
                    Mathf.Max(1, settings.minBladesPerTuft),
                    Mathf.Max(settings.minBladesPerTuft, settings.maxBladesPerTuft) + 1
                );

                float tuftRadius = Mathf.Lerp(
                    settings.minTuftRadius,
                    settings.maxTuftRadius,
                    (float)random.NextDouble()
                );

                float heightNoiseMultiplier = Mathf.Lerp(
                    1f - settings.heightVariationStrength,
                    1f,
                    coverageNoise
                );

                float baseAngle = (float)random.NextDouble() * 360f;

                for (int i = 0; i < bladeCount; i++)
                {
                    Vector2 offset2D = RandomInsideCircle(random) * tuftRadius;

                    float bladeWorldX = tuftCenterWorldX + offset2D.x;
                    float bladeWorldZ = tuftCenterWorldZ + offset2D.y;

                    int sampleX = Mathf.RoundToInt(bladeWorldX);
                    int sampleZ = Mathf.RoundToInt(bladeWorldZ);

                    if (!worldData.IsInsideMap(sampleX, sampleZ))
                    {
                        continue;
                    }

                    float bladeY = worldData.GetHeight(sampleX, sampleZ) + settings.yOffset;

                    Vector3 bladePosition = new Vector3(
                        bladeWorldX - startX,
                        bladeY,
                        bladeWorldZ - startZ
                    );

                    float height = Mathf.Lerp(
                        settings.minBladeHeight,
                        settings.maxBladeHeight,
                        (float)random.NextDouble()
                    ) * heightNoiseMultiplier;

                    float width = Mathf.Lerp(
                        settings.minBladeWidth,
                        settings.maxBladeWidth,
                        (float)random.NextDouble()
                    );

                    float rotation = baseAngle +
                                     (360f / bladeCount) * i +
                                     Mathf.Lerp(-22f, 22f, (float)random.NextDouble());

                    float bend = Mathf.Lerp(-0.08f, 0.08f, (float)random.NextDouble());
                    float phase = (float)random.NextDouble() * 10f;

                    Color bladeColor = GetGrassColor(
                        sampleX,
                        sampleZ,
                        seed,
                        coverageNoise,
                        zone,
                        settings,
                        (float)random.NextDouble()
                    );

                    AddBladeQuad(
                        vertices,
                        triangles,
                        uvs,
                        uv2s,
                        colors,
                        bladePosition,
                        width,
                        height,
                        rotation,
                        bend,
                        phase,
                        bladeColor,
                        settings
                    );
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetColors(colors);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static float GetZoneDensity(TerrainZone zone, GroundGrassSettings settings)
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

    private static Color GetGrassColor(
        int worldX,
        int worldZ,
        int seed,
        float coverageNoise,
        TerrainZone zone,
        GroundGrassSettings settings,
        float randomValue
    )
    {
        float colorNoise = Mathf.PerlinNoise(
            (worldX + seed * 6.11f) / settings.colorNoiseScale,
            (worldZ - seed * 3.07f) / settings.colorNoiseScale
        );

        float dryVsGreen = Mathf.Lerp(coverageNoise, colorNoise, 0.55f);

        Color baseColor;

        if (dryVsGreen < 0.28f)
        {
            baseColor = Color.Lerp(
                settings.dryGrass,
                settings.warmGrass,
                dryVsGreen / 0.28f
            );
        }
        else if (dryVsGreen < 0.65f)
        {
            baseColor = Color.Lerp(
                settings.warmGrass,
                settings.oliveGrass,
                (dryVsGreen - 0.28f) / 0.37f
            );
        }
        else
        {
            baseColor = Color.Lerp(
                settings.oliveGrass,
                settings.lushGrass,
                (dryVsGreen - 0.65f) / 0.35f
            );
        }

        float zoneTint = 0f;

        switch (zone)
        {
            case TerrainZone.Arena:
                zoneTint = -0.08f;
                break;
            case TerrainZone.Transition:
                zoneTint = 0f;
                break;
            case TerrainZone.Resource:
                zoneTint = 0.03f;
                break;
            case TerrainZone.Border:
                zoneTint = 0.05f;
                break;
        }

        float variation = Mathf.Lerp(
            -settings.randomColorVariation,
            settings.randomColorVariation,
            randomValue
        );

        baseColor *= 1f + variation + zoneTint;

        return new Color(
            Mathf.Clamp01(baseColor.r),
            Mathf.Clamp01(baseColor.g),
            Mathf.Clamp01(baseColor.b),
            1f
        );
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

    private static Vector2 RandomInsideCircle(System.Random random)
    {
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float radius = Mathf.Sqrt((float)random.NextDouble());

        return new Vector2(
            Mathf.Cos(angle) * radius,
            Mathf.Sin(angle) * radius
        );
    }

    private static void AddBladeQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        List<Vector2> uv2s,
        List<Color> colors,
        Vector3 position,
        float width,
        float height,
        float rotationDegrees,
        float bend,
        float phase,
        Color color,
        GroundGrassSettings settings
    )
    {
        int startIndex = vertices.Count;

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        float topWidth = width * 0.22f;

        Vector3 bottomLeft = position - right * width * 0.5f;
        Vector3 bottomRight = position + right * width * 0.5f;

        Vector3 topCenter = position +
                            Vector3.up * height +
                            forward * bend;

        Vector3 topLeft = topCenter - right * topWidth * 0.5f;
        Vector3 topRight = topCenter + right * topWidth * 0.5f;

        vertices.Add(bottomLeft);
        vertices.Add(bottomRight);
        vertices.Add(topRight);
        vertices.Add(topLeft);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        uv2s.Add(new Vector2(0f, phase));
        uv2s.Add(new Vector2(0f, phase));
        uv2s.Add(new Vector2(1f, phase));
        uv2s.Add(new Vector2(1f, phase));

        Color bottomColor = color * (1f - settings.baseDarkening);
        Color topColor = color * (1f + settings.tipLightening);

        colors.Add(bottomColor);
        colors.Add(bottomColor);
        colors.Add(topColor);
        colors.Add(topColor);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 1);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 3);
        triangles.Add(startIndex + 2);
    }
}