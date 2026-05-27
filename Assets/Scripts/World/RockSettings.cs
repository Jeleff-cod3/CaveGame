using UnityEngine;

[System.Serializable]
public class RockSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 7;

    [Range(0f, 1f)]
    public float arenaDensity = 0.03f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.08f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.14f;

    [Range(0f, 1f)]
    public float borderDensity = 0.18f;

    [Header("Patch Distribution")]
    public float largePatchScale = 120f;
    public float smallPatchScale = 28f;

    [Range(0f, 2f)]
    public float patchStrength = 1.1f;

    [Header("Shape")]
    public float minRadius = 0.35f;
    public float maxRadius = 1.35f;

    public float minHeight = 0.18f;
    public float maxHeight = 0.75f;

    [Header("Placement")]
    public float yOffset = 0.035f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 32f;

    [Header("Colors")]
    public Color lightRock = new Color(0.55f, 0.49f, 0.39f);
    public Color warmRock = new Color(0.45f, 0.36f, 0.25f);
    public Color darkRock = new Color(0.27f, 0.24f, 0.20f);

    [Header("Shadows")]
    public bool generateShadows = true;
    public float shadowYOffset = 0.02f;
    public float shadowRadiusXMultiplier = 0.95f;
    public float shadowRadiusZMultiplier = 0.55f;
    public float shadowOffsetX = 0.08f;
    public float shadowOffsetZ = -0.05f;
}