using System.Collections.Generic;
using UnityEngine;

public class WorldChunkRenderer : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Material terrainMaterial;
    public GameObject cavePrefab;

    [Header("Map Settings")]
    public int mapSize = 1000;
    public int chunkSize = 50;
    public int viewDistance = 3;

    [Header("Arena Settings")]
    public int arenaRadius = 120;
    public int transitionDistance = 40;
    public int resourceRadius = 460;

    [Header("Terrain Settings")]
    public int seed = 12345;
    public float arenaHeightMultiplier = 2f;
    public float resourceHeightMultiplier = 16f;
    public float noiseScale = 120f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.45f;
    public float lacunarity = 2f;
    public float uvScale = 30f;

    private WorldData worldData;

    private Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();
    private Vector2Int currentPlayerChunk;

    private GameObject caveInstance;

    private void Start()
    {
        GenerateWorld();
        DebugWorldHeightRange();
        PlacePlayerAtArenaCenter();
        currentPlayerChunk = GetPlayerChunkCoord();
        UpdateVisibleChunks();
        SpawnCave();
    }

    private void Update()
    {
        Vector2Int newPlayerChunk = GetPlayerChunkCoord();

        if (newPlayerChunk != currentPlayerChunk)
        {
            currentPlayerChunk = newPlayerChunk;
            UpdateVisibleChunks();
        }
    }

    private void DebugWorldHeightRange()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int z = 0; z <= worldData.size; z++)
        {
            for (int x = 0; x <= worldData.size; x++)
            {
                float h = worldData.GetHeight(x, z);

                if (h < min) min = h;
                if (h > max) max = h;
            }
        }

        Debug.Log($"WORLD HEIGHT RANGE: min={min}, max={max}, difference={max - min}");
    }

    private void PlacePlayerAtArenaCenter()
    {
        if (player == null || worldData == null)
        {
            Debug.LogWarning("Player or worldData is missing.");
            return;
        }

        Vector2Int center = worldData.arenaCenter;

        int testX = center.x + arenaRadius + transitionDistance + 150;
        int testZ = center.y;

        float height = worldData.GetHeight(testX, testZ);

        player.position = new Vector3(
            testX,
            height + 20f,
            testZ
        );

        Debug.Log($"Player placed in RESOURCE AREA: {player.position}, terrain height={height}");
    }

    private void GenerateWorld()
    {
        worldData = WorldDataGenerator.GenerateWorldData(
            mapSize,
            seed,
            arenaRadius,
            transitionDistance,
            resourceRadius,
            arenaHeightMultiplier,
            resourceHeightMultiplier,
            noiseScale,
            octaves,
            persistence,
            lacunarity
        );
    }

    private Vector2Int GetPlayerChunkCoord()
    {
        int chunkX = Mathf.FloorToInt(player.position.x / chunkSize);
        int chunkZ = Mathf.FloorToInt(player.position.z / chunkSize);

        return new Vector2Int(chunkX, chunkZ);
    }

    private void UpdateVisibleChunks()
    {
        Vector2Int playerChunk = GetPlayerChunkCoord();

        for (int zOffset = -viewDistance; zOffset <= viewDistance; zOffset++)
        {
            for (int xOffset = -viewDistance; xOffset <= viewDistance; xOffset++)
            {
                Vector2Int chunkCoord = new Vector2Int(
                    playerChunk.x + xOffset,
                    playerChunk.y + zOffset
                );

                if (IsChunkInsideMap(chunkCoord) && !activeChunks.ContainsKey(chunkCoord))
                {
                    CreateChunk(chunkCoord);
                }
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();

        foreach (Vector2Int coord in activeChunks.Keys)
        {
            float distance = Vector2Int.Distance(coord, playerChunk);

            if (distance > viewDistance + 1)
            {
                chunksToRemove.Add(coord);
            }
        }

        foreach (Vector2Int coord in chunksToRemove)
        {
            Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }
    }

    private bool IsChunkInsideMap(Vector2Int chunkCoord)
    {
        int startX = chunkCoord.x * chunkSize;
        int startZ = chunkCoord.y * chunkSize;

        return startX >= 0 &&
               startZ >= 0 &&
               startX + chunkSize <= mapSize &&
               startZ + chunkSize <= mapSize;
    }

    private void CreateChunk(Vector2Int chunkCoord)
    {
        int startX = chunkCoord.x * chunkSize;
        int startZ = chunkCoord.y * chunkSize;

        GameObject chunkObject = new GameObject($"Chunk {chunkCoord.x}, {chunkCoord.y}");
        chunkObject.transform.parent = transform;
        chunkObject.transform.position = new Vector3(startX, 0f, startZ);

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();

        meshRenderer.sharedMaterial = terrainMaterial;

        Mesh mesh = MeshGenerator.GenerateChunkMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            uvScale
        );

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;

        activeChunks.Add(chunkCoord, chunkObject);
    }

    private void SpawnCave()
    {
        if (cavePrefab == null)
        {
            return;
        }

        Vector2Int cavePosition = worldData.cavePosition;
        float caveHeight = worldData.GetHeight(cavePosition.x, cavePosition.y);

        caveInstance = Instantiate(
            cavePrefab,
            new Vector3(cavePosition.x, caveHeight, cavePosition.y),
            Quaternion.identity
        );

        caveInstance.name = "Cave Entrance";
    }
}