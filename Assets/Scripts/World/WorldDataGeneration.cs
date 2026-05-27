using UnityEngine;

public static class WorldDataGenerator
{
    public static WorldData GenerateWorldData(
        int mapSize,
        int seed,
        int arenaRadius,
        int transitionDistance,
        int resourceRadius,
        float arenaHeightMultiplier,
        float resourceHeightMultiplier,
        float noiseScale,
        int octaves,
        float persistence,
        float lacunarity
    )
    {
        WorldData worldData = new WorldData(mapSize);

        Vector2Int center = new Vector2Int(mapSize / 2, mapSize / 2);

        worldData.arenaCenter = center;
        worldData.arenaRadius = arenaRadius;
        worldData.resourceRadius = resourceRadius;

        System.Random random = new System.Random(seed);

        worldData.cavePosition = new Vector2Int(
            random.Next(mapSize / 4, mapSize - mapSize / 4),
            mapSize - 60
        );

        Vector2[] octaveOffsets = CreateOctaveOffsets(seed, octaves);
        float maxPossibleHeight = GetMaxPossibleHeight(octaves, persistence);

        for (int z = 0; z <= mapSize; z++)
        {
            for (int x = 0; x <= mapSize; x++)
            {
                Vector2 position = new Vector2(x, z);
                float distanceFromCenter = Vector2.Distance(position, center);

                float arenaMask = 1f - Mathf.SmoothStep(
                    arenaRadius,
                    arenaRadius + transitionDistance,
                    distanceFromCenter
                );

                float baseNoise = GetFractalNoise(
                    x,
                    z,
                    noiseScale,
                    octaves,
                    persistence,
                    lacunarity,
                    octaveOffsets,
                    maxPossibleHeight
                );

                float detailNoise = GetFractalNoise(
                    x + 10000,
                    z + 10000,
                    noiseScale * 0.35f,
                    2,
                    0.4f,
                    2f,
                    octaveOffsets,
                    maxPossibleHeight
                );

                float arenaHeight = detailNoise * arenaHeightMultiplier;

                // Make resource terrain more dramatic.
                // Pow makes hills stand out instead of everything becoming a boring plateau.
                float resourceHeight = Mathf.Pow(baseNoise, 1.7f) * resourceHeightMultiplier;

                // Blend arena into resource area.
                float finalHeight = Mathf.Lerp(resourceHeight, arenaHeight, arenaMask);

                // Fade the outside border down.
                //finalHeight *= resourceMask;

                finalHeight = Mathf.Clamp(finalHeight, 0f, 80f);
                worldData.heights[x, z] = finalHeight;

                int borderWidth = 12;

                bool isOuterMapBorder =
                    x < borderWidth ||
                    z < borderWidth ||
                    x > mapSize - borderWidth ||
                    z > mapSize - borderWidth;

                if (isOuterMapBorder)
                {
                    worldData.zones[x, z] = TerrainZone.Border;
                }
                else if (distanceFromCenter <= arenaRadius)
                {
                    worldData.zones[x, z] = TerrainZone.Arena;
                }
                else if (distanceFromCenter <= arenaRadius + transitionDistance)
                {
                    worldData.zones[x, z] = TerrainZone.Transition;
                }
                else
                {
                    worldData.zones[x, z] = TerrainZone.Resource;
                }
            }
        }

        //ShapeCaveArea(worldData);
        ClampAllHeights(worldData);

        return worldData;
    }

    private static Vector2[] CreateOctaveOffsets(int seed, int octaves)
    {
        System.Random random = new System.Random(seed);
        Vector2[] offsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new Vector2(
                random.Next(-100000, 100000),
                random.Next(-100000, 100000)
            );
        }

        return offsets;
    }

    private static float GetMaxPossibleHeight(int octaves, float persistence)
    {
        float amplitude = 1f;
        float maxHeight = 0f;

        for (int i = 0; i < octaves; i++)
        {
            maxHeight += amplitude;
            amplitude *= persistence;
        }

        return maxHeight;
    }

    private static float GetFractalNoise(
        float x,
        float z,
        float scale,
        int octaves,
        float persistence,
        float lacunarity,
        Vector2[] octaveOffsets,
        float maxPossibleHeight
    )
    {
        if (scale <= 0f)
        {
            scale = 0.0001f;
        }

        float amplitude = 1f;
        float frequency = 1f;
        float noiseHeight = 0f;

        for (int i = 0; i < octaves; i++)
        {
            int offsetIndex = i % octaveOffsets.Length;

            float sampleX = (x + octaveOffsets[offsetIndex].x) / scale * frequency;
            float sampleZ = (z + octaveOffsets[offsetIndex].y) / scale * frequency;

            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;

            noiseHeight += perlinValue * amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalized = (noiseHeight + maxPossibleHeight) / (2f * maxPossibleHeight);
        return Mathf.Clamp01(normalized);
    }

    private static void ClampAllHeights(WorldData worldData)
    {
        for (int z = 0; z <= worldData.size; z++)
        {
            for (int x = 0; x <= worldData.size; x++)
            {
                worldData.heights[x, z] = Mathf.Clamp(worldData.heights[x, z], 0f, 80f);
            }
        }
    }

    private static void ShapeCaveArea(WorldData worldData)
    {
        Vector2 cave = worldData.cavePosition;

        int caveFlattenRadius = 18;
        int caveHillRadius = 55;

        for (int z = -caveHillRadius; z <= caveHillRadius; z++)
        {
            for (int x = -caveHillRadius; x <= caveHillRadius; x++)
            {
                int worldX = worldData.cavePosition.x + x;
                int worldZ = worldData.cavePosition.y + z;

                if (!worldData.IsInsideMap(worldX, worldZ))
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    new Vector2(worldX, worldZ),
                    cave
                );

                if (distance > caveHillRadius)
                {
                    continue;
                }

                float hillMask = 1f - Mathf.SmoothStep(0f, caveHillRadius, distance);
                float flattenMask = 1f - Mathf.SmoothStep(0f, caveFlattenRadius, distance);

                worldData.heights[worldX, worldZ] += hillMask * 12f;

                float entranceHeight = worldData.heights[
                    worldData.cavePosition.x,
                    worldData.cavePosition.y
                ];

                worldData.heights[worldX, worldZ] = Mathf.Lerp(
                    worldData.heights[worldX, worldZ],
                    entranceHeight,
                    flattenMask * 0.8f
                );
            }
        }
    }
}