using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The "Conteúdo educativo" index ViewModel.
    ///
    /// <para>Two things here carry more weight than the usual property round-trips. The
    /// first is that <b>category order is first-appearance, not alphabetical</b>: the
    /// author's <c>order</c> is the only lever over what a reader sees first, and sorting
    /// the category headings would quietly hand that lever to the Portuguese alphabet —
    /// so the ordering test uses categories whose alphabetical order is the reverse of
    /// their authored order, and would fail if anyone "tidied up" with a Sort. The second
    /// is that <b>empty and unavailable are different states</b> with different copy: a
    /// library nobody has written yet is normal, a build with no generated articles.json
    /// is broken, and telling a reader the wrong one is the whole point of
    /// <c>IArticleCatalog.IsAvailable</c> existing.</para>
    /// </summary>
    public class ArticlesViewModelTests
    {
        // ---- Fake ------------------------------------------------------------

        /// <summary>
        /// A catalog with no file behind it. <see cref="All"/> hands back exactly what it
        /// was given, in that order — the real loader sorts, and a fake that sorted too
        /// would hide a ViewModel that re-sorted.
        /// </summary>
        sealed class FakeCatalog : IArticleCatalog
        {
            public readonly List<Article> Items = new List<Article>();
            public bool Available = true;
            public int AllCallCount;

            public bool IsAvailable => Available;

            public IReadOnlyList<ArticleSummary> All()
            {
                AllCallCount++;
                var rows = new List<ArticleSummary>(Items.Count);
                // A null entry is passed through as a null row on purpose — the real
                // loader cannot produce one, but the ViewModel must survive it rather
                // than taking a screen down with an NRE.
                foreach (var item in Items) rows.Add(item?.ToSummary());
                return rows;
            }

            public Article Find(string id)
            {
                foreach (var item in Items) if (item != null && item.Id == id) return item;
                return null;
            }

            public FakeCatalog With(string id, string title, string category,
                int order = 0, int wordCount = 400, string hero = null, string heroCredit = null,
                string summary = "Resumo.", DateTime? updated = null)
            {
                Items.Add(new Article
                {
                    Id = id,
                    Title = title,
                    Summary = summary,
                    Category = category,
                    Order = order,
                    WordCount = wordCount,
                    HeroImage = hero,
                    HeroCredit = heroCredit,
                    Updated = updated,
                });
                return this;
            }
        }

        static FakeCatalog Catalog() => new FakeCatalog();

        // ---- Order and grouping ----------------------------------------------

        [Test]
        public void ArticlesArriveInTheOrderTheCatalogGivesThem()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Primeiro", "Espécies", order: 10)
                .With("b", "Segundo", "Praias", order: 20)
                .With("c", "Terceiro", "Espécies", order: 30));

            Assert.That(Ids(vm.Articles), Is.EqualTo(new[] { "a", "b", "c" }),
                "the catalog owns index order; the ViewModel must not re-sort");
        }

        [Test]
        public void CategoriesKeepFirstAppearanceOrderNotAlphabeticalOrder()
        {
            // "Zonas" sorts after "Aves" alphabetically but is authored first. A Sort
            // anywhere in the grouping would flip these and fail here.
            var vm = new ArticlesViewModel(Catalog()
                .With("z1", "Zona um", "Zonas", order: 10)
                .With("a1", "Ave um", "Aves", order: 20)
                .With("z2", "Zona dois", "Zonas", order: 30));

            Assert.That(vm.Categories, Is.EqualTo(new[] { "Zonas", "Aves" }));
            Assert.That(Headings(vm.Groups), Is.EqualTo(new[] { "Zonas", "Aves" }));
        }

        [Test]
        public void ArticlesOfOneCategoryGroupTogetherEvenWhenInterleaved()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("z1", "Zona um", "Zonas", order: 10)
                .With("a1", "Ave um", "Aves", order: 20)
                .With("z2", "Zona dois", "Zonas", order: 30));

            Assert.That(vm.Groups.Count, Is.EqualTo(2));
            Assert.That(Ids(vm.Groups[0].Articles), Is.EqualTo(new[] { "z1", "z2" }),
                "a category opened by the lowest-order article collects the later ones too");
            Assert.That(Ids(vm.Groups[1].Articles), Is.EqualTo(new[] { "a1" }));
            Assert.That(vm.Groups[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void CategoriesAreComparedExactlySoCaseMakesTwoGroups()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Um", "Espécies", order: 10)
                .With("b", "Dois", "espécies", order: 20));

            Assert.That(vm.Categories, Is.EqualTo(new[] { "Espécies", "espécies" }),
                "the category is both the key and the visible heading, so normalising it " +
                "would make a heading disagree with what the author typed");
        }

        [Test]
        public void AnArticleWithABlankCategoryStillRendersUnderAHeadinglessGroup()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Com categoria", "Espécies", order: 10)
                .With("b", "Sem categoria", "  ", order: 20));

            Assert.That(vm.Categories, Is.EqualTo(new[] { "Espécies" }),
                "a blank category is a content bug, not a category of its own");
            Assert.That(vm.Groups.Count, Is.EqualTo(2));
            Assert.That(vm.Groups[1].HasHeading, Is.False);
            Assert.That(vm.Groups[1].HeadingText, Is.Null);
            Assert.That(Ids(vm.Groups[1].Articles), Is.EqualTo(new[] { "b" }),
                "the article is still reachable — swallowing it would hide the bug from " +
                "the reader as well as the author");
        }

        [Test]
        public void ANullEntryInTheCatalogIsSkippedRatherThanCrashing()
        {
            var catalog = Catalog().With("a", "Um", "Espécies");
            catalog.Items.Add(null);

            var vm = new ArticlesViewModel(catalog);

            Assert.That(Ids(vm.Articles), Is.EqualTo(new[] { "a" }));
        }

        // ---- Cards -----------------------------------------------------------

        [Test]
        public void CardsCarryFormattedStringsAndNeverRawData()
        {
            var vm = new ArticlesViewModel(Catalog().With(
                "a", "Os tubarões", "Espécies", order: 10, wordCount: 412,
                hero: "Animals/tiger_shark",
                heroCredit: "Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)"));

            var card = vm.Articles[0];
            Assert.That(card.Id, Is.EqualTo("a"));
            Assert.That(card.TitleText, Is.EqualTo("Os tubarões"));
            Assert.That(card.SummaryText, Is.EqualTo("Resumo."));
            Assert.That(card.CategoryText, Is.EqualTo("Espécies"));
            Assert.That(card.HeroImage, Is.EqualTo("Animals/tiger_shark"));
            Assert.That(card.HeroCreditText,
                Is.EqualTo("Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)"));
            Assert.That(card.ReadingTimeText, Is.EqualTo("3 min de leitura"));
            Assert.That(card.MetaText, Is.EqualTo("3 min de leitura · Espécies"));
        }

        [Test]
        public void ACardWithNothingOptionalHidesEveryOptionalElement()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Só o título", "Espécies", wordCount: 0, summary: null));

            var card = vm.Articles[0];
            Assert.That(card.HasSummary, Is.False);
            Assert.That(card.HasHeroImage, Is.False);
            Assert.That(card.HasHeroCredit, Is.False);
            Assert.That(card.HasReadingTime, Is.False,
                "no word count means no estimate — the element hides rather than printing 0");
            Assert.That(card.MetaText, Is.EqualTo("Espécies"),
                "the meta line drops the missing half and its separator");
        }

        [Test]
        public void ACardWithNoTitleFallsBackToItsIdSoTheRowIsNeverBlank()
        {
            var vm = new ArticlesViewModel(Catalog().With("orfao", null, "Espécies"));

            Assert.That(vm.Articles[0].TitleText, Is.EqualTo("orfao"));
        }

        // ---- Chrome ----------------------------------------------------------

        [Test]
        public void ChromeComesFromTheFormatter()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Espécies"));

            Assert.That(vm.TitleText, Is.EqualTo(ArticleFormatter.IndexTitle));
            Assert.That(vm.SubtitleText, Is.EqualTo(ArticleFormatter.IndexSubtitle));
        }

        [Test]
        public void CountTextCountsWhatIsVisible()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Um", "Zonas", order: 10)
                .With("b", "Dois", "Aves", order: 20));

            Assert.That(vm.CountText, Is.EqualTo("2 conteúdos"));
            Assert.That(vm.HasCountText, Is.True);

            vm.SelectCategory("Aves");
            Assert.That(vm.CountText, Is.EqualTo("1 conteúdo"),
                "the caption must agree with the filter, not with the whole library");
        }

        [Test]
        public void CountTextIsHiddenWhenThereIsNothingToCount()
        {
            var vm = new ArticlesViewModel(Catalog());

            Assert.That(vm.CountText, Is.Null);
            Assert.That(vm.HasCountText, Is.False);
        }

        // ---- Empty vs unavailable --------------------------------------------

        [Test]
        public void AReadableButEmptyCatalogIsEmptyNotUnavailable()
        {
            var vm = new ArticlesViewModel(Catalog());

            Assert.That(vm.IsEmpty, Is.True);
            Assert.That(vm.IsUnavailable, Is.False);
            Assert.That(vm.ShowStateView, Is.True);
            Assert.That(vm.HasArticles, Is.False);
            Assert.That(vm.StateTitleText, Is.EqualTo(StateViewCopy.ArticlesEmptyTitle));
            Assert.That(vm.StateBodyText, Is.EqualTo(StateViewCopy.ArticlesEmptyBody));
        }

        [Test]
        public void AnUnreadableCatalogIsUnavailableNotEmpty()
        {
            var catalog = Catalog();
            catalog.Available = false;

            var vm = new ArticlesViewModel(catalog);

            Assert.That(vm.IsUnavailable, Is.True);
            Assert.That(vm.IsEmpty, Is.False,
                "'nothing authored yet' would be a false statement about a broken build");
            Assert.That(vm.ShowStateView, Is.True);
            Assert.That(vm.StateTitleText, Is.EqualTo(StateViewCopy.ErrorTitle));
            Assert.That(vm.StateBodyText, Is.EqualTo(StateViewCopy.ArticlesErrorBody));
        }

        [Test]
        public void APopulatedCatalogShowsNoStateView()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Espécies"));

            Assert.That(vm.ShowStateView, Is.False);
            Assert.That(vm.IsEmpty, Is.False);
            Assert.That(vm.IsUnavailable, Is.False);
        }

        [Test]
        public void NeitherStateOffersARetry()
        {
            var vm = new ArticlesViewModel(Catalog());
            Assert.That(vm.StateActionLabel, Is.Null);
            Assert.That(vm.HasStateAction, Is.False);

            var broken = Catalog();
            broken.Available = false;
            var vm2 = new ArticlesViewModel(broken);
            Assert.That(vm2.StateActionLabel, Is.Null,
                "articles.json ships in the APK — there is nothing a retry could reach");
        }

        [Test]
        public void ANullCatalogIsTreatedAsUnavailableRatherThanThrowing()
        {
            var vm = new ArticlesViewModel(null);

            Assert.That(vm.IsUnavailable, Is.True);
            Assert.That(vm.Articles, Is.Empty);
            Assert.That(vm.Groups, Is.Empty);
            Assert.That(vm.Categories, Is.Empty);
            Assert.DoesNotThrow(() => vm.Refresh());
        }

        // ---- Filter ----------------------------------------------------------

        [Test]
        public void TheFilterDefaultsToAllCategories()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Um", "Zonas", order: 10)
                .With("b", "Dois", "Aves", order: 20));

            Assert.That(vm.SelectedCategory, Is.Null);
            Assert.That(vm.AllCategoriesSelected, Is.True);
            Assert.That(vm.Articles.Count, Is.EqualTo(2));
        }

        [Test]
        public void SelectingACategoryNarrowsBothViewsButNotTheCategoryList()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Um", "Zonas", order: 10)
                .With("b", "Dois", "Aves", order: 20)
                .With("c", "Três", "Zonas", order: 30));

            Assert.That(vm.SelectCategory("Zonas"), Is.True);
            Assert.That(vm.SelectedCategory, Is.EqualTo("Zonas"));
            Assert.That(vm.AllCategoriesSelected, Is.False);
            Assert.That(Ids(vm.Articles), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(vm.Groups.Count, Is.EqualTo(1));
            Assert.That(vm.Categories, Is.EqualTo(new[] { "Zonas", "Aves" }),
                "a filter control must keep offering every category while one is selected");
        }

        [Test]
        public void SelectingAnUnknownCategoryChangesNothing()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Zonas"));
            int changed = 0;
            vm.Changed += () => changed++;

            Assert.That(vm.SelectCategory("Répteis"), Is.False);
            Assert.That(vm.SelectedCategory, Is.Null);
            Assert.That(vm.Articles.Count, Is.EqualTo(1),
                "an unknown category would empty the list and read as 'nothing authored yet'");
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void ABlankCategoryClearsTheFilter()
        {
            var vm = new ArticlesViewModel(Catalog()
                .With("a", "Um", "Zonas", order: 10)
                .With("b", "Dois", "Aves", order: 20));
            vm.SelectCategory("Zonas");

            Assert.That(vm.SelectCategory(null), Is.True);
            Assert.That(vm.AllCategoriesSelected, Is.True);
            Assert.That(vm.Articles.Count, Is.EqualTo(2));

            vm.SelectCategory("Aves");
            vm.ClearCategory();
            Assert.That(vm.AllCategoriesSelected, Is.True);
        }

        [Test]
        public void SelectingTheSameCategoryTwiceRaisesNothing()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Zonas"));
            vm.SelectCategory("Zonas");
            int changed = 0;
            vm.Changed += () => changed++;

            Assert.That(vm.SelectCategory("Zonas"), Is.True);
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void SelectingACategoryIsWhitespaceTolerant()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Zonas"));

            Assert.That(vm.SelectCategory("  Zonas  "), Is.True);
            Assert.That(vm.SelectedCategory, Is.EqualTo("Zonas"));
        }

        // ---- Refresh ----------------------------------------------------------

        [Test]
        public void RefreshRereadsTheCatalogAndRaisesChanged()
        {
            var catalog = Catalog().With("a", "Um", "Espécies");
            var vm = new ArticlesViewModel(catalog);
            int changed = 0;
            vm.Changed += () => changed++;

            catalog.With("b", "Dois", "Praias", order: 20);
            vm.Refresh();

            Assert.That(Ids(vm.Articles), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void RefreshDropsAFilterWhoseCategoryIsGone()
        {
            var catalog = Catalog().With("a", "Um", "Zonas").With("b", "Dois", "Aves", order: 20);
            var vm = new ArticlesViewModel(catalog);
            vm.SelectCategory("Aves");

            catalog.Items.RemoveAll(a => a.Category == "Aves");
            vm.Refresh();

            Assert.That(vm.SelectedCategory, Is.Null,
                "a filter surviving a reload would silently hide every remaining article");
            Assert.That(Ids(vm.Articles), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void TheCatalogIsReadOncePerBuildNotOncePerBinding()
        {
            var catalog = Catalog().With("a", "Um", "Espécies");
            var vm = new ArticlesViewModel(catalog);
            int afterConstruction = catalog.AllCallCount;

            // The screen reads these repeatedly while painting.
            _ = vm.Articles;
            _ = vm.Groups;
            _ = vm.CountText;
            _ = vm.IsEmpty;

            Assert.That(catalog.AllCallCount, Is.EqualTo(afterConstruction));
        }

        // ---- Dispose ----------------------------------------------------------

        [Test]
        public void DisposeIsIdempotentAndStopsRaisingChanged()
        {
            var vm = new ArticlesViewModel(Catalog().With("a", "Um", "Zonas"));
            int changed = 0;
            vm.Changed += () => changed++;

            vm.Dispose();
            Assert.DoesNotThrow(() => vm.Dispose());

            vm.Refresh();
            Assert.That(changed, Is.Zero, "a disposed ViewModel must not repaint a torn-down screen");
        }

        // ---- Helpers ----------------------------------------------------------

        static string[] Ids(IReadOnlyList<ArticleCardView> cards)
        {
            var ids = new string[cards.Count];
            for (int i = 0; i < cards.Count; i++) ids[i] = cards[i].Id;
            return ids;
        }

        static string[] Headings(IReadOnlyList<ArticleCategoryGroup> groups)
        {
            var headings = new string[groups.Count];
            for (int i = 0; i < groups.Count; i++) headings[i] = groups[i].HeadingText;
            return headings;
        }
    }
}
