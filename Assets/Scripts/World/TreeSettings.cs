using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(2)]
    public int spacing = 5;

    [Range(0f, 1f)]
    public float arenaDensity = 0.04f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.18f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.32f;

    [Header("Patch Distribution")]
    public float largePatchScale = 110f;
    public float smallPatchScale = 32f;

    [Tooltip("How strongly the noise patches affect tree density.")]
    [Range(0f, 2f)]
    public float patchStrength = 1.15f;

    [Header("Placement")]
    public float yOffset = 0.03f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 26f;

    [Header("Acacia Shape")]
    [Range(0f, 0.5f)]
    public float trunkLeanStrength = 0.18f;

    [Min(1)]
    public int minBranchCount = 2;

    [Min(1)]
    public int maxBranchCount = 4;

    public float minBranchLength = 1.2f;
    public float maxBranchLength = 2.8f;

    [Range(0f, 1f)]
    public float branchUpwardBias = 0.35f;

    [Header("Leaf Clumps")]
    [Min(1)]
    public int minLeafClumps = 4;

    [Min(1)]
    public int maxLeafClumps = 8;

    public float minLeafClumpRadius = 0.7f;
    public float maxLeafClumpRadius = 1.6f;

    public float minLeafClumpHeight = 0.28f;
    public float maxLeafClumpHeight = 0.65f;

    [Range(0.1f, 1f)]
    public float crownFlatness = 0.35f;

    [Header("Trunk")]
    public float minTrunkHeight = 1.3f;
    public float maxTrunkHeight = 2.3f;

    public float minTrunkRadius = 0.10f;
    public float maxTrunkRadius = 0.18f;

    [Header("Canopy")]
    public float minCanopyRadius = 1.7f;
    public float maxCanopyRadius = 3.1f;

    public float minCanopyHeight = 0.40f;
    public float maxCanopyHeight = 0.80f;

    [Header("Colors")]
    public Color trunkColor = new Color(0.29f, 0.17f, 0.09f);
    public Color dryLeafColor = new Color(0.58f, 0.57f, 0.23f);
    public Color oliveLeafColor = new Color(0.39f, 0.49f, 0.20f);
    public Color greenLeafColor = new Color(0.27f, 0.42f, 0.18f);

    [Header("Shadows")]
    public bool generateShadows = true;
    public float shadowYOffset = 0.025f;
    public float shadowOffsetX = 0.18f;
    public float shadowOffsetZ = -0.12f;
    public float shadowRadiusXMultiplier = 0.95f;
    public float shadowRadiusZMultiplier = 0.58f;
}
