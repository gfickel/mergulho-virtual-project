using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One article as the index card draws it: the identity the row needs, plus every
    /// derived string already formatted. The card carries no
    /// <see cref="ArticleSummary"/> and no body — a row must not be able to reach a
    /// parsed block list it will never draw, and it must not be able to build a string
    /// either.
    /// </summary>
    public sealed class ArticleCardView
    {
        /// <summary>Route payload — the stable kebab-case id. Never rendered.</summary>
        public readonly string Id;

        /// <summary>Headline. Falls back to the id only if an article somehow reached
        /// the catalog with no title, so a row is never blank and the broken entry is
        /// identifiable on screen.</summary>
        public readonly string TitleText;

        /// <summary>One-sentence teaser; null hides the line.</summary>
        public readonly string SummaryText;

        /// <summary>The author's category, as typed; null hides it.</summary>
        public readonly string CategoryText;

        /// <summary>Cover sprite path for <c>Resources.Load&lt;Sprite&gt;</c>, no
        /// extension; null means the card is text-only.</summary>
        public readonly string HeroImage;

        /// <summary>Attribution for the cover photo — a licence condition, so the card
        /// shows it wherever it shows the photo. Null when the photo needs none.</summary>
        public readonly string HeroCreditText;

        /// <summary>"5 min de leitura"; null when there is no word count to derive it
        /// from, which is the only honest answer — see
        /// <see cref="ArticleFormatter.ReadingTime"/>.</summary>
        public readonly string ReadingTimeText;

        /// <summary>
        /// "5 min de leitura · Espécies" — the reading estimate and the category joined,
        /// with whichever half is absent dropped so no stray separator survives. Null
        /// when both are absent. Offered alongside the two halves because a card that
        /// groups by category already has the category as a heading above it and wants
        /// only the estimate.
        /// </summary>
        public readonly string MetaText;

        public ArticleCardView(ArticleSummary summary)
        {
            Id = summary?.Id;
            TitleText = summary == null ? null
                : summary.HasTitle ? summary.Title : summary.Id;
            SummaryText = summary != null && summary.HasSummary ? summary.Summary : null;
            CategoryText = ArticleFormatter.CategoryHeading(summary?.Category);
            HeroImage = summary != null && summary.HasHeroImage ? summary.HeroImage : null;
            HeroCreditText = ArticleFormatter.Credit(summary?.HeroCredit);
            ReadingTimeText = ArticleFormatter.ReadingTime(summary?.WordCount ?? 0);
            MetaText = ArticleFormatter.MetaLine(ReadingTimeText, CategoryText);
        }

        public bool HasSummary => SummaryText != null;
        public bool HasCategory => CategoryText != null;
        public bool HasHeroImage => HeroImage != null;
        public bool HasHeroCredit => HeroCreditText != null;
        public bool HasReadingTime => ReadingTimeText != null;
        public bool HasMeta => MetaText != null;
    }

    /// <summary>
    /// One category section of the index: its heading and the cards under it, in index
    /// order. A group only exists because at least one article is in it, so
    /// <see cref="Articles"/> is never empty.
    /// </summary>
    public sealed class ArticleCategoryGroup
    {
        /// <summary>
        /// The grouping key — the author's category text, trimmed. Compared exactly, so
        /// "Espécies" and "espécies" are two groups; that is deliberate, the category is
        /// simultaneously the key and the visible heading and normalising it would make
        /// the heading disagree with what was typed.
        /// </summary>
        public readonly string Category;

        /// <summary>The visible heading; null for an article whose category is blank
        /// (a content bug — the cards still render, uncaptioned, rather than
        /// disappearing).</summary>
        public readonly string HeadingText;

        public readonly IReadOnlyList<ArticleCardView> Articles;

        public ArticleCategoryGroup(string category, IReadOnlyList<ArticleCardView> articles)
        {
            Category = category;
            HeadingText = ArticleFormatter.CategoryHeading(category);
            Articles = articles ?? Array.Empty<ArticleCardView>();
        }

        public int Count => Articles.Count;
        public bool HasHeading => HeadingText != null;
    }

    /// <summary>
    /// State + presentation logic for the "Conteúdo educativo" index — the list of
    /// educational articles, grouped by category. Plain C#, no UnityEngine, same
    /// contract as every other ViewModel here.
    ///
    /// <para><b>Category order is first-appearance, never alphabetical.</b>
    /// <see cref="IArticleCatalog.All"/> already returns index order (the author's
    /// <c>order</c> ascending), so walking it once and opening a group the first time a
    /// category is seen makes the section order a consequence of the same lever: the
    /// author puts the article they want read first at the lowest <c>order</c>, and its
    /// category leads the page. Sorting the categories would silently take that lever
    /// away and hand it to the Portuguese alphabet.</para>
    ///
    /// <para><b>No clock is injected, because no string here depends on "now".</b>
    /// "Atualizado em 29 de setembro de 2026" is an absolute calendar date and the
    /// reading estimate is a function of the word count alone — so unlike
    /// <see cref="HomeViewModel"/> or <see cref="BeachDetailViewModel"/> there is
    /// nothing for a <c>utcNow</c> to make deterministic, and taking one would only
    /// suggest there is. If a relative form ("atualizado há 3 dias") is ever wanted,
    /// inject it then and the tests stay timezone-proof by construction.</para>
    ///
    /// <para><b>The two empty states are different things and must read differently.</b>
    /// <see cref="IsEmpty"/> is a readable catalog with nothing in it — content work
    /// nobody has done yet, which is a normal stage of the project. <see cref="IsUnavailable"/>
    /// is a catalog that could not be read at all, which means the build shipped without
    /// its generated <c>articles.json</c>. Neither is retryable (the file lives inside
    /// the APK), which is why <see cref="StateActionLabel"/> is null; see
    /// <see cref="ArticleFormatter.StateActionLabel"/> for why that is a decision rather
    /// than an omission.</para>
    /// </summary>
    public sealed class ArticlesViewModel : IDisposable
    {
        readonly IArticleCatalog catalog;
        readonly List<ArticleCardView> visible = new List<ArticleCardView>();
        readonly List<ArticleCategoryGroup> groups = new List<ArticleCategoryGroup>();
        readonly List<string> categories = new List<string>();
        IReadOnlyList<ArticleSummary> summaries = Array.Empty<ArticleSummary>();
        bool available;
        bool disposed;

        /// <summary>Raised when the list, the grouping or the category filter changed.</summary>
        public event Action Changed;

        public ArticlesViewModel(IArticleCatalog catalog)
        {
            this.catalog = catalog;
            Rebuild();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Nothing is subscribed: the catalog is an immutable read of a file that
            // ships in the build, so it has no change event to unhook. The guard and the
            // interface are kept so every ViewModel in this layer is disposed the same
            // way by the host, and so a future catalog that does raise events has an
            // obvious place to be unhooked from.
            Changed = null;
        }

        // ---- Chrome ----------------------------------------------------------

        /// <summary>Screen headline. From the formatter — the screen may not spell it.</summary>
        public string TitleText => ArticleFormatter.IndexTitle;

        /// <summary>One line under the headline.</summary>
        public string SubtitleText => ArticleFormatter.IndexSubtitle;

        /// <summary>"12 conteúdos"; null at zero, where the state view says it better.
        /// Counts the <i>visible</i> articles, so it agrees with the filter.</summary>
        public string CountText => ArticleFormatter.ArticleCount(visible.Count);

        public bool HasCountText => CountText != null;

        // ---- The list --------------------------------------------------------

        /// <summary>
        /// Every visible article's card, flat and in index order. Never null. Honours
        /// <see cref="SelectedCategory"/>, so it is the same set
        /// <see cref="Groups"/> covers.
        /// </summary>
        public IReadOnlyList<ArticleCardView> Articles => visible;

        /// <summary>
        /// The visible articles grouped into category sections, categories in
        /// first-appearance order. Never null; empty exactly when
        /// <see cref="Articles"/> is.
        /// </summary>
        public IReadOnlyList<ArticleCategoryGroup> Groups => groups;

        /// <summary>
        /// Every category in the catalog, in first-appearance order — the source for a
        /// filter control, and unaffected by the current filter so selecting one cannot
        /// remove the others. Never null.
        /// </summary>
        public IReadOnlyList<string> Categories => categories;

        public bool HasArticles => visible.Count > 0;

        // ---- Filter ----------------------------------------------------------

        /// <summary>
        /// The category the list is narrowed to, or <b>null for all of them</b>, which
        /// is the default and the only state the shipped screen uses — there is no
        /// filter UI in scope. It exists so the screen that eventually grows one has a
        /// tested place to put it rather than filtering in the view.
        /// </summary>
        public string SelectedCategory { get; private set; }

        /// <summary>True in the default state: no category selected, everything shown.</summary>
        public bool AllCategoriesSelected => SelectedCategory == null;

        /// <summary>
        /// Narrows the list to one category, or widens it to all of them when
        /// <paramref name="category"/> is null or blank. Returns false, having changed
        /// nothing, for a category the catalog does not have — an unknown category would
        /// otherwise empty the list and render as "nothing authored yet", which is a
        /// different and untrue statement.
        /// </summary>
        public bool SelectCategory(string category)
        {
            string wanted = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            if (wanted != null && !categories.Contains(wanted)) return false;
            if (string.Equals(wanted, SelectedCategory, StringComparison.Ordinal)) return true;
            SelectedCategory = wanted;
            RebuildVisible();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Clears the filter. Idempotent.</summary>
        public void ClearCategory() => SelectCategory(null);

        // ---- States ----------------------------------------------------------

        /// <summary>
        /// The catalog was readable but holds no articles — nothing is authored yet.
        /// Distinct from <see cref="IsUnavailable"/>, and the two say different things to
        /// the reader. Note this reports the <i>catalog</i>, not the filtered view: a
        /// filter that matched nothing cannot happen (<see cref="SelectCategory"/>
        /// refuses an unknown category) and would not be an empty library if it did.
        /// </summary>
        public bool IsEmpty => available && summaries.Count == 0;

        /// <summary>The catalog could not be read at all — a build with no generated
        /// <c>articles.json</c>. Never a connectivity problem; the file is in the APK.</summary>
        public bool IsUnavailable => !available;

        /// <summary>The screen draws its state view instead of the list.</summary>
        public bool ShowStateView => IsUnavailable || IsEmpty;

        /// <summary>Title of the state view; meaningless unless <see cref="ShowStateView"/>.</summary>
        public string StateTitleText => ArticleFormatter.StateTitle(IsUnavailable);

        /// <summary>Body of the state view.</summary>
        public string StateBodyText => ArticleFormatter.StateBody(IsUnavailable);

        /// <summary>Always null — neither state is retryable. See
        /// <see cref="ArticleFormatter.StateActionLabel"/>.</summary>
        public string StateActionLabel => ArticleFormatter.StateActionLabel(IsUnavailable);

        public bool HasStateAction => StateActionLabel != null;

        // ---- Building --------------------------------------------------------

        /// <summary>
        /// Re-reads the catalog and raises <see cref="Changed"/>. There is nothing on a
        /// device that can change the answer — the file ships in the build — so this is
        /// for tests, and for an editor session where <c>ArticleLibrary.Reload()</c> has
        /// picked up a regenerated file.
        /// </summary>
        public void Refresh()
        {
            Rebuild();
            Changed?.Invoke();
        }

        void Rebuild()
        {
            available = catalog != null && catalog.IsAvailable;
            summaries = catalog?.All() ?? Array.Empty<ArticleSummary>();
            if (summaries == null) summaries = Array.Empty<ArticleSummary>();

            // Categories in first-appearance order over the UNFILTERED list, so a
            // filter control keeps offering every category while one is selected.
            categories.Clear();
            foreach (var summary in summaries)
            {
                if (summary == null) continue;
                string category = Key(summary.Category);
                if (category != null && !categories.Contains(category)) categories.Add(category);
            }

            // A filter surviving a reload would silently hide articles; drop it if the
            // category it named is gone.
            if (SelectedCategory != null && !categories.Contains(SelectedCategory))
                SelectedCategory = null;

            RebuildVisible();
        }

        void RebuildVisible()
        {
            visible.Clear();
            groups.Clear();

            // One pass builds both views. The group list is ordered by first appearance,
            // which — because `summaries` arrives in index order — means the category of
            // the lowest-`order` article leads the page.
            var groupIndex = new Dictionary<string, List<ArticleCardView>>(StringComparer.Ordinal);
            var groupOrder = new List<string>();
            // Articles with a blank category are a content bug, not a category of their
            // own; they group under one heading-less section keyed by a value no real
            // category can collide with, so they are still reachable on screen.
            const string NoCategory = " ";

            foreach (var summary in summaries)
            {
                if (summary == null) continue;
                string category = Key(summary.Category);
                if (SelectedCategory != null && !string.Equals(category, SelectedCategory, StringComparison.Ordinal))
                    continue;

                var card = new ArticleCardView(summary);
                visible.Add(card);

                string bucket = category ?? NoCategory;
                if (!groupIndex.TryGetValue(bucket, out var rows))
                {
                    rows = new List<ArticleCardView>();
                    groupIndex[bucket] = rows;
                    groupOrder.Add(bucket);
                }
                rows.Add(card);
            }

            foreach (var bucket in groupOrder)
                groups.Add(new ArticleCategoryGroup(
                    bucket == NoCategory ? null : bucket, groupIndex[bucket]));
        }

        /// <summary>The grouping key for a category: trimmed, or null when blank. Not
        /// lowercased — see <see cref="ArticleCategoryGroup.Category"/>.</summary>
        static string Key(string category) =>
            string.IsNullOrWhiteSpace(category) ? null : category.Trim();
    }
}
