using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the RegisterScreen GameObject hierarchy under the active scene's
/// ScreenUI canvas. Idempotent: deletes a pre-existing "RegisterScreen" first.
///
/// Visual: dark navy background, rounded cards with the project's RoundedRectCard
/// material + RectMask2D pattern, generous padding, full-width purple submit
/// button. Follows every rule in CLAUDE.md → "UI layout rules":
///   - VerticalLayoutGroup: Control Child Size W+H, Force Expand W only
///   - Exactly one child (ScrollRect) gets FlexibleHeight = 1
///   - Children of layout groups use non-stretch anchors
///   - BottomNav stays the last sibling under ScreenUI
///   - ScrollRect pattern: Viewport (Mask) → Content (VLG + ContentSizeFitter Vertical=Preferred)
///   - Bottom clearance for BottomNav (220 px)
///
/// Auto-wires the ScreenManager.registerScreen field and the BottomNav's
/// ReportButton.onClick → ScreenManager.ShowRegister(). After running, the
/// user just needs to press Play and tap the report icon.
/// </summary>
public static class RegisterScreenBuilder
{
    // Palette — matches the existing Beaches/Conditions surfaces.
    static readonly Color BgNavy        = new Color(0.055f, 0.102f, 0.169f, 1f);  // #0E1A2B
    static readonly Color CardSurface   = new Color(0.102f, 0.161f, 0.251f, 1f);  // #1A2940
    static readonly Color CardSurfaceAlt= new Color(0.137f, 0.204f, 0.298f, 1f);  // #233452
    static readonly Color TextPrimary   = Color.white;
    static readonly Color TextSecondary = new Color(0.722f, 0.773f, 0.851f, 1f);  // #B8C5D9
    static readonly Color TextMuted     = new Color(0.482f, 0.553f, 0.659f, 1f);  // #7B8DA8
    static readonly Color Accent        = new Color(0.517f, 0.420f, 1.0f, 1f);    // #846BFF (existing purple accent)
    static readonly Color AccentPressed = new Color(0.420f, 0.341f, 0.875f, 1f);

    const float ScreenSidePadding = 32f;
    const float ScreenTopPadding  = 48f;
    const float BottomNavClearance = 240f;

    [MenuItem("Tools/Mergulho Virtual/Create Register Screen", priority = 100)]
    public static void Build()
    {
        var screenUI = GameObject.Find("ScreenUI");
        if (screenUI == null)
        {
            EditorUtility.DisplayDialog("Register Screen",
                "Could not find a 'ScreenUI' GameObject in the active scene. Open MainScene first.",
                "OK");
            return;
        }

        // Reset existing if any.
        var existing = screenUI.transform.Find("RegisterScreen");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Register Screen",
                "RegisterScreen already exists under ScreenUI. Replace it?",
                "Replace", "Cancel")) return;
            Object.DestroyImmediate(existing.gameObject);
        }

        Material roundedMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/UI/RoundedRectCard.mat");
        if (roundedMat == null)
            Debug.LogWarning("[RegisterScreenBuilder] RoundedRectCard.mat not found — cards will have square corners.");

        // ========================================================================
        // RegisterScreen root: full-stretch under ScreenUI, dark navy background.
        // ========================================================================
        var screen = NewUI("RegisterScreen", screenUI.transform);
        StretchFull(screen);
        var bg = screen.AddComponent<Image>();
        bg.color = BgNavy;
        bg.raycastTarget = true; // block clicks falling through to AR

        // ========================================================================
        // ScrollView: leaves room at the bottom for BottomNav (240 px clearance).
        // ========================================================================
        var scrollGo = NewUI("Scroll View", screen.transform);
        var scrollRT = scrollGo.GetComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0, 0);
        scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.offsetMin = new Vector2(0, BottomNavClearance);
        scrollRT.offsetMax = new Vector2(0, 0);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 30f;

        var viewport = NewUI("Viewport", scrollGo.transform);
        StretchFull(viewport);
        // RectMask2D clips by rect — no Graphic needed. (A Mask with a transparent
        // Image gets culled by CanvasRenderer.cullTransparentMesh and clips everything.)
        viewport.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();

        var content = NewUI("Content", viewport.transform);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot     = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 0);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)ScreenSidePadding, (int)ScreenSidePadding, (int)ScreenTopPadding, 64);
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

        // ========================================================================
        // Header: "Reportar Avistamento" + subtitle.
        // ========================================================================
        var title = NewText("Title", content.transform, "Reportar Avistamento", 44, FontStyles.Bold, TextPrimary);
        AddLayoutElement(title.gameObject, preferredHeight: 56);
        var subtitle = NewText("Subtitle", content.transform,
            "Compartilhe o que você viu no mar de Fernando de Noronha.",
            18, FontStyles.Normal, TextSecondary);
        AddLayoutElement(subtitle.gameObject, preferredHeight: 48);
        subtitle.textWrappingMode = TextWrappingModes.Normal;

        // ========================================================================
        // Photo card — tap-to-pick. Large 4:3-ish area.
        // ========================================================================
        var photoCard = NewUI("PhotoCard", content.transform);
        AddLayoutElement(photoCard, preferredHeight: 360);
        photoCard.AddComponent<RectMask2D>();
        var photoBg = photoCard.AddComponent<Image>();
        photoBg.color = CardSurface;
        if (roundedMat != null) photoBg.material = roundedMat;
        photoBg.raycastTarget = true;

        var photoPreview = NewUI("Preview", photoCard.transform);
        StretchFull(photoPreview);
        var previewImg = photoPreview.AddComponent<Image>();
        previewImg.color = Color.white;
        previewImg.preserveAspect = true;
        previewImg.enabled = false; // hidden until a photo is picked
        previewImg.raycastTarget = false; // let clicks pass to the parent Button
        if (roundedMat != null) previewImg.material = roundedMat;

        var placeholder = NewUI("Placeholder", photoCard.transform);
        StretchFull(placeholder);
        var placeholderVlg = placeholder.AddComponent<VerticalLayoutGroup>();
        placeholderVlg.padding = new RectOffset(24, 24, 24, 24);
        placeholderVlg.spacing = 12f;
        placeholderVlg.childAlignment = TextAnchor.MiddleCenter;
        placeholderVlg.childControlWidth = true;
        placeholderVlg.childControlHeight = true;
        placeholderVlg.childForceExpandWidth = false;
        placeholderVlg.childForceExpandHeight = false;
        var icon = NewUI("Icon", placeholder.transform);
        var iconImg = icon.AddComponent<Image>();
        Sprite registerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/UI/ui_register.png");
        if (registerSprite != null) iconImg.sprite = registerSprite;
        iconImg.color = TextSecondary;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false; // let clicks pass to the parent photoCard Button
        AddLayoutElement(icon, preferredWidth: 96, preferredHeight: 96);
        var hint = NewText("Hint", placeholder.transform,
            "Toque para escolher uma foto", 22, FontStyles.Bold, TextPrimary);
        AddLayoutElement(hint.gameObject, preferredHeight: 32);
        var hint2 = NewText("Hint2", placeholder.transform,
            "EXIF da imagem original é preservado.", 16, FontStyles.Italic, TextMuted);
        AddLayoutElement(hint2.gameObject, preferredHeight: 24);

        var pickButton = photoCard.AddComponent<Button>();
        pickButton.targetGraphic = photoBg;
        var pickColors = pickButton.colors;
        pickColors.highlightedColor = new Color(0.137f, 0.204f, 0.298f, 1f);
        pickColors.pressedColor     = new Color(0.078f, 0.122f, 0.196f, 1f);
        pickButton.colors = pickColors;

        // ========================================================================
        // Beach card — TMP_Dropdown styled to match the dark surface.
        // ========================================================================
        var beachCard = MakeFieldCard("BeachCard", content.transform, "Praia", roundedMat);
        var beachDropdown = MakeStyledDropdown("BeachDropdown", beachCard.bodyHolder.transform,
            new[] { "Detectar pela localização" }, roundedMat);

        // ========================================================================
        // Date + time card — two inputs side by side.
        // ========================================================================
        var whenCard = MakeFieldCard("WhenCard", content.transform, "Quando", roundedMat);
        var whenRow = NewUI("Row", whenCard.bodyHolder.transform);
        var rowHlg = whenRow.AddComponent<HorizontalLayoutGroup>();
        rowHlg.spacing = 16f;
        rowHlg.padding = new RectOffset(0, 0, 0, 0);
        rowHlg.childControlWidth = true;
        rowHlg.childControlHeight = true;
        rowHlg.childForceExpandWidth = true;
        rowHlg.childForceExpandHeight = false;
        AddLayoutElement(whenRow, preferredHeight: 64);
        var dateInput = MakeStyledInput("DateInput", whenRow.transform, "DD/MM/AAAA", roundedMat);
        var timeInput = MakeStyledInput("TimeInput", whenRow.transform, "HH:MM", roundedMat);

        // ========================================================================
        // Optional fields card — species + notes.
        // ========================================================================
        var optionalCard = MakeFieldCard("OptionalCard", content.transform,
            "Detalhes (opcional)", roundedMat);
        var speciesInput = MakeStyledInput("SpeciesInput", optionalCard.bodyHolder.transform,
            "Espécie (ex.: tubarão-martelo)", roundedMat);
        AddLayoutElement(speciesInput.gameObject, preferredHeight: 64);
        var notesInput = MakeStyledInput("NotesInput", optionalCard.bodyHolder.transform,
            "Observações", roundedMat, multiline: true);
        AddLayoutElement(notesInput.gameObject, preferredHeight: 140);

        // ========================================================================
        // Submit button — full width, accent purple, prominent.
        // ========================================================================
        var submitGo = NewUI("SubmitButton", content.transform);
        AddLayoutElement(submitGo, preferredHeight: 72);
        var submitBg = submitGo.AddComponent<Image>();
        submitBg.color = Accent;
        if (roundedMat != null) submitBg.material = roundedMat;
        var submitButton = submitGo.AddComponent<Button>();
        submitButton.targetGraphic = submitBg;
        var submitColors = submitButton.colors;
        submitColors.highlightedColor = Accent;
        submitColors.pressedColor = AccentPressed;
        submitColors.disabledColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        submitButton.colors = submitColors;
        var submitLabel = NewText("Label", submitGo.transform, "Enviar Avistamento", 24, FontStyles.Bold, Color.white);
        StretchFull(submitLabel.gameObject);
        submitLabel.alignment = TextAlignmentOptions.Center;

        // ========================================================================
        // Status text — appears under the submit button, color-coded.
        // ========================================================================
        var status = NewText("StatusText", content.transform, "", 16, FontStyles.Normal, TextSecondary);
        status.alignment = TextAlignmentOptions.Center;
        status.textWrappingMode = TextWrappingModes.Normal;
        AddLayoutElement(status.gameObject, preferredHeight: 60, flexibleHeight: 1);

        // ========================================================================
        // Attach controller and wire fields.
        // ========================================================================
        var controller = screen.AddComponent<RegisterScreenController>();
        var so = new SerializedObject(controller);
        so.FindProperty("pickPhotoButton").objectReferenceValue = pickButton;
        so.FindProperty("photoPreview").objectReferenceValue = previewImg;
        so.FindProperty("photoPlaceholder").objectReferenceValue = placeholder;
        so.FindProperty("beachDropdown").objectReferenceValue = beachDropdown;
        so.FindProperty("dateInput").objectReferenceValue = dateInput;
        so.FindProperty("timeInput").objectReferenceValue = timeInput;
        so.FindProperty("speciesInput").objectReferenceValue = speciesInput;
        so.FindProperty("notesInput").objectReferenceValue = notesInput;
        so.FindProperty("submitButton").objectReferenceValue = submitButton;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
        var gpsGo = GameObject.Find("LocationServices");
        if (gpsGo != null)
        {
            var gps = gpsGo.GetComponent<GPSHandler>();
            if (gps != null) so.FindProperty("gpsHandler").objectReferenceValue = gps;
        }
        var smGoEarly = GameObject.Find("ScreenManager");
        if (smGoEarly != null)
        {
            var sm = smGoEarly.GetComponent<ScreenManager>();
            if (sm != null) so.FindProperty("screenManager").objectReferenceValue = sm;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // ========================================================================
        // Ensure RegisterScreen is hidden on scene load (ScreenManager activates it).
        // BottomNav must remain the LAST sibling under ScreenUI.
        // ========================================================================
        screen.SetActive(false);
        var bottomNav = screenUI.transform.Find("BottomNav");
        if (bottomNav != null) bottomNav.SetAsLastSibling();

        // ========================================================================
        // Auto-wire ScreenManager.registerScreen and the BottomNav ReportButton.
        // ========================================================================
        WireScreenManager(screen);
        WireReportButton();

        Selection.activeGameObject = screen;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[RegisterScreenBuilder] RegisterScreen created. " +
                  "Don't forget to save the scene (Ctrl+S). " +
                  "If the BottomNav ReportButton wasn't auto-wired, set its OnClick to ScreenManager.ShowRegister().");
    }

    // ------------------------------------------------------------------------
    // Field card factory — a "section" with a heading TMP and a body holder
    // (the body holder is a child VLG that the caller fills with controls).
    // ------------------------------------------------------------------------
    struct FieldCard
    {
        public GameObject card;
        public GameObject bodyHolder;
    }

    static FieldCard MakeFieldCard(string name, Transform parent, string heading, Material roundedMat)
    {
        var card = NewUI(name, parent);
        card.AddComponent<RectMask2D>();
        var bg = card.AddComponent<Image>();
        bg.color = CardSurface;
        if (roundedMat != null) bg.material = roundedMat;

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 20, 24);
        vlg.spacing = 12f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        // No ContentSizeFitter here — the parent (Content) VLG already controls
        // our height by reading our VLG's preferredHeight. Adding a fitter would
        // race with the parent and can collapse the card to 0 height.

        var headingText = NewText("Heading", card.transform, heading, 18, FontStyles.Bold, TextSecondary);
        headingText.characterSpacing = 5f;
        headingText.text = heading.ToUpperInvariant();
        AddLayoutElement(headingText.gameObject, preferredHeight: 24);

        // Body holder so the caller's controls share spacing with the heading.
        var body = NewUI("Body", card.transform);
        var bvlg = body.AddComponent<VerticalLayoutGroup>();
        bvlg.spacing = 8f;
        bvlg.childControlWidth = true;
        bvlg.childControlHeight = true;
        bvlg.childForceExpandWidth = true;
        bvlg.childForceExpandHeight = false;

        return new FieldCard { card = card, bodyHolder = body };
    }

    // ------------------------------------------------------------------------
    // TMP_InputField — dark surface, rounded corners, white text.
    // ------------------------------------------------------------------------
    static TMP_InputField MakeStyledInput(string name, Transform parent, string placeholder,
                                          Material roundedMat, bool multiline = false)
    {
        var go = NewUI(name, parent);
        go.AddComponent<RectMask2D>();
        var bg = go.AddComponent<Image>();
        bg.color = CardSurfaceAlt;
        if (roundedMat != null) bg.material = roundedMat;

        var input = go.AddComponent<TMP_InputField>();
        input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.targetGraphic = bg;
        input.caretWidth = 2;
        input.caretColor = TextPrimary;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

        var textArea = NewUI("Text Area", go.transform);
        var textAreaRT = textArea.GetComponent<RectTransform>();
        textAreaRT.anchorMin = new Vector2(0, 0);
        textAreaRT.anchorMax = new Vector2(1, 1);
        textAreaRT.offsetMin = new Vector2(20, 12);
        textAreaRT.offsetMax = new Vector2(-20, -12);
        textArea.AddComponent<RectMask2D>();

        var placeholderText = NewText("Placeholder", textArea.transform, placeholder,
            18, FontStyles.Italic, TextMuted);
        StretchFull(placeholderText.gameObject);
        placeholderText.alignment = multiline
            ? TextAlignmentOptions.TopLeft
            : TextAlignmentOptions.Left;

        var inputText = NewText("Text", textArea.transform, "", 18, FontStyles.Normal, TextPrimary);
        StretchFull(inputText.gameObject);
        inputText.alignment = multiline
            ? TextAlignmentOptions.TopLeft
            : TextAlignmentOptions.Left;
        inputText.textWrappingMode = TextWrappingModes.Normal;

        input.textViewport = textAreaRT;
        input.textComponent = inputText;
        input.placeholder = placeholderText;

        return input;
    }

    // ------------------------------------------------------------------------
    // TMP_Dropdown — dark surface, rounded corners. Option list inherits.
    // ------------------------------------------------------------------------
    static TMP_Dropdown MakeStyledDropdown(string name, Transform parent, string[] initialOptions,
                                           Material roundedMat)
    {
        var go = NewUI(name, parent);
        go.AddComponent<RectMask2D>();
        var bg = go.AddComponent<Image>();
        bg.color = CardSurfaceAlt;
        if (roundedMat != null) bg.material = roundedMat;

        var dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = bg;
        AddLayoutElement(go, preferredHeight: 64);

        var label = NewText("Label", go.transform, initialOptions.Length > 0 ? initialOptions[0] : "",
            18, FontStyles.Normal, TextPrimary);
        var labelRT = label.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(1, 1);
        labelRT.offsetMin = new Vector2(20, 8);
        labelRT.offsetMax = new Vector2(-48, -8);
        label.alignment = TextAlignmentOptions.Left;
        dropdown.captionText = label;

        // Build the popup template.
        var template = NewUI("Template", go.transform);
        var templateRT = template.GetComponent<RectTransform>();
        templateRT.anchorMin = new Vector2(0, 0);
        templateRT.anchorMax = new Vector2(1, 0);
        templateRT.pivot     = new Vector2(0.5f, 1);
        templateRT.anchoredPosition = new Vector2(0, 4);
        templateRT.sizeDelta = new Vector2(0, 280);
        var templateBg = template.AddComponent<Image>();
        templateBg.color = CardSurface;
        if (roundedMat != null) templateBg.material = roundedMat;
        template.AddComponent<RectMask2D>();
        var templateScroll = template.AddComponent<ScrollRect>();

        var tViewport = NewUI("Viewport", template.transform);
        StretchFull(tViewport);
        tViewport.AddComponent<RectMask2D>();
        templateScroll.viewport = tViewport.GetComponent<RectTransform>();

        var tContent = NewUI("Content", tViewport.transform);
        var tContentRT = tContent.GetComponent<RectTransform>();
        tContentRT.anchorMin = new Vector2(0, 1);
        tContentRT.anchorMax = new Vector2(1, 1);
        tContentRT.pivot     = new Vector2(0.5f, 1);
        tContentRT.sizeDelta = new Vector2(0, 56);
        templateScroll.content = tContentRT;

        var item = NewUI("Item", tContent.transform);
        var itemRT = item.GetComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 0.5f);
        itemRT.anchorMax = new Vector2(1, 0.5f);
        itemRT.sizeDelta = new Vector2(0, 56);
        var itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = null;

        var itemBg = NewUI("Item Background", item.transform);
        StretchFull(itemBg);
        var itemBgImg = itemBg.AddComponent<Image>();
        itemBgImg.color = new Color(0, 0, 0, 0);
        itemToggle.targetGraphic = itemBgImg;

        var itemCheck = NewUI("Item Checkmark", item.transform);
        var checkRT = itemCheck.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0, 0.5f);
        checkRT.anchorMax = new Vector2(0, 0.5f);
        checkRT.pivot     = new Vector2(0.5f, 0.5f);
        checkRT.anchoredPosition = new Vector2(20, 0);
        checkRT.sizeDelta = new Vector2(20, 20);
        var checkImg = itemCheck.AddComponent<Image>();
        checkImg.color = Accent;
        itemToggle.graphic = checkImg;

        var itemLabel = NewText("Item Label", item.transform, "Option", 18, FontStyles.Normal, TextPrimary);
        var itemLabelRT = itemLabel.GetComponent<RectTransform>();
        itemLabelRT.anchorMin = new Vector2(0, 0);
        itemLabelRT.anchorMax = new Vector2(1, 1);
        itemLabelRT.offsetMin = new Vector2(48, 0);
        itemLabelRT.offsetMax = new Vector2(-16, 0);
        itemLabel.alignment = TextAlignmentOptions.Left;

        dropdown.template = templateRT;
        dropdown.itemText = itemLabel;
        dropdown.ClearOptions();
        var opts = new List<TMP_Dropdown.OptionData>();
        foreach (var o in initialOptions) opts.Add(new TMP_Dropdown.OptionData(o));
        dropdown.AddOptions(opts);
        template.SetActive(false); // popup is opened on click

        return dropdown;
    }

    // ------------------------------------------------------------------------
    // ScreenManager + ReportButton wiring.
    // ------------------------------------------------------------------------
    static void WireScreenManager(GameObject registerScreen)
    {
        var smGo = GameObject.Find("ScreenManager");
        if (smGo == null) return;
        var sm = smGo.GetComponent<ScreenManager>();
        if (sm == null) return;
        var so = new SerializedObject(sm);
        var prop = so.FindProperty("registerScreen");
        if (prop == null)
        {
            Debug.LogWarning("[RegisterScreenBuilder] ScreenManager has no 'registerScreen' field. " +
                             "Recompile, then re-run.");
            return;
        }
        prop.objectReferenceValue = registerScreen;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireReportButton()
    {
        // Find the ReportButton under BottomNav and bind its OnClick to ScreenManager.ShowRegister.
        var reportTransform = FindDeep("BottomNav", "ReportButton");
        if (reportTransform == null) return;
        var btn = reportTransform.GetComponent<Button>();
        if (btn == null) return;
        var smGo = GameObject.Find("ScreenManager");
        if (smGo == null) return;
        var sm = smGo.GetComponent<ScreenManager>();
        if (sm == null) return;

        // Avoid duplicating if the call is already there.
        for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
        {
            if (btn.onClick.GetPersistentTarget(i) == sm
                && btn.onClick.GetPersistentMethodName(i) == nameof(ScreenManager.ShowRegister))
                return;
        }
        UnityEventTools.AddPersistentListener(btn.onClick, sm.ShowRegister);
        EditorUtility.SetDirty(btn);
    }

    static Transform FindDeep(string parentName, string childName)
    {
        var parent = GameObject.Find(parentName);
        if (parent == null) return null;
        return RecurseFind(parent.transform, childName);
    }

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

    // ------------------------------------------------------------------------
    // Low-level helpers.
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

    static void AddLayoutElement(GameObject go, float preferredWidth = -1, float preferredHeight = -1,
                                 float flexibleHeight = -1)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        if (preferredWidth >= 0)  le.preferredWidth = preferredWidth;
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
        if (flexibleHeight >= 0)  le.flexibleHeight = flexibleHeight;
    }
}
