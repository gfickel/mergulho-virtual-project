using System;
using System.Collections.Generic;
using MergulhoVirtual.UI.Navigation;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One entry of the Início feature grid: the copy, the Material Symbols icon
    /// name for its 36dp navy tile, and the <see cref="AppRoutes"/> key the screen
    /// emits when it is tapped. The screen never routes by itself — it raises the
    /// key and the host maps it — so this stays plain data.
    /// </summary>
    public sealed class HomeFeature
    {
        public readonly string Route;
        public readonly string Title;
        public readonly string Body;
        public readonly string Icon;

        public HomeFeature(string route, string title, string body, string icon)
        {
            Route = route;
            Title = title;
            Body = body;
            Icon = icon;
        }
    }

    /// <summary>
    /// State + presentation logic for the Início (Home) screen — Tela 7, plus
    /// Tela 6's first-run welcome card. Plain C#, no UnityEngine, so it is fully
    /// unit-testable with fake services; the clock and the UTC→local conversion are
    /// injected so tests are deterministic and timezone-proof (same contract as
    /// <see cref="BeachesViewModel"/>).
    ///
    /// The conditions rows are the same five strings the Beaches card renders —
    /// both go through <see cref="ConditionsFormatter"/>, which is the only place
    /// that pt-BR formatting exists.
    /// </summary>
    public sealed class HomeViewModel : IDisposable
    {
        /// <summary>DHN "Nível Médio" (MSL→LAT offset) — the tide sparkline baseline.</summary>
        public const float TideBaselineM = ConditionsFormatter.TideBaselineM;

        // ---- Copy (transcribed from the Figma frames; the Sobre entry is ours — see below).
        public const string ConditionsCardTitle = "Hoje";
        public const string TideTableLinkText = "Baixar a tábua de maré do mês";
        public const string WelcomeTitle = "Bem-vindo ao Mergulho Virtual";
        public const string WelcomeBody =
            "Explore as praias, acompanhe avistamentos marinhos e fique por dentro das condições " +
            "de segurança para uma experiência mais segura e conectada com Fernando de Noronha.";

        /// <summary>
        /// Stand-in glyph for the Avistamentos tile — an alias of
        /// <see cref="AppIcons.AvistamentosPlaceholder"/>, which the bottom-bar tab
        /// renders too. One constant, two sites: the tile and the tab must not drift
        /// into two different guesses at an asset that is still owed (Decision D7).
        /// </summary>
        public const string AvistamentosIconPlaceholder = AppIcons.AvistamentosPlaceholder;

        readonly IConditionsService conditions;
        readonly ITideService tides;
        readonly IOnboardingState onboarding;
        readonly IConnectivity connectivity;
        readonly Func<DateTime> utcNow;
        readonly Func<DateTime, DateTime> toLocalTime;
        bool disposed;

        /// <summary>The 2×2 grid, in Figma order.</summary>
        public IReadOnlyList<HomeFeature> GridFeatures { get; }

        /// <summary>
        /// The full-width entry below the grid. Per Decision D2, Sobre (and with it
        /// the Instagram widget) loses its bottom-bar tab and becomes a Home entry;
        /// it is NOT in the Figma frame. Full width rather than a fifth grid cell so
        /// it reads as a deliberate section instead of an orphaned half-row.
        /// </summary>
        public HomeFeature WideFeature { get; }

        /// <summary>
        /// The "Conteúdo educativo" entry, below <see cref="WideFeature"/>. Like that
        /// one it is <b>NOT in the V2 Figma frame</b> — V2 predates the educational-
        /// content feature entirely, and the feature needs a discoverable entry point
        /// because it gets no bottom-bar tab (the bar has four destinations and that is
        /// fixed). Same deliberate-addition rule as the Praia detalhe description card:
        /// added in the existing card language rather than as a new visual pattern.
        ///
        /// <para>A separate property rather than a fifth <see cref="GridFeatures"/> cell
        /// or a change to <see cref="WideFeature"/>: the grid is a 2×2 the frame draws
        /// and a fifth cell would orphan a half-row, and re-pointing WideFeature would
        /// silently delete the Sobre entry. Additive, so nothing that already reads
        /// those two moves.</para>
        /// </summary>
        public HomeFeature LearnFeature { get; }

        /// <summary>First-run welcome card (Tela 6) visibility.</summary>
        public bool ShowWelcome { get; private set; }

        /// <summary>Raised when <see cref="ShowWelcome"/> changes.</summary>
        public event Action WelcomeChanged;

        /// <summary>Raised when any displayed row may have changed (conditions, tide, freshness).</summary>
        public event Action DataChanged;

        public HomeViewModel(
            IConditionsService conditions,
            ITideService tides,
            IOnboardingState onboarding = null,
            IConnectivity connectivity = null,
            Func<DateTime> utcNow = null,
            Func<DateTime, DateTime> toLocalTime = null)
        {
            this.conditions = conditions;
            this.tides = tides;
            this.onboarding = onboarding;
            this.connectivity = connectivity;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.toLocalTime = toLocalTime ?? (d => d.ToLocalTime());

            // No persistence wired yet => treat it as a first run and keep the
            // dismissal in memory for the session, rather than hiding the card.
            ShowWelcome = onboarding == null || !onboarding.WelcomeDismissed;

            GridFeatures = new[]
            {
                new HomeFeature(AppRoutes.Praias, "Praias",
                    "Explore as praias, condições e informações de segurança.", "location_on"),
                // AR focus frame, not `landscape` (mountains) — same glyph as the tab.
                new HomeFeature(AppRoutes.Mergulho, "Mergulho Virtual",
                    "Veja de perto diversas espécies de tubarões.", "filter_center_focus"),
                new HomeFeature(AppRoutes.Avistamentos, "Avistamentos",
                    "Registre e navegue por avistamentos de vida marinha.", AvistamentosIconPlaceholder),
                new HomeFeature(AppRoutes.Sos, "SOS",
                    "Primeiros socorros, hospitais próximos e números de emergência.", "medical_services"),
            };

            WideFeature = new HomeFeature(AppRoutes.Sobre, "Sobre o projeto",
                "Conheça a iniciativa e acompanhe as últimas publicações.", "info");

            // The educational-content entry — see LearnFeature's remarks for why it is
            // its own property and why it is not in the Figma frame. The title is
            // ArticleFormatter's own EntryPointLabel rather than a second spelling, so
            // the card and the screen it opens cannot drift apart.
            LearnFeature = new HomeFeature(AppRoutes.Conteudos, ArticleFormatter.EntryPointLabel,
                "Artigos sobre a vida marinha, as praias e o projeto.", "menu_book");

            if (this.conditions != null) this.conditions.Changed += OnConditionsChanged;
            if (this.tides != null) this.tides.Changed += OnTideChanged;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (conditions != null) conditions.Changed -= OnConditionsChanged;
            if (tides != null) tides.Changed -= OnTideChanged;
        }

        void OnConditionsChanged(ConditionsData _) => DataChanged?.Invoke();
        void OnTideChanged(TideData _) => DataChanged?.Invoke();

        /// <summary>Re-renders time-relative strings ("Atualizado: há 5m"); call ~1/min.</summary>
        public void NotifyTimePassed() => DataChanged?.Invoke();

        // ---- Welcome card ---------------------------------------------------

        /// <summary>Closes the welcome card and persists it. Idempotent.</summary>
        public void DismissWelcome()
        {
            if (!ShowWelcome) return;
            ShowWelcome = false;
            onboarding?.DismissWelcome();
            WelcomeChanged?.Invoke();
        }

        // ---- Conditions failure state (MvStateView, §8.7) --------------------

        /// <summary>
        /// True when the card has nothing to show AND the service has told us it
        /// tried and could not. Both halves matter: a null snapshot on its own is
        /// also the state before the first fetch returns, and reporting an error
        /// while the request is still in flight would be a lie.
        ///
        /// <para>A cached-but-stale snapshot is deliberately NOT this state — the
        /// rows are real data and the "Atualizado: há Nm" line is already the honest
        /// signal for their age.</para>
        /// </summary>
        public bool ConditionsUnavailable =>
            conditions != null && conditions.Current == null && conditions.LastFetchFailed;

        /// <summary>
        /// The device reports no network link. Chooses the state view's variant and
        /// wording; with no <see cref="IConnectivity"/> wired it is false, i.e. the
        /// generic failure, which is the claim that is always safe.
        /// </summary>
        public bool IsOffline => connectivity != null && !connectivity.IsOnline;

        public string ConditionsStateTitle => StateViewCopy.Title(IsOffline);

        public string ConditionsStateBody => StateViewCopy.ConditionsBody(IsOffline);

        public string ConditionsStateAction => StateViewCopy.RetryAction;

        /// <summary>False while a fetch is in flight, so the retry button can show
        /// that the tap was taken.</summary>
        public bool ConditionsRetryEnabled => conditions == null || !conditions.IsFetching;

        /// <summary>
        /// Re-runs the conditions fetch. A real request, not a repaint: the result
        /// arrives later through <see cref="IConditionsService.Changed"/>.
        /// </summary>
        public void RetryConditions()
        {
            conditions?.Refresh();
            // Repaint now so the button reflects IsFetching without waiting for the
            // service to tell us something changed.
            DataChanged?.Invoke();
        }

        // ---- Conditions rows ------------------------------------------------

        public TideData CurrentTide => tides?.Current ?? default;

        public string WaveText => ConditionsFormatter.Wave(conditions?.Current);

        public string TideText => ConditionsFormatter.Tide(CurrentTide, toLocalTime);

        public string MoonText => ConditionsFormatter.Moon(utcNow());

        public string WindText => ConditionsFormatter.Wind(conditions?.Current);

        public string WaterText => ConditionsFormatter.Water(conditions?.Current);

        public string FreshnessText => ConditionsFormatter.Freshness(conditions?.Current, utcNow());

        /// <summary>Matches MdSparkline.ExtremumLabelFormatter: (sampleIndex, isHigh) → "▲ HH:mm" / "▼ HH:mm".</summary>
        public string FormatTideExtremumLabel(int sampleIndex, bool isHigh) =>
            ConditionsFormatter.TideExtremumLabel(CurrentTide, sampleIndex, isHigh, toLocalTime);
    }
}
