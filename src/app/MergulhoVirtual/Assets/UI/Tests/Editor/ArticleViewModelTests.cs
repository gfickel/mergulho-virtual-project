using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The article reading screen's ViewModel.
    ///
    /// <para>The load-bearing behaviour is <b><see cref="ArticleViewModel.Show"/> on an
    /// id the catalog does not have</b>. It <i>clears</i>, where
    /// <see cref="EspecieViewModel.ShowSpecies"/> refuses in place — because an article
    /// can be opened from inside another article, so leaving the previous one standing
    /// would show the reader the wrong body under a route they believe they navigated.
    /// The second is that <b><see cref="ArticleViewModel.Blocks"/> is the filtered
    /// body</b>: this is the single place a block this build cannot render is dropped, so
    /// a forward-compatible <c>articles.json</c> degrades to silence without any screen
    /// having to remember to skip anything.</para>
    /// </summary>
    public class ArticleViewModelTests
    {
        // ---- Fake ------------------------------------------------------------

        sealed class FakeCatalog : IArticleCatalog
        {
            public readonly List<Article> Items = new List<Article>();
            public bool Available = true;

            public bool IsAvailable => Available;

            public IReadOnlyList<ArticleSummary> All()
            {
                var rows = new List<ArticleSummary>(Items.Count);
                foreach (var item in Items) rows.Add(item?.ToSummary());
                return rows;
            }

            public Article Find(string id)
            {
                foreach (var item in Items) if (item != null && item.Id == id) return item;
                return null;
            }
        }

        static ArticleBlock Paragraph(string text) =>
            new ArticleBlock { Kind = ArticleBlockKind.Paragraph, Text = text };

        static Article Full() => new Article
        {
            Id = "tubaroes-de-noronha",
            Title = "Os tubarões de Fernando de Noronha",
            Summary = "Quem vive aqui, e por que eles não são o que o cinema contou.",
            Category = "Espécies",
            HeroImage = "Animals/tiger_shark",
            HeroCredit = "Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)",
            Order = 10,
            Updated = new DateTime(2026, 9, 29),
            WordCount = 412,
            Blocks = new List<ArticleBlock>
            {
                new ArticleBlock { Kind = ArticleBlockKind.Heading, Level = 2, Text = "Quem vive aqui" },
                Paragraph("Corpo do texto."),
            },
        };

        static ArticleViewModel With(params Article[] articles)
        {
            var catalog = new FakeCatalog();
            catalog.Items.AddRange(articles);
            return new ArticleViewModel(catalog);
        }

        // ---- Nothing selected -------------------------------------------------

        [Test]
        public void ANewViewModelIsARenderableNothingSelectedState()
        {
            var vm = With(Full());

            Assert.That(vm.HasArticle, Is.False);
            Assert.That(vm.Current, Is.Null);
            Assert.That(vm.ArticleId, Is.Null);
            Assert.That(vm.TitleText, Is.Null);
            Assert.That(vm.SummaryText, Is.Null);
            Assert.That(vm.CategoryText, Is.Null);
            Assert.That(vm.HeroImage, Is.Null);
            Assert.That(vm.HeroCreditText, Is.Null);
            Assert.That(vm.ReadingTimeText, Is.Null);
            Assert.That(vm.UpdatedText, Is.Null);
            Assert.That(vm.MetaText, Is.Null);
            Assert.That(vm.Blocks, Is.Empty);
            Assert.That(vm.HasBlocks, Is.False);
            Assert.That(vm.SkippedBlockCount, Is.Zero);
        }

        // ---- Show -------------------------------------------------------------

        [Test]
        public void ShowOpensTheArticleAndFormatsEveryHeaderString()
        {
            var vm = With(Full());
            int changed = 0;
            vm.Changed += () => changed++;

            Assert.That(vm.Show("tubaroes-de-noronha"), Is.True);
            Assert.That(changed, Is.EqualTo(1));

            Assert.That(vm.HasArticle, Is.True);
            Assert.That(vm.ArticleId, Is.EqualTo("tubaroes-de-noronha"));
            Assert.That(vm.TitleText, Is.EqualTo("Os tubarões de Fernando de Noronha"));
            Assert.That(vm.SummaryText,
                Is.EqualTo("Quem vive aqui, e por que eles não são o que o cinema contou."));
            Assert.That(vm.CategoryText, Is.EqualTo("Espécies"));
            Assert.That(vm.HeroImage, Is.EqualTo("Animals/tiger_shark"));
            Assert.That(vm.HeroCreditText,
                Is.EqualTo("Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)"));
            Assert.That(vm.ReadingTimeText, Is.EqualTo("3 min de leitura"));
            Assert.That(vm.UpdatedText, Is.EqualTo("Atualizado em 29 de setembro de 2026"));
            Assert.That(vm.MetaText, Is.EqualTo("3 min de leitura · Espécies"));
            Assert.That(vm.Blocks.Count, Is.EqualTo(2));
        }

        [Test]
        public void ShowIsWhitespaceTolerantOnTheId()
        {
            var vm = With(Full());
            Assert.That(vm.Show("  tubaroes-de-noronha  "), Is.True);
        }

        [Test]
        public void ShowingAnUnknownIdClearsRatherThanLeavingTheLastArticleStanding()
        {
            var vm = With(Full());
            vm.Show("tubaroes-de-noronha");
            int changed = 0;
            vm.Changed += () => changed++;

            Assert.That(vm.Show("nao-existe"), Is.False);
            Assert.That(vm.HasArticle, Is.False,
                "an article can be opened from inside another article — a stale body under a " +
                "new route is worse than an empty screen");
            Assert.That(vm.TitleText, Is.Null);
            Assert.That(vm.Blocks, Is.Empty);
            Assert.That(changed, Is.EqualTo(1), "the screen has to repaint the empty state");
        }

        [Test]
        public void ShowingANullOrBlankIdIsSafe()
        {
            var vm = With(Full());

            Assert.That(vm.Show(null), Is.False);
            Assert.That(vm.HasArticle, Is.False);
            Assert.That(vm.Show("   "), Is.False);
            Assert.That(vm.HasArticle, Is.False);

            vm.Show("tubaroes-de-noronha");
            Assert.That(vm.Show(""), Is.False);
            Assert.That(vm.HasArticle, Is.False);
        }

        [Test]
        public void ReopeningTheSameArticleRaisesNothing()
        {
            var vm = With(Full());
            vm.Show("tubaroes-de-noronha");
            int changed = 0;
            vm.Changed += () => changed++;

            Assert.That(vm.Show("tubaroes-de-noronha"), Is.True);
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void ShowAgainstANullCatalogIsSafe()
        {
            var vm = new ArticleViewModel(null);

            Assert.That(vm.Show("qualquer-coisa"), Is.False);
            Assert.That(vm.HasArticle, Is.False);
        }

        // ---- Clear ------------------------------------------------------------

        [Test]
        public void ClearEmptiesTheScreenAndIsIdempotent()
        {
            var vm = With(Full());
            vm.Show("tubaroes-de-noronha");
            int changed = 0;
            vm.Changed += () => changed++;

            vm.Clear();
            Assert.That(vm.HasArticle, Is.False);
            Assert.That(vm.Blocks, Is.Empty);
            Assert.That(changed, Is.EqualTo(1));

            vm.Clear();
            Assert.That(changed, Is.EqualTo(1), "nothing changed, so nothing is raised");
        }

        // ---- Optional fields --------------------------------------------------

        [Test]
        public void AnArticleWithOnlyRequiredFrontMatterHidesEveryOptionalElement()
        {
            var vm = With(new Article
            {
                Id = "minimo",
                Title = "Mínimo",
                Summary = "Uma frase.",
                Category = "Espécies",
            });
            vm.Show("minimo");

            Assert.That(vm.HasHeroImage, Is.False);
            Assert.That(vm.HasHeroCredit, Is.False);
            Assert.That(vm.HasUpdated, Is.False,
                "the `updated` key is optional — the line hides rather than guessing a date");
            Assert.That(vm.HasReadingTime, Is.False, "no word count, no estimate");
            Assert.That(vm.HasBlocks, Is.False, "front matter with no body is legitimate");
            Assert.That(vm.MetaText, Is.EqualTo("Espécies"));
        }

        [Test]
        public void AnArticleWithNoTitleFallsBackToItsId()
        {
            var vm = With(new Article { Id = "orfao", Category = "Espécies" });
            vm.Show("orfao");

            Assert.That(vm.TitleText, Is.EqualTo("orfao"),
                "the page is never blank, and the broken entry stays identifiable on screen");
        }

        // ---- Block filtering --------------------------------------------------

        [Test]
        public void AnUnknownBlockKindIsDroppedSoItRendersAsNothing()
        {
            var vm = With(new Article
            {
                Id = "futuro",
                Title = "De um build mais novo",
                Category = "Espécies",
                Blocks = new List<ArticleBlock>
                {
                    Paragraph("Isso eu sei desenhar."),
                    // What a newer build_articles.py's `{"type":"table", …}` becomes.
                    new ArticleBlock { Kind = ArticleBlockKind.Unknown, Text = "linha | linha" },
                    Paragraph("E isso também."),
                },
            });
            vm.Show("futuro");

            Assert.That(vm.Blocks.Count, Is.EqualTo(2));
            Assert.That(vm.SkippedBlockCount, Is.EqualTo(1));
            foreach (var block in vm.Blocks)
                Assert.That(block.Kind, Is.Not.EqualTo(ArticleBlockKind.Unknown));
        }

        [Test]
        public void ABlockMissingItsPayloadIsDroppedToo()
        {
            var vm = With(new Article
            {
                Id = "vazios",
                Title = "Blocos vazios",
                Category = "Espécies",
                Blocks = new List<ArticleBlock>
                {
                    new ArticleBlock { Kind = ArticleBlockKind.Paragraph, Text = "   " },
                    new ArticleBlock { Kind = ArticleBlockKind.BulletList },
                    new ArticleBlock { Kind = ArticleBlockKind.Image },
                    new ArticleBlock { Kind = ArticleBlockKind.Video },
                    new ArticleBlock { Kind = ArticleBlockKind.SpeciesRef },
                    new ArticleBlock { Kind = ArticleBlockKind.BeachRef },
                    Paragraph("O único que sobra."),
                },
            });
            vm.Show("vazios");

            Assert.That(vm.Blocks.Count, Is.EqualTo(1));
            Assert.That(vm.Blocks[0].Text, Is.EqualTo("O único que sobra."));
            Assert.That(vm.SkippedBlockCount, Is.EqualTo(6));
        }

        [Test]
        public void ANullBlockIsSkipped()
        {
            var vm = With(new Article
            {
                Id = "nulo",
                Title = "Com um nulo",
                Category = "Espécies",
                Blocks = new List<ArticleBlock> { null, Paragraph("Texto.") },
            });
            vm.Show("nulo");

            Assert.That(vm.Blocks.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheArticlesOwnBlockListStaysHonest()
        {
            var article = new Article
            {
                Id = "futuro",
                Title = "Honesto",
                Category = "Espécies",
                Blocks = new List<ArticleBlock>
                {
                    Paragraph("Texto."),
                    new ArticleBlock { Kind = ArticleBlockKind.Unknown },
                },
            };
            var vm = With(article);
            vm.Show("futuro");

            Assert.That(article.Blocks.Count, Is.EqualTo(2),
                "the data layer keeps the unknown block so tooling can see it; only the " +
                "ViewModel filters, and it must not mutate the catalog's copy");
            Assert.That(vm.Blocks.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryKnownBlockKindWithAPayloadSurvivesTheFilter()
        {
            var vm = With(new Article
            {
                Id = "todos",
                Title = "Todos os blocos",
                Category = "Espécies",
                Blocks = new List<ArticleBlock>
                {
                    new ArticleBlock { Kind = ArticleBlockKind.Heading, Level = 2, Text = "H2" },
                    new ArticleBlock { Kind = ArticleBlockKind.Paragraph, Text = "P" },
                    new ArticleBlock { Kind = ArticleBlockKind.BulletList, Items = new[] { "a" } },
                    new ArticleBlock { Kind = ArticleBlockKind.NumberedList, Items = new[] { "a" } },
                    new ArticleBlock { Kind = ArticleBlockKind.Callout, Text = "C" },
                    new ArticleBlock { Kind = ArticleBlockKind.Quote, Text = "Q" },
                    new ArticleBlock { Kind = ArticleBlockKind.Image, Src = "Beaches/x" },
                    new ArticleBlock { Kind = ArticleBlockKind.Video, Url = "https://x/y.mp4" },
                    new ArticleBlock { Kind = ArticleBlockKind.SpeciesRef, SpeciesKey = "tiger_shark" },
                    new ArticleBlock { Kind = ArticleBlockKind.BeachRef, BeachName = "Sueste Beach" },
                },
            });
            vm.Show("todos");

            Assert.That(vm.Blocks.Count, Is.EqualTo(10));
            Assert.That(vm.SkippedBlockCount, Is.Zero);
        }

        // ---- Per-block presentation -------------------------------------------

        [Test]
        public void PerBlockStringsComeFromTheFormatter()
        {
            var vm = With(Full());
            vm.Show("tubaroes-de-noronha");

            Assert.That(vm.ListNumberFor(0), Is.EqualTo("1."));
            Assert.That(vm.BulletMarker, Is.EqualTo(ArticleFormatter.BulletMarker));
            Assert.That(vm.CalloutLabelFor(ArticleCalloutTone.Warning), Is.EqualTo("Atenção"));
            Assert.That(vm.SpeciesRefLabel, Is.EqualTo(ArticleFormatter.SpeciesRefLabel));
            Assert.That(vm.BeachRefLabel, Is.EqualTo(ArticleFormatter.BeachRefLabel));

            var quote = new ArticleBlock { Kind = ArticleBlockKind.Quote, Attribution = "Ana Silva" };
            Assert.That(vm.QuoteAttributionFor(quote), Is.EqualTo("— Ana Silva"));
            Assert.That(vm.QuoteAttributionFor(new ArticleBlock()), Is.Null);
            Assert.That(vm.QuoteAttributionFor(null), Is.Null);

            var image = new ArticleBlock
            {
                Kind = ArticleBlockKind.Image,
                Src = "Beaches/x",
                Caption = " Legenda ",
                Credit = "Foto: Autor / CC BY 4.0 (Wikimedia Commons)",
            };
            Assert.That(vm.CaptionFor(image), Is.EqualTo("Legenda"));
            Assert.That(vm.CreditFor(image), Is.EqualTo("Foto: Autor / CC BY 4.0 (Wikimedia Commons)"));
            Assert.That(vm.CaptionFor(null), Is.Null);
            Assert.That(vm.CreditFor(new ArticleBlock()), Is.Null);

            var video = new ArticleBlock { Kind = ArticleBlockKind.Video, Title = "Título" };
            Assert.That(vm.VideoTitleFor(video), Is.EqualTo("Título"));
            Assert.That(vm.VideoTitleFor(new ArticleBlock()), Is.Null);
        }

        // ---- Dispose ----------------------------------------------------------

        [Test]
        public void DisposeDropsTheArticleIsIdempotentAndStopsRaisingChanged()
        {
            var vm = With(Full());
            vm.Show("tubaroes-de-noronha");
            int changed = 0;
            vm.Changed += () => changed++;

            vm.Dispose();
            Assert.That(vm.HasArticle, Is.False,
                "a screen the router kept alive must not be able to repaint a disposed article");
            Assert.That(vm.Blocks, Is.Empty);

            Assert.DoesNotThrow(() => vm.Dispose());
            vm.Show("tubaroes-de-noronha");
            Assert.That(changed, Is.Zero);
        }
    }
}
