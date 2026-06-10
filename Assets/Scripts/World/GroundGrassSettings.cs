using UnityEngine;

[System.Serializable]
public class GroundGrassSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 1;

    [Min(1)]
    public int minBladesPerTuft = 4;

    [Min(1)]
    public int maxBladesPerTuft = 7;

    [Header("Zone Density")]
    [Range(0f, 1f)]
    public float arenaDensity = 0.18f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.72f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.9f;

    [Range(0f, 1f)]
    public float borderDensity = 0.98f;

    [Header("Coverage Noise")]
    public float largePatchScale = 90f;
    public float mediumPatchScale = 28f;
    public float smallPatchScale = 12f;

    [Range(0f, 1f)]
    public float patchContrast = 0.28f;

    [Range(0f, 1f)]
    public float barePatchChance = 0.06f;

    [Header("Tuft Shape")]
    public float minBladeHeight = 0.09f;
    public float maxBladeHeight = 0.22f;

    public float minBladeWidth = 0.05f;
    public float maxBladeWidth = 0.11f;

    public float minTuftRadius = 0.05f;
    public float maxTuftRadius = 0.15f;

    [Range(0f, 1f)]
    public float pointJitter = 0.18f;

    [Range(0f, 1f)]
    public float heightVariationStrength = 0.35f;

    [Header("Placement")]
    public float yOffset = 0.028f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 32f;

    [Header("Colors")]
    public Color dryGrass = new Color(0.76f, 0.66f, 0.24f);
    public Color warmGrass = new Color(0.68f, 0.63f, 0.25f);
    public Color oliveGrass = new Color(0.33f, 0.52f, 0.18f);
    public Color lushGrass = new Color(0.34f, 0.66f, 0.20f);

    public float colorNoiseScale = 34f;

    [Range(0f, 1f)]
    public float randomColorVariation = 0.16f;

    [Range(0f, 1f)]
    public float baseDarkening = 0.18f;

    [Range(0f, 1f)]
    public float tipLightening = 0.10f;

    [Header("Material Interaction")]
    public float windStrength = 0.06f;
    public float windSpeed = 1.4f;
    public float windScale = 0.45f;

    public float playerPushRadius = 1.5f;
    public float playerPushStrength = 0.26f;
    public float playerFlattenStrength = 0.05f;
}
