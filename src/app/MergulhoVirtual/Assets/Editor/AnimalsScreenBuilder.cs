using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the AnimalsScreen GameObject hierarchy under the active scene's
/// ScreenUI canvas, plus a sibling-of-ScreenUI 3D viewer rig at the scene root.
///
/// The list view reuses the project's ListItem prefab + ListItemView (same
/// cards as Beaches). The detail view shows a hero 3D viewer card (a RawImage
/// rendering a dedicated camera's RenderTexture, with AnimalViewerInput for
/// drag-rotate / pinch-zoom) and a scrollable name + description below.
///
/// The 3D viewer rig is positioned far from world origin so it can never
/// overlap or be visible from the AR camera; the dedicated viewer camera and
/// its lights are layer-culled to "AnimalViewer" so they don't interfere with
/// the AR scene either way.
///
/// Idempotent: deletes a pre-existing "AnimalsScreen" + "AnimalViewerRig"
/// before rebuilding. Auto-wires AnimalsScreenController fields,
/// AnimalViewerInput fields, ScreenManager.animalsScreen, and the BottomNav
/// AnimalsButton.onClick → ScreenManager.ShowAnimals().
/// </summary>
public static class AnimalsScreenBuilder
{
    // Palette — matches RegisterScreenBuilder for visual consistency.
    static readonly Color BgNavy        = new Color(0.055f, 0.102f, 0.169f, 1f);  // #0E1A2B
    static readonly Color CardSurface   = new Color(0.102f, 0.161f, 0.251f, 1f);  // #1A2940
    static readonly Color CardSurfaceAlt= new Color(0.137f, 0.204f, 0.298f, 1f);  // #233452
    static readonly Color TextPrimary   = Color.white;
    static readonly Color TextSecondary = new Color(0.722f, 0.773f, 0.851f, 1f);  // #B8C5D9
    static readonly Color TextMuted     = new Color(0.482f, 0.553f, 0.659f, 1f);  // #7B8DA8
    static readonly Color Accent        = new Color(0.517f, 0.420f, 1.0f, 1f);    // #846BFF

    // Deep ocean clear color for the 3D viewer camera — lets the model "float"
    // against an aquarium-blue backdrop.
    static readonly Color ViewerClear   = new Color(0.027f, 0.082f, 0.149f, 1f);  // #071526

    const float ScreenSidePadding = 32f;
    const float ScreenTopPadding  = 48f;
    const float BottomNavClearance = 240f;
    const float ViewerCardHeight  = 420f;   // hero size in canvas units (Reference 800x600, MatchWidthOrHeight 0.5)
    const string ViewerLayerName  = "AnimalViewer";
    // Far from world origin so the rig is never within the AR camera's far plane.
    static readonly Vector3 RigWorldPosition = new Vector3(5000f, 0f, 0f);

    [MenuItem("Tools/Mergulho Virtual/Create Animals Screen", priority = 101)]
    public static void Build()
    {
        var screenUI = GameObject.Find("ScreenUI");
        if (screenUI == null)
        {
            EditorUtility.DisplayDialog("Animals Screen",
                "Could not find a 'ScreenUI' GameObject in the active scene. Open MainScene first.",
                "OK");
            return;
        }

        int viewerLayer = LayerMask.NameToLayer(ViewerLayerName);
        if (viewerLayer < 0)
        {
            EditorUtility.DisplayDialog("Animals Screen",
                $"Layer '{ViewerLayerName}' is not defined. Add it under Project Settings > Tags and Layers, then re-run.",
                "OK");
            return;
        }

        var existingScreen = screenUI.transform.Find("AnimalsScreen");
        var existingRig    = GameObject.Find("AnimalViewerRig");
        if (existingScreen != null || existingRig != null)
        {
            if (!EditorUtility.DisplayDialog("Animals Screen",
                "AnimalsScreen / AnimalViewerRig already exist. Replace them?",
                "Replace", "Cancel")) return;
            if (existingScreen != null) Object.DestroyImmediate(existingScreen.gameObject);
            if (existingRig != null) Object.DestroyImmediate(existingRig);
        }

        // Asset references — load up front so we can warn about missing pieces.
        Material roundedMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/UI/RoundedRectCard.mat");
        if (roundedMat == null)
            Debug.LogWarning("[AnimalsScreenBuilder] RoundedRectCard.mat not found — cards will have square corners.");

        RenderTexture viewerRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(
            "Assets/RenderTextures/AnimalViewer.renderTexture");
        if (viewerRT == null)
            Debug.LogWarning("[AnimalsScreenBuilder] AnimalViewer.renderTexture not found at Assets/RenderTextures/ — viewer will render to a default texture.");

        GameObject listItemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/UI/ListItem.prefab");
        if (listItemPrefab == null)
            Debug.LogWarning("[AnimalsScreenBuilder] ListItem.prefab not found — list cards won't be wired.");
        ListItemView listItemView = listItemPrefab != null ? listItemPrefab.GetComponent<ListItemView>() : null;

        // ========================================================================
        // 3D viewer rig (scene root, far from origin).
        // ========================================================================
        var rig = new GameObject("AnimalViewerRig");
        rig.transform.position = RigWorldPosition;

        // Turntable — empty Transform; the controller spawns models under it.
        var turntable = new GameObject("Turntable");
        turntable.transform.SetParent(rig.transform, worldPositionStays: false);
        turntable.transform.localPosition = Vector3.zero;
        turntable.layer = viewerLayer;

        // Viewer camera — looks at the turntable from a slightly elevated angle.
        var camGo = new GameObject("ViewerCamera");
        camGo.transform.SetParent(rig.transform, worldPositionStays: false);
        camGo.transform.localPosition = new Vector3(0f, 0.6f, -3.2f);
        camGo.transform.LookAt(rig.transform.position);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ViewerClear;
        cam.cullingMask = 1 << viewerLayer;
        cam.orthographic = false;
        cam.fieldOfView = 35f;        // gentle telephoto = product-render look
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 50f;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.targetTexture = viewerRT;
        camGo.layer = viewerLayer;

        // Key light — warm, from upper-front.
        var keyGo = new GameObject("KeyLight");
        keyGo.transform.SetParent(rig.transform, worldPositionStays: false);
        keyGo.transform.localPosition = Vector3.zero;
        keyGo.transform.localRotation = Quaternion.Euler(40f, -30f, 0f);
        var keyLight = keyGo.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1f, 0.96f, 0.88f, 1f);
        keyLight.intensity = 1.6f;
        keyLight.cullingMask = 1 << viewerLayer;
        keyGo.layer = viewerLayer;

        // Fill light — cool, from below-back; suggests light bouncing off water.
        var fillGo = new GameObject("FillLight");
        fillGo.transform.SetParent(rig.transform, worldPositionStays: false);
        fillGo.transform.localRotation = Quaternion.Euler(-15f, 150f, 0f);
        var fillLight = fillGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(0.55f, 0.78f, 1f, 1f);
        fillLight.intensity = 0.6f;
        fillLight.cullingMask = 1 << viewerLayer;
        fillGo.layer = viewerLayer;

        // ========================================================================
        // AnimalsScreen root: full-stretch under ScreenUI, dark navy background.
        // ========================================================================
        var screen = NewUI("AnimalsScreen", screenUI.transform);
        StretchFull(screen);
        var bg = screen.AddComponent<Image>();
        bg.color = BgNavy;
        bg.raycastTarget = true; // block clicks from falling through to AR

        // ========================================================================
        // ListPanel — Header (top) + ScrollView (rest).
        // ========================================================================
        var listPanel = NewUI("ListPanel", screen.transform);
        StretchFull(listPanel);

        var listHeader = NewText("Header", listPanel.transform, "Animais",
            44, FontStyles.Bold, TextPrimary);
        var listHeaderRT = listHeader.GetComponent<RectTransform>();
        listHeaderRT.anchorMin = new Vector2(0, 1);
        listHeaderRT.anchorMax = new Vector2(1, 1);
        listHeaderRT.pivot     = new Vector2(0.5f, 1);
        listHeaderRT.anchoredPosition = new Vector2(0, -ScreenTopPadding);
        listHeaderRT.sizeDelta = new Vector2(-ScreenSidePadding * 2, 64);
        listHeader.alignment = TextAlignmentOptions.Left;

        var listScrollGo = NewUI("Scroll View", listPanel.transform);
        var listScrollRT = listScrollGo.GetComponent<RectTransform>();
        listScrollRT.anchorMin = new Vector2(0, 0);
        listScrollRT.anchorMax = new Vector2(1, 1);
        listScrollRT.pivot     = new Vector2(0.5f, 0.5f);
        listScrollRT.offsetMin = new Vector2(0, BottomNavClearance);
        listScrollRT.offsetMax = new Vector2(0, -(ScreenTopPadding + 80f));
        var listScrollRect = listScrollGo.AddComponent<ScrollRect>();
        listScrollRect.horizontal = false;
        listScrollRect.vertical = true;
        listScrollRect.movementType = ScrollRect.MovementType.Elastic;
        listScrollRect.scrollSensitivity = 30f;

        var listViewport = NewUI("Viewport", listScrollGo.transform);
        StretchFull(listViewport);
        listViewport.AddComponent<RectMask2D>();
        listScrollRect.viewport = listViewport.GetComponent<RectTransform>();

        var listContent = NewUI("Content", listViewport.transform);
        var listContentRT = listContent.GetComponent<RectTransform>();
        listContentRT.anchorMin = new Vector2(0, 1);
        listContentRT.anchorMax = new Vector2(1, 1);
        listContentRT.pivot     = new Vector2(0.5f, 1);
        listContentRT.anchoredPosition = Vector2.zero;
        listContentRT.sizeDelta = Vector2.zero;
        var listVlg = listContent.AddComponent<VerticalLayoutGroup>();
        listVlg.padding = new RectOffset((int)ScreenSidePadding, (int)ScreenSidePadding, 16, 64);
        listVlg.spacing = 24f;
        listVlg.childControlWidth = true;
        listVlg.childControlHeight = true;
        listVlg.childForceExpandWidth = true;
        listVlg.childForceExpandHeight = false;
        listVlg.childAlignment = TextAnchor.UpperCenter;
        var listFitter = listContent.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        listFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        listScrollRect.content = listContentRT;

        // ========================================================================
        // DetailPanel — ViewerCard (top hero), InfoCard (description below),
        // BackButton (top-left overlay).
        // ========================================================================
        var detailPanel = NewUI("DetailPanel", screen.transform);
        StretchFull(detailPanel);
        detailPanel.SetActive(false);

        // ViewerCard: anchored top, fixed height, side margin.
        var viewerCard = NewUI("ViewerCard", detailPanel.transform);
        var viewerCardRT = viewerCard.GetComponent<RectTransform>();
        viewerCardRT.anchorMin = new Vector2(0, 1);
        viewerCardRT.anchorMax = new Vector2(1, 1);
        viewerCardRT.pivot     = new Vector2(0.5f, 1);
        viewerCardRT.anchoredPosition = new Vector2(0, -ScreenTopPadding);
        viewerCardRT.sizeDelta = new Vector2(-ScreenSidePadding * 2, ViewerCardHeight);
        viewerCard.AddComponent<RectMask2D>();
        var viewerCardBg = viewerCard.AddComponent<Image>();
        viewerCardBg.color = CardSurface;
        if (roundedMat != null) viewerCardBg.material = roundedMat;
        viewerCardBg.raycastTarget = false;

        // RawImage holding the RenderTexture — fills the card. AnimalViewerInput
        // lives on this RectTransform so drag/pinch are scoped to the viewer area.
        var rawGo = NewUI("Viewport3D", viewerCard.transform);
        StretchFull(rawGo);
        var rawImage = rawGo.AddComponent<RawImage>();
        rawImage.texture = viewerRT;
        rawImage.color = Color.white;
        rawImage.raycastTarget = true;
        if (roundedMat != null) rawImage.material = roundedMat;

        var viewerInput = rawGo.AddComponent<AnimalViewerInput>();
        var viewerInputSO = new SerializedObject(viewerInput);
        viewerInputSO.FindProperty("turntable").objectReferenceValue = turntable.transform;
        viewerInputSO.FindProperty("viewerCamera").objectReferenceValue = camGo.transform;
        viewerInputSO.ApplyModifiedPropertiesWithoutUndo();

        // Hint label at the bottom of the viewer card.
        var hint = NewText("Hint", viewerCard.transform,
            "Arraste para girar  ·  Pinça para ampliar",
            16, FontStyles.Italic, TextMuted);
        var hintRT = hint.GetComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(0, 0);
        hintRT.anchorMax = new Vector2(1, 0);
        hintRT.pivot     = new Vector2(0.5f, 0);
        hintRT.anchoredPosition = new Vector2(0, 16);
        hintRT.sizeDelta = new Vector2(-32, 24);
        hint.alignment = TextAlignmentOptions.Center;
        hint.raycastTarget = false;

        // InfoCard: full-stretch with margins so it sits below the viewer card
        // and clears BottomNav at the bottom.
        var infoCard = NewUI("InfoCard", detailPanel.transform);
        var infoRT = infoCard.GetComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0, 0);
        infoRT.anchorMax = new Vector2(1, 1);
        infoRT.pivot     = new Vector2(0.5f, 0.5f);
        infoRT.offsetMin = new Vector2(ScreenSidePadding, BottomNavClearance);
        infoRT.offsetMax = new Vector2(-ScreenSidePadding, -(ScreenTopPadding + ViewerCardHeight + 24f));
        infoCard.AddComponent<RectMask2D>();
        var infoBg = infoCard.AddComponent<Image>();
        infoBg.color = CardSurface;
        if (roundedMat != null) infoBg.material = roundedMat;
        infoBg.raycastTarget = true;

        // ScrollView inside the info card.
        var infoScrollGo = NewUI("Scroll View", infoCard.transform);
        StretchFull(infoScrollGo);
        var infoScrollRT = infoScrollGo.GetComponent<RectTransform>();
        infoScrollRT.offsetMin = new Vector2(8, 8);
        infoScrollRT.offsetMax = new Vector2(-8, -8);
        var infoScrollRect = infoScrollGo.AddComponent<ScrollRect>();
        infoScrollRect.horizontal = false;
        infoScrollRect.vertical = true;
        infoScrollRect.movementType = ScrollRect.MovementType.Elastic;
        infoScrollRect.scrollSensitivity = 30f;

        var infoViewport = NewUI("Viewport", infoScrollGo.transform);
        StretchFull(infoViewport);
        infoViewport.AddComponent<RectMask2D>();
        infoScrollRect.viewport = infoViewport.GetComponent<RectTransform>();

        var infoContent = NewUI("Content", infoViewport.transform);
        var infoContentRT = infoContent.GetComponent<RectTransform>();
        infoContentRT.anchorMin = new Vector2(0, 1);
        infoContentRT.anchorMax = new Vector2(1, 1);
        infoContentRT.pivot     = new Vector2(0.5f, 1);
        infoContentRT.anchoredPosition = Vector2.zero;
        infoContentRT.sizeDelta = Vector2.zero;
        var infoVlg = infoContent.AddComponent<VerticalLayoutGroup>();
        infoVlg.padding = new RectOffset(20, 20, 16, 24);
        infoVlg.spacing = 12f;
        infoVlg.childControlWidth = true;
        infoVlg.childControlHeight = true;
        infoVlg.childForceExpandWidth = true;
        infoVlg.childForceExpandHeight = false;
        var infoFitter = infoContent.AddComponent<ContentSizeFitter>();
        infoFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        infoFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        infoScrollRect.content = infoContentRT;

        var nameText = NewText("NameText", infoContent.transform, "Nome", 36, FontStyles.Bold, TextPrimary);
        AddLayoutElement(nameText.gameObject, preferredHeight: 48);
        nameText.alignment = TextAlignmentOptions.Left;

        var descText = NewText("DescriptionText", infoContent.transform, "Descrição",
            18, FontStyles.Normal, TextSecondary);
        descText.alignment = TextAlignmentOptions.TopLeft;
        descText.textWrappingMode = TextWrappingModes.Normal;
        AddLayoutElement(descText.gameObject, flexibleHeight: 1);

        // BackButton — top-left, semi-transparent dark pill with "← Voltar".
        var backGo = NewUI("BackButton", detailPanel.transform);
        var backRT = backGo.GetComponent<RectTransform>();
        backRT.anchorMin = new Vector2(0, 1);
        backRT.anchorMax = new Vector2(0, 1);
        backRT.pivot     = new Vector2(0, 1);
        backRT.anchoredPosition = new Vector2(ScreenSidePadding, -ScreenTopPadding);
        backRT.sizeDelta = new Vector2(160, 64);
        var backBg = backGo.AddComponent<Image>();
        backBg.color = new Color(0f, 0f, 0f, 0.55f);
        if (roundedMat != null) backBg.material = roundedMat;
        backGo.AddComponent<RectMask2D>();
        var backButton = backGo.AddComponent<Button>();
        backButton.targetGraphic = backBg;
        var backColors = backButton.colors;
        backColors.normalColor      = Color.white;
        backColors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        backColors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
        backButton.colors = backColors;
        var backLabel = NewText("Label", backGo.transform, "← Voltar", 20, FontStyles.Bold, TextPrimary);
        StretchFull(backLabel.gameObject);
        backLabel.alignment = TextAlignmentOptions.Center;

        // ========================================================================
        // Attach controller and wire fields.
        // ========================================================================
        var controller = screen.AddComponent<AnimalsScreenController>();
        var so = new SerializedObject(controller);
        so.FindProperty("listPanel").objectReferenceValue = listPanel;
        so.FindProperty("listContent").objectReferenceValue = listContent.transform;
        if (listItemView != null)
            so.FindProperty("listItemPrefab").objectReferenceValue = listItemView;
        so.FindProperty("detailPanel").objectReferenceValue = detailPanel;
        so.FindProperty("detailScroll").objectReferenceValue = infoScrollRect;
        so.FindProperty("detailName").objectReferenceValue = nameText;
        so.FindProperty("detailDescription").objectReferenceValue = descText;
        so.FindProperty("backButton").objectReferenceValue = backButton;
        so.FindProperty("viewerRig").objectReferenceValue = rig;
        so.FindProperty("turntable").objectReferenceValue = turntable.transform;
        so.FindProperty("viewerLayerName").stringValue = ViewerLayerName;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ========================================================================
        // Hide rig + screen on scene load. ScreenManager activates the screen,
        // and the controller activates the rig only while in detail view.
        // ========================================================================
        rig.SetActive(false);
        screen.SetActive(false);
        var bottomNav = screenUI.transform.Find("BottomNav");
        if (bottomNav != null) bottomNav.SetAsLastSibling();

        // ========================================================================
        // Auto-wire ScreenManager.animalsScreen and BottomNav AnimalsButton.
        // ========================================================================
        WireScreenManager(screen);
        WireAnimalsButton();

        Selection.activeGameObject = screen;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[AnimalsScreenBuilder] AnimalsScreen + AnimalViewerRig created. " +
                  "Save the scene (Ctrl+S). Tune AnimalDef.viewerScale/viewerOffset per species " +
                  "if the model is too big/small inside the viewer card.");
    }

    // ------------------------------------------------------------------------
    // ScreenManager + BottomNav wiring.
    // ------------------------------------------------------------------------
    static void WireScreenManager(GameObject animalsScreen)
    {
        var smGo = GameObject.Find("ScreenManager");
        if (smGo == null) return;
        var sm = smGo.GetComponent<ScreenManager>();
        if (sm == null) return;
        var so = new SerializedObject(sm);
        var prop = so.FindProperty("animalsScreen");
        if (prop == null)
        {
            Debug.LogWarning("[AnimalsScreenBuilder] ScreenManager has no 'animalsScreen' field. Recompile, then re-run.");
            return;
        }
        prop.objectReferenceValue = animalsScreen;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireAnimalsButton()
    {
        var animalsTransform = FindDeep("BottomNav", "AnimalsButton");
        if (animalsTransform == null)
        {
            Debug.LogWarning("[AnimalsScreenBuilder] Could not find BottomNav/AnimalsButton — wire its OnClick to ScreenManager.ShowAnimals() manually.");
            return;
        }
        var btn = animalsTransform.GetComponent<Button>();
        if (btn == null) return;
        var smGo = GameObject.Find("ScreenManager");
        if (smGo == null) return;
        var sm = smGo.GetComponent<ScreenManager>();
        if (sm == null) return;

        for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
        {
            if (btn.onClick.GetPersistentTarget(i) == sm
                && btn.onClick.GetPersistentMethodName(i) == nameof(ScreenManager.ShowAnimals))
                return;
        }
        UnityEventTools.AddPersistentListener(btn.onClick, sm.ShowAnimals);
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
