using UnityEngine;

[System.Serializable]
public class ResourceForestTreeSettings
{
    public bool enabled = true;
    public int spacing = 5;
    [Range(0f, 1f)]
    public float resourceDensity = 0.58f;

    [Header("Patch Noise")]
    public float largePatchScale = 100f;
    public float smallPatchScale = 28f;
    [Range(0f, 2f)]
    public float patchStrength = 1.45f;

    [Header("Placement")]
    public float yOffset = 0.03f;
    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;

    [Header("Forest Mix")]
    [Range(0f, 1f)]
    public float pineChance = 0.62f;

    [Header("Tree Type 1 - Pine")]
    public float minTrunkHeight1 = 2.6f;
    public float maxTrunkHeight1 = 4.8f;
    public float minTrunkRadius1 = 0.10f;
    public float maxTrunkRadius1 = 0.18f;
    public float minCanopyRadius1 = 1.1f;
    public float maxCanopyRadius1 = 2.2f;
    public Color trunkColor1 = new Color(0.35f, 0.20f, 0.10f);
    public Color leafColor1 = new Color(0.09f, 0.42f, 0.12f);

    [Header("Tree Type 2 - Oak")]
    public float minTrunkHeight2 = 1.9f;
    public float maxTrunkHeight2 = 3.2f;
    public float minTrunkRadius2 = 0.14f;
    public float maxTrunkRadius2 = 0.26f;
    public float minCanopyRadius2 = 1.9f;
    public float maxCanopyRadius2 = 3.3f;
    public Color trunkColor2 = new Color(0.30f, 0.18f, 0.08f);
    public Color leafColor2 = new Color(0.20f, 0.58f, 0.16f);

    [Header("Shadows")]
    public bool generateShadows = true;
    public float shadowYOffset = 0.03f;
    public float shadowOffsetX = 0.12f;
    public float shadowOffsetZ = -0.16f;
}
