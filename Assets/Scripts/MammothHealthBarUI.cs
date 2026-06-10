using UnityEngine;
using UnityEngine.UI;

public class MammothHealthBarUI : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField] private EnemyHealth target;
    [SerializeField] private string preferredTargetName = "Mammoth";

    [Header("Layout")]
    [SerializeField] private float bottomOffset = 24f;
    [SerializeField] private float barHeight = 30f;
    [SerializeField] private float horizontalPaddingPercent = 0.08f;
    [SerializeField] private float minSidePaddingPixels = 40f;
    [SerializeField] private float innerPadding = 3f;
    [SerializeField] private bool useDedicatedOverlayCanvas = true;

    [Header("Resolution")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] [Range(0f, 1f)] private float widthHeightMatch = 0.5f;

    [Header("Animation")]
    [SerializeField] private float healthSlideSpeed = 0.9f;
    [SerializeField] private float damageHoldDuration = 0.18f;
    [SerializeField] private float damageBurnSpeed = 0.55f;
    [SerializeField] private float damageFadeSpeed = 2.4f;
    [SerializeField] private float bloodTrailPulseSpeed = 8f;
    [SerializeField] private float bloodTrailMaxWidth = 22f;
    [SerializeField] private float bleedParticlesPerFullBar = 28f;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.82f);
    [SerializeField] private Color fillColor = new Color(0.66f, 0.03f, 0.02f, 0.98f);
    [SerializeField] private Color damageColor = new Color(0.94f, 0.24f, 0.2f, 0.95f);
    [SerializeField] private Color trailColor = new Color(0.98f, 0.22f, 0.18f, 0.92f);
    [SerializeField] private Color trailGlowColor = new Color(1f, 0.56f, 0.45f, 0.75f);

    [Header("Effects")]
    [SerializeField] private ParticleSystem bleedParticles;

    private RectTransform root;
    private RectTransform contentRoot;
    private RectTransform fillRect;
    private RectTransform damageRect;
    private Image fillImage;
    private Image damageImage;
    private RectTransform trailRoot;
    private Image trailImage;
    private Image trailGlowImage;

    private EnemyHealth boundTarget;
    private bool isSubscribed;
    private int lastKnownCurrentHealth = -1;
    private int lastKnownMaxHealth = -1;
    private float targetPercent = 1f;
    private float displayedPercent = 1f;
    private float damageGhostPercent = 1f;
    private float damageHoldTimer;
    private float damageAlpha;
    private float bleedEmissionAccumulator;
    private Canvas uiCanvas;
    private static Canvas sharedOverlayCanvas;

    private void Awake()
    {
        BuildBar();
    }

    private void OnEnable()
    {
        BindTarget(target != null ? target : FindTarget());
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (boundTarget == null)
        {
            BindTarget(target != null ? target : FindTarget());
            AnimateBar();
            return;
        }

        if (lastKnownCurrentHealth != boundTarget.CurrentHealth || lastKnownMaxHealth != boundTarget.MaxHealth)
        {
            ApplyHealthSnapshot(boundTarget.CurrentHealth, boundTarget.MaxHealth);
        }

        AnimateBar();
    }

    private void BindTarget(EnemyHealth newTarget)
    {
        if (boundTarget == newTarget)
        {
            if (boundTarget == null)
            {
                SetVisible(false);
            }

            return;
        }

        Unsubscribe();
        boundTarget = newTarget;

        if (boundTarget == null)
        {
            SetVisible(false);
            return;
        }

        boundTarget.HealthChanged += HandleHealthChanged;
        isSubscribed = true;
        SetVisible(true);
        ApplyHealthSnapshot(boundTarget.CurrentHealth, boundTarget.MaxHealth, true);
    }

    private void Unsubscribe()
    {
        if (boundTarget != null && isSubscribed)
        {
            boundTarget.HealthChanged -= HandleHealthChanged;
        }

        isSubscribed = false;
        boundTarget = null;
    }

    private EnemyHealth FindTarget()
    {
        EnemyHealth[] candidates = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude);

        foreach (EnemyHealth candidate in candidates)
        {
            string candidateName = candidate.gameObject.name;
            if (candidateName.IndexOf(preferredTargetName, System.StringComparison.OrdinalIgnoreCase) >= 0
                || candidateName.IndexOf("Mamoth", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate;
            }
        }

        return candidates.Length > 0 ? candidates[0] : null;
    }

    private void HandleHealthChanged(int current, int max)
    {
        ApplyHealthSnapshot(current, max);
    }

    private void ApplyHealthSnapshot(int current, int max, bool snapImmediately = false)
    {
        float nextPercent = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (snapImmediately || lastKnownCurrentHealth < 0 || nextPercent > targetPercent)
        {
            targetPercent = nextPercent;
            displayedPercent = nextPercent;
            damageGhostPercent = nextPercent;
            damageHoldTimer = 0f;
            damageAlpha = 0f;
            lastKnownCurrentHealth = current;
            lastKnownMaxHealth = max;
            RefreshVisuals();
            return;
        }

        if (nextPercent < targetPercent)
        {
            damageGhostPercent = Mathf.Max(damageGhostPercent, displayedPercent, targetPercent);
            damageHoldTimer = damageHoldDuration;
            damageAlpha = 1f;
        }

        targetPercent = nextPercent;
        lastKnownCurrentHealth = current;
        lastKnownMaxHealth = max;
        RefreshVisuals();
    }

    private void AnimateBar()
    {
        bool changed = false;
        float previousDisplayedPercent = displayedPercent;

        if (!Mathf.Approximately(displayedPercent, targetPercent))
        {
            displayedPercent = Mathf.MoveTowards(displayedPercent, targetPercent, healthSlideSpeed * Time.unscaledDeltaTime);
            changed = true;
        }

        if (damageHoldTimer > 0f)
        {
            damageHoldTimer = Mathf.Max(0f, damageHoldTimer - Time.unscaledDeltaTime);
            damageAlpha = 1f;
            changed = true;
        }
        else if (damageGhostPercent > displayedPercent)
        {
            damageGhostPercent = Mathf.MoveTowards(damageGhostPercent, displayedPercent, damageBurnSpeed * Time.unscaledDeltaTime);
            damageAlpha = Mathf.MoveTowards(damageAlpha, 0f, damageFadeSpeed * Time.unscaledDeltaTime);
            changed = true;
        }
        else if (!Mathf.Approximately(damageGhostPercent, displayedPercent))
        {
            damageGhostPercent = displayedPercent;
            damageAlpha = 0f;
            changed = true;
        }
        else if (damageAlpha > 0f)
        {
            damageAlpha = 0f;
            changed = true;
        }

        float shrinkDelta = previousDisplayedPercent - displayedPercent;
        if (shrinkDelta > 0f)
        {
            EmitBleed(shrinkDelta);
        }

        if (changed)
        {
            RefreshVisuals();
        }
    }

    private void BuildBar()
    {
        if (root != null)
        {
            return;
        }

        uiCanvas = ResolveCanvas();
        Transform parent = uiCanvas != null ? uiCanvas.transform : transform;

        GameObject rootObject = new GameObject("Mammoth HP Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rootObject.transform.SetParent(parent, false);
        root = rootObject.GetComponent<RectTransform>();
        float percentPadding = Mathf.Clamp(horizontalPaddingPercent, 0f, 0.45f);
        root.anchorMin = new Vector2(percentPadding, 0f);
        root.anchorMax = new Vector2(1f - percentPadding, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, bottomOffset);
        root.sizeDelta = new Vector2(-(2f * Mathf.Max(0f, minSidePaddingPixels)), barHeight);
        rootObject.transform.SetAsLastSibling();

        Image background = rootObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(root, false);
        contentRoot = contentObject.GetComponent<RectTransform>();
        contentRoot.anchorMin = Vector2.zero;
        contentRoot.anchorMax = Vector2.one;
        contentRoot.offsetMin = new Vector2(innerPadding, innerPadding);
        contentRoot.offsetMax = new Vector2(-innerPadding, -innerPadding);

        damageImage = CreateFillImage("Damage Ghost", contentRoot, damageColor);
        fillImage = CreateFillImage("Main Fill", contentRoot, fillColor);
        damageRect = damageImage.rectTransform;
        fillRect = fillImage.rectTransform;

        GameObject trailObject = new GameObject("Blood Trail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trailObject.transform.SetParent(contentRoot, false);
        trailRoot = trailObject.GetComponent<RectTransform>();
        trailRoot.anchorMin = new Vector2(0f, 0.5f);
        trailRoot.anchorMax = new Vector2(0f, 0.5f);
        trailRoot.pivot = new Vector2(0.5f, 0.5f);
        trailRoot.sizeDelta = new Vector2(10f, 0f);

        trailImage = trailObject.GetComponent<Image>();
        trailImage.color = trailColor;
        trailImage.raycastTarget = false;

        GameObject glowObject = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        glowObject.transform.SetParent(trailRoot, false);
        RectTransform glowRect = glowObject.GetComponent<RectTransform>();
        glowRect.anchorMin = Vector2.zero;
        glowRect.anchorMax = Vector2.one;
        glowRect.offsetMin = new Vector2(-8f, -2f);
        glowRect.offsetMax = new Vector2(8f, 2f);

        trailGlowImage = glowObject.GetComponent<Image>();
        trailGlowImage.color = trailGlowColor;
        trailGlowImage.raycastTarget = false;

        RefreshVisuals();
    }

    private Canvas ResolveCanvas()
    {
        if (!useDedicatedOverlayCanvas)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                return parentCanvas;
            }
        }

        if (sharedOverlayCanvas != null)
        {
            return sharedOverlayCanvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
        {
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                sharedOverlayCanvas = canvas;
                EnsureCanvasScaler(sharedOverlayCanvas);
                return sharedOverlayCanvas;
            }
        }

        GameObject canvasObject = new GameObject("Mammoth HP Overlay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas createdCanvas = canvasObject.GetComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        createdCanvas.overrideSorting = true;
        createdCanvas.sortingOrder = 500;
        EnsureCanvasScaler(createdCanvas);
        DontDestroyOnLoad(canvasObject);
        sharedOverlayCanvas = createdCanvas;
        return sharedOverlayCanvas;
    }

    private void EnsureCanvasScaler(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = widthHeightMatch;
    }

    private static Image CreateFillImage(string name, Transform parent, Color color)
    {
        GameObject fillObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(parent, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image image = fillObject.GetComponent<Image>();
        image.color = color;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private void RefreshVisuals()
    {
        if (fillImage == null || damageImage == null)
        {
            return;
        }

        float clampedDisplayed = Mathf.Clamp01(displayedPercent);
        float clampedGhost = Mathf.Clamp01(Mathf.Max(damageGhostPercent, clampedDisplayed));

        if (fillRect != null)
        {
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(clampedDisplayed, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        if (damageRect != null)
        {
            damageRect.anchorMin = new Vector2(clampedDisplayed, 0f);
            damageRect.anchorMax = new Vector2(clampedGhost, 1f);
            damageRect.offsetMin = Vector2.zero;
            damageRect.offsetMax = Vector2.zero;
        }

        fillImage.color = fillColor;
        damageImage.color = new Color(damageColor.r, damageColor.g, damageColor.b, Mathf.Clamp01(damageColor.a * damageAlpha));

        UpdateTrailVisual();
    }

    private void UpdateTrailVisual()
    {
        if (trailRoot == null || contentRoot == null || trailImage == null || trailGlowImage == null)
        {
            return;
        }

        float lostPercent = Mathf.Max(0f, damageGhostPercent - displayedPercent);
        bool showTrail = lostPercent > 0.001f;
        trailRoot.gameObject.SetActive(showTrail);

        if (!showTrail)
        {
            return;
        }

        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * bloodTrailPulseSpeed);
        float contentWidth = Mathf.Max(1f, contentRoot.rect.width);
        float xPosition = contentWidth * damageGhostPercent;
        float width = Mathf.Lerp(8f, bloodTrailMaxWidth, Mathf.Clamp01(lostPercent * 3.5f)) * pulse;
        float trailAlpha = Mathf.Lerp(0.35f, 0.95f, Mathf.Clamp01(lostPercent * 5f));

        trailRoot.anchoredPosition = new Vector2(xPosition, 0f);
        trailRoot.sizeDelta = new Vector2(width, contentRoot.rect.height);
        trailImage.color = new Color(trailColor.r, trailColor.g, trailColor.b, trailAlpha);
        trailGlowImage.color = new Color(trailGlowColor.r, trailGlowColor.g, trailGlowColor.b, trailAlpha * 0.9f);
    }

    private void EmitBleed(float shrinkDelta)
    {
        if (bleedParticles == null || !bleedParticles.gameObject.activeInHierarchy)
        {
            return;
        }

        bleedEmissionAccumulator += shrinkDelta * Mathf.Max(0f, bleedParticlesPerFullBar);
        int emitCount = Mathf.FloorToInt(bleedEmissionAccumulator);
        if (emitCount <= 0)
        {
            return;
        }

        bleedEmissionAccumulator -= emitCount;
        bleedParticles.Emit(emitCount);
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }
}
