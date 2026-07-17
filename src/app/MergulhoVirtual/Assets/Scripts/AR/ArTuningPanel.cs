using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-device tuning panel for the AR stabilization stack. Lives on the AR HUD
/// (MainScreen) behind a small "AR ⚙" pill; built by
/// Tools > Mergulho Virtual > Setup AR Stabilization.
///
/// The builder only creates the SHELL (toggle button, panel card, telemetry
/// text, scroll view). All interactive rows — sliders for every Kalman /
/// gate / stillness parameter, on/off toggles, save/reset buttons — are
/// generated at runtime from the parameter table in BuildRows(), so adding a
/// tunable is one line here and zero Inspector work. Every change is pushed
/// straight into the live components and persisted (debounced) to
/// ArStabilizationController's JSON, i.e. tune on the beach, no recompile.
/// </summary>
public class ArTuningPanel : MonoBehaviour
{
    [Header("References (auto-wired by the builder)")]
    public ArStabilizationController controller;
    public GameObject panelRoot;        // the card that Toggle() shows/hides
    public TMP_Text telemetryText;      // live status block at the top
    public RectTransform rowsParent;    // ScrollRect content — rows spawn here

    // Palette — mirrors RegisterScreenBuilder's tokens.
    static readonly Color CardSurfaceAlt = new Color(0.137f, 0.204f, 0.298f, 1f);
    static readonly Color TextPrimary    = Color.white;
    static readonly Color TextSecondary  = new Color(0.722f, 0.773f, 0.851f, 1f);
    static readonly Color Accent         = new Color(0.517f, 0.420f, 1.0f, 1f);
    static readonly Color Danger         = new Color(0.85f, 0.35f, 0.35f, 1f);

    bool rowsBuilt;
    float telemetryDueAt;
    readonly List<Action> refreshers = new List<Action>();

    /// <summary>Wired to the HUD "AR ⚙" button.</summary>
    public void Toggle()
    {
        if (panelRoot == null) return;
        bool show = !panelRoot.activeSelf;
        if (show && !rowsBuilt) BuildRows();
        panelRoot.SetActive(show);
        if (show) RefreshAllRows();
    }

    void Update()
    {
        if (panelRoot == null || !panelRoot.activeSelf || telemetryText == null) return;
        if (Time.unscaledTime < telemetryDueAt) return;
        telemetryDueAt = Time.unscaledTime + 0.25f;
        telemetryText.text = controller != null ? controller.BuildTelemetry() : "(sem controller)";
    }

    void RefreshAllRows()
    {
        foreach (var r in refreshers) r();
    }

    // ------------------------------------------------------------------ rows

    void BuildRows()
    {
        rowsBuilt = true;
        if (rowsParent == null || controller == null) return;
        var st = controller.stillness;
        var gt = controller.gate;
        var fu = controller.fusion;

        Header("Filtro de Kalman (GPS ↔ AR)");
        if (fu != null)
        {
            SliderRow("Ruído de processo (m²/m)", 0.005f, 0.5f, "F3",
                () => fu.processNoisePerMeter, v => fu.processNoisePerMeter = v);
            SliderRow("Acurácia GPS mínima (m)", 1f, 10f, "F1",
                () => fu.minGpsAccuracy, v => fu.minGpsAccuracy = v);
            SliderRow("Acurácia máx. usável (m)", 5f, 50f, "F0",
                () => fu.maxUsableAccuracy, v => fu.maxUsableAccuracy = v);
            SliderRow("Vel. máx. de correção (m/s)", 0.02f, 1f, "F2",
                () => fu.maxCorrectionSpeed, v => fu.maxCorrectionSpeed = v);
            SliderRow("Segmento p/ calibrar rumo (m)", 3f, 25f, "F0",
                () => fu.headingSegmentMeters, v => fu.headingSegmentMeters = v);
            SliderRow("Mistura do rumo (0–1)", 0.05f, 1f, "F2",
                () => fu.headingBlend, v => fu.headingBlend = v);
        }

        Header("Detector de imobilidade (IMU)");
        if (st != null)
        {
            SliderRow("Limiar giroscópio (rad/s)", 0.02f, 0.6f, "F3",
                () => st.gyroStillThreshold, v => st.gyroStillThreshold = v);
            SliderRow("Limiar aceleração (g)", 0.005f, 0.2f, "F3",
                () => st.accelStillThreshold, v => st.accelStillThreshold = v);
            SliderRow("Tempo p/ declarar parado (s)", 0.1f, 2f, "F2",
                () => st.enterStillTime, v => st.enterStillTime = v);
            SliderRow("Multiplicador de saída", 1.1f, 3f, "F2",
                () => st.exitMultiplier, v => st.exitMultiplier = v);
            SliderRow("Suavização EMA", 0.02f, 1f, "F2",
                () => st.emaAlpha, v => st.emaAlpha = v);
        }

        Header("Gate anti-ondas");
        if (gt != null)
        {
            SliderRow("Delta mínimo (mm/frame)", 0f, 5f, "F2",
                () => gt.minDelta * 1000f, v => gt.minDelta = v / 1000f);
            SliderRow("Limiar de relocalização (m)", 0f, 1f, "F2",
                () => gt.relocalizationJumpThreshold, v => gt.relocalizationJumpThreshold = v);
        }

        Header("Chaves");
        ToggleRow("Gate IMU", () => controller.gateEnabled, v => controller.gateEnabled = v);
        ToggleRow("Correção de deriva GPS", () => controller.driftCorrectionEnabled,
            v => controller.driftCorrectionEnabled = v);
        ToggleRow("Corrigir só andando", () => controller.correctOnlyWhileMoving,
            v => controller.correctOnlyWhileMoving = v);

        ButtonRow("Salvar agora", Accent, () => controller.SaveTuning());
        ButtonRow("Restaurar padrões", Danger, () =>
        {
            controller.ResetToDefaults();
            RefreshAllRows();
        });
        ButtonRow("Zerar contadores", CardSurfaceAlt, () =>
        {
            if (controller.gate != null) controller.gate.ResetSuppressed();
        });
        ButtonRow("Fechar", CardSurfaceAlt, Toggle);
    }

    // ------------------------------------------------------- widget factories

    void Header(string label)
    {
        var t = NewText(rowsParent, label, 16, FontStyles.Bold, Accent);
        t.characterSpacing = 3f;
        AddLayout(t.gameObject, 30);
        t.alignment = TextAlignmentOptions.BottomLeft;
    }

    void SliderRow(string label, float min, float max, string fmt,
                   Func<float> get, Action<float> set)
    {
        var row = NewUI("Row", rowsParent);
        AddLayout(row, 58);

        var caption = NewText(row.transform, "", 14, FontStyles.Normal, TextSecondary);
        var capRT = caption.rectTransform;
        capRT.anchorMin = new Vector2(0, 1); capRT.anchorMax = new Vector2(1, 1);
        capRT.pivot = new Vector2(0.5f, 1);
        capRT.anchoredPosition = Vector2.zero;
        capRT.sizeDelta = new Vector2(0, 22);

        var slider = MakeSlider(row.transform, min, max);
        void UpdateCaption() =>
            caption.text = label + ":  <b><color=#FFFFFF>" + get().ToString(fmt) + "</color></b>";

        slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
        UpdateCaption();
        slider.onValueChanged.AddListener(v =>
        {
            set(v);
            UpdateCaption();
            controller.MarkTuningDirty();
        });
        refreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
            UpdateCaption();
        });
    }

    void ToggleRow(string label, Func<bool> get, Action<bool> set)
    {
        var go = NewUI(label, rowsParent);
        AddLayout(go, 44);
        var bg = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var text = NewText(go.transform, "", 15, FontStyles.Bold, TextPrimary);
        StretchFull(text.gameObject);
        text.alignment = TextAlignmentOptions.Center;

        void Refresh()
        {
            bool on = get();
            bg.color = on ? new Color(Accent.r, Accent.g, Accent.b, 0.55f) : CardSurfaceAlt;
            text.text = label + ": " + (on ? "LIGADO" : "desligado");
        }
        Refresh();
        btn.onClick.AddListener(() =>
        {
            set(!get());
            Refresh();
            controller.MarkTuningDirty();
        });
        refreshers.Add(Refresh);
    }

    void ButtonRow(string label, Color color, Action onClick)
    {
        var go = NewUI(label, rowsParent);
        AddLayout(go, 44);
        var bg = go.AddComponent<Image>();
        bg.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var text = NewText(go.transform, label, 15, FontStyles.Bold, TextPrimary);
        StretchFull(text.gameObject);
        text.alignment = TextAlignmentOptions.Center;
        btn.onClick.AddListener(() => onClick());
    }

    Slider MakeSlider(Transform parent, float min, float max)
    {
        var go = NewUI("Slider", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 3);
        rt.sizeDelta = new Vector2(0, 28);

        var bgGo = NewUI("Background", go.transform);
        var bgRT = bgGo.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0.5f); bgRT.anchorMax = new Vector2(1, 0.5f);
        bgRT.pivot = new Vector2(0.5f, 0.5f);
        bgRT.anchoredPosition = Vector2.zero;
        bgRT.sizeDelta = new Vector2(0, 6);
        bgGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var fillArea = NewUI("Fill Area", go.transform);
        var faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0, 0.5f); faRT.anchorMax = new Vector2(1, 0.5f);
        faRT.pivot = new Vector2(0.5f, 0.5f);
        faRT.anchoredPosition = Vector2.zero;
        faRT.sizeDelta = new Vector2(-20, 6);
        var fill = NewUI("Fill", fillArea.transform);
        var fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = new Vector2(0, 1);
        fillRT.sizeDelta = new Vector2(10, 0);
        fill.AddComponent<Image>().color = Accent;

        var handleArea = NewUI("Handle Slide Area", go.transform);
        var haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = new Vector2(0, 0.5f); haRT.anchorMax = new Vector2(1, 0.5f);
        haRT.pivot = new Vector2(0.5f, 0.5f);
        haRT.anchoredPosition = Vector2.zero;
        haRT.sizeDelta = new Vector2(-20, 0);
        var handle = NewUI("Handle", handleArea.transform);
        var hRT = handle.GetComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(26, 26);
        var hImg = handle.AddComponent<Image>();
        hImg.color = TextPrimary;

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.handleRect = hRT;
        slider.targetGraphic = hImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        return slider;
    }

    // --------------------------------------------------------------- low-level

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
        rt.sizeDelta = new Vector2(0, 60);
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

    static TextMeshProUGUI NewText(Transform parent, string content, float size,
                                   FontStyles style, Color color)
    {
        var go = NewUI("Text", parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.raycastTarget = false;
        return t;
    }

    static void AddLayout(GameObject go, float preferredHeight)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
    }
}
