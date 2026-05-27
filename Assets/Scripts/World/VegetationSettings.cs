using UnityEngine;

[System.Serializable]
public class VegetationSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 4;

    [Range(0f, 1f)]
    public float arenaDensity = 0.08f;

    [Range(0f, 1f)]
    public float borderDensity = 0.18f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.24f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.42f;

    [Header("Patch Distribution")]
    public float largePatchScale = 95f;
    public float smallPatchScale = 26f;

    [Range(0f, 2f)]
    public float patchStrength = 1.2f;

    [Header("Type Chances")]
    [Range(0f, 1f)]
    public float deadBushChance = 0.32f;

    [Range(0f, 1f)]
    public float deadTreeChance = 0.08f;

    [Header("Bush Shape")]
    public float minBushRadius = 0.45f;
    public float maxBushRadius = 1.15f;

    public float minBushHeight = 0.35f;
    public float maxBushHeight = 0.95f;

    [Header("Dead Tree Shape")]
    public float minDeadTreeHeight = 1.2f;
    public float maxDeadTreeHeight = 2.6f;

    public float minDeadTreeRadius = 0.06f;
    public float maxDeadTreeRadius = 0.13f;

    [Header("Placement")]
    public float yOffset = 0.04f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;

    [Header("Colors")]
    public Color aliveBushColorA = new Color(0.42f, 0.46f, 0.18f);
    public Color aliveBushColorB = new Color(0.58f, 0.52f, 0.22f);

    public Color deadBushColor = new Color(0.42f, 0.28f, 0.13f);
    public Color dryTwigColor = new Color(0.30f, 0.19f, 0.09f);

    [Header("Shadows")]
    public bool generateShadows = true;
    public float shadowYOffset = 0.022f;
    public float shadowRadiusXMultiplier = 0.9f;
    public float shadowRadiusZMultiplier = 0.55f;
    public float shadowOffsetX = 0.12f;
    public float shadowOffsetZ = -0.08f;
}