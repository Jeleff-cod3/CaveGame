using UnityEngine;

[System.Serializable]
public class TerrainColorSettings
{
    [Header("Noise Patches")]
    public float largePatchScale = 180f;
    public float mediumPatchScale = 65f;
    public float smallPatchScale = 22f;

    [Range(0f, 1f)]
    public float mediumInfluence = 0.35f;

    [Range(0f, 1f)]
    public float smallInfluence = 0.15f;

    [Header("Savannah Colors")]
    public Color lightSand = new Color(0.78f, 0.63f, 0.33f);
    public Color dryYellow = new Color(0.88f, 0.72f, 0.32f);
    public Color orangeDirt = new Color(0.66f, 0.42f, 0.20f);
    public Color paleGrass = new Color(0.55f, 0.58f, 0.25f);

    [Header("Zone Tinting")]
    [Range(0f, 1f)]
    public float arenaPaleness = 0.35f;

    [Range(0f, 1f)]
    public float resourceGreenness = 0.25f;

    [Header("Height Tinting")]
    [Range(0f, 1f)]
    public float heightDarkening = 0.18f;
}