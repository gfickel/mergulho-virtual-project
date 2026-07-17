using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Builds the "Siga-nos no Instagram" widget on the AboutScreen:
///
///   ScreenUI/AboutScreen
///   ├── Image        (existing cover-fit background — untouched)
///   ├── AboutScroll  (created on first run — ScrollRect › Viewport › Content;
///   │                 the existing "Text (TMP)" title is MOVED into Content so
///   │                 the screen can grow without overflowing — the
///   │                 "Tall detail panels need a ScrollRect" pattern)
///   │   └── Viewport › Content
///   │       ├── Text (TMP)        (the pre-existing About title)
///   │       └── InstagramSection  (built/replaced by every run)
///   │           ├── SectionHeader ("Siga-nos no Instagram")
///   │           └── InstagramCard (rounded card, tap → permalink)
///   │               ├── MediaFrame (thumbnail RawImage + video RawImage +
///   │               │              play overlay + controls bar + loading/error)
///   │               └── CaptionHolder (caption + link hint)
///   └── Back         (existing button — kept last sibling inside AboutScreen)
///
/// Follows the CLAUDE.md UI rules: explicit LayoutElements, VLGs with
/// Control Child Size W+H / Force Expand W only, non-stretch anchors for layout
/// children, RoundedRectCard material + RectMask2D on the card, BottomNav kept
/// last under ScreenUI. Idempotent: prompts before replacing an existing
/// InstagramSection; the AboutScroll wrapper is created once and then reused.
///
/// Every serialized field of InstagramPostWidget and the card's
/// VideoPlayerController is wired here — no manual Inspector dragging.
/// </summary>
public static class InstagramWidgetBuilder
{
    // Palette — matches RegisterScreenBuilder / the Beaches surfaces.
    static readonly Color CardSurface   = new Color(0.102f, 0.161f, 0.251f, 1f);  // #1A2940
    static readonly Color TextPrimary   = Color.white;
    static readonly Color TextSecondary = new Color(0.722f, 0.773f, 0.851f, 1f);  // #B8C5D9
    static readonly Color TextMuted     = new Color(0.482f, 0.553f, 0.659f, 1f);  // #7B8DA8
    static readonly Color Accent        = new Color(0.517f, 0.420f, 1.0f, 1f);    // #846BFF
    static readonly Color PosterColor   = new Color(0.027f, 0.082f, 0.149f, 1f);  // #071526

    const float ScreenSidePadding  = 32f;
    const float ScreenTopPadding   = 48f;
    const float BottomNavClearance = 240f;

    [MenuItem("Tools/Mergulho Virtual/Create Instagram Widget", priority = 103)]
    public static void Build()
    {
        // GameObject.Find skips inactive objects (screens are often saved
        // inactive) — walk the scene roots instead, like AnimalsScreenBuilder.
        GameObject screenUI = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "ScreenUI") { screenUI = root; break; }
        if (screenUI == null)
        {
            EditorUtility.DisplayDialog("Instagram Widget",
                "Could not find a 'ScreenUI' GameObject in the active scene. Open MainScene first.",
                "OK");
            return;
        }

        Transform about = screenUI.transform.Find("AboutScreen");
        if (about == null)
        {
            EditorUtility.DisplayDialog("Instagram Widget",
                "ScreenUI has no 'AboutScreen' child. The widget is designed to live there.",
                "OK");
            return;
        }

        // Idempotency: replace an existing section after confirmation.
        Transform existing = RecurseFind(about, "InstagramSection");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Instagram Widget",
                "InstagramSection already exists under AboutScreen. Replace it?",
                "Replace", "Cancel")) return;
            Object.DestroyImmediate(existing.gameObject);
        }

        Material roundedMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/UI/RoundedRectCard.mat");
        if (roundedMat == null)
            Debug.LogWarning("[InstagramWidgetBuilder] RoundedRectCard.mat not found — square corners.");

        GameObject content = EnsureAboutScroll(about);
        if (content == null) return; // malformed AboutScroll; message already shown

        BuildSection(content.transform, roundedMat);

        // Back button stays the last sibling INSIDE AboutScreen (renders above
        // the scroll); BottomNav stays the last sibling under ScreenUI.
        var back = about.Find("Back");
        if (back != null) back.SetAsLastSibling();
        var bottomNav = screenUI.transform.Find("BottomNav");
        if (bottomNav != null) bottomNav.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[InstagramWidgetBuilder] Instagram widget created on AboutScreen. " +
                  "Save the scene (Ctrl+S). Press Play and tap the About button to see it.");
    }

    // ------------------------------------------------------------------------
    // AboutScroll — created once. Moves the pre-existing About title into the
    // scroll content so the screen follows the tall-detail-panel pattern.
    // ------------------------------------------------------------------------
    static GameObject EnsureAboutScroll(Transform about)
    {
        var existingScroll = about.Find("AboutScroll");
        if (existingScroll != null)
        {
            var c = existingScroll.Find("Viewport/Content");
            if (c != null) return c.gameObject;
            EditorUtility.DisplayDialog("Instagram Widget",
                "AboutScreen/AboutScroll exists but has no Viewport/Content — it was " +
                "hand-modified. Delete or fix it, then re-run.", "OK");
            return null;
        }

        var scrollGo = NewUI("AboutScroll", about);
        var scrollRT = scrollGo.GetComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0, 0);
        scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.pivot     = new Vector2(0.5f, 0.5f);
        scrollRT.offsetMin = new Vector2(0, BottomNavClearance); // clearance for BottomNav
        scrollRT.offsetMax = new Vector2(0, 0);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 30f;

        var viewport = NewUI("Viewport", scrollGo.transform);
        StretchFull(viewport);
        viewport.AddComponent<RectMask2D>(); // rect clip, no Graphic needed
        scrollRect.viewport = viewport.GetComponent<RectTransform>();

        var content = NewUI("Content", viewport.transform);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot     = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)ScreenSidePadding, (int)ScreenSidePadding,
                                     (int)ScreenTopPadding, 64);
        vlg.spacing = 24f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        scrollRect.content = contentRT;

        // Move the existing About title into the scroll so it participates in
        // the layout instead of floating full-stretch over everything.
        var title = about.Find("Text (TMP)");
        if (title != null)
        {
            title.SetParent(content.transform, worldPositionStays: false);
            title.SetAsFirstSibling();
            var rt = (RectTransform)title;
            rt.anchorMin = new Vector2(0, 1);   // non-stretch: layout group child
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, 100); // VLG drives real size from TMP preferred height
        }

        return content;
    }

    // ------------------------------------------------------------------------
    // The Instagram section itself (rebuilt on every run).
    // ------------------------------------------------------------------------
    static void BuildSection(Transform contentParent, Material roundedMat)
    {
        var section = NewUI("InstagramSection", contentParent);
        var widget = section.AddComponent<InstagramPostWidget>();
        var sectionVlg = section.AddComponent<VerticalLayoutGroup>();
        sectionVlg.padding = new RectOffset(0, 0, 8, 0);
        sectionVlg.spacing = 16f;
        sectionVlg.childControlWidth = true;
        sectionVlg.childControlHeight = true;
        sectionVlg.childForceExpandWidth = true;
        sectionVlg.childForceExpandHeight = false;
        sectionVlg.childAlignment = TextAnchor.UpperCenter;

        var header = NewText("SectionHeader", section.transform, "Siga-nos no Instagram",
            28, FontStyles.Bold, TextPrimary);
        AddLayoutElement(header.gameObject, preferredHeight: 40);

        // --- Card: rounded surface, whole card taps open the permalink ------
        var card = NewUI("InstagramCard", section.transform);
        card.AddComponent<RectMask2D>(); // load-bearing for the SDF rounded corners
        var cardBg = card.AddComponent<Image>();
        cardBg.color = CardSurface;
        if (roundedMat != null) cardBg.material = roundedMat;
        cardBg.raycastTarget = true;
        var cardVlg = card.AddComponent<VerticalLayoutGroup>();
        cardVlg.padding = new RectOffset(0, 0, 0, 20); // media edge-to-edge, breathing room below
        cardVlg.spacing = 12f;
        cardVlg.childControlWidth = true;
        cardVlg.childControlHeight = true;
        cardVlg.childForceExpandWidth = true;
        cardVlg.childForceExpandHeight = false;
        var cardButton = card.AddComponent<Button>();
        cardButton.targetGraphic = cardBg;
        cardButton.transition = Selectable.Transition.None; // never tint the media

        // --- MediaFrame: overlay stack (not a layout group) -----------------
        var mediaFrame = NewUI("MediaFrame", card.transform);
        AddLayoutElement(mediaFrame, preferredHeight: 420);

        // Thumbnail / photo surface. Widget assigns texture + sets color white.
        var thumbGo = NewUI("Thumbnail", mediaFrame.transform);
        var thumbRT = thumbGo.GetComponent<RectTransform>();
        thumbRT.anchorMin = thumbRT.anchorMax = new Vector2(0.5f, 0.5f);
        thumbRT.pivot = new Vector2(0.5f, 0.5f);
        var thumb = thumbGo.AddComponent<RawImage>();
        thumb.color = PosterColor; // placeholder until a texture arrives
        thumb.raycastTarget = false; // taps fall through to the card button
        if (roundedMat != null) thumb.material = roundedMat; // corners follow the card
        var thumbFitter = thumbGo.AddComponent<AspectRatioFitter>();
        thumbFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        thumbFitter.aspectRatio = 1f; // square placeholder; widget sets the real ratio

        // Video surface — layered over the thumbnail; VideoPlayerController
        // makes it visible (color white + texture) once a stream is prepared.
        // Idle poster color is set TRANSPARENT below so the thumbnail shows.
        var videoGo = NewUI("VideoSurface", mediaFrame.transform);
        var videoRT = videoGo.GetComponent<RectTransform>();
        videoRT.anchorMin = videoRT.anchorMax = new Vector2(0.5f, 0.5f);
        videoRT.pivot = new Vector2(0.5f, 0.5f);
        var videoSurface = videoGo.AddComponent<RawImage>();
        videoSurface.color = new Color(0, 0, 0, 0);
        videoSurface.raycastTarget = false;
        if (roundedMat != null) videoSurface.material = roundedMat; // corners follow the card, like the thumbnail
        var videoFitter = videoGo.AddComponent<AspectRatioFitter>();
        videoFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        videoFitter.aspectRatio = 0.5625f; // 9:16 default; controller overwrites on prepare

        // Widget's initial loading state (image fetch).
        var mediaLoading = NewText("MediaLoading", mediaFrame.transform, "Carregando…",
            22, FontStyles.Normal, TextSecondary);
        StretchFull(mediaLoading.gameObject);
        mediaLoading.alignment = TextAlignmentOptions.Center;

        // Controller's buffering state (video prepare).
        var videoLoading = NewText("VideoLoading", mediaFrame.transform, "Carregando…",
            22, FontStyles.Normal, TextSecondary);
        StretchFull(videoLoading.gameObject);
        videoLoading.alignment = TextAlignmentOptions.Center;
        videoLoading.gameObject.SetActive(false);

        var videoError = NewText("VideoError", mediaFrame.transform, string.Empty,
            18, FontStyles.Normal, new Color(1f, 0.6f, 0.55f, 1f));
        StretchFull(videoError.gameObject);
        videoError.alignment = TextAlignmentOptions.Center;
        videoError.gameObject.SetActive(false);

        // Play overlay — VIDEO only. Its Button starts playback; every other
        // tap on the card opens the permalink.
        var playOverlay = NewUI("PlayOverlay", mediaFrame.transform);
        StretchFull(playOverlay);
        var playDim = playOverlay.AddComponent<Image>();
        playDim.color = new Color(0f, 0f, 0f, 0.35f);
        if (roundedMat != null) playDim.material = roundedMat;
        playDim.raycastTarget = true; // catches the tap instead of the card button
        var playButton = playOverlay.AddComponent<Button>();
        playButton.targetGraphic = playDim;
        playButton.transition = Selectable.Transition.None;
        var playLabel = NewText("PlayLabel", playOverlay.transform, "▶  Assistir",
            40, FontStyles.Bold, TextPrimary);
        StretchFull(playLabel.gameObject);
        playLabel.alignment = TextAlignmentOptions.Center;
        playOverlay.SetActive(false); // widget enables it for VIDEO posts

        // Controls bar — play/pause + seek + mute + time (controller shows it
        // once the stream is prepared). Swallows taps so they don't hit the card.
        var controlsBar = NewUI("ControlsBar", mediaFrame.transform);
        var cbRT = controlsBar.GetComponent<RectTransform>();
        cbRT.anchorMin = new Vector2(0, 0);
        cbRT.anchorMax = new Vector2(1, 0);
        cbRT.pivot     = new Vector2(0.5f, 0);
        cbRT.anchoredPosition = new Vector2(0, 12);
        cbRT.sizeDelta = new Vector2(-24, 52);
        var cbBg = controlsBar.AddComponent<Image>();
        cbBg.color = new Color(0f, 0f, 0f, 0.5f);
        cbBg.raycastTarget = true;
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
        var ppLabel = NewText("Label", ppGo.transform, "Pausar", 18, FontStyles.Bold, TextPrimary);
        StretchFull(ppLabel.gameObject);
        ppLabel.alignment = TextAlignmentOptions.Center;

        var seekSlider = MakeHorizontalSlider("SeekSlider", controlsBar.transform,
            new Color(1f, 1f, 1f, 0.25f), Accent, Color.white);
        var seekLE = seekSlider.gameObject.AddComponent<LayoutElement>();
        seekLE.flexibleWidth = 1f;
        seekLE.minWidth = 40f;
        var seekHeld = seekSlider.gameObject.AddComponent<PointerHeldFlag>();

        var muteGo = NewUI("MuteButton", controlsBar.transform);
        var muteBg = muteGo.AddComponent<Image>();
        muteBg.color = new Color(1f, 1f, 1f, 0.12f);
        var muteButton = muteGo.AddComponent<Button>();
        muteButton.targetGraphic = muteBg;
        AddLayoutElement(muteGo, preferredWidth: 110);
        var muteLabel = NewText("Label", muteGo.transform, "Ativar som", 16, FontStyles.Bold, TextPrimary);
        StretchFull(muteLabel.gameObject);
        muteLabel.alignment = TextAlignmentOptions.Center;

        var timeLabel = NewText("TimeLabel", controlsBar.transform, "0:00 / 0:00",
            16, FontStyles.Normal, TextSecondary);
        AddLayoutElement(timeLabel.gameObject, preferredWidth: 100);
        timeLabel.alignment = TextAlignmentOptions.MidlineRight;
        timeLabel.textWrappingMode = TextWrappingModes.NoWrap;

        controlsBar.SetActive(false);

        // Video engine on the MediaFrame.
        var videoPlayer = mediaFrame.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false; // controller configures the rest lazily
        var videoCtrl = mediaFrame.AddComponent<VideoPlayerController>();

        // --- Caption + link hint --------------------------------------------
        var captionHolder = NewUI("CaptionHolder", card.transform);
        var chVlg = captionHolder.AddComponent<VerticalLayoutGroup>();
        chVlg.padding = new RectOffset(24, 24, 0, 0);
        chVlg.spacing = 8f;
        chVlg.childControlWidth = true;
        chVlg.childControlHeight = true;
        chVlg.childForceExpandWidth = true;
        chVlg.childForceExpandHeight = false;

        var caption = NewText("Caption", captionHolder.transform, string.Empty,
            18, FontStyles.Normal, TextSecondary);
        caption.textWrappingMode = TextWrappingModes.Normal;
        caption.richText = false; // never let post text inject TMP markup

        var linkHint = NewText("LinkHint", captionHolder.transform,
            "Toque para ver a publicação no Instagram", 15, FontStyles.Italic, TextMuted);
        AddLayoutElement(linkHint.gameObject, preferredHeight: 22);

        // --- Wiring ----------------------------------------------------------
        var widgetSO = new SerializedObject(widget);
        widgetSO.FindProperty("mediaImage").objectReferenceValue = thumb;
        widgetSO.FindProperty("mediaAspect").objectReferenceValue = thumbFitter;
        widgetSO.FindProperty("captionText").objectReferenceValue = caption;
        widgetSO.FindProperty("playOverlay").objectReferenceValue = playOverlay;
        widgetSO.FindProperty("loadingIndicator").objectReferenceValue = mediaLoading.gameObject;
        widgetSO.FindProperty("videoController").objectReferenceValue = videoCtrl;
        widgetSO.ApplyModifiedPropertiesWithoutUndo();

        var ctrlSO = new SerializedObject(videoCtrl);
        ctrlSO.FindProperty("surface").objectReferenceValue = videoSurface;
        ctrlSO.FindProperty("aspectFitter").objectReferenceValue = videoFitter;
        ctrlSO.FindProperty("playOverlay").objectReferenceValue = playOverlay;
        ctrlSO.FindProperty("loadingIndicator").objectReferenceValue = videoLoading.gameObject;
        ctrlSO.FindProperty("errorText").objectReferenceValue = videoError;
        ctrlSO.FindProperty("controlsBar").objectReferenceValue = controlsBar;
        ctrlSO.FindProperty("playPauseLabel").objectReferenceValue = ppLabel;
        ctrlSO.FindProperty("seekSlider").objectReferenceValue = seekSlider;
        ctrlSO.FindProperty("seekHeld").objectReferenceValue = seekHeld;
        ctrlSO.FindProperty("timeLabel").objectReferenceValue = timeLabel;
        // Transparent idle poster — the widget's thumbnail must stay visible
        // beneath the (unprepared) video surface.
        ctrlSO.FindProperty("posterColor").colorValue = new Color(0, 0, 0, 0);
        // Instagram-style: start muted, unmute via the controls-bar button.
        ctrlSO.FindProperty("startMuted").boolValue = true;
        ctrlSO.FindProperty("muteButton").objectReferenceValue = muteGo;
        ctrlSO.FindProperty("muteLabel").objectReferenceValue = muteLabel;
        ctrlSO.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(cardButton.onClick, widget.OpenPermalink);
        UnityEventTools.AddPersistentListener(playButton.onClick, videoCtrl.OnCardClicked);
        UnityEventTools.AddPersistentListener(ppButton.onClick, videoCtrl.OnCardClicked);
        UnityEventTools.AddPersistentListener(muteButton.onClick, videoCtrl.ToggleMute);

        Selection.activeGameObject = section;
    }

    // ------------------------------------------------------------------------
    // Low-level helpers (kept local to mirror the other screen builders).
    // ------------------------------------------------------------------------
    static Transform RecurseFind(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = RecurseFind(t.GetChild(i), name);
            if (r != null) return r;
        }
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
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        if (preferredWidth >= 0)  le.preferredWidth = preferredWidth;
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
    }

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
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.pivot     = new Vector2(0.5f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImg.raycastTarget = false;
        slider.fillRect = fillRT;

        var handleArea = NewUI("Handle Slide Area", go.transform);
        var haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero;
        haRT.anchorMax = Vector2.one;
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
