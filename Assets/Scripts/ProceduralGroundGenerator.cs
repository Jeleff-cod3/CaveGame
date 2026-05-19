using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class SmoothTerrain : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int width = 256;          // X-axis vertices (increase for bigger maps)
    public int height = 256;         // Z-axis vertices
    public float scale = 1f;         // distance between vertices
    public float heightNoise = 10f;  // max vertical height
    public float noiseScale = 20f;   // controls the "size" of hills

    public Material terrainMaterial; // one material for the whole mesh

    private Mesh mesh;
    private float seed;

    void Start()
    {
        seed = Random.Range(0f, 10000f);
        GenerateTerrain();
    }

    void GenerateTerrain()
    {
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // support large meshes
        GetComponent<MeshFilter>().mesh = mesh;
        GetComponent<MeshRenderer>().material = terrainMaterial;

        int vertCountX = width + 1;
        int vertCountZ = height + 1;

        Vector3[] vertices = new Vector3[vertCountX * vertCountZ];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[width * height * 6];

        // --- Generate vertices ---
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                // Multiple Perlin noise layers for variety
                float baseNoise = Mathf.PerlinNoise((x + seed) / noiseScale, (z + seed) / noiseScale) * heightNoise;
                float detailNoise = Mathf.PerlinNoise((x + seed + 1000) / (noiseScale / 2f), (z + seed + 1000) / (noiseScale / 2f)) * (heightNoise / 2f);
                float macroNoise = Mathf.PerlinNoise((x + seed + 2000) / (noiseScale * 2f), (z + seed + 2000) / (noiseScale * 2f)) * (heightNoise * 1.5f);

                // Regional factor to create valleys and highlands
                float regionFactor = Mathf.PerlinNoise((x + seed) / (noiseScale * 4f), (z + seed) / (noiseScale * 4f));

                float y = (baseNoise + detailNoise + macroNoise) * regionFactor;

                vertices[z * vertCountX + x] = new Vector3(x * scale, y, z * scale);
                uvs[z * vertCountX + x] = new Vector2((float)x / width, (float)z / height);
            }
        }

        // --- Generate triangles ---
        int t = 0;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * vertCountX + x;

                triangles[t++] = i;
                triangles[t++] = i + vertCountX;
                triangles[t++] = i + 1;

                triangles[t++] = i + 1;
                triangles[t++] = i + vertCountX;
                triangles[t++] = i + vertCountX + 1;
            }
        }

        // --- Assign mesh ---
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        // --- Collider ---
        MeshCollider mc = GetComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;
    }
}