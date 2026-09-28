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
            Func<DateTime> utcNow = null,
            Func<DateTime, DateTime> toLocalTime = null)
        {
            this.conditions = conditions;
            this.tides = tides;
            this.onboarding = onboarding;
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
