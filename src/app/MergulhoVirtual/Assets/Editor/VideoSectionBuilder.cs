using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Builds a reusable <see cref="VideoSection"/> — a "Vídeos" header plus an
/// inactive video-card template (poster + tap-to-play surface, play overlay,
/// title caption, and a control bar with play/pause, a seek slider, and
/// elapsed/total time) — and returns the wired VideoSection component.
///
/// Any screen builder (Animals now; Beaches/About later) calls
/// <see cref="Build"/> and drops the result into its detail scroll, then wires
/// the screen controller's VideoSection reference to it. The card layout +
/// VideoPlayerController wiring live here once, so every screen gets an
/// identical card. Palette is passed in so the card matches the host screen.
/// </summary>
public static class VideoSectionBuilder
{
    static readonly Color PosterColor = new Color(0.027f, 0.082f, 0.149f, 1f); // #071526

    /// <param name="parent">Layout-group content the section is added under.</param>
    /// <returns>The VideoSection component (on the section root), template wired.</returns>
    public static VideoSection Build(Transform parent, Material roundedMat,
                                     Color accent, Color cardSurface,
                                     Color textPrimary, Color textSecondary)
    {
        // VideosSection: VLG (header + cloned cards). No ContentSizeFitter — its
        // own VLG preferred height is read by the outer Content VLG. The
        // VideoSection component toggles this GO off when there are no videos.
        var section = NewUI("VideosSection", parent);
        var sectionVideoSection = section.AddComponent<VideoSection>();
        var vlg = section.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 8, 0);
        vlg.spacing = 16f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        var header = NewText("VideosHeader", section.transform, "Vídeos",
            28, FontStyles.Bold, textPrimary);
        AddLayoutElement(header.gameObject, preferredHeight: 40);
        header.alignment = TextAlignmentOptions.Left;

        // --- Video-card template (inactive; cloned per video by VideoSection) ---
        var card = NewUI("VideoCardTemplate", section.transform);
        var cardBg = card.AddComponent<Image>();
        cardBg.color = cardSurface;
        if (roundedMat != null) cardBg.material = roundedMat;
        cardBg.raycastTarget = true; // whole card is the tap target
        card.AddComponent<RectMask2D>();
        AddLayoutElement(card, preferredHeight: 380f);

        var cardButton = card.AddComponent<Button>();
        cardButton.targetGraphic = cardBg;
        cardButton.transition = Selectable.Transition.None; // never tint the video

        var videoPlayer = card.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false; // controller configures the rest in Awake
        var ctrl = card.AddComponent<VideoPlayerController>();

        // Surface — video texture, letterboxed to clip aspect (controller sets
        // the real ratio on prepare). Plain material; raycasts pass to the card.
        var surfaceGo = NewUI("Surface", card.transform);
        var surfaceRT = surfaceGo.GetComponent<RectTransform>();
        surfaceRT.anchorMin = surfaceRT.anchorMax = new Vector2(0.5f, 0.5f);
        surfaceRT.pivot = new Vector2(0.5f, 0.5f);
        var surface = surfaceGo.AddComponent<RawImage>();
        surface.color = PosterColor;
        surface.raycastTarget = false;
        var aspectFitter = surfaceGo.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspectFitter.aspectRatio = 0.5625f; // 9:16 default (overwritten on prepare)

        // Play overlay — dims the poster + shows a play affordance; hidden while playing.
        var playOverlay = NewUI("PlayOverlay", card.transform);
        StretchFull(playOverlay);
        var playDim = playOverlay.AddComponent<Image>();
        playDim.color = new Color(0f, 0f, 0f, 0.35f);
        if (roundedMat != null) playDim.material = roundedMat;
        playDim.raycastTarget = false;
        var playLabel = NewText("PlayLabel", playOverlay.transform, "▶  Assistir",
            40, FontStyles.Bold, textPrimary);
        StretchFull(playLabel.gameObject);
        playLabel.alignment = TextAlignmentOptions.Center;

        // Title caption — top-left (the control bar owns the bottom).
        var title = NewText("TitleText", card.transform, string.Empty,
            18, FontStyles.Bold, textPrimary);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot     = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = new Vector2(0, -12);
        titleRT.sizeDelta = new Vector2(-32, 32);
        title.alignment = TextAlignmentOptions.Left;

        // Control bar — floating dark strip: play/pause, seek slider, time.
        var controlsBar = NewUI("ControlsBar", card.transform);
        var cbRT = controlsBar.GetComponent<RectTransform>();
        cbRT.anchorMin = new Vector2(0, 0);
        cbRT.anchorMax = new Vector2(1, 0);
        cbRT.pivot     = new Vector2(0.5f, 0);
        cbRT.anchoredPosition = new Vector2(0, 12);
        cbRT.sizeDelta = new Vector2(-24, 52);
        var cbBg = controlsBar.AddComponent<Image>();
        cbBg.color = new Color(0f, 0f, 0f, 0.5f);
        cbBg.raycastTarget = true; // swallow taps so they don't toggle play
        var cbHlg = controlsBar.AddComponent<HorizontalLayoutGroup>();
        cbHlg.padding = new RectOffset(12, 16, 8, 8);
        cbHlg.spacing = 12f;
        cbHlg.childControlWidth = true;
        cbHlg.childControlHeight = true;
        cbHlg.childForceExpandWidth = false;
        cbHlg.childForceExpandHeight = true;
        cbHlg.childAlignment = TextAnchor.MiddleLeft;

        var ppGo = NewUI("PlayPauseButton", controlsBar.transform);
        var ppBg = ppGo.AddComponent<Image>();
        ppBg.color = new Color(1f, 1f, 1f, 0.12f);
        var ppButton = ppGo.AddComponent<Button>();
        ppButton.targetGraphic = ppBg;
        AddLayoutElement(ppGo, preferredWidth: 88);
        var ppLabel = NewText("Label", ppGo.transform, "Pausar", 18, FontStyles.Bold, textPrimary);
        StretchFull(ppLabel.gameObject);
        ppLabel.alignment = TextAlignmentOptions.Center;

        var seekSlider = MakeHorizontalSlider("SeekSlider", controlsBar.transform,
            new Color(1f, 1f, 1f, 0.25f), accent, Color.white);
        var seekLE = seekSlider.gameObject.AddComponent<LayoutElement>();
        seekLE.flexibleWidth = 1f;
        seekLE.minWidth = 40f;
        var seekHeld = seekSlider.gameObject.AddComponent<PointerHeldFlag>();

        var timeLabel = NewText("TimeLabel", controlsBar.transform, "0:00 / 0:00",
            16, FontStyles.Normal, textSecondary);
        AddLayoutElement(timeLabel.gameObject, preferredWidth: 100);
        timeLabel.alignment = TextAlignmentOptions.MidlineRight;
        timeLabel.textWrappingMode = TextWrappingModes.NoWrap;

        controlsBar.SetActive(false); // controller shows it once the video is prepared

        // Loading + error states — centered, hidden by default.
        var loading = NewText("LoadingText", card.transform, "Carregando…",
            22, FontStyles.Normal, textSecondary);
        StretchFull(loading.gameObject);
        loading.alignment = TextAlignmentOptions.Center;
        loading.gameObject.SetActive(false);

        var error = NewText("ErrorText", card.transform, string.Empty,
            18, FontStyles.Normal, new Color(1f, 0.6f, 0.55f, 1f));
        StretchFull(error.gameObject);
        error.alignment = TextAlignmentOptions.Center;
        error.gameObject.SetActive(false);

        // Wire VideoPlayerController fields + the card/button taps → OnCardClicked.
        // The Button's persistent target points at this card's own controller;
        // Instantiate remaps it to each clone's controller (self-reference within
        // the cloned hierarchy), so every spawned card drives its own video.
        var ctrlSO = new SerializedObject(ctrl);
        ctrlSO.FindProperty("surface").objectReferenceValue = surface;
        ctrlSO.FindProperty("aspectFitter").objectReferenceValue = aspectFitter;
        ctrlSO.FindProperty("playOverlay").objectReferenceValue = playOverlay;
        ctrlSO.FindProperty("loadingIndicator").objectReferenceValue = loading.gameObject;
        ctrlSO.FindProperty("titleText").objectReferenceValue = title;
        ctrlSO.FindProperty("errorText").objectReferenceValue = error;
        ctrlSO.FindProperty("controlsBar").objectReferenceValue = controlsBar;
        ctrlSO.FindProperty("playPauseLabel").objectReferenceValue = ppLabel;
        ctrlSO.FindProperty("seekSlider").objectReferenceValue = seekSlider;
        ctrlSO.FindProperty("seekHeld").objectReferenceValue = seekHeld;
        ctrlSO.FindProperty("timeLabel").objectReferenceValue = timeLabel;
        ctrlSO.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(cardButton.onClick, ctrl.OnCardClicked);
        UnityEventTools.AddPersistentListener(ppButton.onClick, ctrl.OnCardClicked);

        card.SetActive(false); // template stays inactive; VideoSection clones it

        var sectionSO = new SerializedObject(sectionVideoSection);
        sectionSO.FindProperty("cardTemplate").objectReferenceValue = card;
        sectionSO.ApplyModifiedPropertiesWithoutUndo();

        return sectionVideoSection;
    }

    // ------------------------------------------------------------------------
    // Low-level helpers (kept local to mirror the other screen builders).
    // ------------------------------------------------------------------------
    static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 100);
        return go;
    }

    static void StretchFull(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static TMP_Text NewText(string name, Transform parent, string content, float size,
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

    static void AddLayoutElement(GameObject go, float preferredWidth = -1, float preferredHeight = -1)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (preferredWidth >= 0)  le.preferredWidth = preferredWidth;
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
    }

    // Horizontal Slider (track + fill + handle). The Slider drives fill/handle
    // anchors from its value at runtime, so the initial anchors only need to be
    // sane, not exact.
    static Slider MakeHorizontalSlider(string name, Transform parent,
                                       Color trackColor, Color fillColor, Color handleColor)
    {
        var go = NewUI(name, parent);
        var bg = go.AddComponent<Image>();
        bg.color = trackColor;
        bg.raycastTarget = true;
        var slider = go.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        var fillArea = NewUI("Fill Area", go.transform);
        var faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.5f);
        faRT.anchorMax = new Vector2(1f, 0.5f);
        faRT.pivot     = new Vector2(0.5f, 0.5f);
        faRT.offsetMin = new Vector2(8f, -3f);
        faRT.offsetMax = new Vector2(-8f, 3f);

        var fill = NewUI("Fill", fillArea.transform);
        var fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.pivot     = new Vector2(0.5f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImg.raycastTarget = false;
        slider.fillRect = fillRT;

        var handleArea = NewUI("Handle Slide Area", go.transform);
        var haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = new Vector2(0f, 0f);
        haRT.anchorMax = new Vector2(1f, 1f);
        haRT.pivot     = new Vector2(0.5f, 0.5f);
        haRT.offsetMin = new Vector2(8f, 0f);
        haRT.offsetMax = new Vector2(-8f, 0f);

        var handle = NewUI("Handle", handleArea.transform);
        var handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0.5f);
        handleRT.anchorMax = new Vector2(0f, 0.5f);
        handleRT.pivot     = new Vector2(0.5f, 0.5f);
        handleRT.sizeDelta = new Vector2(18f, 18f);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = handleColor;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;

        return slider;
    }
}
