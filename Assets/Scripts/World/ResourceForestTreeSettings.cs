using UnityEngine;

[System.Serializable]
public class ResourceForestTreeSettings
{
    public bool enabled = true;
    public int spacing = 6;
    [Range(0f, 1f)]
    public float resourceDensity = 0.45f;

    [Header("Patch Noise")]
    public float largePatchScale = 100f;
    public float smallPatchScale = 28f;
    [Range(0f, 2f)]
    public float patchStrength = 1.2f;

    [Header("Placement")]
    public float yOffset = 0.03f;
    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;

    [Header("Tree Type 1 - Pine")]
    public float minTrunkHeight1 = 2.0f;
    public float maxTrunkHeight1 = 3.5f;
    public float minTrunkRadius1 = 0.08f;
    public float maxTrunkRadius1 = 0.15f;
    public float minCanopyRadius1 = 1.0f;
    public float maxCanopyRadius1 = 2.0f;
    public Color trunkColor1 = new Color(0.35f, 0.20f, 0.10f);
    public Color leafColor1 = new Color(0.05f, 0.45f, 0.05f);

    [Header("Tree Type 2 - Oak")]
    public float minTrunkHeight2 = 1.5f;
    public float maxTrunkHeight2 = 2.5f;
    public float minTrunkRadius2 = 0.12f;
    public float maxTrunkRadius2 = 0.22f;
    public float minCanopyRadius2 = 2.0f;
    public float maxCanopyRadius2 = 3.5f;
    public Color trunkColor2 = new Color(0.30f, 0.18f, 0.08f);
    public Color leafColor2 = new Color(0.12f, 0.55f, 0.12f);

    [Header("Shadows")]
    public bool generateShadows = true;
}