using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the AnimalsScreen GameObject hierarchy under the active scene's
/// ScreenUI canvas, plus a sibling-of-ScreenUI 3D viewer rig at the scene root.
///
/// The list view reuses the project's ListItem prefab + ListItemView (same
/// cards as Beaches). The detail view is wrapped in a single ScrollRect so
/// the whole panel — hero 3D viewer card, name, description, credits —
/// scrolls vertically; this is the only way the description stays reachable
/// in landscape, where the fixed-height viewer card alone exceeds the
/// available height. AnimalViewerInput on the RawImage intercepts drag/pinch
/// before the ScrollRect, so dragging on the viewer rotates the model and
/// dragging anywhere else scrolls.
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
    const float BottomNavClearance = 120f;
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
        // GameObject.Find skips inactive objects, and the controller deactivates
        // AnimalViewerRig whenever the screen isn't visible — so a re-run while
        // on any other screen would silently leave the old rig in place and add
        // a second one. Iterate scene roots instead, which sees inactive too.
        var existingRigs = new System.Collections.Generic.List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "AnimalViewerRig") existingRigs.Add(root);
        if (existingScreen != null || existingRigs.Count > 0)
        {
            if (!EditorUtility.DisplayDialog("Animals Screen",
                "AnimalsScreen / AnimalViewerRig already exist. Replace them?",
                "Replace", "Cancel")) return;
            if (existingScreen != null) Object.DestroyImmediate(existingScreen.gameObject);
            foreach (var oldRig in existingRigs) Object.DestroyImmediate(oldRig);
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
        else if (viewerRT.antiAliasing != 8)
        {
            // Force MSAA 8x on the target — combined with SMAA High on the
            // camera below this is the highest-quality AA URP can produce
            // for an RT-targeted camera.
            if (viewerRT.IsCreated()) viewerRT.Release();
            viewerRT.antiAliasing = 8;
            EditorUtility.SetDirty(viewerRT);
        }

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

        // URP per-camera AA — SMAA at High quality, layered on top of the
        // RT's MSAA 8x. The combination is what "High quality" antialiasing
        // looks like for the model viewer.
        var urpCamData = cam.GetUniversalAdditionalCameraData();
        urpCamData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        urpCamData.antialiasingQuality = AntialiasingQuality.High;

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
        // DetailPanel — single ScrollRect wraps ViewerCard + InfoCard so the
        // full detail scrolls vertically (required in landscape, where the
        // viewer card alone is taller than the screen). BackButton is an
        // overlay sibling so it stays anchored regardless of scroll position.
        // ========================================================================
        var detailPanel = NewUI("DetailPanel", screen.transform);
        StretchFull(detailPanel);
        detailPanel.SetActive(false);

        var detailScrollGo = NewUI("Scroll View", detailPanel.transform);
        var detailScrollRT = detailScrollGo.GetComponent<RectTransform>();
        detailScrollRT.anchorMin = new Vector2(0, 0);
        detailScrollRT.anchorMax = new Vector2(1, 1);
        detailScrollRT.pivot     = new Vector2(0.5f, 0.5f);
        detailScrollRT.offsetMin = new Vector2(0, BottomNavClearance);
        detailScrollRT.offsetMax = Vector2.zero;
        var detailScrollRect = detailScrollGo.AddComponent<ScrollRect>();
        detailScrollRect.horizontal = false;
        detailScrollRect.vertical = true;
        detailScrollRect.movementType = ScrollRect.MovementType.Elastic;
        detailScrollRect.scrollSensitivity = 30f;

        var detailViewport = NewUI("Viewport", detailScrollGo.transform);
        StretchFull(detailViewport);
        detailViewport.AddComponent<RectMask2D>();
        detailScrollRect.viewport = detailViewport.GetComponent<RectTransform>();

        var detailContent = NewUI("Content", detailViewport.transform);
        var detailContentRT = detailContent.GetComponent<RectTransform>();
        detailContentRT.anchorMin = new Vector2(0, 1);
        detailContentRT.anchorMax = new Vector2(1, 1);
        detailContentRT.pivot     = new Vector2(0.5f, 1);
        detailContentRT.anchoredPosition = Vector2.zero;
        detailContentRT.sizeDelta = Vector2.zero;
        var detailVlg = detailContent.AddComponent<VerticalLayoutGroup>();
        detailVlg.padding = new RectOffset((int)ScreenSidePadding, (int)ScreenSidePadding,
                                           (int)ScreenTopPadding, 24);
        detailVlg.spacing = 24f;
        detailVlg.childControlWidth = true;
        detailVlg.childControlHeight = true;
        detailVlg.childForceExpandWidth = true;
        detailVlg.childForceExpandHeight = false;
        detailVlg.childAlignment = TextAnchor.UpperCenter;
        var detailFitter = detailContent.AddComponent<ContentSizeFitter>();
        detailFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        detailFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        detailScrollRect.content = detailContentRT;

        // ViewerCard: fixed-height card sitting at the top of the scroll content.
        var viewerCard = NewUI("ViewerCard", detailContent.transform);
        viewerCard.AddComponent<RectMask2D>();
        var viewerCardBg = viewerCard.AddComponent<Image>();
        viewerCardBg.color = CardSurface;
        if (roundedMat != null) viewerCardBg.material = roundedMat;
        viewerCardBg.raycastTarget = false;
        AddLayoutElement(viewerCard, preferredHeight: ViewerCardHeight);

        // RawImage holding the RenderTexture — fills the card. AnimalViewerInput
        // lives on this RectTransform so drag/pinch are scoped to the viewer area.
        // Because AnimalViewerInput implements IDragHandler on a child of the
        // ScrollRect, drags inside the viewer rotate the model and never bubble
        // up to scroll the page; drags outside the viewer scroll normally.
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

        // InfoCard: VerticalLayoutGroup whose own preferred height (sum of
        // children) is read by the outer Content VLG — no ContentSizeFitter
        // needed here, the outer fitter on Content drives the scroll height.
        var infoCard = NewUI("InfoCard", detailContent.transform);
        var infoBg = infoCard.AddComponent<Image>();
        infoBg.color = CardSurface;
        if (roundedMat != null) infoBg.material = roundedMat;
        infoBg.raycastTarget = true;
        infoCard.AddComponent<RectMask2D>();
        var infoVlg = infoCard.AddComponent<VerticalLayoutGroup>();
        infoVlg.padding = new RectOffset(20, 20, 16, 24);
        infoVlg.spacing = 12f;
        infoVlg.childControlWidth = true;
        infoVlg.childControlHeight = true;
        infoVlg.childForceExpandWidth = true;
        infoVlg.childForceExpandHeight = false;

        var nameText = NewText("NameText", infoCard.transform, "Nome", 36, FontStyles.Bold, TextPrimary);
        AddLayoutElement(nameText.gameObject, preferredHeight: 48);
        nameText.alignment = TextAlignmentOptions.Left;

        var descText = NewText("DescriptionText", infoCard.transform, "Descrição",
            18, FontStyles.Normal, TextSecondary);
        descText.alignment = TextAlignmentOptions.TopLeft;
        descText.textWrappingMode = TextWrappingModes.Normal;

        var creditsText = NewText("CreditsText", infoCard.transform, string.Empty,
            13, FontStyles.Italic, TextMuted);
        creditsText.alignment = TextAlignmentOptions.TopLeft;
        creditsText.textWrappingMode = TextWrappingModes.Normal;

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
        so.FindProperty("detailScroll").objectReferenceValue = detailScrollRect;
        so.FindProperty("detailName").objectReferenceValue = nameText;
        so.FindProperty("detailDescription").objectReferenceValue = descText;
        so.FindProperty("detailCredits").objectReferenceValue = creditsText;
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
