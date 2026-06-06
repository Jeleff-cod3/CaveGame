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
    public static ResourceForestTreeChunkMeshes GenerateTrees(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        ResourceForestTreeSettings settings
    )
    {
        List<Vector3> treeVerts = new List<Vector3>();
        List<int> treeTris = new List<int>();
        List<Color> treeColors = new List<Color>();
        List<Vector3> shadowVerts = new List<Vector3>();
        List<int> shadowTris = new List<int>();

        System.Random random = new System.Random(seed ^ startX * 73856093 ^ startZ * 19349663);

        for (int z = 0; z <= chunkSize; z += settings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += settings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ)) continue;
                if (worldData.GetZone(worldX, worldZ) != TerrainZone.Resource) continue;

                float patchValue = Mathf.PerlinNoise(
                    (worldX + seed * 11.7f) / settings.largePatchScale,
                    (worldZ - seed * 8.3f) / settings.largePatchScale
                );
                float density = Mathf.Clamp01(settings.resourceDensity * Mathf.Lerp(0.5f, 1.7f, Mathf.Pow(patchValue, 1.1f)));
                if ((float)random.NextDouble() > density) continue;

                float jitter = settings.spacing * 0.4f;
                float finalX = worldX + Mathf.Lerp(-jitter, jitter, (float)random.NextDouble());
                float finalZ = worldZ + Mathf.Lerp(-jitter, jitter, (float)random.NextDouble());

                int sampleX = Mathf.RoundToInt(finalX);
                int sampleZ = Mathf.RoundToInt(finalZ);
                if (!worldData.IsInsideMap(sampleX, sampleZ)) continue;
                if (IsTooSteep(worldData, sampleX, sampleZ, settings.maxSlopeAngle)) continue;

                float groundY = worldData.GetHeight(sampleX, sampleZ) + settings.yOffset;
                Vector3 pos = new Vector3(finalX - startX, groundY, finalZ - startZ);

                // Pick Pine or Oak
                bool usePine = random.NextDouble() < 0.5;
                float trunkH = usePine ? Mathf.Lerp(settings.minTrunkHeight1, settings.maxTrunkHeight1, (float)random.NextDouble())
                                        : Mathf.Lerp(settings.minTrunkHeight2, settings.maxTrunkHeight2, (float)random.NextDouble());
                float trunkR = usePine ? Mathf.Lerp(settings.minTrunkRadius1, settings.maxTrunkRadius1, (float)random.NextDouble())
                                        : Mathf.Lerp(settings.minTrunkRadius2, settings.maxTrunkRadius2, (float)random.NextDouble());
                float canopyR = usePine ? Mathf.Lerp(settings.minCanopyRadius1, settings.maxCanopyRadius1, (float)random.NextDouble())
                                         : Mathf.Lerp(settings.minCanopyRadius2, settings.maxCanopyRadius2, (float)random.NextDouble());
                Color trunkC = usePine ? settings.trunkColor1 : settings.trunkColor2;
                Color leafC = usePine ? settings.leafColor1 : settings.leafColor2;

                AddLowPolyTree(treeVerts, treeTris, treeColors, pos, trunkH, trunkR, canopyR, trunkC, leafC, random);

                if (settings.generateShadows)
                {
                    AddShadow(shadowVerts, shadowTris, pos, canopyR);
                }
            }
        }

        ResourceForestTreeChunkMeshes result = new ResourceForestTreeChunkMeshes();
        if (treeVerts.Count > 0)
        {
            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(treeVerts);
            mesh.SetTriangles(treeTris, 0);
            mesh.SetColors(treeColors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            result.treeMesh = mesh;
        }
        if (shadowVerts.Count > 0)
        {
            Mesh shadowMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            shadowMesh.SetVertices(shadowVerts);
            shadowMesh.SetTriangles(shadowTris, 0);
            shadowMesh.RecalculateNormals();
            shadowMesh.RecalculateBounds();
            result.shadowMesh = shadowMesh;
        }
        return result;
    }

    private static bool IsTooSteep(WorldData worldData, int x, int z, float maxSlope)
    {
        float c = worldData.GetHeight(x, z);
        float r = worldData.GetHeight(x + 1, z);
        float f = worldData.GetHeight(x, z + 1);
        return Vector3.Angle(Vector3.Cross(new Vector3(0, f - c, 1), new Vector3(1, r - c, 0)), Vector3.up) > maxSlope;
    }

    private static void AddLowPolyTree(List<Vector3> verts, List<int> tris, List<Color> colors,
        Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius, Color trunkCol, Color leafCol, System.Random r)
    {
        int startIndex = verts.Count;
        verts.Add(pos + Vector3.zero);
        verts.Add(pos + Vector3.up * trunkHeight);
        tris.Add(startIndex);
        tris.Add(startIndex + 1);
        tris.Add(startIndex); 
        colors.Add(trunkCol);
        colors.Add(trunkCol);

        Vector3 canopyTop = pos + Vector3.up * (trunkHeight + canopyRadius);
        verts.Add(pos + Vector3.up * trunkHeight);
        verts.Add(canopyTop);
        tris.Add(startIndex + 2);
        tris.Add(startIndex + 3);
        tris.Add(startIndex + 2);
        colors.Add(leafCol);
        colors.Add(leafCol);
    }

    private static void AddShadow(List<Vector3> verts, List<int> tris, Vector3 pos, float radius)
    {
        int startIndex = verts.Count;
        verts.Add(pos);
        verts.Add(pos + new Vector3(radius, 0, 0));
        tris.Add(startIndex);
        tris.Add(startIndex + 1);
        tris.Add(startIndex);
    }
}