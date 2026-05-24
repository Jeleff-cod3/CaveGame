using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct VegetationChunkMeshes
{
    public Mesh vegetationMesh;
    public Mesh shadowMesh;
}

public static class VegetationChunkGenerator
{
    public static VegetationChunkMeshes GenerateVegetationMeshes(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        VegetationSettings settings
    )
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();

        List<Vector3> shadowVertices = new List<Vector3>();
        List<int> shadowTriangles = new List<int>();

        System.Random random = new System.Random(
            seed ^
            startX * 49663 ^
            startZ * 92791
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
                    (worldX + seed * 3.71f) / settings.largePatchScale,
                    (worldZ - seed * 9.17f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 4.11f) / settings.smallPatchScale,
                    (worldZ + seed * 2.93f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.4f);

                float patchMultiplier = Mathf.Lerp(
                    0.35f,
                    1.85f + settings.patchStrength * 0.25f,
                    Mathf.Pow(patchValue, 1.15f)
                );

                float density = Mathf.Clamp01(baseDensity * patchMultiplier);

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                float jitterRange = settings.spacing * 0.45f;

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

                if (worldData.GetZone(sampleX, sampleZ) == TerrainZone.Border)
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

                float roll = (float)random.NextDouble();
                float rotation = (float)random.NextDouble() * 360f;

                if (roll < settings.deadTreeChance)
                {
                    float height = Mathf.Lerp(
                        settings.minDeadTreeHeight,
                        settings.maxDeadTreeHeight,
                        (float)random.NextDouble()
                    );

                    float radius = Mathf.Lerp(
                        settings.minDeadTreeRadius,
                        settings.maxDeadTreeRadius,
                        (float)random.NextDouble()
                    );

                    if (settings.generateShadows)
                    {
                        AddShadow(
                            shadowVertices,
                            shadowTriangles,
                            localPosition,
                            height * 0.45f,
                            rotation,
                            settings
                        );
                    }

                    AddDeadTree(
                        vertices,
                        triangles,
                        colors,
                        localPosition,
                        height,
                        radius,
                        rotation,
                        settings.dryTwigColor
                    );
                }
                else if (roll < settings.deadTreeChance + settings.deadBushChance)
                {
                    float radius = Mathf.Lerp(
                        settings.minBushRadius,
                        settings.maxBushRadius,
                        (float)random.NextDouble()
                    );

                    float height = Mathf.Lerp(
                        settings.minBushHeight,
                        settings.maxBushHeight,
                        (float)random.NextDouble()
                    );

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

                    AddDeadBush(
                        vertices,
                        triangles,
                        colors,
                        localPosition,
                        radius,
                        height,
                        rotation,
                        settings.deadBushColor,
                        settings.dryTwigColor
                    );
                }
                else
                {
                    float radius = Mathf.Lerp(
                        settings.minBushRadius,
                        settings.maxBushRadius,
                        (float)random.NextDouble()
                    );

                    float height = Mathf.Lerp(
                        settings.minBushHeight,
                        settings.maxBushHeight,
                        (float)random.NextDouble()
                    );

                    Color bushColor = Color.Lerp(
                        settings.aliveBushColorA,
                        settings.aliveBushColorB,
                        patchValue
                    );

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

                    AddAliveBush(
                        vertices,
                        triangles,
                        colors,
                        localPosition,
                        radius,
                        height,
                        rotation,
                        bushColor
                    );
                }
            }
        }

        VegetationChunkMeshes result = new VegetationChunkMeshes();

        if (vertices.Count > 0)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            result.vegetationMesh = mesh;
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

    private static float GetZoneDensity(TerrainZone zone, VegetationSettings settings)
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

    private static void AddAliveBush(
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
        int clumpCount = 3;

        for (int i = 0; i < clumpCount; i++)
        {
            float angle = rotationDegrees + i * 120f;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 offset = rotation * new Vector3(radius * 0.25f, 0f, 0f);

            AddLowPolyBlob(
                vertices,
                triangles,
                colors,
                position + offset,
                radius * RandomFactor(i, 0.65f, 0.95f),
                height * RandomFactor(i + 7, 0.75f, 1.15f),
                angle,
                color * RandomFactor(i + 13, 0.85f, 1.08f)
            );
        }
    }

    private static void AddDeadBush(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        float radius,
        float height,
        float rotationDegrees,
        Color bushColor,
        Color twigColor
    )
    {
        int branchCount = 7;

        for (int i = 0; i < branchCount; i++)
        {
            float angle = rotationDegrees + i * (360f / branchCount);
            float branchLength = radius * RandomFactor(i, 0.55f, 1.1f);
            float branchHeight = height * RandomFactor(i + 3, 0.55f, 1.05f);

            AddBranchQuad(
                vertices,
                triangles,
                colors,
                position,
                angle,
                branchLength,
                branchHeight,
                0.035f,
                Color.Lerp(bushColor, twigColor, 0.65f)
            );
        }
    }

    private static void AddDeadTree(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        float height,
        float radius,
        float rotationDegrees,
        Color color
    )
    {
        AddTrunk(
            vertices,
            triangles,
            colors,
            position,
            height,
            radius,
            rotationDegrees,
            color
        );

        Vector3 branchBase = position + Vector3.up * height * 0.55f;

        AddBranchQuad(
            vertices,
            triangles,
            colors,
            branchBase,
            rotationDegrees + 35f,
            height * 0.45f,
            height * 0.28f,
            radius * 0.7f,
            color
        );

        AddBranchQuad(
            vertices,
            triangles,
            colors,
            branchBase,
            rotationDegrees - 55f,
            height * 0.38f,
            height * 0.22f,
            radius * 0.6f,
            color
        );

        AddBranchQuad(
            vertices,
            triangles,
            colors,
            branchBase + Vector3.up * height * 0.18f,
            rotationDegrees + 145f,
            height * 0.32f,
            height * 0.18f,
            radius * 0.5f,
            color
        );
    }

    private static void AddLowPolyBlob(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 center,
        float radius,
        float height,
        float rotationDegrees,
        Color color
    )
    {
        int segments = 8;
        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        int topIndex = vertices.Count;
        vertices.Add(center + Vector3.up * height);
        colors.Add(color);

        int ringStart = vertices.Count;

        for (int i = 0; i < segments; i++)
        {
            float angle = ((float)i / segments) * Mathf.PI * 2f;

            float irregularity = 0.8f + 0.22f * Mathf.Sin(angle * 3f + rotationDegrees);
            float finalRadius = radius * irregularity;

            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            dir = rotation * dir;

            vertices.Add(center + dir * finalRadius);
            colors.Add(color * (0.85f + 0.15f * Mathf.Sin(angle * 2f)));
        }

        for (int i = 0; i < segments; i++)
        {
            int current = ringStart + i;
            int next = ringStart + ((i + 1) % segments);

            triangles.Add(topIndex);
            triangles.Add(current);
            triangles.Add(next);
        }
    }

    private static void AddTrunk(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 basePosition,
        float height,
        float radius,
        float rotationDegrees,
        Color color
    )
    {
        int sides = 5;
        int startIndex = vertices.Count;

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        for (int i = 0; i < sides; i++)
        {
            float angle = ((float)i / sides) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            dir = rotation * dir;

            vertices.Add(basePosition + dir * radius);
            vertices.Add(basePosition + Vector3.up * height + dir * radius * 0.45f);

            colors.Add(color);
            colors.Add(color * 1.08f);
        }

        for (int i = 0; i < sides; i++)
        {
            int currentBottom = startIndex + i * 2;
            int currentTop = currentBottom + 1;

            int nextBottom = startIndex + ((i + 1) % sides) * 2;
            int nextTop = nextBottom + 1;

            triangles.Add(currentBottom);
            triangles.Add(currentTop);
            triangles.Add(nextTop);

            triangles.Add(currentBottom);
            triangles.Add(nextTop);
            triangles.Add(nextBottom);
        }
    }

    private static void AddBranchQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 basePosition,
        float rotationDegrees,
        float length,
        float height,
        float width,
        Color color
    )
    {
        int startIndex = vertices.Count;

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;

        Vector3 bottom = basePosition;
        Vector3 top = basePosition + forward * length + Vector3.up * height;

        vertices.Add(bottom - right * width);
        vertices.Add(bottom + right * width);
        vertices.Add(top + right * width * 0.35f);
        vertices.Add(top - right * width * 0.35f);

        colors.Add(color);
        colors.Add(color);
        colors.Add(color * 1.1f);
        colors.Add(color * 1.1f);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 1);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 3);
        triangles.Add(startIndex + 2);
    }

    private static void AddShadow(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 basePosition,
        float radius,
        float rotationDegrees,
        VegetationSettings settings
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

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees + 12f, 0f);

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

    private static float RandomFactor(int seed, float min, float max)
    {
        float value = Mathf.Abs(Mathf.Sin(seed * 91.17f) * 43758.5453f);
        value = value - Mathf.Floor(value);

        return Mathf.Lerp(min, max, value);
    }
}