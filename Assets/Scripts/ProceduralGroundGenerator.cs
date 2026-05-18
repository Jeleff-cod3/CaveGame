using UnityEngine;
using System.Collections.Generic;

public class PixelArtTerrain : MonoBehaviour
{
    [Header("Map Settings")]
    public int mapWidth = 128;
    public int mapHeight = 128;
    public int chunkSize = 16;

    [Header("Tile Settings")]
    public float tileScale = 1f;
    public float heightNoise = 0.2f;

    [Header("Terrain Noise")]
    public float terrainNoise = 12f;
    public float dirtAmount = 0.35f;

    public Material grassMaterial;
    public Material dirtMaterial;

    private float seed;

    void Start()
    {
        seed = Random.Range(0f, 10000f);
        GenerateChunks();
    }

    void GenerateChunks()
    {
        int chunksX = Mathf.CeilToInt(mapWidth / (float)chunkSize);
        int chunksZ = Mathf.CeilToInt(mapHeight / (float)chunkSize);

        for (int cx = 0; cx < chunksX; cx++)
        {
            for (int cz = 0; cz < chunksZ; cz++)
            {
                GenerateChunk(cx, cz);
            }
        }
    }

    void GenerateChunk(int chunkX, int chunkZ)
    {
        GameObject chunk = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunk.transform.parent = transform;

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();
        mr.materials = new Material[] { grassMaterial, dirtMaterial };

        List<Vector3> vertices = new List<Vector3>();
        List<int> grassTriangles = new List<int>();
        List<int> dirtTriangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        int startX = chunkX * chunkSize;
        int startZ = chunkZ * chunkSize;
        int vertexIndex = 0;

        for (int x = 0; x < chunkSize && startX + x < mapWidth; x++)
        {
            for (int z = 0; z < chunkSize && startZ + z < mapHeight; z++)
            {
                float noiseValue = Mathf.PerlinNoise((startX + x + seed) / terrainNoise, (startZ + z + seed) / terrainNoise);
                float y = noiseValue * heightNoise;

                // Add four vertices per tile
                vertices.Add(new Vector3((startX + x) * tileScale, y, (startZ + z) * tileScale));
                vertices.Add(new Vector3((startX + x) * tileScale, y, (startZ + z + 1) * tileScale));
                vertices.Add(new Vector3((startX + x + 1) * tileScale, y, (startZ + z + 1) * tileScale));
                vertices.Add(new Vector3((startX + x + 1) * tileScale, y, (startZ + z) * tileScale));

                // UVs
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(1, 0));

                // Assign triangles to correct submesh
                if (noiseValue < dirtAmount)
                {
                    // Dirt submesh = 1
                    dirtTriangles.Add(vertexIndex);
                    dirtTriangles.Add(vertexIndex + 1);
                    dirtTriangles.Add(vertexIndex + 2);
                    dirtTriangles.Add(vertexIndex);
                    dirtTriangles.Add(vertexIndex + 2);
                    dirtTriangles.Add(vertexIndex + 3);
                }
                else
                {
                    // Grass submesh = 0
                    grassTriangles.Add(vertexIndex);
                    grassTriangles.Add(vertexIndex + 1);
                    grassTriangles.Add(vertexIndex + 2);
                    grassTriangles.Add(vertexIndex);
                    grassTriangles.Add(vertexIndex + 2);
                    grassTriangles.Add(vertexIndex + 3);
                }

                vertexIndex += 4;
            }
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.subMeshCount = 2; // grass and dirt

        mesh.SetTriangles(grassTriangles.ToArray(), 0);
        mesh.SetTriangles(dirtTriangles.ToArray(), 1);

        mesh.RecalculateNormals();

        mf.mesh = mesh;

        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;
    }
}