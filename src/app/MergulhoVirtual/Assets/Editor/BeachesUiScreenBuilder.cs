using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Scaffolds the UI Toolkit Beaches screen (Phase 3 strangler slice) into
/// MainScene: creates/refreshes Assets/UI/AppPanelSettings.asset, the
/// BeachesScreenUITK root GameObject (UIDocument + BeachesUiToolkitHost, wired
/// to the scene services), and points ScreenManager.beachesScreenUiToolkit at
/// it. Idempotent — re-running rewires the same GameObject in place.
///
/// Headless: Unity -batchmode -quit -executeMethod BeachesUiScreenBuilder.BuildHeadless
/// (see `make ui-beaches-setup`).
/// </summary>
public static class BeachesUiScreenBuilder
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";
    const string RootName = "BeachesScreenUITK";
    const string AppPanelSettingsPath = "Assets/UI/AppPanelSettings.asset";
    const string ThemePath = "Assets/DesignSystem/Theme-Dark.tss";
    const string ScreenStylesPath = "Assets/UI/Screens/BeachesScreen.uss";

    [MenuItem("Tools/Mergulho Virtual/Create Beaches Screen (UI Toolkit)", priority = 120)]
    public static void Build()
    {
        EnsureMainSceneOpen();
        BuildIntoOpenScene();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[BeachesUiScreenBuilder] Done — " + RootName + " wired into ScreenManager.");
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

        var screenStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(ScreenStylesPath);
        if (screenStyles == null)
        {
            throw new System.InvalidOperationException(
                $"StyleSheet not found at {ScreenStylesPath} — did the UI folder import?");
        }

        // GameObject.Find skips inactive objects; search scene roots instead
        // (the screen is inactive whenever another tab is showing).
        GameObject root = FindSceneRoot(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);
        }

        var document = root.GetComponent<UIDocument>();
        if (document == null) document = root.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        var host = root.GetComponent<BeachesUiToolkitHost>();
        if (host == null) host = root.AddComponent<BeachesUiToolkitHost>();

        var conditions = Object.FindFirstObjectByType<ConditionsService>(FindObjectsInactive.Include);
        var tides = Object.FindFirstObjectByType<TideService>(FindObjectsInactive.Include);
        var gps = Object.FindFirstObjectByType<GPSHandler>(FindObjectsInactive.Include);
        var bottomNav = FindBottomNav();

        if (conditions == null) Debug.LogWarning("[BeachesUiScreenBuilder] No ConditionsService in scene — conditions rows will stay '—'.");
        if (tides == null) Debug.LogWarning("[BeachesUiScreenBuilder] No TideService in scene — tide row/sparkline will stay empty.");
        if (gps == null) Debug.LogWarning("[BeachesUiScreenBuilder] No GPSHandler in scene — beach override dropdown will be inert.");
        if (bottomNav == null) Debug.LogWarning("[BeachesUiScreenBuilder] ScreenUI/BottomNav not found — no bottom inset reserved.");

        var hostSo = new SerializedObject(host);
        hostSo.FindProperty("document").objectReferenceValue = document;
        hostSo.FindProperty("screenStyles").objectReferenceValue = screenStyles;
        hostSo.FindProperty("conditionsService").objectReferenceValue = conditions;
        hostSo.FindProperty("tideService").objectReferenceValue = tides;
        hostSo.FindProperty("gpsHandler").objectReferenceValue = gps;
        hostSo.FindProperty("bottomNav").objectReferenceValue = bottomNav;
        hostSo.ApplyModifiedPropertiesWithoutUndo();

        // ScreenManager owns activation — start inactive like every other screen.
        root.SetActive(false);

        var screenManager = Object.FindFirstObjectByType<ScreenManager>(FindObjectsInactive.Include);
        if (screenManager != null)
        {
            var smSo = new SerializedObject(screenManager);
            smSo.FindProperty("beachesScreenUiToolkit").objectReferenceValue = root;
            smSo.FindProperty("useUiToolkitBeaches").boolValue = true;
            smSo.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[BeachesUiScreenBuilder] No ScreenManager in scene — wire beachesScreenUiToolkit manually.");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
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
        // separate asset so the app theme (dark) never fights the gallery default.
        settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
        settings.referenceDpi = 160;
        settings.fallbackDpi = 160;
        settings.themeStyleSheet = theme;

        if (created) AssetDatabase.CreateAsset(settings, AppPanelSettingsPath);
        else EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static GameObject FindSceneRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == name) return go;
        }
        return null;
    }

    static RectTransform FindBottomNav()
    {
        var screenUi = FindSceneRoot("ScreenUI");
        if (screenUi == null) return null;
        var nav = screenUi.transform.Find("BottomNav");
        return nav as RectTransform;
    }
}
