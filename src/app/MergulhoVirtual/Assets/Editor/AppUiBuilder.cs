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
///   • ScreenManager's remaining fields (splash, shell, AR gate);
///   • deactivation of the uGUI BottomNav and the screens nothing routes to.
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
        "Assets/UI/Screens/PraiasScreen.uss",
        "Assets/UI/Screens/PraiaDetalheScreen.uss",
        "Assets/UI/Screens/ReportScreen.uss",
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
    /// Legacy uGUI objects the router does not drive at all: the BottomNav that
    /// MdNavigationBar replaced, the uGUI Beaches screen the UITK one superseded,
    /// RegisterScreen (superseded by the UITK ReportScreen in Slice 3), and
    /// Animais, which lost its destination with no home yet (Decision D1).
    /// AboutScreen is NOT here — it lost its tab too, but Decision D2 makes it a
    /// sub-screen pushed from Início, so the router owns its activation.
    ///
    /// <para>They all STAY in the scene (the strangler rule — Slice 6 deletes
    /// them); this list only guarantees they start, and stay, deactivated, since
    /// nothing will ever activate them again.</para>
    /// </summary>
    static readonly string[] UnroutedLegacyScreens =
        { "BottomNav", "BeachesScreen", "AnimalsScreen", "RegisterScreen" };

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
        var mainScreen = FindScreenUiChild("MainScreen");
        var aboutScreen = FindScreenUiChild("AboutScreen");
        var splashScreen = FindScreenUiChild("SplashScreen");

        if (conditions == null) Debug.LogWarning("[AppUiBuilder] No ConditionsService in scene — conditions rows will stay '—'.");
        if (tides == null) Debug.LogWarning("[AppUiBuilder] No TideService in scene — tide row/sparkline will stay empty.");
        if (gps == null) Debug.LogWarning("[AppUiBuilder] No GPSHandler in scene — beach override dropdown will be inert.");
        if (mainScreen == null) Debug.LogWarning("[AppUiBuilder] ScreenUI/MainScreen not found — the Mergulho (AR) destination will be unroutable.");
        if (aboutScreen == null) Debug.LogWarning("[AppUiBuilder] ScreenUI/AboutScreen not found — the Início \"Sobre o projeto\" entry will be unroutable.");

        var hostSo = new SerializedObject(host);
        hostSo.FindProperty("document").objectReferenceValue = document;
        SetObjectArray(hostSo.FindProperty("screenStyles"), screenStyles);
        hostSo.FindProperty("conditionsService").objectReferenceValue = conditions;
        hostSo.FindProperty("tideService").objectReferenceValue = tides;
        hostSo.FindProperty("gpsHandler").objectReferenceValue = gps;
        hostSo.FindProperty("legacyArScreen").objectReferenceValue = mainScreen;
        hostSo.FindProperty("legacyAboutScreen").objectReferenceValue = aboutScreen;
        hostSo.FindProperty("screenManager").objectReferenceValue = screenManager;
        hostSo.ApplyModifiedPropertiesWithoutUndo();

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

        // The uGUI BottomNav is replaced by MdNavigationBar; Animais/Sobre lost
        // their tab (Decisions D1/D2); the legacy uGUI Beaches screen and the uGUI
        // RegisterScreen are superseded by their UI Toolkit replacements. All stay
        // in the scene for the strangler, just inactive.
        foreach (var name in UnroutedLegacyScreens)
        {
            var go = FindScreenUiChild(name);
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
                EditorUtility.SetDirty(go);
            }
        }

        // MainScreen/AboutScreen are activated by LegacyUguiScreen via the router;
        // start them hidden so the first frame shows only one screen.
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
        settings.fallbackDpi = 160;
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
            Debug.LogWarning($"[AppUiBuilder] ScreenManager has no '{propertyName}' field — recompile, then re-run.");
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
