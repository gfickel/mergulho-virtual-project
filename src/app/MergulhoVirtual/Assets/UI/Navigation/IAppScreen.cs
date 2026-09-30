using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Navigation
{
    /// <summary>
    /// Contract every UI Toolkit screen implements so <see cref="MdRouter"/> can own
    /// show/hide, the back stack and edge insets uniformly.
    ///
    /// Screens stay <see cref="VisualElement"/> subclasses constructed with their
    /// ViewModel (the pattern HomeScreen established) — <see cref="Root"/> is
    /// normally just <c>this</c>. The router never constructs a screen; the host
    /// composition root does, then registers it.
    ///
    /// Lifetime: a screen is built once and kept. <see cref="OnEnter"/> /
    /// <see cref="OnExit"/> fire on every navigation, so they own subscriptions,
    /// scroll reset and any per-visit refresh — never construction.
    /// </summary>
    public interface IAppScreen
    {
        /// <summary>Route key — one of the <see cref="AppRoutes"/> constants.</summary>
        string Key { get; }

        /// <summary>The element the router parents into the screen container.</summary>
        VisualElement Root { get; }

        /// <summary>Screen became visible. Subscribe, refresh, reset scroll here.</summary>
        void OnEnter();

        /// <summary>Screen was hidden. Unsubscribe and release anything per-visit.</summary>
        void OnExit();

        /// <summary>
        /// Safe-area insets in dp, applied by the host (not SafeAreaElement — see the
        /// deliberate deviation documented on HomeScreen). A screen that paints an
        /// opaque surface under the status bar applies these as padding on its own
        /// content container.
        /// </summary>
        void SetEdgeInsets(float top, float left, float right, float bottom);
    }

    /// <summary>Route keys. Tabs are the four V2 bottom-bar destinations; the rest are sub-screens reached with a back stack.</summary>
    public static class AppRoutes
    {
        // Bottom-bar destinations, in V2 order.
        public const string Home = "home";
        public const string Mergulho = "mergulho";
        public const string Praias = "praias";
        public const string Avistamentos = "avistamentos";

        // Sub-screens (pushed, dismissed with Back).
        public const string PraiaDetalhe = "praia-detalhe";
        public const string Sos = "sos";
        public const string Especie = "especie";
        public const string Sobre = "sobre";

        /// <summary>
        /// The "Conteúdo educativo" index — the list of educational articles, grouped
        /// by category. A SUB-SCREEN, not a fifth tab: V2's bottom bar has exactly four
        /// destinations and that is fixed, so the feature is reached the way Sobre is
        /// (Decision D2), from a card on Início.
        /// </summary>
        public const string Conteudos = "conteudos";

        /// <summary>
        /// One article, open for reading. Pushed <i>with a payload</i> — the article id
        /// travels on its own event so <c>ArticleViewModel</c> is set before the push
        /// (see <c>AppUiHost.OnArticleRequested</c>); a bare route would open the reader
        /// on whatever article happened to be there last.
        /// </summary>
        public const string Conteudo = "conteudo";

        public static readonly string[] Tabs = { Home, Mergulho, Praias, Avistamentos };
    }

    /// <summary>
    /// Icon names that more than one place has to agree on, so they cannot drift
    /// apart. Everything else names its Material Symbol inline, where it is used.
    /// </summary>
    public static class AppIcons
    {
        /// <summary>
        /// The ONE stand-in for Avistamentos, rendered by BOTH the bottom-bar tab
        /// (<see cref="AppTabs.Default"/>) and the Início feature tile
        /// (HomeViewModel.GridFeatures).
        ///
        /// <para>TODO (Decision D7 / DESIGN_IMPLEMENTATION.md §3.7): V2 draws a
        /// CUSTOM shark fin and Material Symbols has no equivalent — `waves`,
        /// `surfing`, `scuba_diving` and `pool` are all explicitly ruled out in
        /// tools/design_system/material_symbols_icons.txt because a plausible-looking
        /// marine glyph would quietly become the shipped icon. `visibility` is a
        /// deliberately neutral placeholder: an avistamento is a *sighting*, and it
        /// reads as "not final". Replace this one line (and delete this TODO) when
        /// the designer ships the SVG; both sites move together.</para>
        /// </summary>
        public const string AvistamentosPlaceholder = "visibility";
    }
}
