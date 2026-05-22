using UnityEngine;

public class WorldData
{
    public int size;
    public float[,] heights;
    public TerrainZone[,] zones;

    public Vector2Int arenaCenter;
    public int arenaRadius;
    public int resourceRadius;

    public Vector2Int cavePosition;

    public WorldData(int size)
    {
        this.size = size;
        heights = new float[size + 1, size + 1];
        zones = new TerrainZone[size + 1, size + 1];
    }

    public bool IsInsideMap(int x, int z)
    {
        return x >= 0 && z >= 0 && x <= size && z <= size;
    }

    public float GetHeight(int x, int z)
    {
        if (!IsInsideMap(x, z))
        {
            return 0f;
        }
        return heights[x, z];
    }

    public TerrainZone GetZone(int x, int z)
    {
        if (!IsInsideMap(x, z))
        {
            return TerrainZone.Border;
        }

        return zones[x, z];
    }
}
