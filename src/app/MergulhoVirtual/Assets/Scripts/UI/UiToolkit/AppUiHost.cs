using System;
using MergulhoVirtual.UI;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// App-wide composition root for the UI Toolkit shell (Phase 3 / Slice 1).
/// Generalizes the former BeachesUiToolkitHost: one GameObject, one UIDocument,
/// one <see cref="MdRouter"/> that owns every routable screen and the persistent
/// bottom navigation bar.
///
/// <para>Responsibilities, in order: construct the service adapters and
/// ViewModels (Awake, once — they survive every shell toggle), build the visual
/// tree and register the screens (OnEnable — UIDocument tears the tree down on
/// deactivate, so it must be rebuildable), apply safe-area insets, and report
/// the active route to <see cref="ScreenManager"/> so the AR performance gate
/// follows navigation.</para>
///
/// <para>Draw order: the panel is put ABOVE the uGUI ScreenUI canvas via
/// PanelSettings.sortingOrder (see AppUiBuilder). The router root and screen
/// container are <see cref="PickingMode.Ignore"/>, so wherever the shell paints
/// nothing — which is the whole screen while a LegacyUguiScreen is active —
/// taps fall through to that canvas.</para>
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AppUiHost : MonoBehaviour
{
    [SerializeField] UIDocument document;
    [Tooltip("Screen stylesheets loaded onto the panel root. Each UI Toolkit screen contributes its own .uss here.")]
    [SerializeField] StyleSheet[] screenStyles;

    [Header("Services")]
    [SerializeField] ConditionsService conditionsService;
    [SerializeField] TideService tideService;
    [SerializeField] GPSHandler gpsHandler;

    // ScreenUI/RegisterScreen is NOT here any more: Slice 3 replaced it with the
    // UI Toolkit ReportScreen, so nothing routes to it. The GameObject stays in
    // the scene until Slice 6 (the strangler rule) — AppUiBuilder just leaves it
    // deactivated with the other unrouted legacy screens.
    [Header("Legacy uGUI screens (strangler — replaced in Slice 4 / 6)")]
    [Tooltip("ScreenUI/MainScreen — the AR HUD, routed as AppRoutes.Mergulho.")]
    [SerializeField] GameObject legacyArScreen;
    [Tooltip("ScreenUI/AboutScreen — Sobre + the Instagram widget. No tab in V2 (Decision D2); pushed from the Início grid as AppRoutes.Sobre.")]
    [SerializeField] GameObject legacyAboutScreen;

    [Header("Routing")]
    [Tooltip("Receives the active route so the AR session / frame-rate gate follows navigation.")]
    [SerializeField] ScreenManager screenManager;
    [Tooltip("Route shown on startup. Falls back to the first registered tab when this route has no screen yet.")]
    [SerializeField] string initialRoute = AppRoutes.Home;

    const float FreshnessRefreshInterval = 60f;

    /// <summary>Single instance; lets non-UI components navigate without a serialized reference.</summary>
    public static AppUiHost Instance { get; private set; }

    BeachesViewModel beachesViewModel;
    BeachDetailViewModel beachDetailViewModel;
    PraiasViewModel praiasViewModel;
    HomeViewModel homeViewModel;
    ReportViewModel reportViewModel;
    UiServiceAdapters.ConditionsServiceAdapter conditionsAdapter;
    UiServiceAdapters.TideServiceAdapter tideAdapter;
    UiServiceAdapters.ActiveBeachAdapter activeBeachAdapter;
    UiServiceAdapters.SightingReportsAdapter sightingReportsAdapter;
    MdRouter router;
    float freshnessTimer;

    /// <summary>The live router, or null before the first OnEnable / after OnDisable.</summary>
    public MdRouter Router => router;

    /// <summary>
    /// Per-beach state for the Praia detalhe screen (Slice 2). Constructed here
    /// with the rest of the ViewModels so it outlives every shell rebuild, and
    /// exposed so the screen can be registered against it.
    ///
    /// <para>There is exactly ONE instance, shared by the Praias landing and the
    /// Praia detalhe screen, and <see cref="PraiasViewModel"/> keeps it pointed
    /// at the active beach — so the two screens can never disagree about which
    /// beach is open.</para>
    /// </summary>
    public BeachDetailViewModel BeachDetail => beachDetailViewModel;

    /// <summary>The Praias slice's ViewModel: the landing's card and the beach
    /// selector both Praias screens share.</summary>
    public PraiasViewModel Praias => praiasViewModel;

    // ---- Static navigation facade (used by legacy uGUI components) ----------

    /// <summary>Navigate to a bottom-bar route. False if the shell or the route is not available.</summary>
    public static bool NavigateTo(string route) => Instance?.router?.Navigate(route) ?? false;

    /// <summary>Push a sub-screen route. False if the shell or the route is not available.</summary>
    public static bool PushTo(string route) => Instance?.router?.Push(route) ?? false;

    /// <summary>Pop the sub-screen back stack. False when there is nothing to pop.</summary>
    public static bool GoBack() => Instance?.router?.Back() ?? false;

    // ---- Lifecycle ----------------------------------------------------------

    void Awake()
    {
        Instance = this;
        if (document == null) document = GetComponent<UIDocument>();

        conditionsAdapter = new UiServiceAdapters.ConditionsServiceAdapter(conditionsService);
        tideAdapter = new UiServiceAdapters.TideServiceAdapter(tideService);
        beachesViewModel = new BeachesViewModel(
            new UiServiceAdapters.BeachCatalogAdapter(),
            conditionsAdapter,
            tideAdapter,
            new UiServiceAdapters.BeachOverrideAdapter(gpsHandler));
        // Shares the two service adapters with Beaches — both screens render the
        // same five condition rows, so a second subscription would be a second
        // copy of the same snapshot, not a second source.
        homeViewModel = new HomeViewModel(
            conditionsAdapter,
            tideAdapter,
            new UiServiceAdapters.OnboardingStateAdapter());
        // Praia detalhe (Slice 2). Its own dependencies are the hand-authored
        // content file and the species catalog, which no other screen reads; it
        // shares the tide adapter for the live "Maré Ideal" caption.
        beachDetailViewModel = new BeachDetailViewModel(
            new UiServiceAdapters.BeachCatalogAdapter(),
            new UiServiceAdapters.BeachContentAdapter(),
            new UiServiceAdapters.SpeciesCatalogAdapter(),
            tideAdapter);
        // The Praias slice: keeps the detail ViewModel on the beach GPS (or the
        // manual override) reports, and owns the selector both Praias screens
        // open. It borrows BeachesViewModel for the override choices — the list
        // screen those once belonged to is gone, the "Automático (GPS)" entry
        // and its key mapping are not.
        activeBeachAdapter = new UiServiceAdapters.ActiveBeachAdapter(gpsHandler);
        praiasViewModel = new PraiasViewModel(beachDetailViewModel, beachesViewModel, activeBeachAdapter);
        // Reportar (Slice 3). Constructing the reports adapter is what spins up the
        // JobQueue and loads what is on disk, so a sighting left queued by a previous
        // run resumes AND shows up in the feed — it must happen here, in Awake, not on
        // the first visit to the tab. It shares the active-beach adapter with Praias:
        // the report is filed under the beach the app says you are at, and a second
        // subscription would be a second copy of the same state, not a second source.
        sightingReportsAdapter = new UiServiceAdapters.SightingReportsAdapter();
        reportViewModel = new ReportViewModel(
            new UiServiceAdapters.SpeciesCatalogAdapter(),
            sightingReportsAdapter,
            new UiServiceAdapters.GalleryPhotoPickerAdapter(),
            activeBeachAdapter,
            new UiServiceAdapters.BeachCatalogAdapter());
    }

    void OnEnable()
    {
        var root = document != null ? document.rootVisualElement : null;
        if (root == null) return;

        root.Clear();
        // Nothing on the panel root itself is a tap target; only the screens and
        // the nav bar are. Required for the AR HUD underneath to stay tappable.
        root.pickingMode = PickingMode.Ignore;

        if (screenStyles != null)
        {
            foreach (var sheet in screenStyles)
            {
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
        }

        router = new MdRouter();
        router.SetTabs(AppTabs.Default);
        RegisterScreens(router);
        router.RouteChanged += OnRouteChanged;
        root.Add(router);

        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        NavigateToInitialRoute();
    }

    void OnDisable()
    {
        if (router != null) router.RouteChanged -= OnRouteChanged;
        router = null;

        var root = document != null ? document.rootVisualElement : null;
        if (root != null)
        {
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.Clear();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        praiasViewModel?.Dispose();
        beachesViewModel?.Dispose();
        beachDetailViewModel?.Dispose();
        homeViewModel?.Dispose();
        // Before the adapter it subscribes to.
        reportViewModel?.Dispose();
        conditionsAdapter?.Dispose();
        tideAdapter?.Dispose();
        activeBeachAdapter?.Dispose();
        sightingReportsAdapter?.Dispose();
    }

    void Update()
    {
        freshnessTimer += Time.unscaledDeltaTime;
        if (freshnessTimer >= FreshnessRefreshInterval)
        {
            freshnessTimer = 0f;
            beachesViewModel?.NotifyTimePassed();
            beachDetailViewModel?.NotifyTimePassed();
            homeViewModel?.NotifyTimePassed();
        }
    }

    // ---- Screen registration ------------------------------------------------

    /// <summary>
    /// Every routable screen in the app, constructed here and handed to the
    /// router. Screens are built once per shell rebuild and kept alive; per-visit
    /// work lives in their OnEnter/OnExit.
    /// </summary>
    void RegisterScreens(MdRouter r)
    {
        // Início — the landing route. The screen is router-agnostic by design: it
        // raises AppRoutes keys and this host decides Navigate vs Push, so the
        // screen stays unit-testable without a router. Subscriptions need no
        // teardown here — the screen is discarded with the rest of the tree on
        // the next OnEnable rebuild.
        var home = new HomeScreen(homeViewModel);
        home.NavigationRequested += OnScreenNavigationRequested;
        home.TideTableRequested += OnTideTableRequested;
        r.Register(home);

        // Mergulho — the AR HUD, still the legacy uGUI screen (Slice 4 replaces it).
        if (legacyArScreen != null)
            r.Register(new LegacyUguiScreen(AppRoutes.Mergulho, legacyArScreen));

        // Praias — the tab root (Tela 1). It replaced the browsable beach list,
        // so the beach page is reached by pushing PraiaDetalhe from here and the
        // hero selector on that screen is how any OTHER beach is reached.
        var praias = new PraiasScreen(praiasViewModel, LoadBeachSprite);
        praias.NavigationRequested += OnScreenNavigationRequested;
        praias.DetailRequested += () => router?.Push(AppRoutes.PraiaDetalhe);
        r.Register(praias);

        // Praia detalhe (Tela 4 + Tela 9) — a sub-screen: the bar stays visible
        // with Praias selected, and the hero's back button pops the stack.
        var praiaDetalhe = new PraiaDetalheScreen(praiasViewModel, LoadBeachSprite, LoadSpeciesSprite);
        praiaDetalhe.NavigationRequested += OnScreenNavigationRequested;
        praiaDetalhe.BackRequested += () => router?.Back();
        r.Register(praiaDetalhe);

        // Avistamentos (Tela 11 + Tela 12) — the tab root, replacing the uGUI
        // RegisterScreen. It leaves for Início after a submit the queue took, which
        // arrives here as an ordinary route key; BackRequested is wired for the day
        // the same screen is pushed rather than tabbed to (its back button is hidden
        // on a tab root — see ReportScreen.ShowBackButton).
        var report = new ReportScreen(reportViewModel);
        report.NavigationRequested += OnScreenNavigationRequested;
        report.BackRequested += () => router?.Back();
        r.Register(report);

        // Sobre — no bottom-bar destination in V2 (Decision D2), so it is a
        // sub-screen pushed from the Início grid. Still the legacy uGUI About
        // screen, which is what keeps the shipped Instagram widget reachable.
        // It has no back button of its own: the nav bar stays visible over a
        // pushed screen and is how the user leaves it.
        if (legacyAboutScreen != null)
            r.Register(new LegacyUguiScreen(AppRoutes.Sobre, legacyAboutScreen));
    }

    /// <summary>
    /// A screen raises an <see cref="AppRoutes"/> key; the host decides how to
    /// get there. A bottom-bar destination replaces the stack (<c>Navigate</c>);
    /// anything else is a sub-screen (<c>Push</c>), which keeps the bar visible
    /// with the originating tab still selected.
    ///
    /// <para>A key with no registered screen — <see cref="AppRoutes.Sos"/> until
    /// Slice 5, <see cref="AppRoutes.Especie"/> until Animais finds its home
    /// (Decision D1) — is a deliberate no-op: <see cref="MdRouter"/> logs one
    /// warning, returns false, and leaves both the visible screen and the bar's
    /// selection untouched. The SOS tile and the floating SOS pill therefore do
    /// nothing at all today rather than opening a blank screen.</para>
    /// </summary>
    void OnScreenNavigationRequested(string route)
    {
        if (router == null || string.IsNullOrEmpty(route)) return;

        if (Array.IndexOf(AppRoutes.Tabs, route) >= 0) router.Navigate(route);
        else router.Push(route);
    }

    /// <summary>
    /// "Baixar a tábua de maré do mês" on the Início conditions card. Deliberately
    /// a no-op: the DHN tide table is a PDF the project parses offline
    /// (tools/parse_dhn_tide_table.py) and publishes nowhere — there is no URL and
    /// no backend endpoint to open, and inventing one would ship a dead link.
    /// Subscribed rather than left dangling so the gap is visible at the
    /// composition root, which is where the decision belongs.
    ///
    /// <para>TODO: decide with the project either to host the PDF (a public object
    /// in the `conteudos-educacionais` bucket + Application.OpenURL, same pattern
    /// as the educational videos) or to drop the link from the card. Until then a
    /// tap logs this line and changes nothing on screen.</para>
    /// </summary>
    void OnTideTableRequested() =>
        Debug.Log("[AppUiHost] Tide-table link tapped — no published DHN PDF to open yet.");

    void NavigateToInitialRoute()
    {
        if (!string.IsNullOrEmpty(initialRoute) && router.IsRegistered(initialRoute))
        {
            router.Navigate(initialRoute);
            return;
        }

        foreach (var tab in router.Tabs)
        {
            if (router.IsRegistered(tab.Route))
            {
                router.Navigate(tab.Route);
                return;
            }
        }
        Debug.LogWarning("[AppUiHost] No screen registered for any bottom-bar destination — the shell will be empty.");
    }

    void OnRouteChanged(string route)
    {
        if (screenManager != null) screenManager.SetRoute(route);
    }

    // ---- Shell visibility ---------------------------------------------------

    /// <summary>
    /// Hides/shows the whole shell without tearing the visual tree down — used by
    /// <see cref="ScreenManager"/> so the legacy uGUI splash owns the screen on
    /// its own, then hands over.
    /// </summary>
    public void SetShellVisible(bool visible)
    {
        var root = document != null ? document.rootVisualElement : null;
        if (root == null) return;
        root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ---- Insets -------------------------------------------------------------

    void OnRootGeometryChanged(GeometryChangedEvent _) => ApplyEdgeInsets();

    /// <summary>
    /// Safe-area insets in panel units. Applied by the host rather than by
    /// SafeAreaElement so screen surfaces still paint under the status bar/notch
    /// (SafeAreaElement would leave that strip transparent → raw camera feed),
    /// and so the Device Simulator's mocked Screen.safeArea works in-editor.
    /// The router decides who consumes each edge.
    /// </summary>
    void ApplyEdgeInsets()
    {
        if (router == null || Screen.width <= 0 || Screen.height <= 0) return;
        var root = document != null ? document.rootVisualElement : null;
        if (root == null) return;
        float rootWidth = root.resolvedStyle.width;
        if (float.IsNaN(rootWidth) || rootWidth <= 0f) return;
        float panelUnitsPerPixel = rootWidth / Screen.width;

        Rect safe = Screen.safeArea;
        float topPx = Screen.height - safe.yMax;
        float leftPx = safe.xMin;
        float rightPx = Screen.width - safe.xMax;
        float bottomPx = safe.yMin;

        router.SetEdgeInsets(
            Mathf.Max(0f, topPx) * panelUnitsPerPixel,
            Mathf.Max(0f, leftPx) * panelUnitsPerPixel,
            Mathf.Max(0f, rightPx) * panelUnitsPerPixel,
            Mathf.Max(0f, bottomPx) * panelUnitsPerPixel);
    }

    static Sprite LoadBeachSprite(string imageName) =>
        string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Beaches/" + imageName);

    /// <summary>
    /// Species photos live next to their AnimalDef assets in Resources/Animals —
    /// the same folder the Animais catalog loads from, so the beach species card
    /// and the 3D catalog show the same picture of the same animal.
    /// </summary>
    static Sprite LoadSpeciesSprite(string imageName) =>
        string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Animals/" + imageName);
}
