using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// State + presentation logic for one open article — the reading screen. Plain C#,
    /// no UnityEngine, same contract as every other ViewModel here.
    ///
    /// <para><b>Entered with a payload.</b> The article id travels from whichever
    /// screen raised it (an index card, a link inside another article) to
    /// <c>AppUiHost</c>, which calls <see cref="Show"/> and only then pushes the route
    /// — so by the time <c>OnEnter</c> runs the state is already correct.</para>
    ///
    /// <para><b><see cref="Show"/> clears on an unknown id; it does not refuse in
    /// place.</b> That is the deliberate difference from
    /// <see cref="EspecieViewModel.ShowSpecies"/>, which leaves the previous species
    /// standing. An article can be opened <i>from another article</i>, so a stale body
    /// left behind a failed lookup would not be a harmless no-op — it would be the
    /// previous article's text under a route the user believes they navigated. Both
    /// <c>Show(null)</c> and <c>Show("no-such-id")</c> therefore land in the same
    /// renderable nothing-selected state (<see cref="HasArticle"/> false, every string
    /// null, no blocks) and return false, which is still the host's cue not to
    /// navigate.</para>
    ///
    /// <para><b>No clock is injected</b>, for the same reason as
    /// <see cref="ArticlesViewModel"/>: "Atualizado em 29 de setembro de 2026" is an
    /// absolute calendar date and the reading estimate is a function of the word count,
    /// so nothing here depends on "now" and a <c>utcNow</c> would only imply
    /// otherwise.</para>
    ///
    /// <para><b><see cref="Blocks"/> is the filtered body, and this is the one place
    /// the filter is applied.</b> <c>Article.Blocks</c> is honest — it still carries
    /// blocks this build cannot render, including the
    /// <see cref="ArticleBlockKind.Unknown"/> ones a newer <c>articles.json</c>
    /// produces. Filtering here rather than in the screen means a second screen (or the
    /// screenshot harness) cannot arrive at a different answer, and that a forward-
    /// compatible block renders as nothing without any screen having to remember to
    /// skip it.</para>
    /// </summary>
    public sealed class ArticleViewModel : IDisposable
    {
        readonly IArticleCatalog catalog;
        readonly List<ArticleBlock> blocks = new List<ArticleBlock>();
        bool disposed;

        /// <summary>Raised when the open article changed, including to none.</summary>
        public event Action Changed;

        /// <summary>The open article, or null before the first <see cref="Show"/> and
        /// after a <see cref="Clear"/> or a failed lookup.</summary>
        public Article Current { get; private set; }

        public ArticleViewModel(IArticleCatalog catalog)
        {
            this.catalog = catalog;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Nothing to unsubscribe — the catalog is an immutable read of a file that
            // ships in the build. The guard exists so the host disposes every ViewModel
            // the same way, and dropping the state means a screen kept alive by the
            // router cannot repaint a disposed article.
            Current = null;
            blocks.Clear();
            Changed = null;
        }

        // ---- Selection -------------------------------------------------------

        /// <summary>
        /// Opens an article by id (the stable kebab-case front-matter key).
        ///
        /// <para>Returns true when an article is now open. A blank or unknown id
        /// <b>clears</b> the screen and returns false — see the class remarks for why
        /// clearing beats refusing in place. Re-opening the article already shown is a
        /// no-op that still returns true and raises nothing.</para>
        /// </summary>
        public bool Show(string id)
        {
            var found = string.IsNullOrWhiteSpace(id) ? null : catalog?.Find(id.Trim());
            if (found == null)
            {
                Clear();
                return false;
            }
            if (ReferenceEquals(found, Current)) return true;

            Current = found;
            RebuildBlocks();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Closes the article, leaving a renderable empty screen. Idempotent —
        /// raises nothing when nothing was open.</summary>
        public void Clear()
        {
            if (Current == null && blocks.Count == 0) return;
            Current = null;
            blocks.Clear();
            Changed?.Invoke();
        }

        public bool HasArticle => Current != null;

        /// <summary>The open article's id, for a back-link or a share payload. Not a
        /// label; null when nothing is open.</summary>
        public string ArticleId => Current?.Id;

        // ---- Header ----------------------------------------------------------

        /// <summary>
        /// The headline. Falls back to the id if an article somehow has no title, so the
        /// page is never blank and the broken entry is identifiable on screen — the same
        /// belt-and-braces <see cref="SpeciesCardFormatter.Title"/> applies to a species.
        /// Null when nothing is open.
        /// </summary>
        public string TitleText
        {
            get
            {
                if (Current == null) return null;
                return Current.HasTitle ? Current.Title : Current.Id;
            }
        }

        /// <summary>The one-sentence teaser, shown as a standfirst above the body; null
        /// hides it.</summary>
        public string SummaryText => Current != null && Current.HasSummary ? Current.Summary : null;

        public bool HasSummary => SummaryText != null;

        /// <summary>The author's category, as typed; null hides it.</summary>
        public string CategoryText => ArticleFormatter.CategoryHeading(Current?.Category);

        public bool HasCategory => CategoryText != null;

        /// <summary>Hero sprite path for <c>Resources.Load&lt;Sprite&gt;</c>, no
        /// extension; null means the page opens on its title.</summary>
        public string HeroImage => Current != null && Current.HasHeroImage ? Current.HeroImage : null;

        public bool HasHeroImage => HeroImage != null;

        /// <summary>Attribution for the hero photo, verbatim. A <b>licence condition</b>
        /// wherever the photo is shown, so it is never dimmed and never shortened; null
        /// when the photo needs none.</summary>
        public string HeroCreditText => ArticleFormatter.Credit(Current?.HeroCredit);

        public bool HasHeroCredit => HeroCreditText != null;

        /// <summary>"5 min de leitura"; null when the word count cannot support an
        /// estimate — see <see cref="ArticleFormatter.ReadingTime"/>.</summary>
        public string ReadingTimeText => ArticleFormatter.ReadingTime(Current?.WordCount ?? 0);

        public bool HasReadingTime => ReadingTimeText != null;

        /// <summary>"Atualizado em 29 de setembro de 2026"; null when the author omitted
        /// the date, rather than claiming one.</summary>
        public string UpdatedText => ArticleFormatter.UpdatedOn(Current?.Updated);

        public bool HasUpdated => UpdatedText != null;

        /// <summary>
        /// "5 min de leitura · Espécies" — the two header facts on one line, with an
        /// absent half dropped and no stray separator. Null when both are absent.
        /// </summary>
        public string MetaText => ArticleFormatter.MetaLine(ReadingTimeText, CategoryText);

        public bool HasMeta => MetaText != null;

        // ---- Body ------------------------------------------------------------

        /// <summary>
        /// The body blocks the screen should draw, in authoring order: everything in
        /// <c>Article.Blocks</c> whose <see cref="ArticleBlock.IsRenderable"/> is true.
        /// Never null; empty is legitimate (front matter with no body renders as hero +
        /// title + summary).
        /// </summary>
        public IReadOnlyList<ArticleBlock> Blocks => blocks;

        public bool HasBlocks => blocks.Count > 0;

        /// <summary>
        /// How many of the article's blocks this build could not render — unknown kinds
        /// from a newer content file, plus any that arrived without their payload. 0 in
        /// every healthy case. Not shown to the reader (there is nothing they could do
        /// about it); it exists so a test and a diagnostic can assert the degradation
        /// actually happened rather than inferring it from a count that came out short.
        /// </summary>
        public int SkippedBlockCount =>
            Current == null ? 0 : Current.Blocks.Count - blocks.Count;

        void RebuildBlocks()
        {
            blocks.Clear();
            if (Current?.Blocks == null) return;
            foreach (var block in Current.Blocks)
            {
                if (block != null && block.IsRenderable) blocks.Add(block);
            }
        }

        // ---- Per-block presentation ------------------------------------------
        // Thin, but they belong here rather than in the screen: rule 2 says a screen may
        // not build a user-visible string, and a marker or a tone label is one.

        /// <summary>The visible marker of a numbered-list row ("1.", "2.").</summary>
        public string ListNumberFor(int index) => ArticleFormatter.ListNumber(index);

        /// <summary>The bullet glyph of a bullet-list row.</summary>
        public string BulletMarker => ArticleFormatter.BulletMarker;

        /// <summary>A callout's accessible name, per tone.</summary>
        public string CalloutLabelFor(ArticleCalloutTone tone) => ArticleFormatter.CalloutToneLabel(tone);

        /// <summary>"— Autor" for a quote block, or null for an unattributed one.</summary>
        public string QuoteAttributionFor(ArticleBlock block) =>
            ArticleFormatter.QuoteAttribution(block?.Attribution);

        /// <summary>An image block's caption; null hides the line.</summary>
        public string CaptionFor(ArticleBlock block) => ArticleFormatter.Caption(block?.Caption);

        /// <summary>An image block's credit, verbatim; null when it needs none.</summary>
        public string CreditFor(ArticleBlock block) => ArticleFormatter.Credit(block?.Credit);

        /// <summary>A video block's caption; null when the clip was authored without one.</summary>
        public string VideoTitleFor(ArticleBlock block) => ArticleFormatter.VideoTitle(block?.Title);

        /// <summary>Label of a species cross-reference link.</summary>
        public string SpeciesRefLabel => ArticleFormatter.SpeciesRefLabel;

        /// <summary>Label of a beach cross-reference link.</summary>
        public string BeachRefLabel => ArticleFormatter.BeachRefLabel;
    }
}
