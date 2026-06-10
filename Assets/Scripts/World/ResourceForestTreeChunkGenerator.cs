using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct ResourceForestTreeChunkMeshes
{
    public Mesh treeMesh;
    public Mesh shadowMesh;
}

public static class ResourceForestTreeChunkGenerator
{
    private const int TrunkSides = 6;
    private const int CanopySides = 5;

    public static ResourceForestTreeChunkMeshes GenerateTrees(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        ResourceForestTreeSettings settings
    )
    {
        ResourceForestTreeChunkMeshes result = new ResourceForestTreeChunkMeshes();

        if (worldData == null || settings == null || !settings.enabled)
        {
            return result;
        }

        List<Vector3> treeVertices = new List<Vector3>();
        List<int> treeTriangles = new List<int>();
        List<Color> treeColors = new List<Color>();

        List<Vector3> shadowVertices = new List<Vector3>();
        List<int> shadowTriangles = new List<int>();

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

                if (worldData.GetZone(worldX, worldZ) != TerrainZone.Resource)
                {
                    continue;
                }

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 11.7f) / settings.largePatchScale,
                    (worldZ - seed * 8.3f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 4.9f) / settings.smallPatchScale,
                    (worldZ + seed * 6.2f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.42f);
                float patchMultiplier = Mathf.Lerp(
                    0.45f,
                    1.55f + settings.patchStrength * 0.22f,
                    Mathf.Pow(patchValue, 1.18f)
                );
                float density = Mathf.Clamp01(settings.resourceDensity * patchMultiplier);

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                float jitterRange = settings.spacing * 0.45f;
                float jitterX = Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());
                float jitterZ = Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());

                float finalWorldX = worldX + jitterX;
                float finalWorldZ = worldZ + jitterZ;

                int sampleX = Mathf.RoundToInt(finalWorldX);
                int sampleZ = Mathf.RoundToInt(finalWorldZ);

                if (!worldData.IsInsideMap(sampleX, sampleZ))
                {
                    continue;
                }

                if (worldData.GetZone(sampleX, sampleZ) != TerrainZone.Resource)
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

                bool usePine = random.NextDouble() < settings.pineChance;
                float yaw = (float)random.NextDouble() * 360f;

                if (usePine)
                {
                    AddPineTree(
                        treeVertices,
                        treeTriangles,
                        treeColors,
                        localPosition,
                        settings,
                        yaw,
                        random
                    );
                }
                else
                {
                    AddBroadleafTree(
                        treeVertices,
                        treeTriangles,
                        treeColors,
                        localPosition,
                        settings,
                        yaw,
                        random
                    );
                }

                if (settings.generateShadows)
                {
                    float shadowRadius = usePine
                        ? Mathf.Lerp(settings.minCanopyRadius1, settings.maxCanopyRadius1, 0.7f)
                        : Mathf.Lerp(settings.minCanopyRadius2, settings.maxCanopyRadius2, 0.72f);

                    AddShadow(
                        shadowVertices,
                        shadowTriangles,
                        localPosition,
                        shadowRadius,
                        yaw,
                        settings
                    );
                }
            }
        }

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

    private static void AddPineTree(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        ResourceForestTreeSettings settings,
        float yaw,
        System.Random random
    )
    {
        float trunkHeight = Mathf.Lerp(
            settings.minTrunkHeight1,
            settings.maxTrunkHeight1,
            (float)random.NextDouble()
        );
        float trunkRadius = Mathf.Lerp(
            settings.minTrunkRadius1,
            settings.maxTrunkRadius1,
            (float)random.NextDouble()
        );
        float canopyRadius = Mathf.Lerp(
            settings.minCanopyRadius1,
            settings.maxCanopyRadius1,
            (float)random.NextDouble()
        );

        AddTrunkPrism(
            vertices,
            triangles,
            colors,
            position,
            trunkHeight,
            trunkRadius,
            settings.trunkColor1,
            yaw
        );

        float lowerHeight = canopyRadius * 1.55f;
        float middleHeight = canopyRadius * 1.2f;
        float upperHeight = canopyRadius * 0.9f;

        AddConeCanopy(
            vertices,
            triangles,
            colors,
            position + Vector3.up * (trunkHeight * 0.48f),
            canopyRadius * 0.95f,
            lowerHeight,
            Tint(settings.leafColor1, 0.92f),
            yaw + 8f
        );
        AddConeCanopy(
            vertices,
            triangles,
            colors,
            position + Vector3.up * (trunkHeight * 0.92f),
            canopyRadius * 0.72f,
            middleHeight,
            Tint(settings.leafColor1, 1.02f),
            yaw - 11f
        );
        AddConeCanopy(
            vertices,
            triangles,
            colors,
            position + Vector3.up * (trunkHeight * 1.22f),
            canopyRadius * 0.46f,
            upperHeight,
            Tint(settings.leafColor1, 1.1f),
            yaw + 19f
        );
    }

    private static void AddBroadleafTree(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        ResourceForestTreeSettings settings,
        float yaw,
        System.Random random
    )
    {
        float trunkHeight = Mathf.Lerp(
            settings.minTrunkHeight2,
            settings.maxTrunkHeight2,
            (float)random.NextDouble()
        );
        float trunkRadius = Mathf.Lerp(
            settings.minTrunkRadius2,
            settings.maxTrunkRadius2,
            (float)random.NextDouble()
        );
        float canopyRadius = Mathf.Lerp(
            settings.minCanopyRadius2,
            settings.maxCanopyRadius2,
            (float)random.NextDouble()
        );

        AddTrunkPrism(
            vertices,
            triangles,
            colors,
            position,
            trunkHeight,
            trunkRadius,
            settings.trunkColor2,
            yaw
        );

        Vector3 crownCenter = position + Vector3.up * (trunkHeight + canopyRadius * 0.36f);

        AddLeafClump(
            vertices,
            triangles,
            colors,
            crownCenter,
            canopyRadius * 0.88f,
            canopyRadius * 0.64f,
            Tint(settings.leafColor2, 1.02f),
            yaw
        );
        AddLeafClump(
            vertices,
            triangles,
            colors,
            crownCenter + new Vector3(canopyRadius * 0.38f, canopyRadius * 0.14f, -canopyRadius * 0.12f),
            canopyRadius * 0.56f,
            canopyRadius * 0.44f,
            Tint(settings.leafColor2, 0.92f),
            yaw + 27f
        );
        AddLeafClump(
            vertices,
            triangles,
            colors,
            crownCenter + new Vector3(-canopyRadius * 0.3f, canopyRadius * 0.1f, canopyRadius * 0.24f),
            canopyRadius * 0.48f,
            canopyRadius * 0.38f,
            Tint(settings.leafColor2, 1.08f),
            yaw - 18f
        );
    }

    private static void AddTrunkPrism(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 position,
        float height,
        float radius,
        Color color,
        float yaw
    )
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        int startIndex = vertices.Count;

        for (int i = 0; i < TrunkSides; i++)
        {
            float t = i / (float)TrunkSides;
            float angle = t * Mathf.PI * 2f;
            Vector3 ringOffset = rotation * new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            vertices.Add(position + ringOffset);
            vertices.Add(position + ringOffset + Vector3.up * height);

            float shade = Mathf.Lerp(0.84f, 1.08f, t);
            colors.Add(Tint(color, shade));
            colors.Add(Tint(color, shade * 1.04f));
        }

        for (int i = 0; i < TrunkSides; i++)
        {
            int currentBottom = startIndex + i * 2;
            int currentTop = currentBottom + 1;
            int nextBottom = startIndex + ((i + 1) % TrunkSides) * 2;
            int nextTop = nextBottom + 1;

            triangles.Add(currentBottom);
            triangles.Add(currentTop);
            triangles.Add(nextTop);

            triangles.Add(currentBottom);
            triangles.Add(nextTop);
            triangles.Add(nextBottom);
        }

        int topCenter = vertices.Count;
        vertices.Add(position + Vector3.up * height);
        colors.Add(Tint(color, 1.08f));

        for (int i = 0; i < TrunkSides; i++)
        {
            int currentTop = startIndex + i * 2 + 1;
            int nextTop = startIndex + ((i + 1) % TrunkSides) * 2 + 1;
            triangles.Add(topCenter);
            triangles.Add(currentTop);
            triangles.Add(nextTop);
        }
    }

    private static void AddConeCanopy(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 baseCenter,
        float radius,
        float height,
        Color color,
        float yaw
    )
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        int baseStart = vertices.Count;

        for (int i = 0; i < CanopySides; i++)
        {
            float t = i / (float)CanopySides;
            float angle = t * Mathf.PI * 2f;
            Vector3 ringOffset = rotation * new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            vertices.Add(baseCenter + ringOffset);
            colors.Add(Tint(color, Mathf.Lerp(0.9f, 1.06f, t)));
        }

        int tipIndex = vertices.Count;
        vertices.Add(baseCenter + Vector3.up * height);
        colors.Add(Tint(color, 1.12f));

        for (int i = 0; i < CanopySides; i++)
        {
            int current = baseStart + i;
            int next = baseStart + (i + 1) % CanopySides;
            triangles.Add(current);
            triangles.Add(tipIndex);
            triangles.Add(next);
        }
    }

    private static void AddLeafClump(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 center,
        float radius,
        float height,
        Color color,
        float yaw
    )
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        int start = vertices.Count;

        vertices.Add(center + Vector3.up * height);
        vertices.Add(center + Vector3.down * (height * 0.55f));
        colors.Add(Tint(color, 1.12f));
        colors.Add(Tint(color, 0.82f));

        for (int i = 0; i < CanopySides; i++)
        {
            float t = i / (float)CanopySides;
            float angle = t * Mathf.PI * 2f;
            Vector3 ringOffset = rotation * new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius
            );
            vertices.Add(center + ringOffset);
            colors.Add(Tint(color, Mathf.Lerp(0.9f, 1.06f, t)));
        }

        int top = start;
        int bottom = start + 1;
        int ringStart = start + 2;

        for (int i = 0; i < CanopySides; i++)
        {
            int current = ringStart + i;
            int next = ringStart + (i + 1) % CanopySides;

            triangles.Add(top);
            triangles.Add(current);
            triangles.Add(next);

            triangles.Add(bottom);
            triangles.Add(next);
            triangles.Add(current);
        }
    }

    private static void AddShadow(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 position,
        float radius,
        float yaw,
        ResourceForestTreeSettings settings
    )
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 center = position + new Vector3(
            settings.shadowOffsetX,
            settings.shadowYOffset,
            settings.shadowOffsetZ
        );
        Vector3 right = rotation * new Vector3(radius * 0.8f, 0f, 0f);
        Vector3 forward = rotation * new Vector3(0f, 0f, radius * 0.52f);

        int start = vertices.Count;
        vertices.Add(center - right - forward);
        vertices.Add(center - right + forward);
        vertices.Add(center + right + forward);
        vertices.Add(center + right - forward);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);

        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static bool IsTooSteep(WorldData worldData, int x, int z, float maxSlope)
    {
        float center = worldData.GetHeight(x, z);
        float right = worldData.GetHeight(x + 1, z);
        float forward = worldData.GetHeight(x, z + 1);

        Vector3 surfaceNormal = Vector3.Cross(
            new Vector3(0f, forward - center, 1f),
            new Vector3(1f, right - center, 0f)
        ).normalized;

        return Vector3.Angle(surfaceNormal, Vector3.up) > maxSlope;
    }

    private static Color Tint(Color color, float multiplier)
    {
        Color tinted = color * multiplier;
        tinted.a = 1f;
        return tinted;
    }
}
