using System;
using MergulhoVirtual.UI;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.Serialization;
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
    [Tooltip("AR tap source for the Mergulho species card. Optional — with none, the HUD just never opens a card.")]
    [SerializeField] ObjectInteraction arSelection;
    [Tooltip("Scene-root AnimalViewerRig — the 3D turntable the Espécie screen renders. Optional: with none, that screen simply has no 3D section.")]
    [SerializeField] GameObject animalViewerRig;

    // ScreenUI/RegisterScreen is NOT here any more: Slice 3 replaced it with the
    // UI Toolkit ReportScreen, so nothing routes to it. The GameObject stays in
    // the scene until Slice 6 (the strangler rule) — AppUiBuilder just leaves it
    // deactivated with the other unrouted legacy screens.
    [Header("Legacy uGUI screens (strangler — replaced in Slice 6)")]
    [Tooltip("ScreenUI/MainScreen — no longer a routed screen, and empty since the ArTuning panel was removed on 2026-09-29. Still toggled with the Mergulho route; both this field and the GameObject are ready to be deleted.")]
    [FormerlySerializedAs("legacyArScreen")]
    [SerializeField] GameObject legacyArOverlay;
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
    MergulhoViewModel mergulhoViewModel;
    EspecieViewModel especieViewModel;
    ArticlesViewModel articlesViewModel;
    ArticleViewModel articleViewModel;
    UiServiceAdapters.ArticleCatalogAdapter articleCatalogAdapter;
    UiServiceAdapters.ConditionsServiceAdapter conditionsAdapter;
    UiServiceAdapters.TideServiceAdapter tideAdapter;
    UiServiceAdapters.ActiveBeachAdapter activeBeachAdapter;
    UiServiceAdapters.SightingReportsAdapter sightingReportsAdapter;
    UiServiceAdapters.ArSelectionAdapter arSelectionAdapter;
    UiServiceAdapters.SpeciesModelViewerAdapter modelViewerAdapter;
    UiServiceAdapters.VideoPlaybackAdapter videoPlaybackAdapter;
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
            new UiServiceAdapters.OnboardingStateAdapter(),
            // Decides whether a failed conditions load is reported as offline or as
            // a generic error. Stateless poll — no subscription, nothing to dispose.
            new UiServiceAdapters.ConnectivityAdapter());
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
        // Mergulho (Slice 4). The AR HUD owns only "which species' card is open";
        // the beach pill on the same top bar is praiasViewModel's, deliberately —
        // picking a beach there IS setting the GPS override, so the AR spawner and
        // the HUD can never disagree about where the user is.
        arSelectionAdapter = new UiServiceAdapters.ArSelectionAdapter(arSelection);
        mergulhoViewModel = new MergulhoViewModel(
            new UiServiceAdapters.SpeciesCatalogAdapter(),
            arSelectionAdapter);
        // Espécie (Decision D1) — the species sub-screen that gives the 3D viewer and
        // the inline videos a home now that the Animais tab is gone. The two media
        // adapters are built here with everything else so they outlive every shell
        // rebuild: the viewer holds a render target keyed to the scene's rig, and the
        // player owns a VideoPlayer GameObject parented to this one, so both would
        // otherwise be reallocated (and the video restarted) on each OnEnable.
        modelViewerAdapter = new UiServiceAdapters.SpeciesModelViewerAdapter(animalViewerRig);
        videoPlaybackAdapter = new UiServiceAdapters.VideoPlaybackAdapter(transform);
        especieViewModel = new EspecieViewModel(
            new UiServiceAdapters.SpeciesCatalogAdapter(), videoPlaybackAdapter);
        // Conteúdo educativo — the article index and the reader. ONE catalog adapter
        // feeds both ViewModels: it is an immutable read of a file that ships in the
        // build, so a second instance would be a second copy of the same bytes rather
        // than a second source. The reader needs no clock and no services of its own
        // (see ArticleViewModel's remarks); the two engine-bound things its SCREEN needs
        // — the shared video player and the sprite loader — are handed to the screen at
        // registration, not to the ViewModel, for the usual reason: a UnityEngine type
        // cannot pass through a layer required to stay engine-free.
        articleCatalogAdapter = new UiServiceAdapters.ArticleCatalogAdapter();
        articlesViewModel = new ArticlesViewModel(articleCatalogAdapter);
        articleViewModel = new ArticleViewModel(articleCatalogAdapter);
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

        // Windows review build only — a no-op on Android and in the editor. Must
        // run here rather than in Awake: UIDocument tears the tree down on
        // deactivate, so `root` and `router` are both freshly built above and a
        // reference captured in Awake would already be stale.
        // See docs/windows-review-build.md, Steps 1/1b/1c.
        DesktopReviewMode.Apply(document != null ? document.panelSettings : null, root, router);

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
        mergulhoViewModel?.Dispose();
        especieViewModel?.Dispose();
        // Neither subscribes to anything — the article catalog is an immutable file read
        // with no change event — but both are disposed with the rest so the host has one
        // rule, and so a catalog that ever grows one has an obvious place to be unhooked.
        articlesViewModel?.Dispose();
        articleViewModel?.Dispose();
        conditionsAdapter?.Dispose();
        tideAdapter?.Dispose();
        activeBeachAdapter?.Dispose();
        sightingReportsAdapter?.Dispose();
        arSelectionAdapter?.Dispose();
        // Both hold engine resources the GC will not reclaim on its own: a
        // RenderTexture and a VideoPlayer GameObject.
        modelViewerAdapter?.Dispose();
        videoPlaybackAdapter?.Dispose();
    }

    void Update()
    {
        // Device-frame picker for the Windows review build (F2/F3, digits 1-5).
        // Compiles to an empty call off desktop, so no #if at the call site.
        DesktopReviewMode.PollHotkeys();

        freshnessTimer += Time.unscaledDeltaTime;
        if (freshnessTimer >= FreshnessRefreshInterval)
        {
            freshnessTimer = 0f;
            beachesViewModel?.NotifyTimePassed();
            beachDetailViewModel?.NotifyTimePassed();
            homeViewModel?.NotifyTimePassed();
            // The pending-sightings feed needs this too: ISightingReports.Changed
            // fires only on Success/PermanentFailure, never on a transient retry, so
            // without the tick a retrying row's state and attempt count stay frozen
            // and a healthy queue looks stuck.
            reportViewModel?.NotifyTimePassed();
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

        // Mergulho (Tela 8) — the AR HUD, a real UI Toolkit screen since Slice 4.
        // It paints nothing but its two surfaces, so the camera shows through and
        // taps reach the AR scene; ScreenUI/MainScreen is no longer routed but is
        // still switched on for this route by OnRouteChanged — vestigially, since
        // the ArTuning pill it carried was removed on 2026-09-29.
        var mergulho = new MergulhoScreen(mergulhoViewModel, praiasViewModel);
        mergulho.BackRequested += OnMergulhoBackRequested;
        r.Register(mergulho);

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
        praiaDetalhe.SpeciesRequested += OnSpeciesRequested;
        praiaDetalhe.BackRequested += () => router?.Back();
        r.Register(praiaDetalhe);

        // Espécie — the sub-screen Decision D1 sends the Animais content to: the
        // description, the 3D turntable and the inline videos, reached from a species
        // card instead of from a list. The two media services are handed to the SCREEN
        // rather than to its ViewModel because both speak UnityEngine.Texture, which a
        // ViewModel required to stay engine-free cannot carry — the same reason the
        // sprite loaders are constructor arguments everywhere here.
        var especie = new EspecieScreen(
            especieViewModel, modelViewerAdapter, videoPlaybackAdapter, LoadSpeciesSprite);
        especie.BackRequested += () => router?.Back();
        r.Register(especie);

        // Avistamentos (Tela 11 + Tela 12) — the tab root, replacing the uGUI
        // RegisterScreen. It leaves for Início after a submit the queue took, which
        // arrives here as an ordinary route key; BackRequested is wired for the day
        // the same screen is pushed rather than tabbed to (its back button is hidden
        // on a tab root — see ReportScreen.ShowBackButton).
        var report = new ReportScreen(reportViewModel);
        report.NavigationRequested += OnScreenNavigationRequested;
        report.BackRequested += () => router?.Back();
        r.Register(report);

        // Conteúdo educativo — the article index. A sub-screen, pushed from the Início
        // "Conteúdo educativo" card: V2's bottom bar has four destinations and that is
        // fixed, so this feature is reached the way Sobre is. The sprite loader is
        // LoadArticleSprite, NOT the beach or species one — an article's `hero`/`src`
        // carries its own Resources folder, so the path must not be prefixed.
        var conteudos = new ArticlesScreen(articlesViewModel, LoadArticleSprite);
        conteudos.ArticleRequested += OnArticleRequested;
        conteudos.BackRequested += () => router?.Back();
        r.Register(conteudos);

        // Conteúdo — the reader. It takes the SHARED video player (the same instance
        // the Espécie screen holds; the router only ever shows one screen, and both
        // release it in OnExit) plus the two catalogs, which it uses only to turn a
        // block's machine key into the destination's pt-BR name — see ArticleScreen's
        // remarks for why that lookup is in the screen rather than in the ViewModel.
        var conteudo = new ArticleScreen(
            articleViewModel,
            videoPlaybackAdapter,
            new UiServiceAdapters.SpeciesCatalogAdapter(),
            new UiServiceAdapters.BeachCatalogAdapter(),
            LoadArticleSprite);
        conteudo.BackRequested += () => router?.Back();
        // Reuses the Praia detalhe handler, so an article's species link lands on the
        // same Espécie screen by the same path — one answer to "open this species".
        conteudo.SpeciesRequested += OnSpeciesRequested;
        conteudo.BeachRequested += OnBeachRequested;
        r.Register(conteudo);

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
    /// Slice 5 — is a deliberate no-op: <see cref="MdRouter"/> logs one warning,
    /// returns false, and leaves both the visible screen and the bar's selection
    /// untouched. The SOS tile and the floating SOS pill therefore do nothing at all
    /// today rather than opening a blank screen.
    /// (<see cref="AppRoutes.Especie"/> is no longer in that list: Decision D1 is
    /// implemented and it is reached through <see cref="OnSpeciesRequested"/>, which
    /// carries the species key this event cannot.)</para>
    /// </summary>
    void OnScreenNavigationRequested(string route)
    {
        if (router == null || string.IsNullOrEmpty(route)) return;

        if (Array.IndexOf(AppRoutes.Tabs, route) >= 0) router.Navigate(route);
        else router.Push(route);
    }

    /// <summary>
    /// "Saiba mais sobre a espécie" — the one navigation in the app that carries a
    /// <b>payload</b> rather than just a route.
    ///
    /// <para><b>Why it is a second event and not an argument on the router.</b>
    /// <see cref="OnScreenNavigationRequested"/> carries a route key and nothing else,
    /// so it cannot say WHICH species to open. Three shapes were available:</para>
    /// <list type="number">
    /// <item>a general-purpose argument bag on <see cref="MdRouter"/> — rejected: one
    /// caller does not justify an untyped <c>object</c> parameter threaded through
    /// every <c>Navigate</c>/<c>Push</c>/<c>Back</c> and every screen's
    /// <c>OnEnter</c>, and it would make every future route's payload contract
    /// invisible;</item>
    /// <item>a shared ViewModel holding "the selected species", the way
    /// <see cref="BeachDetail"/> is shared by the two Praias screens — rejected:
    /// that pattern works there because ONE thing (the active beach) drives both
    /// screens, whereas here the species comes from whichever card was tapped, and a
    /// second source (the AR card, §4) would then be writing to the same slot with no
    /// ownership rule;</item>
    /// <item><b>a distinct event carrying the key</b>, handled here by setting the
    /// state and only then pushing — chosen. It keeps screens router-agnostic
    /// (<see cref="PraiaDetalheScreen"/> raises a species key and knows nothing about
    /// navigation), keeps the payload typed and visible at the composition root, and
    /// costs one method.</item>
    /// </list>
    ///
    /// <para><b>The order is load-bearing:</b> the ViewModel is set BEFORE the push, so
    /// the screen's <c>OnEnter</c> already sees the right species and never renders a
    /// frame of the previous one. And an unresolved key does not navigate at all —
    /// <c>ShowSpecies</c> returning false means a typo in <c>beaches_content.json</c>
    /// or a prefab that was never catalogued, and staying put beats pushing a blank
    /// page.</para>
    /// </summary>
    void OnSpeciesRequested(string speciesKey)
    {
        if (router == null || especieViewModel == null) return;
        if (!especieViewModel.ShowSpecies(speciesKey))
        {
            Debug.LogWarning($"[AppUiHost] No species catalogued for key \"{speciesKey}\" — not opening the species screen.");
            return;
        }
        router.Push(AppRoutes.Especie);
    }

    /// <summary>
    /// An index card was tapped — the <b>second</b> payload navigation in the app, and
    /// it follows <see cref="OnSpeciesRequested"/> exactly: read that method's comment
    /// for why a payload travels on its own event rather than as a router argument.
    ///
    /// <para><b>The order is load-bearing:</b> <see cref="ArticleViewModel.Show"/> runs
    /// BEFORE the push, so the reader's <c>OnEnter</c> already sees the right article
    /// and never renders a frame of the previous one. That matters more here than for
    /// species, because an article can be opened <i>from another article</i> — a stale
    /// body under a route the user believes they navigated is the exact failure
    /// <see cref="ArticleViewModel"/> clears state to avoid.</para>
    ///
    /// <para>An id the catalog cannot resolve does not navigate at all: it means a
    /// stale link or a typo in the content file, and a blank reading screen is worse
    /// than staying put. <c>Show</c> has already cleared the reader by then, which is
    /// its documented behaviour, so nothing stale is left behind either.</para>
    /// </summary>
    void OnArticleRequested(string id)
    {
        if (router == null || articleViewModel == null) return;
        if (!articleViewModel.Show(id))
        {
            Debug.LogWarning($"[AppUiHost] No article for id \"{id}\" — not opening the reader.");
            return;
        }
        router.Push(AppRoutes.Conteudo);
    }

    /// <summary>
    /// A <c>beachRef</c> block inside an article was tapped — "Saiba mais sobre a
    /// praia" — carrying the places.json beach <b>name</b>. The third payload
    /// navigation, and it follows <see cref="OnSpeciesRequested"/>'s shape exactly:
    /// set the ViewModel, THEN push, so the screen's <c>OnEnter</c> never renders a
    /// frame of the previous beach.
    ///
    /// <para><b>Why <see cref="BeachDetailViewModel.ShowBeach"/> and not
    /// <c>PraiasViewModel.SelectBeach</c>.</b> Both open
    /// <see cref="PraiaDetalheScreen"/>, which renders from <c>praias.Detail</c> —
    /// the very instance this method drives. The difference is the side effect:
    /// <c>SelectBeach</c> goes on to set the <see cref="IBeachOverride"/>, which is
    /// right for the Praias hero pill (V2 deleted the browsable beach list, so
    /// picking a beach there IS the user declaring where they are, and the override
    /// re-points the <c>BeachSharkSpawner</c>, the conditions fetch and this screen
    /// together) and wrong here. Reading a cross-reference in prose is not that
    /// declaration: it would silently re-home the user's AR animals and sea
    /// conditions to whatever beach the text happened to mention, with no way back
    /// but the selector's "Automático (GPS)" row. <c>ShowBeach</c> sets the same
    /// state — beach, content, species, alerts, and the hero pill's selection — and
    /// touches the override nowhere.</para>
    ///
    /// <para>A name the catalog does not have does not navigate: it means a typo in the
    /// content file, and the beach screen would open on whatever was there before.</para>
    /// </summary>
    void OnBeachRequested(string beachName)
    {
        if (router == null || beachDetailViewModel == null) return;
        if (string.IsNullOrEmpty(beachName)) return;

        beachDetailViewModel.ShowBeach(beachName);

        // ShowBeach is a silent no-op on a key places.json does not have, so the
        // push is gated on the ViewModel actually being where it was asked to go —
        // which is the precondition the push depends on, rather than a second
        // lookup that could agree with the catalog and still miss.
        var opened = beachDetailViewModel.Beach;
        if (opened == null || !string.Equals(opened.Name, beachName, StringComparison.Ordinal))
        {
            Debug.LogWarning($"[AppUiHost] No beach named \"{beachName}\" in places.json — not opening the beach screen.");
            return;
        }

        router.Push(AppRoutes.PraiaDetalhe);
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

    /// <summary>
    /// The AR HUD's back arrow. Tela 8 draws one even though Mergulho is a
    /// bottom-bar destination with an empty back stack, so "back" is resolved
    /// here, where the router lives: pop a sub-screen if one is open, otherwise
    /// leave the immersive AR view for the landing route. The alternative —
    /// hiding the arrow the way <see cref="ReportScreen"/> does on its own tab
    /// root — would drop an element the design draws and leave the user with no
    /// affordance to exit AR other than the bar.
    /// </summary>
    void OnMergulhoBackRequested()
    {
        if (router == null) return;
        if (router.Back()) return;
        router.Navigate(AppRoutes.Home);
    }

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

    /// <summary>
    /// Route changes reach two places outside the router: the AR performance gate
    /// (<see cref="ScreenManager"/>, which decides whether the AR session runs),
    /// and the legacy uGUI AR overlay.
    ///
    /// <para><b>Why ScreenUI/MainScreen is still switched on and off here — and
    /// why it no longer needs to be.</b> Slice 4 replaced its TopBar with
    /// <see cref="MergulhoScreen"/>, so it is no longer a
    /// <see cref="LegacyUguiScreen"/> and the router does not own it; what kept it
    /// in the scene was <c>ArTuning</c>, the on-beach panel that tuned the AR
    /// stabilisation filters live, which followed the Mergulho route as a companion
    /// beneath the UI Toolkit screen. <b>ArTuning was deleted on 2026-09-29</b>, so
    /// MainScreen now contains only the TopBar that AppUiBuilder deactivates: this
    /// toggle is vestigial, and the field, this branch and the GameObject can be
    /// removed together.</para>
    /// </summary>
    void OnRouteChanged(string route)
    {
        if (screenManager != null) screenManager.SetRoute(route);

        bool arRoute = route == AppRoutes.Mergulho;
        if (legacyArOverlay != null && legacyArOverlay.activeSelf != arRoute)
            legacyArOverlay.SetActive(arRoute);
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
    ///
    /// <para>On the Windows review build the insets come from the selected device
    /// preset instead — see the comment on the branch below.</para>
    /// </summary>
    void ApplyEdgeInsets()
    {
        if (router == null || Screen.width <= 0 || Screen.height <= 0) return;
#if UNITY_STANDALONE && !UNITY_EDITOR
        // Screen.safeArea on a desktop window is the WHOLE window, so the mobile
        // path below resolves to 0,0,0,0 — and this method runs on every single
        // GeometryChangedEvent, i.e. on every preset switch and every window
        // resize. Left as-is it would immediately clobber the per-preset insets
        // DesktopReviewMode just pushed through the router, and the designer
        // would review content flush against the frame's top edge and a nav bar
        // 34 dp shorter than on device (and report both as bugs).
        //
        // Insets are a property of the simulated device, not of the desktop
        // window (docs/windows-review-build.md, Step 1c), so take them from the
        // active preset. Left/right are 0: every preset is portrait — the app is
        // portrait-locked — and no portrait preset has a side inset.
        var preset = DesktopReviewMode.Current;
        router.SetEdgeInsets(preset.Top, 0f, 0f, preset.Bottom);
#else
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
#endif
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

    /// <summary>
    /// Article covers and inline figures. <b>No folder is prepended, unlike the two
    /// loaders above</b>: an article's <c>hero</c> / <c>src</c> is authored as a
    /// complete Resources path ("Beaches/praia_do_sancho", "Animals/tiger_shark",
    /// "Articles/&lt;name&gt;"), which is what lets one article illustrate itself with a
    /// beach photo, a species photo and an image of its own. Prefixing it would make
    /// every figure resolve to null and every one of them silently disappear.
    /// </summary>
    static Sprite LoadArticleSprite(string resourcePath) =>
        string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Sprite>(resourcePath);
}
