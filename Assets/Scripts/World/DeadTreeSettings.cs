using UnityEngine;

[System.Serializable]
public class DeadTreeSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 8;

    [Range(0f, 1f)]
    public float arenaDensity = 0.01f;

    [Range(0f, 1f)]
    public float borderDensity = 0.08f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.05f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.12f;

    [Header("Patch Distribution")]
    public float largePatchScale = 115f;
    public float smallPatchScale = 30f;

    [Range(0f, 2f)]
    public float patchStrength = 1.25f;

    [Header("Dead Tree Shape")]
    public float minHeight = 1.2f;
    public float maxHeight = 2.6f;

    public float minRadius = 0.06f;
    public float maxRadius = 0.13f;

    [Header("Placement")]
    public float yOffset = 0.04f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;

    [Header("Colors")]
    public Color dryTwigColor = new Color(0.30f, 0.19f, 0.09f);

    [Header("Shadows")]
    public bool generateShadows = true;
    public float shadowYOffset = 0.022f;
    public float shadowRadiusXMultiplier = 0.9f;
    public float shadowRadiusZMultiplier = 0.55f;
    public float shadowOffsetX = 0.12f;
    public float shadowOffsetZ = -0.08f;
}
