using MergulhoVirtual.UI;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Composition root for the UI Toolkit Beaches screen (Phase 3 strangler
/// slice). Lives on the BeachesScreenUITK root GameObject next to a
/// UIDocument; ScreenManager toggles that GameObject like any other screen.
/// Builds the service adapters + ViewModel once, and rebuilds the visual tree
/// on every enable (UIDocument tears the tree down when deactivated).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class BeachesUiToolkitHost : MonoBehaviour
{
    [SerializeField] UIDocument document;
    [SerializeField] StyleSheet screenStyles;
    [SerializeField] ConditionsService conditionsService;
    [SerializeField] TideService tideService;
    [SerializeField] GPSHandler gpsHandler;
    [Tooltip("The uGUI BottomNav bar — the screen reserves its height so the nav stays visible and tappable.")]
    [SerializeField] RectTransform bottomNav;

    const float FreshnessRefreshInterval = 60f;

    BeachesViewModel viewModel;
    UiServiceAdapters.ConditionsServiceAdapter conditionsAdapter;
    UiServiceAdapters.TideServiceAdapter tideAdapter;
    BeachesScreen screen;
    float freshnessTimer;

    void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
        conditionsAdapter = new UiServiceAdapters.ConditionsServiceAdapter(conditionsService);
        tideAdapter = new UiServiceAdapters.TideServiceAdapter(tideService);
        viewModel = new BeachesViewModel(
            new UiServiceAdapters.BeachCatalogAdapter(),
            conditionsAdapter,
            tideAdapter,
            new UiServiceAdapters.BeachOverrideAdapter(gpsHandler));
    }

    void OnEnable()
    {
        var root = document != null ? document.rootVisualElement : null;
        if (root == null) return;

        root.Clear();
        if (screenStyles != null && !root.styleSheets.Contains(screenStyles))
            root.styleSheets.Add(screenStyles);

        // Match the legacy screen: re-entering the tab always starts at the list.
        viewModel.ShowList();

        screen = new BeachesScreen(viewModel, LoadBeachSprite);
        root.Add(screen);

        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    }

    void OnDisable()
    {
        var root = document != null ? document.rootVisualElement : null;
        if (root != null)
        {
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.Clear();
        }
        screen = null;
    }

    void OnDestroy()
    {
        viewModel?.Dispose();
        conditionsAdapter?.Dispose();
        tideAdapter?.Dispose();
    }

    void Update()
    {
        freshnessTimer += Time.unscaledDeltaTime;
        if (freshnessTimer >= FreshnessRefreshInterval)
        {
            freshnessTimer = 0f;
            viewModel?.NotifyTimePassed();
        }
    }

    void OnRootGeometryChanged(GeometryChangedEvent _) => ApplyEdgeInsets();

    /// <summary>
    /// Top/left/right insets come from Screen.safeArea (handled here instead of
    /// SafeAreaElement so the screen surface still paints under the status bar,
    /// and so the Device Simulator's mocked safeArea works in-editor too).
    /// Bottom reserves the uGUI BottomNav strip, measured from its rect, so the
    /// nav stays visible/tappable whichever of uGUI/UI Toolkit draws on top.
    /// </summary>
    void ApplyEdgeInsets()
    {
        if (screen == null || Screen.width <= 0 || Screen.height <= 0) return;
        var root = document != null ? document.rootVisualElement : null;
        if (root == null) return;
        float rootWidth = root.resolvedStyle.width;
        if (float.IsNaN(rootWidth) || rootWidth <= 0f) return;
        float panelUnitsPerPixel = rootWidth / Screen.width;

        Rect safe = Screen.safeArea;
        float topPx = Screen.height - safe.yMax;
        float leftPx = safe.xMin;
        float rightPx = Screen.width - safe.xMax;

        float navTopPx = 0f;
        if (bottomNav != null)
        {
            var canvas = bottomNav.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var corners = new Vector3[4];
            bottomNav.GetWorldCorners(corners);
            // corners[1]/[2] are the top edge; take the max Y in screen pixels.
            navTopPx = Mathf.Max(
                RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[1]).y,
                RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]).y);
        }
        float bottomPx = Mathf.Max(navTopPx, safe.yMin);

        screen.SetEdgeInsets(
            Mathf.Max(0f, topPx) * panelUnitsPerPixel,
            Mathf.Max(0f, leftPx) * panelUnitsPerPixel,
            Mathf.Max(0f, rightPx) * panelUnitsPerPixel,
            Mathf.Max(0f, bottomPx) * panelUnitsPerPixel);
    }

    static Sprite LoadBeachSprite(string imageName) =>
        string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Beaches/" + imageName);
}
