namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR copy for the <c>MvStateView</c> blocks — the error / offline states of
    /// DESIGN_IMPLEMENTATION.md §8.7 (frames <c>79:1304</c> / <c>81:1403</c>).
    ///
    /// <para>Same contract as <see cref="ConditionsFormatter"/> and
    /// <see cref="ReportFormatter"/>: plain static C#, no UnityEngine, no state.
    /// <b>Screens must not build user-visible strings themselves</b>, and the design
    /// system must not either — <c>MvStateView</c> deliberately ships with no copy
    /// of its own, so this is the single place these sentences exist and they are
    /// pinned by a test.</para>
    ///
    /// <para><b>Nothing here claims more than the code knows.</b> The offline
    /// wording is used only when the device reports no network link at all
    /// (<see cref="IConnectivity"/>); every other failure — a captive portal, the
    /// API being down, a malformed response — gets the generic wording, because
    /// those are indistinguishable from each other at this layer and a message that
    /// picked one would be guessing.</para>
    /// </summary>
    public static class StateViewCopy
    {
        // ---- The designed frames -------------------------------------------

        /// <summary>Title of frame 79:1304, verbatim.</summary>
        public const string ErrorTitle = "Algo deu errado por aqui";

        /// <summary>Body of frames 79:1304 / 81:1403, verbatim. The generic
        /// fallback for a surface with nothing more specific to say.</summary>
        public const string ErrorBody = "Não conseguimos carregar as informações.";

        /// <summary>The action label on both frames, verbatim.</summary>
        public const string RetryAction = "Tentar novamente";

        /// <summary>
        /// Title for the offline variant. The frames use one title for both states
        /// and let the glyph carry the difference; a wifi-off glyph over "Algo deu
        /// errado" would under-report a condition the user can actually fix, so the
        /// offline state names itself.
        /// </summary>
        public const string OfflineTitle = "Sem conexão";

        // ---- Conditions card (Início "Hoje") --------------------------------

        public const string ConditionsErrorBody = "Não conseguimos carregar as condições do mar.";
        public const string ConditionsOfflineBody = "Conecte-se à internet para ver as condições do mar.";

        // ---- Educational content ("Conteúdo educativo") ---------------------
        // The articles surface contributes its own bodies here, the way the Início
        // conditions card does above, rather than keeping them in ArticleFormatter —
        // this is the one place MvStateView's copy lives, and a second home would let
        // two surfaces drift into two spellings of the same failure.
        //
        // NOTE there is deliberately NO offline variant. articles.json ships inside
        // the APK, so the only way this surface fails is a build that has no generated
        // file — there is no network in the path and nothing a connection would fix.
        // Offering "Sem conexão" here would name a cause the code has no evidence for.

        /// <summary>
        /// Title when the catalog is readable but holds nothing. <b>Invented copy</b> —
        /// V2 has no frame for this screen, let alone its empty state. Phrased as a
        /// state of the library rather than an apology, because zero articles is a
        /// normal stage of a content project, not a fault.
        /// </summary>
        public const string ArticlesEmptyTitle = "Nada por aqui ainda";

        /// <summary>Body for the empty state. <b>Invented copy.</b> It promises only
        /// that the section exists, since nobody can say when an article lands.</summary>
        public const string ArticlesEmptyBody = "Os conteúdos educativos ainda estão sendo preparados.";

        /// <summary>
        /// Body when <c>IArticleCatalog.IsAvailable</c> is false. Names the surface, so
        /// it cannot be mistaken for the sea conditions failing; keeps the generic
        /// <see cref="ErrorTitle"/> above it.
        /// </summary>
        public const string ArticlesErrorBody = "Não conseguimos carregar os conteúdos educativos.";

        // ---- Selection ------------------------------------------------------

        /// <summary>Title for either state.</summary>
        public static string Title(bool offline) => offline ? OfflineTitle : ErrorTitle;

        /// <summary>Body for the Início conditions card.</summary>
        public static string ConditionsBody(bool offline) =>
            offline ? ConditionsOfflineBody : ConditionsErrorBody;

        /// <summary>Title for the articles index's state view — <paramref name="unavailable"/>
        /// is the catalog's <c>IsAvailable</c>, inverted.</summary>
        public static string ArticlesTitle(bool unavailable) =>
            unavailable ? ErrorTitle : ArticlesEmptyTitle;

        /// <summary>Body for the articles index's state view.</summary>
        public static string ArticlesBody(bool unavailable) =>
            unavailable ? ArticlesErrorBody : ArticlesEmptyBody;
    }
}
