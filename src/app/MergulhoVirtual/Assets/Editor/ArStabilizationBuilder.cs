using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the beach AR drift-mitigation stack into the active scene:
///
///   ARStabilization (scene root)
///     ├ StillnessDetector        — IMU "is the phone physically still?"
///     ├ SpuriousMotionGate       — cancels wave-induced phantom translation
///     ├ GnssProvider             — raw Android GNSS (falls back to Input.location)
///     ├ GpsArKalmanFusion        — GPS↔AR Kalman drift estimate + heading calib
///     └ ArStabilizationController— glue, JSON tuning persistence
///
///   ScreenUI/MainScreen
///     └ ArTuning                 — "AJUSTE AR" pill + hidden tuning panel
///        (ArTuningPanel spawns its slider rows at runtime — the builder only
///         creates the shell: button, card, telemetry text, scroll view)
///
/// Idempotent: prompts to replace both pieces if they already exist. All
/// references are wired via SerializedObject — no Inspector dragging needed.
/// </summary>
public static class ArStabilizationBuilder
{
    // Palette — matches RegisterScreenBuilder.
    static readonly Color PanelBg       = new Color(0.055f, 0.102f, 0.169f, 0.97f); // near-opaque navy
    static readonly Color CardSurfaceAlt= new Color(0.137f, 0.204f, 0.298f, 1f);
    static readonly Color TextPrimary   = Color.white;
    static readonly Color TextSecondary = new Color(0.722f, 0.773f, 0.851f, 1f);
    static readonly Color Accent        = new Color(0.517f, 0.420f, 1.0f, 1f);

    const float BottomNavClearance = 240f;

    [MenuItem("Tools/Mergulho Virtual/Setup AR Stabilization", priority = 120)]
    public static void Build()
    {
        // ------------------------------------------------ locate scene anchors
        var xrOriginGo = FindSceneRoot("XR Origin");
        var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("AR Stabilization",
                "Could not find an 'XR Origin' root GameObject with an XROrigin component. Open MainScene first.", "OK");
            return;
        }
        var arCamera = xrOrigin.Camera != null
            ? xrOrigin.Camera.transform
            : xrOriginGo.transform.Find("Camera Offset/Main Camera");
        if (arCamera == null)
        {
            EditorUtility.DisplayDialog("AR Stabilization",
                "Could not find the AR camera under XR Origin/Camera Offset/Main Camera.", "OK");
            return;
        }

        var screenUI = GameObject.Find("ScreenUI");
        var mainScreen = screenUI != null ? screenUI.transform.Find("MainScreen") : null;
        if (mainScreen == null)
        {
            EditorUtility.DisplayDialog("AR Stabilization",
                "Could not find ScreenUI/MainScreen in the active scene.", "OK");
            return;
        }

        // ------------------------------------------------ replace previous run
        var existing = FindSceneRoot("ARStabilization");
        var existingUI = mainScreen.Find("ArTuning");
        if (existing != null || existingUI != null)
        {
            if (!EditorUtility.DisplayDialog("AR Stabilization",
                "ARStabilization already exists in the scene. Replace it (and its tuning UI)?",
                "Replace", "Cancel")) return;
            if (existing != null) Object.DestroyImmediate(existing);
            if (existingUI != null) Object.DestroyImmediate(existingUI.gameObject);
        }

        // ------------------------------------------------ runtime components
        var root = new GameObject("ARStabilization");
        var stillness = root.AddComponent<StillnessDetector>();
        var gate = root.AddComponent<SpuriousMotionGate>();
        var gnss = root.AddComponent<GnssProvider>();
        var fusion = root.AddComponent<GpsArKalmanFusion>();
        var controller = root.AddComponent<ArStabilizationController>();

        Wire(gate, "stillness", stillness);
        Wire(gate, "xrOrigin", xrOrigin);
        Wire(gate, "arCamera", arCamera);

        Wire(fusion, "arCamera", arCamera);
        Wire(fusion, "xrOrigin", xrOrigin);
        Wire(fusion, "gnss", gnss);

        Wire(controller, "stillness", stillness);
        Wire(controller, "gate", gate);
        Wire(controller, "gnss", gnss);
        Wire(controller, "fusion", fusion);

        // ------------------------------------------------ tuning UI shell
        Material roundedMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/UI/RoundedRectCard.mat");

        var uiRoot = NewUI("ArTuning", mainScreen);
        StretchFull(uiRoot);
        var panelComp = uiRoot.AddComponent<ArTuningPanel>();

        // --- HUD pill button (right edge, out of the way of header/footer) ---
        var btnGo = NewUI("ToggleButton", uiRoot.transform);
        var btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(1, 0.72f);
        btnRT.anchorMax = new Vector2(1, 0.72f);
        btnRT.pivot = new Vector2(1, 0.5f);
        btnRT.anchoredPosition = new Vector2(-16, 0);
        btnRT.sizeDelta = new Vector2(136, 44);
        var btnBg = btnGo.AddComponent<Image>();
        btnBg.color = new Color(CardSurfaceAlt.r, CardSurfaceAlt.g, CardSurfaceAlt.b, 0.85f);
        if (roundedMat != null) btnBg.material = roundedMat;
        btnGo.AddComponent<RectMask2D>();
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnBg;
        var btnLabel = NewText("Label", btnGo.transform, "AJUSTE AR", 14, FontStyles.Bold, TextSecondary);
        StretchFull(btnLabel.gameObject);
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.characterSpacing = 4f;

        // --- panel card (hidden by default) ---
        var panel = NewUI("Panel", uiRoot.transform);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0, 0);
        panelRT.anchorMax = new Vector2(1, 1);
        panelRT.offsetMin = new Vector2(56, BottomNavClearance + 40);
        panelRT.offsetMax = new Vector2(-56, -110);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panel.AddComponent<RectMask2D>();
        var panelBg = panel.AddComponent<Image>();
        panelBg.color = PanelBg;
        if (roundedMat != null) panelBg.material = roundedMat;
        panelBg.raycastTarget = true; // block taps into the AR view behind

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 14, 14);
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var title = NewText("Title", panel.transform, "Estabilização AR — ajuste em campo",
            19, FontStyles.Bold, TextPrimary);
        AddLayoutElement(title.gameObject, preferredHeight: 26);

        var telemetry = NewText("TelemetryText", panel.transform, "…", 14, FontStyles.Normal, TextSecondary);
        telemetry.textWrappingMode = TextWrappingModes.Normal;
        telemetry.richText = true;
        AddLayoutElement(telemetry.gameObject, preferredHeight: 170);

        // --- scroll view for the runtime-generated rows ---
        var scrollGo = NewUI("Scroll View", panel.transform);
        AddLayoutElement(scrollGo, preferredHeight: -1, flexibleHeight: 1);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 30f;

        var viewport = NewUI("Viewport", scrollGo.transform);
        StretchFull(viewport);
        viewport.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();

        var content = NewUI("Content", viewport.transform);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;
        var cvlg = content.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing = 10f;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        scrollRect.content = contentRT;

        panel.SetActive(false); // opened via the pill

        // ------------------------------------------------ wire panel + button
        Wire(panelComp, "controller", controller);
        Wire(panelComp, "panelRoot", panel);
        Wire(panelComp, "telemetryText", telemetry);
        Wire(panelComp, "rowsParent", contentRT);
        UnityEventTools.AddPersistentListener(btn.onClick, panelComp.Toggle);
        EditorUtility.SetDirty(btn);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[ArStabilizationBuilder] ARStabilization + tuning UI created. Save the scene (Ctrl+S). " +
                  "Tuning persists at <persistentDataPath>/ar_stabilization_tuning.json on device.");
    }

    // ---------------------------------------------------------------- helpers

    static void Wire(Component target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[ArStabilizationBuilder] {target.GetType().Name} has no '{field}' field — recompile and re-run.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Root-object scan that also finds INACTIVE roots (GameObject.Find can't).</summary>
    static GameObject FindSceneRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 100);
        return go;
    }

    static void StretchFull(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string content, float size,
                                   FontStyles style, Color color)
    {
        var go = NewUI(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.raycastTarget = false;
        return t;
    }

    static void AddLayoutElement(GameObject go, float preferredHeight = -1, float flexibleHeight = -1)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
        if (flexibleHeight >= 0) le.flexibleHeight = flexibleHeight;
    }
}
