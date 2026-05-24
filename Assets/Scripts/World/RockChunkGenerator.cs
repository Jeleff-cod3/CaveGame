using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct RockChunkMeshes
{
    public Mesh rockMesh;
    public Mesh shadowMesh;
}

public static class RockChunkGenerator
{
    public static RockChunkMeshes GenerateRockMeshes(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        RockSettings settings
    )
    {
        List<Vector3> rockVertices = new List<Vector3>();
        List<int> rockTriangles = new List<int>();
        List<Color> rockColors = new List<Color>();

        List<Vector3> shadowVertices = new List<Vector3>();
        List<int> shadowTriangles = new List<int>();

        System.Random random = new System.Random(
            seed ^
            startX * 19349663 ^
            startZ * 83492791
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
                float baseDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 6.17f) / settings.largePatchScale,
                    (worldZ - seed * 2.91f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 1.47f) / settings.smallPatchScale,
                    (worldZ + seed * 8.39f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.35f);

                float patchMultiplier = Mathf.Lerp(
                    0.35f,
                    1.65f + settings.patchStrength * 0.25f,
                    Mathf.Pow(patchValue, 1.2f)
                );

                float density = Mathf.Clamp01(baseDensity * patchMultiplier);

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                float jitterRange = settings.spacing * 0.42f;

                float finalWorldX = worldX + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                float finalWorldZ = worldZ + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                int sampleX = Mathf.RoundToInt(finalWorldX);
                int sampleZ = Mathf.RoundToInt(finalWorldZ);

                if (!worldData.IsInsideMap(sampleX, sampleZ))
                {
                    continue;
                }

                if (IsTooSteep(worldData, sampleX, sampleZ, settings.maxSlopeAngle))
                {
                    continue;
                }

                float groundY = worldData.GetHeight(sampleX, sampleZ) + settings.yOffset;

                Vector3 localPosition = new Vector3(
                    finalWorldX - startX,
                    groundY,
                    finalWorldZ - startZ
                );

                float radius = Mathf.Lerp(
                    settings.minRadius,
                    settings.maxRadius,
                    (float)random.NextDouble()
                );

                float height = Mathf.Lerp(
                    settings.minHeight,
                    settings.maxHeight,
                    (float)random.NextDouble()
                );

                float rotation = (float)random.NextDouble() * 360f;

                Color color = GetRockColor(patchValue, settings);

                if (settings.generateShadows)
                {
                    AddShadow(
                        shadowVertices,
                        shadowTriangles,
                        localPosition,
                        radius,
                        rotation,
                        settings
                    );
                }

                AddRock(
                    rockVertices,
                    rockTriangles,
                    rockColors,
                    localPosition,
                    radius,
                    height,
                    rotation,
                    color
                );
            }
        }

        RockChunkMeshes result = new RockChunkMeshes();

        if (rockVertices.Count > 0)
        {
            Mesh rockMesh = new Mesh();
            rockMesh.indexFormat = IndexFormat.UInt32;

            rockMesh.SetVertices(rockVertices);
            rockMesh.SetTriangles(rockTriangles, 0);
            rockMesh.SetColors(rockColors);

            rockMesh.RecalculateNormals();
            rockMesh.RecalculateBounds();

            result.rockMesh = rockMesh;
        }

        if (shadowVertices.Count > 0)
        {
            Mesh shadowMesh = new Mesh();
            shadowMesh.indexFormat = IndexFormat.UInt32;

            shadowMesh.SetVertices(shadowVertices);
            shadowMesh.SetTriangles(shadowTriangles, 0);

            shadowMesh.RecalculateNormals();
            shadowMesh.RecalculateBounds();

            result.shadowMesh = shadowMesh;
        }

        return result;
    }

    private static float GetZoneDensity(TerrainZone zone, RockSettings settings)
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

    private static Color GetRockColor(float noise, RockSettings settings)
    {
        if (noise < 0.5f)
        {
            return Color.Lerp(settings.lightRock, settings.warmRock, noise / 0.5f);
        }

        return Color.Lerp(settings.warmRock, settings.darkRock, (noise - 0.5f) / 0.5f);
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

    private static void AddRock(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        float radius,
        float height,
        float rotationDegrees,
        Color color
    )
    {
        int segments = 7;
        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        int topIndex = vertices.Count;
        vertices.Add(position + Vector3.up * height);
        colors.Add(color * 1.08f);

        int ringStart = vertices.Count;

        for (int i = 0; i < segments; i++)
        {
            float angle = ((float)i / segments) * Mathf.PI * 2f;

            float irregularity = 0.72f + 0.32f * Mathf.Sin(angle * 2.7f + rotationDegrees * 0.03f);
            float finalRadius = radius * irregularity;

            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            dir = rotation * dir;

            float sideHeightOffset = Mathf.Sin(angle * 3f) * height * 0.08f;

            vertices.Add(position + dir * finalRadius + Vector3.up * sideHeightOffset);
            colors.Add(color * (0.78f + 0.18f * Mathf.Sin(angle * 2f)));
        }

        int bottomIndex = vertices.Count;
        vertices.Add(position - Vector3.up * height * 0.05f);
        colors.Add(color * 0.65f);

        for (int i = 0; i < segments; i++)
        {
            int current = ringStart + i;
            int next = ringStart + ((i + 1) % segments);

            triangles.Add(topIndex);
            triangles.Add(current);
            triangles.Add(next);

            triangles.Add(bottomIndex);
            triangles.Add(next);
            triangles.Add(current);
        }
    }

    private static void AddShadow(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 basePosition,
        float radius,
        float rotationDegrees,
        RockSettings settings
    )
    {
        int segments = 10;
        int centerIndex = vertices.Count;

        Vector3 shadowCenter = basePosition + new Vector3(
            settings.shadowOffsetX * radius,
            settings.shadowYOffset,
            settings.shadowOffsetZ * radius
        );

        vertices.Add(shadowCenter);

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees + 8f, 0f);

        int ringStart = vertices.Count;

        float radiusX = radius * settings.shadowRadiusXMultiplier;
        float radiusZ = radius * settings.shadowRadiusZMultiplier;

        for (int i = 0; i < segments; i++)
        {
            float angle = ((float)i / segments) * Mathf.PI * 2f;

            Vector3 local = new Vector3(
                Mathf.Cos(angle) * radiusX,
                0f,
                Mathf.Sin(angle) * radiusZ
            );

            local = rotation * local;

            vertices.Add(shadowCenter + local);
        }

        for (int i = 0; i < segments; i++)
        {
            int current = ringStart + i;
            int next = ringStart + ((i + 1) % segments);

            triangles.Add(centerIndex);
            triangles.Add(next);
            triangles.Add(current);
        }
    }
}