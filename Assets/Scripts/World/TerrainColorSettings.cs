using UnityEngine;

[System.Serializable]
public class TerrainColorSettings
{
    [Header("Noise Patches")]
    public float largePatchScale = 140f;
    public float mediumPatchScale = 48f;
    public float smallPatchScale = 18f;

    [Range(0f, 1f)]
    public float mediumInfluence = 0.45f;

    [Range(0f, 1f)]
    public float smallInfluence = 0.22f;

    [Header("Arena Colors")]
    public Color arenaDust = new Color(0.76f, 0.61f, 0.27f);
    public Color arenaGold = new Color(0.92f, 0.77f, 0.24f);
    public Color arenaStraw = new Color(0.82f, 0.73f, 0.35f);

    [Header("Resource Colors")]
    public Color resourceMoss = new Color(0.24f, 0.44f, 0.16f);
    public Color resourceGrass = new Color(0.30f, 0.58f, 0.18f);
    public Color resourceBrightGrass = new Color(0.44f, 0.70f, 0.26f);

    [Header("Transition Colors")]
    public Color transitionOlive = new Color(0.48f, 0.52f, 0.22f);
    public Color transitionBrush = new Color(0.62f, 0.58f, 0.26f);
    public Color transitionEarth = new Color(0.54f, 0.39f, 0.19f);

    [Header("Border Color")]
    public Color borderColor = new Color(0.58f, 0.43f, 0.22f);

    [Range(0f, 1f)]
    public float borderTintStrength = 0.18f;

    [Header("Zone Contrast")]
    [Range(0f, 1f)]
    public float arenaTintStrength = 0.72f;

    [Range(0f, 1f)]
    public float resourceTintStrength = 0.82f;

    [Range(0f, 1f)]
    public float transitionBlendStrength = 0.58f;

    [Header("Height Tinting")]
    [Range(0f, 1f)]
    public float heightDarkening = 0.12f;
}
