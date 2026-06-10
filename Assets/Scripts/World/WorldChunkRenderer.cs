using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;

public class WorldChunkRenderer : MonoBehaviour
{
    [Header("Collision Layers")]
    public string groundLayerName = "Ground";

    [Header("Navigation")]
    public NavMeshSurface navMeshSurface;
    public bool buildNavMeshAtRuntime = true;
    public bool rocksBlockNavigation = true;
    public bool treesBlockNavigation = true;
    public bool deadTreesBlockNavigation = true;

    [Header("Full Map NavMesh Baking")]
    public bool renderFullMapBeforeNavMeshBake = true;
    public bool hideChunksOutsideViewAfterBake = true;

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

    [Header("Resource Forest Trees")]
    public ResourceForestTreeSettings resourceForestTreeSettings;
    public Material resourceForestTreeMaterial;
    public Material resourceForestTreeShadowMaterial;

    [Header("Rocks")]
    public RockSettings rockSettings = new RockSettings();
    public Material rockMaterial;
    public Material rockShadowMaterial;

    [Header("Extra Vegetation")]
    public VegetationSettings vegetationSettings = new VegetationSettings();
    public Material vegetationMaterial;
    public Material vegetationShadowMaterial;

    [Header("Dead Trees")]
    public DeadTreeSettings deadTreeSettings = new DeadTreeSettings();
    public Material deadTreeMaterial;
    public Material deadTreeShadowMaterial;

    [Header("Terrain Settings")]
    public int seed = 12345;
    public float arenaHeightMultiplier = 2f;
    public float resourceHeightMultiplier = 16f;
    public float noiseScale = 120f;
    public int octaves = 4;

    [Range(0f, 1f)]
    public float persistence = 0.45f;

    public float lacunarity = 2f;
    public float uvScale = 30f;

    private WorldData worldData;

    private readonly Dictionary<Vector2Int, GameObject> activeChunks =
        new Dictionary<Vector2Int, GameObject>();

    private readonly HashSet<Transform> trackedPlayers =
        new HashSet<Transform>();

    private readonly HashSet<Vector2Int> currentTrackedPlayerChunks =
        new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int> trackedPlayerChunkBuffer =
        new HashSet<Vector2Int>();

    private GameObject caveInstance;

    private void Start()
    {
        if (Mathf.Abs(transform.lossyScale.y) < 0.001f)
        {
            Debug.LogError("WorldChunkRenderer parent has Y scale near 0. This will flatten all chunks.");
        }

        Debug.Log($"WorldChunkRenderer scale: {transform.lossyScale}");

        if (navMeshSurface == null)
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
        }

        GenerateWorld();
        DebugWorldHeightRange();
        PlacePlayerAtArenaCenter();
        SyncTrackedPlayerChunks();

        if (renderFullMapBeforeNavMeshBake)
        {
            RenderAllChunksForNavMeshBake();
        }
        else
        {
            UpdateVisibleChunks();
        }

        BuildWorldNavMesh();
        SnapPlayerToNavMesh();

        if (renderFullMapBeforeNavMeshBake && hideChunksOutsideViewAfterBake)
        {
            UpdateChunkVisibilityOnly();
        }

        SpawnCave();
    }

    private void Update()
    {
        if (HaveTrackedPlayerChunksChanged())
        {
            SyncTrackedPlayerChunks();

            if (renderFullMapBeforeNavMeshBake)
            {
                UpdateChunkVisibilityOnly();
            }
            else
            {
                UpdateVisibleChunks();

                // In streaming mode, new blockers can appear after the first bake,
                // so we rebuild when chunks change.
                BuildWorldNavMesh();
            }
        }

        UpdateGroundGrassMaterial();
    }

    public void SetPrimaryPlayer(Transform playerTransform)
    {
        player = playerTransform;
        RegisterTrackedPlayer(playerTransform);
    }

    public void RegisterTrackedPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        trackedPlayers.Add(playerTransform);
    }

    public void UnregisterTrackedPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        trackedPlayers.Remove(playerTransform);

        if (player == playerTransform)
        {
            player = null;
        }
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

    private void RenderAllChunksForNavMeshBake()
    {
        int chunksPerAxis = mapSize / chunkSize;

        for (int z = 0; z < chunksPerAxis; z++)
        {
            for (int x = 0; x < chunksPerAxis; x++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, z);

                if (IsChunkInsideMap(chunkCoord) && !activeChunks.ContainsKey(chunkCoord))
                {
                    CreateChunk(chunkCoord);
                }
            }
        }

        Debug.Log($"Rendered {activeChunks.Count} chunks before NavMesh bake.");
    }

    private void BuildWorldNavMesh()
    {
        if (!buildNavMeshAtRuntime)
        {
            return;
        }

        if (navMeshSurface == null)
        {
            Debug.LogWarning("NavMeshSurface is missing.");
            return;
        }

        navMeshSurface.BuildNavMesh();

        Debug.Log("Runtime NavMesh built for generated world.");
    }

    private Vector2Int GetChunkCoord(Vector3 worldPosition)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkZ = Mathf.FloorToInt(worldPosition.z / chunkSize);

        return new Vector2Int(chunkX, chunkZ);
    }

    private void UpdateVisibleChunks()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);

        foreach (Vector2Int playerChunk in trackedPlayerChunkBuffer)
        {
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
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();

        foreach (Vector2Int coord in activeChunks.Keys)
        {
            if (!IsChunkInRangeOfTrackedPlayers(coord, trackedPlayerChunkBuffer, viewDistance + 1))
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

    private void UpdateChunkVisibilityOnly()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);

        foreach (KeyValuePair<Vector2Int, GameObject> pair in activeChunks)
        {
            bool shouldBeVisible = IsChunkInRangeOfTrackedPlayers(
                pair.Key,
                trackedPlayerChunkBuffer,
                viewDistance + 1
            );
            SetChunkRenderersEnabled(pair.Value, shouldBeVisible);
        }
    }

    private bool HaveTrackedPlayerChunksChanged()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);
        return !currentTrackedPlayerChunks.SetEquals(trackedPlayerChunkBuffer);
    }

    private void SyncTrackedPlayerChunks()
    {
        GetTrackedPlayerChunks(currentTrackedPlayerChunks);
    }

    private void GetTrackedPlayerChunks(HashSet<Vector2Int> chunkCoords)
    {
        chunkCoords.Clear();
        CleanupTrackedPlayers();

        if (player != null)
        {
            chunkCoords.Add(GetChunkCoord(player.position));
        }

        foreach (Transform trackedPlayer in trackedPlayers)
        {
            if (trackedPlayer == null || trackedPlayer == player)
            {
                continue;
            }

            chunkCoords.Add(GetChunkCoord(trackedPlayer.position));
        }

        if (chunkCoords.Count == 0)
        {
            chunkCoords.Add(Vector2Int.zero);
        }
    }

    private void CleanupTrackedPlayers()
    {
        trackedPlayers.RemoveWhere(trackedPlayer => trackedPlayer == null);

        if (player == null)
        {
            return;
        }

        if (!player.gameObject.scene.IsValid())
        {
            player = null;
        }
    }

    private static bool IsChunkInRangeOfTrackedPlayers(
        Vector2Int chunkCoord,
        IEnumerable<Vector2Int> playerChunks,
        float maxDistance
    )
    {
        foreach (Vector2Int playerChunk in playerChunks)
        {
            if (Vector2Int.Distance(chunkCoord, playerChunk) <= maxDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void SetChunkRenderersEnabled(GameObject chunkObject, bool enabled)
    {
        MeshRenderer[] renderers = chunkObject.GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer meshRenderer in renderers)
        {
            meshRenderer.enabled = enabled;
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
        int terrainLayer = LayerMask.NameToLayer(groundLayerName);

        if (terrainLayer >= 0)
        {
            chunkObject.layer = terrainLayer;
        }
        else
        {
            Debug.LogWarning($"Layer '{groundLayerName}' does not exist. Create it in Project Settings > Tags and Layers.");
        }
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
        CreateResourceForestTreesForChunk(chunkObject, startX, startZ);
        CreateVegetationForChunk(chunkObject, startX, startZ);
        CreateDeadTreesForChunk(chunkObject, startX, startZ);
        CreateRocksForChunk(chunkObject, startX, startZ);

        activeChunks.Add(chunkCoord, chunkObject);
    }

    private void MarkObjectAsNotWalkable(GameObject obj)
    {
        NavMeshModifier modifier = obj.AddComponent<NavMeshModifier>();

        modifier.overrideArea = true;

        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");

        if (notWalkableArea >= 0)
        {
            modifier.area = notWalkableArea;
        }
        else
        {
            modifier.area = 1;
        }
    }

    private void UpdateGroundGrassMaterial()
    {
        if (groundGrassMaterial == null || groundGrassSettings == null)
        {
            return;
        }

        Transform grassPlayer = player;

        if (grassPlayer == null)
        {
            foreach (Transform trackedPlayer in trackedPlayers)
            {
                if (trackedPlayer != null)
                {
                    grassPlayer = trackedPlayer;
                    break;
                }
            }
        }

        if (grassPlayer == null)
        {
            return;
        }

        groundGrassMaterial.SetVector("_PlayerPosition", grassPlayer.position);

        groundGrassMaterial.SetFloat("_WindStrength", groundGrassSettings.windStrength);
        groundGrassMaterial.SetFloat("_WindSpeed", groundGrassSettings.windSpeed);
        groundGrassMaterial.SetFloat("_WindScale", groundGrassSettings.windScale);

        groundGrassMaterial.SetFloat("_PushRadius", groundGrassSettings.playerPushRadius);
        groundGrassMaterial.SetFloat("_PushStrength", groundGrassSettings.playerPushStrength);
        groundGrassMaterial.SetFloat("_FlattenStrength", groundGrassSettings.playerFlattenStrength);
    }

    private void DebugWorldHeightRange()
    {
        if (worldData == null)
        {
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int z = 0; z <= worldData.size; z++)
        {
            for (int x = 0; x <= worldData.size; x++)
            {
                float h = worldData.GetHeight(x, z);

                if (h < min)
                {
                    min = h;
                }

                if (h > max)
                {
                    max = h;
                }
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
            height + 1f,
            testZ
        );

        Debug.Log($"Player placed in RESOURCE AREA: {player.position}, terrain height={height}");
    }

    private void SnapPlayerToNavMesh()
    {
        if (player == null)
        {
            return;
        }

        if (NavMesh.SamplePosition(player.position, out NavMeshHit hit, 50f, NavMesh.AllAreas))
        {
            player.position = hit.position + Vector3.up * 0.1f;

            NavMeshAgent agent = player.GetComponent<NavMeshAgent>();

            if (agent != null)
            {
                agent.Warp(hit.position);
            }

            Debug.Log($"Player snapped to NavMesh at {hit.position}");
        }
        else
        {
            Debug.LogError("Could not find NavMesh near player. NavMesh probably did not bake near the spawn point.");
        }
    }

    private void CreateResourceForestTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (resourceForestTreeSettings == null || !resourceForestTreeSettings.enabled)
        {
            return;
        }

        var meshData = ResourceForestTreeChunkGenerator.GenerateTrees(
            worldData, startX, startZ, chunkSize, seed, resourceForestTreeSettings
        );

        if (meshData.treeMesh != null)
        {
            GameObject treeObj = new GameObject("ResourceForestTrees");
            treeObj.transform.SetParent(chunkObject.transform, false);
            var mf = treeObj.AddComponent<MeshFilter>();
            var mr = treeObj.AddComponent<MeshRenderer>();
            mf.sharedMesh = meshData.treeMesh;
            mr.sharedMaterial = resourceForestTreeMaterial != null
                ? resourceForestTreeMaterial
                : treeMaterial;
        }

        if (meshData.shadowMesh != null)
        {
            GameObject shadowObj = new GameObject("ResourceForestShadows");
            shadowObj.transform.SetParent(chunkObject.transform, false);
            var mf = shadowObj.AddComponent<MeshFilter>();
            var mr = shadowObj.AddComponent<MeshRenderer>();
            mf.sharedMesh = meshData.shadowMesh;
            mr.sharedMaterial = resourceForestTreeShadowMaterial != null
                ? resourceForestTreeShadowMaterial
                : treeShadowMaterial;
        }
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

        if (grassMesh == null || grassMesh.vertexCount == 0)
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
            MeshCollider treeCollider = treeObject.AddComponent<MeshCollider>();

            treeFilter.sharedMesh = meshes.treeMesh;
            treeRenderer.sharedMaterial = treeMaterial;
            treeCollider.sharedMesh = meshes.treeMesh;

            if (treesBlockNavigation)
            {
                MarkObjectAsNotWalkable(treeObject);
            }
        }
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
            MeshCollider rockCollider = rockObject.AddComponent<MeshCollider>();

            rockFilter.sharedMesh = meshes.rockMesh;
            rockRenderer.sharedMaterial = rockMaterial;
            rockCollider.sharedMesh = meshes.rockMesh;

            if (rocksBlockNavigation)
            {
                MarkObjectAsNotWalkable(rockObject);
            }
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

    private void CreateDeadTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (deadTreeSettings == null || !deadTreeSettings.enabled)
        {
            return;
        }

        if (deadTreeMaterial == null)
        {
            Debug.LogWarning("Dead tree material is missing.");
            return;
        }

        DeadTreeChunkMeshes meshes = DeadTreeChunkGenerator.GenerateDeadTreeMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            deadTreeSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Dead Tree Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (deadTreeShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = deadTreeShadowMaterial;
            }
        }

        if (meshes.deadTreeMesh != null && meshes.deadTreeMesh.vertexCount > 0)
        {
            GameObject deadTreeObject = new GameObject("Dead Trees");
            deadTreeObject.transform.SetParent(chunkObject.transform, false);
            deadTreeObject.transform.localPosition = Vector3.zero;
            deadTreeObject.transform.localRotation = Quaternion.identity;
            deadTreeObject.transform.localScale = Vector3.one;

            MeshFilter deadTreeFilter = deadTreeObject.AddComponent<MeshFilter>();
            MeshRenderer deadTreeRenderer = deadTreeObject.AddComponent<MeshRenderer>();
            MeshCollider deadTreeCollider = deadTreeObject.AddComponent<MeshCollider>();

            deadTreeFilter.sharedMesh = meshes.deadTreeMesh;
            deadTreeRenderer.sharedMaterial = deadTreeMaterial;
            deadTreeCollider.sharedMesh = meshes.deadTreeMesh;

            if (deadTreesBlockNavigation)
            {
                MarkObjectAsNotWalkable(deadTreeObject);
            }
        }
    }

    private void SpawnCave()
    {
        if (cavePrefab == null || worldData == null)
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
