using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class MapGenerator : MonoBehaviour
{
    [Header("Map Settings")]
    public int mapWidth = 100;
    public int mapHeight = 100;

    [Header("Noise Settings")]
    public int seed = 12345;
    public float noiseScale = 30f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    public Vector2 offset;

    [Header("Height Settings")]
    public float heightMultiplier = 20f;
	public AnimationCurve heightCurve;

    [Header("Normalize Mode")]
    public Noise.NormalizeMode normalizeMode = Noise.NormalizeMode.Local;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    private void Start()
    {
        GenerateMap();
    }

    private void OnValidate()
    {
        if (mapWidth < 2) mapWidth = 2;
        if (mapHeight < 2) mapHeight = 2;
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 1) octaves = 1;
    }

    public void GenerateMap()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        float[,] noiseMap = Noise.GenerateNoiseMap(
            mapWidth,
            mapHeight,
            seed,
            noiseScale,
            octaves,
            persistance,
            lacunarity,
            offset,
            normalizeMode
        );

        Mesh terrainMesh = MeshGenerator.GenerateTerrainMesh(noiseMap, heightMultiplier, heightCurve);

        meshFilter.sharedMesh = terrainMesh;
        meshCollider.sharedMesh = terrainMesh;
    }
}