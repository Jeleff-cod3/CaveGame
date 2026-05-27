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

    [Header("Terrain Colors")]
    public TerrainColorSettings terrainColorSettings = new TerrainColorSettings();

    [Header("Ground Grass")]
    public GroundGrassSettings groundGrassSettings = new GroundGrassSettings();
    public Material groundGrassMaterial;

    [Header("Grass")]
    public GrassSettings grassSettings = new GrassSettings();
    public Material grassMaterial;

    [Header("Trees")]
    public TreeSettings treeSettings = new TreeSettings();
    public Material treeMaterial;
    public Material treeShadowMaterial;

    [Header("Rocks")]
    public RockSettings rockSettings = new RockSettings();
    public Material rockMaterial;
    public Material rockShadowMaterial;

    [Header("Extra Vegetation")]
    public VegetationSettings vegetationSettings = new VegetationSettings();
    public Material vegetationMaterial;
    public Material vegetationShadowMaterial;

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
        if (Mathf.Abs(transform.lossyScale.y) < 0.001f)
        {
            Debug.LogError("WorldChunkRenderer parent has Y scale near 0. This will flatten all chunks.");
        }

        Debug.Log($"WorldChunkRenderer scale: {transform.lossyScale}");
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
        UpdateGroundGrassMaterial();
    }

    private void UpdateGroundGrassMaterial()
    {
        if (groundGrassMaterial == null || player == null || groundGrassSettings == null)
        {
            return;
        }

        groundGrassMaterial.SetVector("_PlayerPosition", player.position);

        groundGrassMaterial.SetFloat("_WindStrength", groundGrassSettings.windStrength);
        groundGrassMaterial.SetFloat("_WindSpeed", groundGrassSettings.windSpeed);
        groundGrassMaterial.SetFloat("_WindScale", groundGrassSettings.windScale);

        groundGrassMaterial.SetFloat("_PushRadius", groundGrassSettings.playerPushRadius);
        groundGrassMaterial.SetFloat("_PushStrength", groundGrassSettings.playerPushStrength);
        groundGrassMaterial.SetFloat("_FlattenStrength", groundGrassSettings.playerFlattenStrength);
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
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.localPosition = new Vector3(startX, 0f, startZ);
        chunkObject.transform.localRotation = Quaternion.identity;
        chunkObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();

        meshRenderer.sharedMaterial = terrainMaterial;

        Mesh mesh = MeshGenerator.GenerateChunkMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            uvScale,
            seed,
            terrainColorSettings
        );

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;

        CreateGroundGrassForChunk(chunkObject, startX, startZ);
        CreateGrassForChunk(chunkObject, startX, startZ);
        CreateTreesForChunk(chunkObject, startX, startZ);
        CreateVegetationForChunk(chunkObject, startX, startZ);
        CreateRocksForChunk(chunkObject, startX, startZ);

        activeChunks.Add(chunkCoord, chunkObject);
    }

    private void CreateGroundGrassForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (groundGrassSettings == null || !groundGrassSettings.enabled)
        {
            return;
        }

        if (groundGrassMaterial == null)
        {
            Debug.LogWarning("Ground grass material is missing.");
            return;
        }

        Mesh groundGrassMesh = GroundGrassChunkGenerator.GenerateGroundGrassMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            groundGrassSettings
        );

        if (groundGrassMesh == null || groundGrassMesh.vertexCount == 0)
        {
            return;
        }

        GameObject groundGrassObject = new GameObject("Ground Grass");
        groundGrassObject.transform.SetParent(chunkObject.transform, false);
        groundGrassObject.transform.localPosition = Vector3.zero;
        groundGrassObject.transform.localRotation = Quaternion.identity;
        groundGrassObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = groundGrassObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = groundGrassObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = groundGrassMesh;
        meshRenderer.sharedMaterial = groundGrassMaterial;
    }

    private void CreateRocksForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (rockSettings == null || !rockSettings.enabled)
        {
            return;
        }

        if (rockMaterial == null)
        {
            Debug.LogWarning("Rock material is missing.");
            return;
        }

        RockChunkMeshes meshes = RockChunkGenerator.GenerateRockMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            rockSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Rock Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (rockShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = rockShadowMaterial;
            }
        }

        if (meshes.rockMesh != null && meshes.rockMesh.vertexCount > 0)
        {
            GameObject rockObject = new GameObject("Rocks");
            rockObject.transform.SetParent(chunkObject.transform, false);
            rockObject.transform.localPosition = Vector3.zero;
            rockObject.transform.localRotation = Quaternion.identity;
            rockObject.transform.localScale = Vector3.one;

            MeshFilter rockFilter = rockObject.AddComponent<MeshFilter>();
            MeshRenderer rockRenderer = rockObject.AddComponent<MeshRenderer>();

            rockFilter.sharedMesh = meshes.rockMesh;
            rockRenderer.sharedMaterial = rockMaterial;
        }
    }

    private void CreateVegetationForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (vegetationSettings == null || !vegetationSettings.enabled)
        {
            return;
        }

        if (vegetationMaterial == null)
        {
            Debug.LogWarning("Vegetation material is missing.");
            return;
        }

        VegetationChunkMeshes meshes = VegetationChunkGenerator.GenerateVegetationMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            vegetationSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Vegetation Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (vegetationShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = vegetationShadowMaterial;
            }
        }

        if (meshes.vegetationMesh != null && meshes.vegetationMesh.vertexCount > 0)
        {
            GameObject vegetationObject = new GameObject("Extra Vegetation");
            vegetationObject.transform.SetParent(chunkObject.transform, false);
            vegetationObject.transform.localPosition = Vector3.zero;
            vegetationObject.transform.localRotation = Quaternion.identity;
            vegetationObject.transform.localScale = Vector3.one;

            MeshFilter vegetationFilter = vegetationObject.AddComponent<MeshFilter>();
            MeshRenderer vegetationRenderer = vegetationObject.AddComponent<MeshRenderer>();

            vegetationFilter.sharedMesh = meshes.vegetationMesh;
            vegetationRenderer.sharedMaterial = vegetationMaterial;
        }
    }

    private void CreateTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (treeSettings == null || !treeSettings.enabled)
        {
            return;
        }

        if (treeMaterial == null)
        {
            Debug.LogWarning("Tree material is missing.");
            return;
        }

        TreeChunkMeshes meshes = TreeChunkGenerator.GenerateTreeMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            treeSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Tree Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (treeShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = treeShadowMaterial;
            }
        }

        if (meshes.treeMesh != null && meshes.treeMesh.vertexCount > 0)
        {
            GameObject treeObject = new GameObject("Trees");
            treeObject.transform.SetParent(chunkObject.transform, false);
            treeObject.transform.localPosition = Vector3.zero;
            treeObject.transform.localRotation = Quaternion.identity;
            treeObject.transform.localScale = Vector3.one;

            MeshFilter treeFilter = treeObject.AddComponent<MeshFilter>();
            MeshRenderer treeRenderer = treeObject.AddComponent<MeshRenderer>();

            treeFilter.sharedMesh = meshes.treeMesh;
            treeRenderer.sharedMaterial = treeMaterial;
        }
    }
    private void CreateGrassForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (grassSettings == null || !grassSettings.enabled)
        {
            return;
        }

        if (grassMaterial == null)
        {
            Debug.LogWarning("Grass material is missing.");
            return;
        }

        Mesh grassMesh = GrassChunkGenerator.GenerateGrassMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            grassSettings
        );

        if (grassMesh.vertexCount == 0)
        {
            return;
        }

        GameObject grassObject = new GameObject("Grass");
        grassObject.transform.SetParent(chunkObject.transform, false);
        grassObject.transform.localPosition = Vector3.zero;
        grassObject.transform.localRotation = Quaternion.identity;
        grassObject.transform.localScale = Vector3.one;

        MeshFilter grassMeshFilter = grassObject.AddComponent<MeshFilter>();
        MeshRenderer grassMeshRenderer = grassObject.AddComponent<MeshRenderer>();

        grassMeshFilter.sharedMesh = grassMesh;
        grassMeshRenderer.sharedMaterial = grassMaterial;
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