using System.Collections.Generic;
using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Material terrainMaterial;

    [Header("Chunk Settings")]
    public int chunkSize = 100;
    public int viewDistance = 2;

    [Header("Noise Settings")]
    public int seed = 12345;
    public float noiseScale = 40f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistance = 0.5f;
    public float lacunarity = 2f;

    [Header("Height Settings")]
    public float heightMultiplier = 20f;
    public AnimationCurve heightCurve;

    [Header("Texture Settings")]
    public float uvScale = 20f;

    [Header("Debug")]
    public bool showRuntimeControls = true;

    private Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
    private Vector2Int currentPlayerChunk;

    private string seedInput;
    private string chunkSizeInput;
    private string viewDistanceInput;
    private string noiseScaleInput;
    private string octavesInput;
    private string persistanceInput;
    private string lacunarityInput;
    private string heightMultiplierInput;
    private string uvScaleInput;

    private void Start()
    {
        if (heightCurve == null || heightCurve.length == 0)
        {
            heightCurve = AnimationCurve.Linear(0, 0, 1, 1);
        }

        SyncInputs();
        currentPlayerChunk = GetPlayerChunkCoord();
        UpdateChunks();
    }

    private void Update()
    {
        Vector2Int newPlayerChunk = GetPlayerChunkCoord();

        if (newPlayerChunk != currentPlayerChunk)
        {
            currentPlayerChunk = newPlayerChunk;
            UpdateChunks();
        }

        if (Input.GetKeyDown(KeyCode.F1))
        {
            showRuntimeControls = !showRuntimeControls;
        }
    }

    private Vector2Int GetPlayerChunkCoord()
    {
        int chunkX = Mathf.FloorToInt(player.position.x / chunkSize);
        int chunkZ = Mathf.FloorToInt(player.position.z / chunkSize);

        return new Vector2Int(chunkX, chunkZ);
    }

    private void UpdateChunks()
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

                if (!chunks.ContainsKey(chunkCoord))
                {
                    CreateChunk(chunkCoord);
                }
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();

        foreach (Vector2Int coord in chunks.Keys)
        {
            float distance = Vector2Int.Distance(coord, playerChunk);

            if (distance > viewDistance + 1)
            {
                chunksToRemove.Add(coord);
            }
        }

        foreach (Vector2Int coord in chunksToRemove)
        {
            Destroy(chunks[coord]);
            chunks.Remove(coord);
        }
    }

    private void CreateChunk(Vector2Int chunkCoord)
    {
        GameObject chunkObject = new GameObject($"Chunk {chunkCoord.x}, {chunkCoord.y}");

        chunkObject.transform.parent = transform;

        chunkObject.transform.position = new Vector3(
            chunkCoord.x * chunkSize,
            0,
            chunkCoord.y * chunkSize
        );

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();

        meshRenderer.sharedMaterial = terrainMaterial;

        Vector2 offset = new Vector2(
            chunkCoord.x * chunkSize,
            chunkCoord.y * chunkSize
        );

        float[,] noiseMap = Noise.GenerateNoiseMap(
            chunkSize + 1,
            chunkSize + 1,
            seed,
            noiseScale,
            octaves,
            persistance,
            lacunarity,
            offset,
            Noise.NormalizeMode.Global
        );

        Mesh mesh = MeshGenerator.GenerateTerrainMesh(
            noiseMap,
            heightMultiplier,
            heightCurve,
            uvScale
        );

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;

        chunks.Add(chunkCoord, chunkObject);
    }

    public void RegenerateWorld()
    {
        foreach (GameObject chunk in chunks.Values)
        {
            Destroy(chunk);
        }

        chunks.Clear();

        currentPlayerChunk = GetPlayerChunkCoord();
        UpdateChunks();
    }

    private void SyncInputs()
    {
        seedInput = seed.ToString();
        chunkSizeInput = chunkSize.ToString();
        viewDistanceInput = viewDistance.ToString();
        noiseScaleInput = noiseScale.ToString();
        octavesInput = octaves.ToString();
        persistanceInput = persistance.ToString();
        lacunarityInput = lacunarity.ToString();
        heightMultiplierInput = heightMultiplier.ToString();
        uvScaleInput = uvScale.ToString();
    }

    private void ApplyInputs()
    {
        int.TryParse(seedInput, out seed);
        int.TryParse(chunkSizeInput, out chunkSize);
        int.TryParse(viewDistanceInput, out viewDistance);
        int.TryParse(octavesInput, out octaves);

        float.TryParse(noiseScaleInput, out noiseScale);
        float.TryParse(persistanceInput, out persistance);
        float.TryParse(lacunarityInput, out lacunarity);
        float.TryParse(heightMultiplierInput, out heightMultiplier);
        float.TryParse(uvScaleInput, out uvScale);

        chunkSize = Mathf.Max(2, chunkSize);
        viewDistance = Mathf.Clamp(viewDistance, 1, 8);
        octaves = Mathf.Clamp(octaves, 1, 10);
        noiseScale = Mathf.Max(0.001f, noiseScale);
        persistance = Mathf.Clamp01(persistance);
        lacunarity = Mathf.Max(1f, lacunarity);
        heightMultiplier = Mathf.Max(0f, heightMultiplier);
        uvScale = Mathf.Max(0.001f, uvScale);
    }

    private void OnGUI()
    {
        if (!showRuntimeControls)
        {
            return;
        }

        GUI.Box(new Rect(10, 10, 260, 430), "World Generator");

        GUI.Label(new Rect(25, 40, 100, 20), "Seed");
        seedInput = GUI.TextField(new Rect(130, 40, 120, 20), seedInput);

        GUI.Label(new Rect(25, 70, 100, 20), "Chunk Size");
        chunkSizeInput = GUI.TextField(new Rect(130, 70, 120, 20), chunkSizeInput);

        GUI.Label(new Rect(25, 100, 100, 20), "View Distance");
        viewDistanceInput = GUI.TextField(new Rect(130, 100, 120, 20), viewDistanceInput);

        GUI.Label(new Rect(25, 130, 100, 20), "Noise Scale");
        noiseScaleInput = GUI.TextField(new Rect(130, 130, 120, 20), noiseScaleInput);

        GUI.Label(new Rect(25, 160, 100, 20), "Octaves");
        octavesInput = GUI.TextField(new Rect(130, 160, 120, 20), octavesInput);

        GUI.Label(new Rect(25, 190, 100, 20), "Persistance");
        persistanceInput = GUI.TextField(new Rect(130, 190, 120, 20), persistanceInput);

        GUI.Label(new Rect(25, 220, 100, 20), "Lacunarity");
        lacunarityInput = GUI.TextField(new Rect(130, 220, 120, 20), lacunarityInput);

        GUI.Label(new Rect(25, 250, 100, 20), "Height");
        heightMultiplierInput = GUI.TextField(new Rect(130, 250, 120, 20), heightMultiplierInput);

        GUI.Label(new Rect(25, 280, 100, 20), "UV Scale");
        uvScaleInput = GUI.TextField(new Rect(130, 280, 120, 20), uvScaleInput);

        if (GUI.Button(new Rect(25, 320, 225, 35), "Apply & Regenerate"))
        {
            ApplyInputs();
            RegenerateWorld();
        }

        if (GUI.Button(new Rect(25, 365, 225, 35), "Random Seed"))
        {
            seed = Random.Range(-100000, 100000);
            SyncInputs();
            RegenerateWorld();
        }

        GUI.Label(new Rect(25, 405, 225, 20), "Press F1 to hide/show");
    }
}