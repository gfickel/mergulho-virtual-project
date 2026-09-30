using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Scaffolds the UI Toolkit app shell (Phase 3 / Slice 1) into MainScene.
/// Supersedes BeachesUiScreenBuilder, which built a single-screen host: the
/// thing being wired is no longer "the Beaches screen" but the whole shell
/// (router + persistent navigation bar + every routable screen), so the builder
/// was renamed rather than extended — a menu item called "Create Beaches
/// Screen" would now lie about what it does.
///
/// It creates/refreshes:
///   • Assets/UI/AppPanelSettings.asset — Theme-Light (Decision D5), 1 USS px =
///     1 dp, and sortingOrder above the uGUI ScreenUI canvas;
///   • the scene-root "AppUI" GameObject (UIDocument + AppUiHost), wired to the
///     scene services and to the legacy uGUI screens the router still drives;
///   • ScreenManager's remaining fields (splash, shell, AR gate).
///
/// Idempotent — re-running rewires the same GameObject in place. Follow the
/// usual rule: iterate on the shell entirely in code (re-run the menu) OR
/// entirely in the Editor, never alternately.
///
/// Headless: Unity -batchmode -quit -executeMethod AppUiBuilder.BuildHeadless
/// (see `make ui-setup`).
/// </summary>
public static class AppUiBuilder
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";
    const string RootName = "AppUI";
    const string LegacyRootName = "BeachesScreenUITK";   // the pre-Slice-1 single-screen host
    /// <summary>
    /// The 3D turntable rig at the scene root, built by
    /// <see cref="AnimalViewerRigBuilder"/>. The Espécie screen renders it
    /// (Decision D1) — it used to belong to the uGUI AnimalsScreen, which Slice 6
    /// deleted; the rig outlived it.
    /// </summary>
    const string AnimalViewerRigName = "AnimalViewerRig";
    const string AppPanelSettingsPath = "Assets/UI/AppPanelSettings.asset";
    const string ThemePath = "Assets/DesignSystem/Theme-Light.tss";
    /// <summary>
    /// Every UI Toolkit screen's stylesheet, loaded onto the panel root by
    /// AppUiHost. A screen whose .uss is missing from this list still compiles
    /// and still builds its tree — it just renders completely unstyled, with no
    /// error anywhere. Add the sheet here in the same commit as the screen.
    /// </summary>
    static readonly string[] ScreenStylePaths =
    {
        "Assets/UI/Screens/HomeScreen.uss",
        "Assets/UI/Screens/MergulhoScreen.uss",
        "Assets/UI/Screens/PraiasScreen.uss",
        "Assets/UI/Screens/PraiaDetalheScreen.uss",
        "Assets/UI/Screens/ReportScreen.uss",
        "Assets/UI/Screens/EspecieScreen.uss",
        "Assets/UI/Screens/ArticlesScreen.uss",
        "Assets/UI/Screens/ArticleScreen.uss",
    };

    /// <summary>
    /// Panel draw order against the uGUI ScreenUI canvas (Screen Space - Overlay,
    /// sortingOrder 0). The shell — above all its navigation bar — must draw on
    /// top of the AR HUD canvas and stay tappable, so the panel is pushed well
    /// clear of it. Chosen on the PanelSettings side rather than by pushing the
    /// Canvas negative because every legacy screen shares that one Canvas, and a
    /// single generated asset this builder owns is easier to reason about.
    /// </summary>
    const float PanelSortingOrder = 100f;

    /// <summary>
    /// Children of ScreenUI/MainScreen that Slice 4 superseded and that must not
    /// draw under the new UI Toolkit AR HUD.
    ///
    /// <para><b>TopBar is the whole of it, and it is a deliberate loss.</b> It
    /// holds the uGUI beach-name label — replaced by the hero selector pill on
    /// MergulhoScreen — and ConditionsPillView, the wave/moon/tide pill that
    /// navigated to Praias. Tela 8 has no conditions pill, the legacy bar is
    /// anchored 16dp from the top with no safe-area awareness and so overlaps the
    /// new control strip on any notched phone, and the pill's content is the same
    /// five rows the Início conditions card already shows.</para>
    ///
    /// <para>TopBar used to be only PART of MainScreen: the rest of the subtree was
    /// ArTuning, the on-beach AR tuning panel, which is why the GameObject stayed
    /// live on the Mergulho route. <b>ArTuning was deleted on 2026-09-29</b>, so
    /// TopBar is now the whole of MainScreen and this deactivation empties it
    /// completely — the GameObject, its route toggle in AppUiHost.OnRouteChanged and
    /// this array are all ready to be deleted together.</para>
    /// </summary>
    static readonly string[] LegacyArOverlayChildrenToHide = { "TopBar" };

    [MenuItem("Tools/Mergulho Virtual/Create App UI Shell (UI Toolkit)", priority = 120)]
    public static void Build()
    {
        EnsureMainSceneOpen();
        BuildIntoOpenScene();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[AppUiBuilder] Done — " + RootName + " shell wired; router owns navigation.");
    }

    /// <summary>Batchmode entry point (opens MainScene explicitly).</summary>
    public static void BuildHeadless() => Build();

    static void EnsureMainSceneOpen()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }

    static void BuildIntoOpenScene()
    {
        var panelSettings = CreateOrUpdateAppPanelSettings();
        var screenStyles = LoadScreenStyles();

        // The pre-Slice-1 host lived on its own root GameObject that ScreenManager
        // toggled. The shell is always-on now, so that object is obsolete.
        var legacyRoot = FindSceneRoot(LegacyRootName);
        if (legacyRoot != null)
        {
            Debug.Log("[AppUiBuilder] Removing the obsolete " + LegacyRootName + " host root.");
            Undo.DestroyObjectImmediate(legacyRoot);
        }

        // GameObject.Find skips inactive objects; search scene roots instead.
        GameObject root = FindSceneRoot(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);
        }

        var document = root.GetComponent<UIDocument>();
        if (document == null) document = root.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        var host = root.GetComponent<AppUiHost>();
        if (host == null) host = root.AddComponent<AppUiHost>();

        var conditions = Object.FindFirstObjectByType<ConditionsService>(FindObjectsInactive.Include);
        var tides = Object.FindFirstObjectByType<TideService>(FindObjectsInactive.Include);
        var gps = Object.FindFirstObjectByType<GPSHandler>(FindObjectsInactive.Include);
        var screenManager = Object.FindFirstObjectByType<ScreenManager>(FindObjectsInactive.Include);
        var arSelection = EnsureArSelection();
        // The 3D turntable the Espécie screen renders (Decision D1). It is a scene
        // ROOT, not a child of ScreenUI, and it is normally INACTIVE — so it has to be
        // found through the scene's root list, the documented GameObject.Find footgun
        // (CLAUDE.md, "Idempotency footgun"). Finding it by name rather than by
        // component because the rig carries no script of its own: it is plain
        // GameObjects (AnimalViewerRigBuilder), driven from the app side by
        // UiServiceAdapters.SpeciesModelViewerAdapter.
        var animalViewerRig = FindSceneRoot(AnimalViewerRigName);
        var mainScreen = FindScreenUiChild("MainScreen");
        var aboutScreen = FindScreenUiChild("AboutScreen");
        var splashScreen = FindScreenUiChild("SplashScreen");

        if (conditions == null) Debug.LogWarning("[AppUiBuilder] No ConditionsService in scene — conditions rows will stay '—'.");
        if (tides == null) Debug.LogWarning("[AppUiBuilder] No TideService in scene — tide row/sparkline will stay empty.");
        if (gps == null) Debug.LogWarning("[AppUiBuilder] No GPSHandler in scene — beach override dropdown will be inert.");
        // Not an error any more: since ArTuning was removed (2026-09-29) MainScreen
        // holds nothing live, so a scene without it is a scene that has already
        // retired it. Logged at info level only so the wiring below stays traceable.
        if (mainScreen == null) Debug.Log("[AppUiBuilder] ScreenUI/MainScreen not found — nothing to do; it has been an empty shell since ArTuning was removed.");
        if (aboutScreen == null) Debug.LogWarning("[AppUiBuilder] ScreenUI/AboutScreen not found — the Início \"Sobre o projeto\" entry will be unroutable.");
        if (animalViewerRig == null) Debug.LogWarning("[AppUiBuilder] No " + AnimalViewerRigName + " scene root — the Espécie screen will have no 3D section. Run Tools > Mergulho Virtual > Create Animal Viewer Rig to build it.");

        var hostSo = new SerializedObject(host);
        hostSo.FindProperty("document").objectReferenceValue = document;
        SetObjectArray(hostSo.FindProperty("screenStyles"), screenStyles);
        hostSo.FindProperty("conditionsService").objectReferenceValue = conditions;
        hostSo.FindProperty("tideService").objectReferenceValue = tides;
        hostSo.FindProperty("gpsHandler").objectReferenceValue = gps;
        hostSo.FindProperty("legacyArOverlay").objectReferenceValue = mainScreen;
        hostSo.FindProperty("arSelection").objectReferenceValue = arSelection;
        hostSo.FindProperty("animalViewerRig").objectReferenceValue = animalViewerRig;
        hostSo.FindProperty("legacyAboutScreen").objectReferenceValue = aboutScreen;
        hostSo.FindProperty("screenManager").objectReferenceValue = screenManager;
        hostSo.ApplyModifiedPropertiesWithoutUndo();

        // The AR tap guard needs the shell's panel so a tap the UI consumed never
        // also fires a raycast into the beach. ObjectInteraction leaves the field
        // optional and falls back to FindAnyObjectByType<UIDocument>() on the first
        // tap, which works but searches the scene and picks an arbitrary document if
        // a second one is ever added. The builder already owns both objects, so it
        // wires them — same reason every other serialized reference is set here.
        if (arSelection != null)
        {
            var arSo = new SerializedObject(arSelection);
            SetIfPresent(arSo, "uiPanelSource", document);
            arSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // The shell is the app now: always active, hidden during splash by
        // ScreenManager toggling the panel root's display (not the GameObject,
        // so the visual tree is never torn down and rebuilt mid-session).
        root.SetActive(true);

        if (screenManager != null)
        {
            var smSo = new SerializedObject(screenManager);
            SetIfPresent(smSo, "splashScreen", splashScreen);
            SetIfPresent(smSo, "appUi", host);
            smSo.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[AppUiBuilder] No ScreenManager in scene — the AR performance gate will not follow navigation.");
        }

        // Slice 6 deleted the uGUI screens nothing routed to — BottomNav (replaced by
        // MdNavigationBar), BeachesScreen, RegisterScreen and AnimalsScreen — so there
        // is no longer a list of screens to hold deactivated here. ScreenUI keeps only
        // Panel (debug overlay), SplashScreen, MainScreen (now an empty shell — its
        // ArTuning panel was removed 2026-09-29) and AboutScreen (Sobre, Decision D2).

        // Slice 4 replaced MainScreen's TopBar with the UI Toolkit MergulhoScreen.
        if (mainScreen != null)
        {
            foreach (var name in LegacyArOverlayChildrenToHide)
            {
                var child = mainScreen.transform.Find(name);
                if (child != null && child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    EditorUtility.SetDirty(child.gameObject);
                }
            }
        }

        // AboutScreen is activated by LegacyUguiScreen via the router and
        // MainScreen by AppUiHost.OnRouteChanged; start both hidden so the first
        // frame shows only one screen.
        foreach (var go in new[] { mainScreen, aboutScreen })
        {
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
                EditorUtility.SetDirty(go);
            }
        }
        if (splashScreen != null && !splashScreen.activeSelf)
        {
            splashScreen.SetActive(true);
            EditorUtility.SetDirty(splashScreen);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// <summary>
    /// The AR tap source the Mergulho species card listens to. It had never been
    /// in the scene at all — <c>ObjectInteraction</c> shipped as an orphan script
    /// with a hardcoded target name that matched nothing — so the builder puts it
    /// there, on the GameObject that already owns the animals it raycasts against
    /// rather than on a new empty wrapper. It idles (<c>Listening = false</c>)
    /// until MergulhoScreen's OnEnter switches it on.
    /// </summary>
    static ObjectInteraction EnsureArSelection()
    {
        var existing = Object.FindFirstObjectByType<ObjectInteraction>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var host = Object.FindFirstObjectByType<BeachSharkSpawner>(FindObjectsInactive.Include);
        if (host == null)
        {
            Debug.LogWarning("[AppUiBuilder] No BeachSharkSpawner in scene — tapping an animal in AR will do nothing.");
            return null;
        }

        var added = Undo.AddComponent<ObjectInteraction>(host.gameObject);
        EditorUtility.SetDirty(host.gameObject);
        Debug.Log("[AppUiBuilder] Added ObjectInteraction to " + host.gameObject.name + " (AR tap source).");
        return added;
    }

    static StyleSheet[] LoadScreenStyles()
    {
        var sheets = new List<StyleSheet>();
        foreach (var path in ScreenStylePaths)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet == null)
            {
                throw new System.InvalidOperationException(
                    $"StyleSheet not found at {path} — did the UI folder import?");
            }
            sheets.Add(sheet);
        }
        return sheets.ToArray();
    }

    static PanelSettings CreateOrUpdateAppPanelSettings()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (theme == null)
        {
            throw new System.InvalidOperationException(
                $"Theme not found at {ThemePath} — run `make ds-setup` / check the DesignSystem folder.");
        }

        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AppPanelSettingsPath);
        bool created = settings == null;
        if (created) settings = ScriptableObject.CreateInstance<PanelSettings>();

        // Same dp calibration as the DesignSystem PanelSettings (1 USS px = 1 dp);
        // separate asset so the app can ship Light (Decision D5) while the gallery
        // keeps its own default and its light/dark toggle.
        settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
        settings.referenceDpi = 160;

        // fallbackDpi is ONLY used when Screen.dpi comes back 0 or invalid, which
        // Unity's own docs say can happen on Android ("returns 0 if the device /
        // platform does not provide DPI information"). It must NOT mirror
        // referenceDpi: at 160 the fallback scale is 1, so the panel would be as
        // many USS units wide as the device has PHYSICAL PIXELS — 1080 units on a
        // 1080p phone, i.e. the whole UI at about a third size, on that device only.
        // 440 is a standard xxhdpi Android density and maps a 1080px-wide screen to
        // 1080 * 160 / 440 = 393 USS units, essentially the 390dp reference frame.
        // So the degenerate case degrades to "very slightly wrong" instead of
        // "unusable", with no effect whatsoever when Screen.dpi is valid.
        settings.fallbackDpi = 440;
        settings.themeStyleSheet = theme;
        settings.sortingOrder = PanelSortingOrder;

        if (created) AssetDatabase.CreateAsset(settings, AppPanelSettingsPath);
        else EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static void SetObjectArray(SerializedProperty array, Object[] values)
    {
        if (array == null) return;
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    static void SetIfPresent(SerializedObject so, string propertyName, Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            // Names the actual target: this is called for ScreenManager AND for
            // ObjectInteraction, and a warning naming the wrong one sends whoever
            // reads it to the wrong file.
            var owner = so.targetObject != null ? so.targetObject.GetType().Name : "target";
            Debug.LogWarning($"[AppUiBuilder] {owner} has no '{propertyName}' field — recompile, then re-run.");
            return;
        }
        prop.objectReferenceValue = value;
    }

    static GameObject FindSceneRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == name) return go;
        }
        return null;
    }

    static GameObject FindScreenUiChild(string name)
    {
        var screenUi = FindSceneRoot("ScreenUI");
        if (screenUi == null) return null;
        var child = screenUi.transform.Find(name);
        return child != null ? child.gameObject : null;
    }
}
