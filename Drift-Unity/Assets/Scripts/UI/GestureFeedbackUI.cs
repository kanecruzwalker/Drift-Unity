using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GestureFeedbackUI — builds and manages the player-facing feedback Canvas at runtime.
///
/// This is the production feedback layer, distinct from GestureDebugUI (the dev overlay).
/// GestureDebugUI shows raw event data for development verification.
/// GestureFeedbackUI shows polished player-visible feedback during actual gameplay.
///
/// Responsibilities:
///   · Create a screen-space overlay Canvas at Awake if none is assigned
///   · Spawn and configure the UI Image elements UIFeedback.cs needs
///   · Provide a clean interface for UIFeedback to reference elements by property
///   · Manage show/hide state for feedback groups (e.g. hold UI hides when no hold active)
///
/// Relationship to UIFeedback:
///   UIFeedback.cs — drives all animations and responds to events
///   GestureFeedbackUI.cs — owns the Canvas and the Image elements UIFeedback drives
///
///   Workflow: GestureFeedbackUI creates elements → UIFeedback animates them.
///   UIFeedback Inspector slots can be assigned manually OR auto-populated by
///   calling GestureFeedbackUI.AutoPopulateFeedback(UIFeedback target).
///
/// Auto-build mode:
///   If "Auto Build Canvas" is enabled (default), this component creates all
///   required UI elements at runtime using placeholder sprites (solid-color circles
///   and arrows). This means the game runs with visible feedback immediately,
///   with no manual Canvas setup required. Assign real sprites in the Inspector
///   to replace the procedural placeholders.
///
/// Attach to: the same persistent GameObject as InputManager and UIFeedback.
/// </summary>
public class GestureFeedbackUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — Canvas
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Canvas")]
    [Tooltip("The screen-space overlay Canvas to use. If null, one is created at runtime.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("If true, all UI Image elements are created automatically at Awake. " +
             "Disable if you prefer to build the Canvas hierarchy manually in the Editor.")]
    [SerializeField] private bool autoBuildCanvas = true;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — Sprite Overrides (optional — placeholders used if null)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Sprite Overrides (optional — procedural placeholders used if null)")]
    [Tooltip("Sprite for the hold radial fill ring. Use a ring/circle sprite with transparent center.")]
    [SerializeField] private Sprite holdRingSprite;

    [Tooltip("Sprite for the tap pop. Use a soft glowing circle.")]
    [SerializeField] private Sprite tapPopSprite;

    [Tooltip("Sprite for the dash flare / share pulse ring.")]
    [SerializeField] private Sprite dashFlareSprite;

    [Tooltip("Sprite for the tilt direction arrow.")]
    [SerializeField] private Sprite tiltArrowSprite;

    [Tooltip("Sprite for the tilt aim cone.")]
    [SerializeField] private Sprite tiltConeSprite;

    [Tooltip("Sprite for the zoom indicator background.")]
    [SerializeField] private Sprite zoomIndicatorSprite;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — UIFeedback auto-populate
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Auto-Populate")]
    [Tooltip("If assigned, GestureFeedbackUI will auto-populate UIFeedback's Inspector " +
             "references after building the Canvas elements. " +
             "Drag the UIFeedback component here to skip manual Inspector wiring.")]
    [SerializeField] private UIFeedback feedbackTarget;

    // ─────────────────────────────────────────────────────────────────────────
    // BUILT ELEMENT REFERENCES (accessible externally for manual assignment)
    // ─────────────────────────────────────────────────────────────────────────

    /// The screen-space overlay Canvas (created or assigned).
    public Canvas FeedbackCanvas { get; private set; }

    /// Radial fill Image — driven by UIFeedback for hold charge and domain pulse.
    public Image HoldRadialFill { get; private set; }

    /// Tap pop Image — flashes at touch position on single tap.
    public Image TapPopImage { get; private set; }

    /// Dash flare / pulse ring Image — used for double-tap flare and multi-finger pulses.
    public Image DashFlareImage { get; private set; }

    /// Tilt arrow Image — shows lean direction at player edge.
    public Image TiltArrowImage { get; private set; }

    /// Tilt cone Image — asymmetric aim cone overlay on hold-pulse circle.
    public Image TiltConeImage { get; private set; }

    /// Zoom indicator Image — fades in on pinch, fades out after.
    public Image ZoomIndicatorImage { get; private set; }

    /// Zoom level text label (optional).
    public Text ZoomLevelText { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (autoBuildCanvas)
            BuildCanvas();
        if (feedbackTarget != null)
            AutoPopulateFeedback(feedbackTarget);

        // Hide all feedback elements during MainMenu/Lobby — keep GameObject active
        // so UIFeedback can still parent dynamic elements (collect ring etc) to the canvas.
        HideAllElements();

        _phaseHandler = phase =>
        {
            bool playing = phase == GameManager.GamePhase.Playing;
            // Show/hide elements but never deactivate the GameObject itself.
            if (!playing) HideAllElements();
        };

        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged += _phaseHandler;
        else
            StartCoroutine(LateSubscribeToPhaseChanges());
    }

    private System.Action<GameManager.GamePhase> _phaseHandler;

    private System.Collections.IEnumerator LateSubscribeToPhaseChanges()
    {
        yield return null;
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged += _phaseHandler;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged -= _phaseHandler;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CANVAS BUILD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the Canvas and all required Image elements.
    /// Uses procedural Texture2D sprites as placeholders when no sprite is assigned.
    /// All elements start at alpha 0 — UIFeedback reveals them as events fire.
    /// </summary>
    private void BuildCanvas()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        if (targetCanvas != null)
        {
            FeedbackCanvas = targetCanvas;
        }
        else
        {
            GameObject canvasGO = new GameObject("GestureFeedbackCanvas");
            canvasGO.transform.SetParent(transform);

            FeedbackCanvas = canvasGO.AddComponent<Canvas>();
            FeedbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            FeedbackCanvas.sortingOrder = 10; // Above game world, below debug overlay.

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        // ── Hold Radial Fill ──────────────────────────────────────────────────
        HoldRadialFill = CreateImage(
            "HoldRadialFill",
            holdRingSprite ?? CreateCircleSprite(new Color(0.3f, 0.8f, 1f, 0.7f), 128),
            new Vector2(160f, 160f),
            Image.Type.Filled
        );

        if (HoldRadialFill != null)
        {
            HoldRadialFill.fillMethod = Image.FillMethod.Radial360;
            HoldRadialFill.fillAmount = 0f;
        }

        // ── Tap Pop ───────────────────────────────────────────────────────────
        TapPopImage = CreateImage(
            "TapPop",
            tapPopSprite ?? CreateCircleSprite(new Color(1f, 1f, 1f, 0.9f), 64),
            new Vector2(80f, 80f)
        );

        // ── Dash Flare ────────────────────────────────────────────────────────
        DashFlareImage = CreateImage(
            "DashFlare",
            dashFlareSprite ?? CreateRingSprite(new Color(1f, 0.85f, 0.2f, 0.8f), 128, 16),
            new Vector2(120f, 120f)
        );

        // ── Tilt Arrow ────────────────────────────────────────────────────────
        TiltArrowImage = CreateImage(
            "TiltArrow",
            tiltArrowSprite ?? CreateArrowSprite(new Color(0.6f, 1f, 0.6f, 0.9f), 64),
            new Vector2(48f, 48f)
        );

        // ── Tilt Cone ─────────────────────────────────────────────────────────
        TiltConeImage = CreateImage(
            "TiltCone",
            tiltConeSprite ?? CreateConeSprite(new Color(0.3f, 0.8f, 1f, 0.5f), 128),
            new Vector2(140f, 100f)
        );

        // ── Zoom Indicator ────────────────────────────────────────────────────
        ZoomIndicatorImage = CreateImage(
            "ZoomIndicator",
            zoomIndicatorSprite ?? CreateCircleSprite(new Color(0.2f, 0.2f, 0.2f, 0.7f), 64),
            new Vector2(180f, 60f)
        );

        if (ZoomIndicatorImage != null)
        {
            // Position zoom indicator at top-center of screen.
            ZoomIndicatorImage.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            ZoomIndicatorImage.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            ZoomIndicatorImage.rectTransform.anchoredPosition = new Vector2(0f, -80f);

            // Add text child for zoom label.
            ZoomLevelText = CreateTextChild(ZoomIndicatorImage.gameObject, "ZoomLabel");
        }

        // Start all elements hidden — UIFeedback reveals them.
        HideAllElements();

        Debug.Log("[GestureFeedbackUI] Canvas and feedback elements built successfully.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTO-POPULATE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Injects all built Image references into a UIFeedback component's serialized fields
    /// via reflection, bypassing the need for manual Inspector wiring.
    ///
    /// Call this after BuildCanvas() if feedbackTarget is assigned, or call manually
    /// from another script after both components are initialized.
    /// </summary>
    public void AutoPopulateFeedback(UIFeedback target)
    {
        if (target == null)
        {
            Debug.LogWarning("[GestureFeedbackUI] AutoPopulateFeedback called with null target.");
            return;
        }

        // Use SerializedObject reflection via UnityEditor in editor, or direct field
        // injection at runtime via System.Reflection.
        var type = typeof(UIFeedback);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        SetField(type, target, flags, "feedbackCanvas", FeedbackCanvas);
        SetField(type, target, flags, "holdRadialFill", HoldRadialFill);
        SetField(type, target, flags, "tapPopImage", TapPopImage);
        SetField(type, target, flags, "dashFlareImage", DashFlareImage);
        SetField(type, target, flags, "tiltArrowImage", TiltArrowImage);
        SetField(type, target, flags, "tiltConeImage", TiltConeImage);
        SetField(type, target, flags, "zoomIndicatorImage", ZoomIndicatorImage);
        SetField(type, target, flags, "zoomLevelText", ZoomLevelText);

        Debug.Log("[GestureFeedbackUI] UIFeedback references auto-populated.");
    }

    private static void SetField(System.Type type, object target,
        System.Reflection.BindingFlags flags, string fieldName, object value)
    {
        var field = type.GetField(fieldName, flags);
        if (field != null)
            field.SetValue(target, value);
        else
            Debug.LogWarning($"[GestureFeedbackUI] Field '{fieldName}' not found on UIFeedback.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ELEMENT FACTORY HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a UI Image GameObject as a child of the feedback canvas.
    /// Starts hidden (alpha 0). Returns the Image component.
    /// </summary>
    private Image CreateImage(string name, Sprite sprite, Vector2 size,
        Image.Type imageType = Image.Type.Simple)
    {
        if (FeedbackCanvas == null) return null;

        GameObject go = new GameObject(name);
        go.transform.SetParent(FeedbackCanvas.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = imageType;
        img.raycastTarget = false; // Feedback images should never block touch input.

        Color c = img.color;
        c.a = 0f;
        img.color = c;

        return img;
    }

    /// <summary>Creates a Text component as a child of the given parent.</summary>
    private static Text CreateTextChild(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text text = go.AddComponent<Text>();
        text.text = "";
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>Hides all built elements by setting alpha to 0.</summary>
    private void HideAllElements()
    {
        SetAlpha(HoldRadialFill, 0f);
        SetAlpha(TapPopImage, 0f);
        SetAlpha(DashFlareImage, 0f);
        SetAlpha(TiltArrowImage, 0f);
        SetAlpha(TiltConeImage, 0f);
        SetAlpha(ZoomIndicatorImage, 0f);
    }

    private static void SetAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PROCEDURAL SPRITE GENERATORS
    // ─────────────────────────────────────────────────────────────────────────
    // These produce minimal placeholder sprites so the game has visible feedback
    // immediately, with no sprite assets required. Replace with real art in the
    // Inspector at any time — no code changes needed.

    /// <summary>Generates a filled circle sprite of the given color and resolution.</summary>
    private static Sprite CreateCircleSprite(Color color, int resolution)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float center = resolution * 0.5f;
        float radius = center - 1f;

        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float edge = Mathf.Clamp01((radius - dist) / 2f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * edge));
            }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, resolution, resolution),
            Vector2.one * 0.5f, resolution);
    }

    /// <summary>Generates a ring (hollow circle) sprite.</summary>
    private static Sprite CreateRingSprite(Color color, int resolution, int ringWidth)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float center = resolution * 0.5f;
        float outerR = center - 1f;
        float innerR = outerR - ringWidth;

        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float outer = Mathf.Clamp01((outerR - dist) / 2f);
                float inner = Mathf.Clamp01((dist - innerR) / 2f);
                float alpha = Mathf.Min(outer, inner);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
            }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, resolution, resolution),
            Vector2.one * 0.5f, resolution);
    }

    /// <summary>
    /// Generates a simple arrow sprite pointing right (+X).
    /// UIFeedback rotates it to face the tilt direction.
    /// </summary>
    private static Sprite CreateArrowSprite(Color color, int resolution)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float half = resolution * 0.5f;

        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                // Arrow shape: triangle pointing right.
                float nx = (x / (float)resolution) * 2f - 1f; // -1 to 1
                float ny = (y / (float)resolution) * 2f - 1f;
                float dist = Mathf.Abs(ny) - (nx + 1f) * 0.5f;
                bool inside = nx > -0.8f && dist < 0f;
                tex.SetPixel(x, y, inside
                    ? new Color(color.r, color.g, color.b, color.a)
                    : Color.clear);
            }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, resolution, resolution),
            Vector2.one * 0.5f, resolution);
    }

    /// <summary>
    /// Generates a half-cone (pie slice) sprite pointing right (+X).
    /// UIFeedback rotates it to face the aim direction.
    /// </summary>
    private static Sprite CreateConeSprite(Color color, int resolution)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float center = resolution * 0.5f;
        float halfAngle = 35f * Mathf.Deg2Rad; // 70° cone total width.

        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(Mathf.Abs(dy), dx);
                bool inside = dx > 0 && angle < halfAngle && dist < center;

                float edgeFade = inside ? Mathf.Clamp01(1f - dist / center) : 0f;
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * edgeFade));
            }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, resolution, resolution),
            Vector2.one * 0.5f, resolution);
    }
}