using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TreeChunkMeshes
{
    public Mesh treeMesh;
    public Mesh shadowMesh;
}

public static class TreeChunkGenerator
{
    private const int MinBranchCount = 2;
    private const int MaxBranchCount = 4;

    private const int MinLeafClumps = 4;
    private const int MaxLeafClumps = 8;

    private const float TrunkLeanStrength = 0.16f;
    private const float BranchUpwardBias = 0.28f;
    private const float CrownFlatness = 0.34f;

    public static TreeChunkMeshes GenerateTreeMeshes(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        TreeSettings settings
    )
    {
        List<Vector3> treeVertices = new List<Vector3>();
        List<int> treeTriangles = new List<int>();
        List<Color> treeColors = new List<Color>();

        List<Vector3> shadowVertices = new List<Vector3>();
        List<int> shadowTriangles = new List<int>();

        System.Random random = new System.Random(
            seed ^
            startX * 83492791 ^
            startZ * 297121507
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

                if (zone == TerrainZone.Border)
                {
                    continue;
                }

                float baseDensity = GetZoneDensity(zone, settings);

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 11.7f) / settings.largePatchScale,
                    (worldZ - seed * 8.3f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 4.9f) / settings.smallPatchScale,
                    (worldZ + seed * 6.2f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.45f);

                float patchMultiplier = Mathf.Lerp(
                    0.55f,
                    1.75f + settings.patchStrength * 0.25f,
                    Mathf.Pow(patchValue, 1.1f)
                );

                float density = Mathf.Clamp01(baseDensity * patchMultiplier);

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                float jitterRange = settings.spacing * 0.45f;

                float jitterX = Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                float jitterZ = Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                float finalWorldX = worldX + jitterX;
                float finalWorldZ = worldZ + jitterZ;

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

                float trunkHeight = Mathf.Lerp(
                    settings.minTrunkHeight,
                    settings.maxTrunkHeight,
                    (float)random.NextDouble()
                );

                float trunkRadius = Mathf.Lerp(
                    settings.minTrunkRadius,
                    settings.maxTrunkRadius,
                    (float)random.NextDouble()
                );

                float canopyRadius = Mathf.Lerp(
                    settings.minCanopyRadius,
                    settings.maxCanopyRadius,
                    (float)random.NextDouble()
                );

                float canopyHeight = Mathf.Lerp(
                    settings.minCanopyHeight,
                    settings.maxCanopyHeight,
                    (float)random.NextDouble()
                );

                float rotation = (float)random.NextDouble() * 360f;

                Color leafColor = GetLeafColor(patchValue, settings);

                if (settings.generateShadows)
                {
                    AddShadow(
                        shadowVertices,
                        shadowTriangles,
                        localPosition,
                        canopyRadius,
                        rotation,
                        settings
                    );
                }

                AddSavannahTree(
                    treeVertices,
                    treeTriangles,
                    treeColors,
                    localPosition,
                    trunkHeight,
                    trunkRadius,
                    canopyRadius,
                    canopyHeight,
                    rotation,
                    settings.trunkColor,
                    leafColor,
                    random
                );
            }
        }

        TreeChunkMeshes result = new TreeChunkMeshes();

        if (treeVertices.Count > 0)
        {
            Mesh treeMesh = new Mesh();
            treeMesh.indexFormat = IndexFormat.UInt32;

            treeMesh.SetVertices(treeVertices);
            treeMesh.SetTriangles(treeTriangles, 0);
            treeMesh.SetColors(treeColors);

            treeMesh.RecalculateNormals();
            treeMesh.RecalculateBounds();

            result.treeMesh = treeMesh;
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

    private static float GetZoneDensity(TerrainZone zone, TreeSettings settings)
    {
        switch (zone)
        {
            case TerrainZone.Arena:
                return settings.arenaDensity;

            case TerrainZone.Transition:
                return settings.transitionDensity;

            case TerrainZone.Resource:
                return settings.resourceDensity;

            default:
                return 0f;
        }
    }

    private static Color GetLeafColor(float noise, TreeSettings settings)
    {
        if (noise < 0.45f)
        {
            return Color.Lerp(
                settings.dryLeafColor,
                settings.oliveLeafColor,
                noise / 0.45f
            );
        }

        return Color.Lerp(
            settings.oliveLeafColor,
            settings.greenLeafColor,
            (noise - 0.45f) / 0.55f
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

    private static void AddSavannahTree(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        float trunkHeight,
        float trunkRadius,
        float canopyRadius,
        float canopyHeight,
        float rotationDegrees,
        Color trunkColor,
        Color leafColor,
        System.Random random
    )
    {
        Vector3 trunkTop = AddLeanTrunk(
            vertices,
            triangles,
            colors,
            position,
            trunkHeight,
            trunkRadius,
            rotationDegrees,
            trunkColor,
            random
        );

        int branchCount = random.Next(MinBranchCount, MaxBranchCount + 1);
        List<Vector3> branchTips = new List<Vector3>();

        for (int i = 0; i < branchCount; i++)
        {
            float angle = rotationDegrees +
                          (360f / branchCount) * i +
                          Mathf.Lerp(-30f, 30f, (float)random.NextDouble());

            float branchLength = Mathf.Lerp(
                canopyRadius * 0.38f,
                canopyRadius * 0.75f,
                (float)random.NextDouble()
            );

            Vector3 branchTip = AddBranch(
                vertices,
                triangles,
                colors,
                trunkTop,
                angle,
                branchLength,
                trunkRadius * 0.65f,
                BranchUpwardBias,
                trunkColor
            );

            branchTips.Add(branchTip);
        }

        int leafClumpCount = random.Next(MinLeafClumps, MaxLeafClumps + 1);

        for (int i = 0; i < leafClumpCount; i++)
        {
            Vector3 anchor = branchTips[random.Next(branchTips.Count)];

            float angle = rotationDegrees +
                          (360f / leafClumpCount) * i +
                          Mathf.Lerp(-45f, 45f, (float)random.NextDouble());

            float spread = Mathf.Lerp(
                canopyRadius * 0.12f,
                canopyRadius * 0.62f,
                (float)random.NextDouble()
            );

            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 offset = rotation * new Vector3(spread, 0f, 0f);

            offset.y = Mathf.Lerp(
                -canopyHeight * 0.25f,
                canopyHeight * 0.12f,
                (float)random.NextDouble()
            );

            Vector3 clumpCenter = anchor + offset;

            float clumpRadius = Mathf.Lerp(
                canopyRadius * 0.28f,
                canopyRadius * 0.52f,
                (float)random.NextDouble()
            );

            float clumpHeight = Mathf.Lerp(
                canopyHeight * 0.35f,
                canopyHeight * 0.75f,
                (float)random.NextDouble()
            );

            Color clumpColor = leafColor * Mathf.Lerp(
                0.88f,
                1.08f,
                (float)random.NextDouble()
            );

            AddLeafClump(
                vertices,
                triangles,
                colors,
                clumpCenter,
                clumpRadius,
                clumpHeight,
                angle,
                clumpColor
            );
        }
    }

    private static Vector3 AddLeanTrunk(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 basePosition,
        float height,
        float radius,
        float rotationDegrees,
        Color color,
        System.Random random
    )
    {
        int sides = 6;
        int startIndex = vertices.Count;

        float leanAngle = Mathf.Lerp(-35f, 35f, (float)random.NextDouble());
        float leanAmount = height * TrunkLeanStrength;

        Quaternion baseRotation = Quaternion.Euler(0f, rotationDegrees, 0f);
        Quaternion leanRotation = Quaternion.Euler(0f, leanAngle, 0f);

        Vector3 leanOffset = leanRotation * (baseRotation * new Vector3(leanAmount, 0f, 0f));
        Vector3 topCenter = basePosition + Vector3.up * height + leanOffset;

        for (int i = 0; i < sides; i++)
        {
            float angle = ((float)i / sides) * Mathf.PI * 2f;

            Vector3 dir = new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)
            );

            dir = baseRotation * dir;

            vertices.Add(basePosition + dir * radius);
            vertices.Add(topCenter + dir * radius * 0.55f);

            colors.Add(color * 0.92f);
            colors.Add(color * 1.05f);
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

        return topCenter;
    }

    private static Vector3 AddBranch(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 start,
        float angleDegrees,
        float length,
        float radius,
        float upwardBias,
        Color color
    )
    {
        int startIndex = vertices.Count;

        Quaternion rotation = Quaternion.Euler(0f, angleDegrees, 0f);

        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;

        Vector3 end = start +
                      forward * length +
                      Vector3.up * (length * upwardBias);

        vertices.Add(start - right * radius);
        vertices.Add(start + right * radius);
        vertices.Add(end + right * radius * 0.35f);
        vertices.Add(end - right * radius * 0.35f);

        colors.Add(color * 0.88f);
        colors.Add(color);
        colors.Add(color * 1.08f);
        colors.Add(color * 0.96f);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 1);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 3);
        triangles.Add(startIndex + 2);

        return end;
    }

    private static void AddLeafClump(
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
        int segments = 10;
        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

        float actualHeight = height * CrownFlatness;

        int topCenterIndex = vertices.Count;
        vertices.Add(center + Vector3.up * actualHeight);
        colors.Add(color * 1.05f);

        int topRingStart = vertices.Count;

        for (int i = 0; i < segments; i++)
        {
            float angle = ((float)i / segments) * Mathf.PI * 2f;

            float irregularity =
                0.78f +
                0.24f * Mathf.Sin(angle * 3f + rotationDegrees * 0.05f);

            float finalRadius = radius * irregularity;

            Vector3 dir = new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)
            );

            dir = rotation * dir;

            vertices.Add(center + dir * finalRadius);
            colors.Add(color * (0.9f + 0.12f * Mathf.Sin(angle * 2f)));
        }

        for (int i = 0; i < segments; i++)
        {
            int current = topRingStart + i;
            int next = topRingStart + ((i + 1) % segments);

            triangles.Add(topCenterIndex);
            triangles.Add(current);
            triangles.Add(next);
        }

        Color underside = color * 0.72f;

        int bottomCenterIndex = vertices.Count;
        vertices.Add(center - Vector3.up * actualHeight * 0.45f);
        colors.Add(underside);

        int bottomRingStart = vertices.Count;

        for (int i = 0; i < segments; i++)
        {
            float angle = ((float)i / segments) * Mathf.PI * 2f;

            float irregularity =
                0.72f +
                0.16f * Mathf.Sin(angle * 2f + 1.7f);

            float finalRadius = radius * irregularity;

            Vector3 dir = new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)
            );

            dir = rotation * dir;

            vertices.Add(center + dir * finalRadius - Vector3.up * actualHeight * 0.3f);
            colors.Add(underside);
        }

        for (int i = 0; i < segments; i++)
        {
            int current = bottomRingStart + i;
            int next = bottomRingStart + ((i + 1) % segments);

            triangles.Add(bottomCenterIndex);
            triangles.Add(next);
            triangles.Add(current);
        }
    }

    private static void AddShadow(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 treeBasePosition,
        float canopyRadius,
        float rotationDegrees,
        TreeSettings settings
    )
    {
        int segments = 12;
        int centerIndex = vertices.Count;

        Vector3 shadowCenter = treeBasePosition + new Vector3(
            settings.shadowOffsetX * canopyRadius,
            settings.shadowYOffset,
            settings.shadowOffsetZ * canopyRadius
        );

        vertices.Add(shadowCenter);

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees + 10f, 0f);

        int ringStart = vertices.Count;

        float radiusX = canopyRadius * settings.shadowRadiusXMultiplier;
        float radiusZ = canopyRadius * settings.shadowRadiusZMultiplier;

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