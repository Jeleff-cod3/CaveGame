using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 2;

    [Range(0f, 1f)]
    public float arenaDensity = 0.35f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.55f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.85f;

    [Header("Shape")]
    public float minHeight = 0.45f;
    public float maxHeight = 0.95f;

    public float minWidth = 0.12f;
    public float maxWidth = 0.28f;

    [Header("Noise Patches")]
    public float largePatchScale = 90f;
    public float smallPatchScale = 24f;

    [Range(0f, 1f)]
    public float patchContrast = 0.65f;

    [Header("Colors")]
    public Color dryGrass = new Color(0.72f, 0.67f, 0.28f);
    public Color oliveGrass = new Color(0.39f, 0.55f, 0.22f);
    public Color greenGrass = new Color(0.28f, 0.62f, 0.24f);

    [Header("Placement")]
    public float yOffset = 0.03f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;
}