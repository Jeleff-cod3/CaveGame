using UnityEngine;
using UnityEngine.UI;

public class MammothHealthBarUI : MonoBehaviour
{
    [SerializeField] private EnemyHealth target;
    [SerializeField] private string preferredTargetName = "Mammoth";
    [SerializeField] private float bottomOffset = 24f;
    [SerializeField] private float barHeight = 30f;
    [SerializeField] private float horizontalPaddingPercent = 0.08f;
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.82f);
    [SerializeField] private Color fillColor = new Color(0.82f, 0.08f, 0.05f, 0.95f);

    private RectTransform root;
    private Image fillImage;
    private EnemyHealth boundTarget;

    private void Awake()
    {
        BuildBar();
        BindTarget(target != null ? target : FindTarget());
    }

    private void OnEnable()
    {
        if (boundTarget != null)
        {
            boundTarget.HealthChanged += HandleHealthChanged;
            SetVisible(true);
            UpdateBar(boundTarget.CurrentHealth, boundTarget.MaxHealth);
        }
    }

    private void OnDisable()
    {
        if (boundTarget != null)
        {
            boundTarget.HealthChanged -= HandleHealthChanged;
        }
    }

    private void Update()
    {
        if (boundTarget == null)
        {
            BindTarget(FindTarget());
        }
    }

    private void BindTarget(EnemyHealth newTarget)
    {
        if (boundTarget == newTarget)
        {
            return;
        }

        if (boundTarget != null)
        {
            boundTarget.HealthChanged -= HandleHealthChanged;
        }

        boundTarget = newTarget;

        if (boundTarget == null)
        {
            SetVisible(false);
            return;
        }

        boundTarget.HealthChanged += HandleHealthChanged;
        SetVisible(true);
        UpdateBar(boundTarget.CurrentHealth, boundTarget.MaxHealth);
    }

    private EnemyHealth FindTarget()
    {
        EnemyHealth[] candidates = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (EnemyHealth candidate in candidates)
        {
            string candidateName = candidate.gameObject.name;
            if (candidateName.IndexOf(preferredTargetName, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidateName.IndexOf("Mamoth", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate;
            }
        }

        return candidates.Length > 0 ? candidates[0] : null;
    }

    private void HandleHealthChanged(int current, int max)
    {
        UpdateBar(current, max);
    }

    private void BuildBar()
    {
        if (root != null)
        {
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform;

        GameObject rootObject = new GameObject("Mammoth HP Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rootObject.transform.SetParent(parent, false);
        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(horizontalPaddingPercent, 0f);
        root.anchorMax = new Vector2(1f - horizontalPaddingPercent, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, bottomOffset);
        root.sizeDelta = new Vector2(0f, barHeight);

        Image background = rootObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(root, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        fillImage = fillObject.GetComponent<Image>();
        fillImage.color = fillColor;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.raycastTarget = false;
    }

    private void UpdateBar(int current, int max)
    {
        if (fillImage == null)
        {
            return;
        }

        float percent = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
        fillImage.fillAmount = percent;
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }
}
