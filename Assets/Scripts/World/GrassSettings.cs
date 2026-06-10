using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    [Header("Generation")]
    public bool enabled = true;

    [Min(1)]
    public int spacing = 2;

    [Range(0f, 1f)]
    public float arenaDensity = 0.2f;
    
    [Range(0f, 1f)]
    public float borderDensity = 0.22f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.55f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.92f;

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
    public Color dryGrass = new Color(0.84f, 0.77f, 0.30f);
    public Color oliveGrass = new Color(0.38f, 0.57f, 0.20f);
    public Color greenGrass = new Color(0.24f, 0.68f, 0.22f);

    [Header("Placement")]
    public float yOffset = 0.03f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 28f;
}
