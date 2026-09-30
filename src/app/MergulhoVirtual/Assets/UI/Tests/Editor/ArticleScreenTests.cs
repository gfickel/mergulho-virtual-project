using System;
using System.Collections.Generic;
using System.Linq;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The article reader — the block renderer. Five things here are structural rather
    /// than cosmetic, and <b>none of them shows up in a screenshot</b>:
    ///
    /// <list type="number">
    /// <item><b>Every block kind produces exactly one element, and an unknown kind
    /// produces none.</b> The screen binds <c>ArticleViewModel.Blocks</c>, which applies
    /// the <see cref="ArticleBlockKind.Unknown"/> filter, so a block type from a newer
    /// <c>articles.json</c> degrades to silence. Reading <c>Current.Blocks</c> instead
    /// would draw an empty box per unknown block, and nothing on screen would say
    /// why.</item>
    /// <item><b>An absent caption / credit / hero produces NO element.</b> One of the
    /// three shipped figures has neither, and one of the four articles has no hero — so
    /// the absence is the normal rendering and two blank lines under a photo would be
    /// the bug.</item>
    /// <item><b>The cross-reference rows carry keys, and the key never reaches the
    /// text.</b> Five consecutive species references in one article would otherwise be
    /// five identical rows.</item>
    /// <item><b>OnExit stops the stream.</b> The router keeps screens alive and only
    /// toggles <c>display</c>, so a missing teardown leaves a clip streaming behind a
    /// hidden screen for the rest of the session — and the player is SHARED with the
    /// Espécie screen, so it would also be holding a URL that screen then has to
    /// displace.</item>
    /// <item><b>The taps are wired to the events, not to a snapshot of them.</b> The
    /// constructor renders once, before any subscriber attaches, so a row that captured
    /// the event's value at build time would be wired to null forever.</item>
    /// </list>
    ///
    /// <para><b>What these tests cannot see: layout.</b> EditMode has no panel, so
    /// nothing below knows whether a heading collides with the paragraph under it or
    /// whether a bullet row wraps at 360dp. <c>make ds-shots SHOT=conteudo</c> is the
    /// check for that. Taps are expressed as direct calls, the same convention
    /// <see cref="EspecieScreenTests"/> and <see cref="ReportScreenTests"/> follow.</para>
    /// </summary>
    public class ArticleScreenTests
    {
        // ---- Fakes -----------------------------------------------------------

        sealed class FakeCatalog : IArticleCatalog
        {
            public readonly List<Article> Items = new List<Article>();
            public bool Available = true;

            public IReadOnlyList<ArticleSummary> All() => Items.Select(a => a.ToSummary()).ToList();

            public Article Find(string id)
            {
                foreach (var item in Items) if (item.Id == id) return item;
                return null;
            }

            public bool IsAvailable => Available;
        }

        sealed class FakeSpeciesCatalog : ISpeciesCatalog
        {
            public readonly List<SpeciesInfo> Items = new List<SpeciesInfo>();
            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
            }
        }

        sealed class FakeBeachCatalog : IBeachCatalog
        {
            public readonly List<BeachInfo> Items = new List<BeachInfo>();
            public IReadOnlyList<BeachInfo> Beaches => Items;
        }

        sealed class FakePlayback : IVideoPlayback
        {
            public bool Available = true;
            public int StopCount;
            public int PauseCount;
            public readonly List<string> Played = new List<string>();
            public double Duration = 42d;
            public double Position;
            public readonly List<double> Seeks = new List<double>();

            public bool IsAvailable => Available;
            public string Url { get; private set; }
            public VideoPlaybackState State { get; set; } = VideoPlaybackState.Idle;
            public Texture Texture => null;
            public double PositionSeconds => State == VideoPlaybackState.Idle ? 0d : Position;
            public double DurationSeconds => State == VideoPlaybackState.Idle ? 0d : Duration;

            public event Action Changed;
            public void Raise() => Changed?.Invoke();

            public void Play(string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                Played.Add(url);
                Url = url;
                State = VideoPlaybackState.Playing;
                Changed?.Invoke();
            }

            public void Pause()
            {
                PauseCount++;
                if (State != VideoPlaybackState.Playing) return;
                State = VideoPlaybackState.Paused;
                Changed?.Invoke();
            }

            public void Stop()
            {
                StopCount++;
                Url = null;
                State = VideoPlaybackState.Idle;
                Changed?.Invoke();
            }

            public void Seek(double seconds) => Seeks.Add(seconds);
        }

        // ---- Block builders --------------------------------------------------

        static ArticleBlock Heading(int level, string text) =>
            new ArticleBlock { Kind = ArticleBlockKind.Heading, Level = level, Text = text };

        static ArticleBlock Paragraph(string text) =>
            new ArticleBlock { Kind = ArticleBlockKind.Paragraph, Text = text };

        static ArticleBlock Bullets(params string[] items) =>
            new ArticleBlock { Kind = ArticleBlockKind.BulletList, Items = items };

        static ArticleBlock Numbered(params string[] items) =>
            new ArticleBlock { Kind = ArticleBlockKind.NumberedList, Items = items };

        static ArticleBlock Callout(ArticleCalloutTone tone, string text) =>
            new ArticleBlock { Kind = ArticleBlockKind.Callout, Tone = tone, Text = text };

        static ArticleBlock Quote(string text, string attribution = null) =>
            new ArticleBlock { Kind = ArticleBlockKind.Quote, Text = text, Attribution = attribution };

        static ArticleBlock Figure(string src, string caption = null, string credit = null) =>
            new ArticleBlock { Kind = ArticleBlockKind.Image, Src = src, Caption = caption, Credit = credit };

        static ArticleBlock Video(string url, string title = null) =>
            new ArticleBlock { Kind = ArticleBlockKind.Video, Url = url, Title = title };

        static ArticleBlock SpeciesRef(string key) =>
            new ArticleBlock { Kind = ArticleBlockKind.SpeciesRef, SpeciesKey = key };

        static ArticleBlock BeachRef(string name) =>
            new ArticleBlock { Kind = ArticleBlockKind.BeachRef, BeachName = name };

        static Article NewArticle(string id, string hero, string heroCredit, params ArticleBlock[] blocks) =>
            new Article
            {
                Id = id,
                Title = "Os tubarões",
                Summary = "Quem vive aqui.",
                Category = "Espécies",
                HeroImage = hero,
                HeroCredit = heroCredit,
                Order = 10,
                Updated = new DateTime(2026, 9, 29),
                WordCount = 400,
                Blocks = blocks,
            };

        // ---- Harness ---------------------------------------------------------

        FakeCatalog catalog;
        FakeSpeciesCatalog species;
        FakeBeachCatalog beaches;
        FakePlayback playback;
        ArticleViewModel vm;

        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly HashSet<string> resolvingPaths = new HashSet<string>(StringComparer.Ordinal);

        [SetUp]
        public void SetUp()
        {
            catalog = new FakeCatalog();
            species = new FakeSpeciesCatalog();
            species.Items.Add(new SpeciesInfo { Key = "tiger_shark", DisplayName = "Tubarão-tigre" });
            species.Items.Add(new SpeciesInfo { Key = "hammerhead", DisplayName = "Tubarão-martelo" });
            beaches = new FakeBeachCatalog();
            beaches.Items.Add(new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" });
            // No displayName: BeachInfo.DisplayName falls back to Name, so this is the
            // "key is also the label" case, which several places.json entries really are.
            beaches.Items.Add(new BeachInfo { Name = "Baía dos Porcos" });
            playback = new FakePlayback();
            resolvingPaths.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            vm?.Dispose();
            foreach (var o in owned)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            owned.Clear();
        }

        /// <summary>A minimal real sprite — the branch under test is only "the loader
        /// returned something"; nothing in EditMode measures or draws it.</summary>
        Sprite NewSprite()
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            owned.Add(texture);
            owned.Add(sprite);
            return sprite;
        }

        /// <summary>Marks a Resources path the loader should answer with an image.
        /// Everything else comes back null, which is the honest default.</summary>
        void Resolves(params string[] paths)
        {
            foreach (var path in paths) resolvingPaths.Add(path);
        }

        ArticleScreen NewScreen()
        {
            vm = new ArticleViewModel(catalog);
            return new ArticleScreen(vm, playback, species, beaches,
                path => path != null && resolvingPaths.Contains(path) ? NewSprite() : null);
        }

        static bool Visible(VisualElement e) => e != null && e.style.display.value == DisplayStyle.Flex;

        static List<VisualElement> Blocks(ArticleScreen s) =>
            s.Query<VisualElement>(className: "mv-conteudo__block").ToList();

        static List<VisualElement> Refs(ArticleScreen s) =>
            s.Query<VisualElement>(className: "mv-conteudo__ref").ToList();

        // ---- Contract --------------------------------------------------------

        [Test]
        public void Key_IsTheConteudoRoute()
        {
            Assert.That(NewScreen().Key, Is.EqualTo(AppRoutes.Conteudo));
        }

        [Test]
        public void TheRootIsTransparentAndTheContentIsThePage()
        {
            var screen = NewScreen();
            Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(screen.Q(className: "mv-conteudo__content"), Is.Not.Null);
        }

        /// <summary>An article page has nothing to select — no beach pill — and it is
        /// always pushed, so it always has a way back. Compact for the same reason
        /// Espécie is: the covers are landscape photographs.</summary>
        [Test]
        public void TheHeroIsACompactPhotoWithABackButtonAndNothingElse()
        {
            var hero = NewScreen().Q<MvHeroHeader>();

            Assert.That(hero, Is.Not.Null);
            Assert.That(hero.ShowBackButton, Is.True);
            Assert.That(hero.ShowSelector, Is.False);
            Assert.That(hero.Compact, Is.True);
        }

        /// <summary>
        /// The no-hero back row uses the SAME control and the SAME glyph constant the
        /// hero's floating button does, so the two cannot drift into two arrows.
        /// </summary>
        [Test]
        public void TheNoHeroBackRowUsesTheSameGlyphAsTheHero()
        {
            var back = NewScreen().Q<MdIconButton>(className: "mv-conteudo__nav-back");

            Assert.That(back, Is.Not.Null);
            Assert.That(back.Icon, Is.EqualTo(MvHeroHeader.DefaultBackIconName));
        }

        /// <summary>
        /// Exactly one back affordance is drawn. A hero with no image is a 240dp empty
        /// box — its foreground is absolutely positioned, so it cannot collapse to its
        /// content — and a blank photo frame above the headline reads as a failed load.
        /// </summary>
        [Test]
        public void WithAHero_TheHeroIsDrawnAndTheNavRowIsNot()
        {
            Resolves("Animals/tiger_shark");
            catalog.Items.Add(NewArticle("a", "Animals/tiger_shark", null, Paragraph("Texto.")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(Visible(screen.Q<MvHeroHeader>()), Is.True);
            Assert.That(Visible(screen.Q(className: "mv-conteudo__nav-row")), Is.False);
        }

        [Test]
        public void WithNoHero_TheNavRowIsDrawnAndTheHeroIsNot()
        {
            catalog.Items.Add(NewArticle("a", null, null, Paragraph("Texto.")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(Visible(screen.Q<MvHeroHeader>()), Is.False);
            Assert.That(Visible(screen.Q(className: "mv-conteudo__nav-row")), Is.True);
        }

        /// <summary>A hero path that does not resolve is the same case as no hero —
        /// not a 240dp grey box with a credit under it.</summary>
        [Test]
        public void AHeroThatDoesNotResolve_FallsBackToTheNavRow_AndDropsItsCredit()
        {
            catalog.Items.Add(NewArticle("a", "Animals/nao_existe", "Foto: Alguém / CC BY 4.0", Paragraph("Texto.")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(Visible(screen.Q<MvHeroHeader>()), Is.False);
            Assert.That(Visible(screen.Q(className: "mv-conteudo__nav-row")), Is.True);
            Assert.That(Visible(screen.Q<Label>(className: "mv-conteudo__credit")), Is.False,
                "a credit for an image nobody can see credits nothing");
        }

        /// <summary>
        /// The top inset goes to BOTH back affordances because only one is ever visible;
        /// it must not become padding on the content container, which would paint a
        /// blank strip above a full-bleed photo.
        /// </summary>
        [Test]
        public void EdgeInsets_ReachTheHeroAndTheNavRow_NotTheContentTop()
        {
            var screen = NewScreen();
            screen.SetEdgeInsets(47f, 3f, 4f, 34f);

            var content = screen.Q(className: "mv-conteudo__content");
            Assert.That(screen.Q<MvHeroHeader>().TopInset, Is.EqualTo(47f));
            Assert.That(screen.Q(className: "mv-conteudo__nav-row").style.paddingTop.value.value,
                Is.EqualTo(47f));
            Assert.That(content.style.paddingTop.keyword, Is.EqualTo(StyleKeyword.Null),
                "the photo runs under the status bar");
            Assert.That(content.style.paddingLeft.value.value, Is.EqualTo(3f));
            Assert.That(content.style.paddingRight.value.value, Is.EqualTo(4f));
            Assert.That(screen.style.paddingBottom.value.value, Is.EqualTo(34f));
        }

        // ---- Header ----------------------------------------------------------

        [Test]
        public void TheHeaderBindsEveryStringFromTheViewModel()
        {
            Resolves("Animals/tiger_shark");
            catalog.Items.Add(NewArticle("a", "Animals/tiger_shark",
                "Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)", Paragraph("Texto.")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudo__title").text, Is.EqualTo(vm.TitleText));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__summary").text, Is.EqualTo(vm.SummaryText));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__meta-label").text, Is.EqualTo(vm.MetaText));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__updated").text, Is.EqualTo(vm.UpdatedText));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__credit").text, Is.EqualTo(vm.HeroCreditText));
        }

        /// <summary>The meta line is the ViewModel's joined one, so the category is not
        /// printed twice (once as its own label and once inside the join).</summary>
        [Test]
        public void TheMetaLineIsTheJoinedOne_NotTwoElements()
        {
            catalog.Items.Add(NewArticle("a", null, null, Paragraph("Texto.")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Query<Label>(className: "mv-conteudo__meta-label").ToList().Count,
                Is.EqualTo(1));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__meta-label").text,
                Is.EqualTo(ArticleFormatter.MetaLine(vm.ReadingTimeText, vm.CategoryText)));
        }

        [Test]
        public void WithNoSummaryAndNoUpdatedDate_ThoseLinesAreAbsent()
        {
            var article = NewArticle("a", null, null, Paragraph("Texto."));
            article.Summary = null;
            article.Updated = null;
            catalog.Items.Add(article);

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(Visible(screen.Q<Label>(className: "mv-conteudo__summary")), Is.False);
            Assert.That(Visible(screen.Q<Label>(className: "mv-conteudo__updated")), Is.False);
        }

        /// <summary>Only reachable before the first push — the host sets the ViewModel
        /// and only navigates if the id resolved. It still must not render half a page.</summary>
        [Test]
        public void WithNoArticle_TheBodyIsNotDrawn()
        {
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Visible(screen.Q(className: "mv-conteudo__body")), Is.False);
            Assert.That(Blocks(screen), Is.Empty);
        }

        // ---- Every block kind ------------------------------------------------

        /// <summary>
        /// The census: one element per renderable block, in authoring order, for every
        /// kind the feature has. The count is the assertion that matters — a kind that
        /// silently produced nothing would pass every per-kind test below.
        /// </summary>
        [Test]
        public void EveryBlockKindProducesExactlyOneElement_InOrder()
        {
            Resolves("Beaches/sancho");
            catalog.Items.Add(NewArticle("a", null, null,
                Heading(2, "Seção"),
                Paragraph("Prosa."),
                Bullets("um", "dois"),
                Numbered("passo um", "passo dois"),
                Callout(ArticleCalloutTone.Info, "Nota."),
                Quote("Citação.", "Autor"),
                Figure("Beaches/sancho", "Legenda.", "Foto: Alguém / CC BY 4.0"),
                Video("https://example.test/a.mp4", "Clipe"),
                SpeciesRef("tiger_shark"),
                BeachRef("Sueste Beach")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(vm.Blocks.Count, Is.EqualTo(10));
            Assert.That(vm.SkippedBlockCount, Is.Zero);
            Assert.That(Blocks(screen).Count, Is.EqualTo(10),
                "one element per renderable block — a kind drawing nothing would fail here");
        }

        /// <summary>
        /// The forward-compatibility contract. A block type from a newer articles.json
        /// arrives as <see cref="ArticleBlockKind.Unknown"/>; the ViewModel filters it
        /// out and the screen therefore cannot draw a placeholder for it — which is only
        /// true because the screen binds <c>Blocks</c> and not <c>Current.Blocks</c>.
        /// </summary>
        [Test]
        public void AnUnknownBlockKindDrawsNothingAtAll()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Paragraph("Antes."),
                new ArticleBlock { Kind = ArticleBlockKind.Unknown, Text = "de um build mais novo" },
                Paragraph("Depois.")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(vm.SkippedBlockCount, Is.EqualTo(1));
            Assert.That(Blocks(screen).Count, Is.EqualTo(2));
            Assert.That(screen.Query<Label>(className: "mv-conteudo__paragraph").ToList()
                .Select(l => l.text), Is.EqualTo(new[] { "Antes.", "Depois." }));
        }

        [Test]
        public void HeadingLevelsTakeDifferentTypeScaleRungs()
        {
            catalog.Items.Add(NewArticle("a", null, null, Heading(2, "Dois"), Heading(3, "Três")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var headings = screen.Query<Label>(className: "mv-conteudo__heading").ToList();
            Assert.That(headings.Count, Is.EqualTo(2));
            Assert.That(headings[0].ClassListContains("md-typescale-title-medium"), Is.True);
            Assert.That(headings[0].ClassListContains("mv-conteudo__heading--2"), Is.True);
            Assert.That(headings[1].ClassListContains("md-typescale-title-small"), Is.True);
            Assert.That(headings[1].ClassListContains("mv-conteudo__heading--3"), Is.True);
        }

        /// <summary>
        /// Rich text reaches the label UNCHANGED. The build script escapes author text
        /// before inserting its own three tags, so re-escaping here would print the tags
        /// and stripping them would drop the author's emphasis.
        /// </summary>
        [Test]
        public void AParagraphsRichTextIsPassedThroughVerbatim()
        {
            const string text = "O <b>Mergulho Virtual</b> e o <i>mar</i>, em " +
                                "<a href=\"https://mergulhovirtual.dev\">mergulhovirtual.dev</a>.";
            catalog.Items.Add(NewArticle("a", null, null, Paragraph(text)));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text, Is.EqualTo(text));
        }

        /// <summary>
        /// The build script's neutralised angle brackets reach the label untouched too.
        /// It wraps each author <c>&lt;</c> in <c>&lt;noparse&gt;</c> (and leaves
        /// <c>&amp;</c> and <c>&gt;</c> alone, because UI Toolkit decodes no HTML
        /// entities — <c>&amp;amp;</c> would print as five characters). Whether that
        /// renders is a question only a shot can answer; the screen's job is simply not
        /// to second-guess it.
        /// </summary>
        [Test]
        public void EscapedAngleBracketsAreNotReEscapedNorUnwrappedByTheScreen()
        {
            const string text = "Foto: <noparse><</noparse>autor> / <noparse><</noparse>licença> " +
                                "(<noparse><</noparse>fonte>) & a fonte.";
            catalog.Items.Add(NewArticle("a", null, null, Paragraph(text)));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text, Is.EqualTo(text));
        }

        [Test]
        public void ABulletListIsOneRowPerItem_WithTheViewModelsMarker()
        {
            catalog.Items.Add(NewArticle("a", null, null, Bullets("um", "dois", "três")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var rows = screen.Query<VisualElement>(className: "mv-conteudo__bullet").ToList();
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows.Select(r => r.Q<Label>(className: "mv-conteudo__bullet-text").text),
                Is.EqualTo(new[] { "um", "dois", "três" }));
            foreach (var row in rows)
            {
                Assert.That(row.Q<Label>(className: "mv-conteudo__bullet-marker").text,
                    Is.EqualTo(vm.BulletMarker), "the screen may not spell even a bullet");
            }
            Assert.That(rows[0].ClassListContains("mv-conteudo__bullet--gutter"), Is.False);
            Assert.That(rows[1].ClassListContains("mv-conteudo__bullet--gutter"), Is.True);
        }

        /// <summary>The design system's component, which numbers its own rows — see the
        /// note on ArticleScreen.BuildNumberedList for why ListNumberFor is unused.</summary>
        [Test]
        public void ANumberedListIsTheDesignSystemComponent()
        {
            catalog.Items.Add(NewArticle("a", null, null, Numbered("passo um", "passo dois")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var list = screen.Q<MvNumberedList>();
            Assert.That(list, Is.Not.Null);
            Assert.That(list.ItemCount, Is.EqualTo(2));
            Assert.That(list.ItemTextAt(0), Is.EqualTo("passo um"));
            Assert.That(list.ItemTextAt(1), Is.EqualTo("passo dois"));
        }

        /// <summary>
        /// All four tones, each with its own token family, its own glyph and its own
        /// NAME — the label is what keeps the distinction for a reader who cannot see
        /// the tint.
        /// </summary>
        [Test]
        public void EveryCalloutToneGetsItsOwnClassGlyphAndLabel()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Callout(ArticleCalloutTone.Info, "i"),
                Callout(ArticleCalloutTone.Warning, "w"),
                Callout(ArticleCalloutTone.Success, "s"),
                Callout(ArticleCalloutTone.Error, "e")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var callouts = screen.Query<VisualElement>(className: "mv-conteudo__callout").ToList();
            Assert.That(callouts.Count, Is.EqualTo(4));

            var expected = new[]
            {
                ("mv-conteudo__callout--info", "info", ArticleCalloutTone.Info),
                ("mv-conteudo__callout--warning", "warning", ArticleCalloutTone.Warning),
                ("mv-conteudo__callout--success", "check_circle", ArticleCalloutTone.Success),
                ("mv-conteudo__callout--error", "error", ArticleCalloutTone.Error),
            };
            for (int i = 0; i < expected.Length; i++)
            {
                var (cls, glyph, tone) = expected[i];
                Assert.That(callouts[i].ClassListContains(cls), Is.True, cls);
                Assert.That(callouts[i].Q<MdIcon>(className: "mv-conteudo__callout-icon").Icon,
                    Is.EqualTo(glyph));
                Assert.That(callouts[i].Q<Label>(className: "mv-conteudo__callout-tone").text,
                    Is.EqualTo(vm.CalloutLabelFor(tone)));
            }
        }

        [Test]
        public void AnAttributedQuoteShowsItsAuthor_AnUnattributedOneShowsNoLine()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Quote("Com autor.", "Equipe do Mergulho Virtual"),
                Quote("Sem autor.")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var quotes = screen.Query<VisualElement>(className: "mv-conteudo__quote").ToList();
            Assert.That(quotes.Count, Is.EqualTo(2));
            Assert.That(quotes[0].Q<Label>(className: "mv-conteudo__quote-author").text,
                Is.EqualTo(ArticleFormatter.QuoteAttribution("Equipe do Mergulho Virtual")));
            Assert.That(quotes[1].Q(className: "mv-conteudo__quote-author"), Is.Null,
                "a bare em dash on its own line is worse than no line");
        }

        // ---- Figures ---------------------------------------------------------

        [Test]
        public void AFigureWithACaptionAndACreditDrawsBoth()
        {
            Resolves("Beaches/sancho");
            catalog.Items.Add(NewArticle("a", null, null,
                Figure("Beaches/sancho", "A praia ao amanhecer.", "Foto: Alguém / CC BY-SA 4.0")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var figure = screen.Q(className: "mv-conteudo__figure");
            Assert.That(figure, Is.Not.Null);
            Assert.That(figure.Q<Label>(className: "mv-conteudo__figure-caption").text,
                Is.EqualTo("A praia ao amanhecer."));
            Assert.That(figure.Q<Label>(className: "mv-conteudo__credit").text,
                Is.EqualTo("Foto: Alguém / CC BY-SA 4.0"));
        }

        /// <summary>
        /// The real case: one of the three shipped image blocks has NEITHER. Two blank
        /// lines under a photo read as a rendering fault, so both elements are absent
        /// rather than empty.
        /// </summary>
        [Test]
        public void AFigureWithNoCaptionAndNoCreditDrawsNeitherElement()
        {
            Resolves("Beaches/sancho");
            catalog.Items.Add(NewArticle("a", null, null, Figure("Beaches/sancho")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var figure = screen.Q(className: "mv-conteudo__figure");
            Assert.That(figure, Is.Not.Null, "the photograph is still drawn");
            Assert.That(figure.Q(className: "mv-conteudo__figure-caption"), Is.Null);
            Assert.That(figure.Q(className: "mv-conteudo__credit"), Is.Null);
        }

        /// <summary>An image nobody can see is not a figure, and its caption describes
        /// something that is not on screen — so the whole block goes.</summary>
        [Test]
        public void AFigureWhoseSpriteDoesNotResolveDrawsNothingAtAll()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Paragraph("Antes."),
                Figure("Beaches/nao_existe", "Uma legenda órfã.", "Foto: Alguém / CC0"),
                Paragraph("Depois.")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q(className: "mv-conteudo__figure"), Is.Null);
            Assert.That(screen.Q(className: "mv-conteudo__figure-caption"), Is.Null);
            Assert.That(Blocks(screen).Count, Is.EqualTo(2), "only the two paragraphs");
        }

        /// <summary>A credit must never be dimmed. Only the inline half is visible to a
        /// structure test; the stylesheet half is a comment and a review item.</summary>
        [Test]
        public void FigureCreditsCarryNoInlineOpacity()
        {
            Resolves("Beaches/sancho");
            catalog.Items.Add(NewArticle("a", null, null,
                Figure("Beaches/sancho", null, "Foto: Alguém / CC BY 4.0")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var credit = screen.Q<Label>(className: "mv-conteudo__credit");
            Assert.That(credit.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // ---- Cross-references ------------------------------------------------

        /// <summary>
        /// The row names the destination and carries the KEY. Five consecutive species
        /// references is the real shape of the tubarões article, and with the fixed label
        /// alone they would be five identical rows.
        /// </summary>
        [Test]
        public void ASpeciesRefNamesTheSpecies_AndTheLabelIsTheFixedOne()
        {
            catalog.Items.Add(NewArticle("a", null, null, SpeciesRef("tiger_shark")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var row = Refs(screen).Single();
            Assert.That(row.Q<Label>(className: "mv-conteudo__ref-name").text, Is.EqualTo("Tubarão-tigre"));
            Assert.That(row.Q<Label>(className: "mv-conteudo__ref-label").text,
                Is.EqualTo(vm.SpeciesRefLabel));
            Assert.That(row.ClassListContains("mv-conteudo__ref--species"), Is.True);
        }

        /// <summary>
        /// A key the catalog does not have leaves the fixed label alone. The MACHINE KEY
        /// must never appear: it is ASCII English and the contract forbids showing it.
        /// </summary>
        [Test]
        public void AnUnknownSpeciesKeyIsNeverRendered()
        {
            catalog.Items.Add(NewArticle("a", null, null, SpeciesRef("whale_shark")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var row = Refs(screen).Single();
            Assert.That(row.Q(className: "mv-conteudo__ref-name"), Is.Null);
            Assert.That(row.Q<Label>(className: "mv-conteudo__ref-label").text,
                Is.EqualTo(vm.SpeciesRefLabel));
            foreach (var label in row.Query<Label>().ToList())
                Assert.That(label.text, Does.Not.Contain("whale_shark"));
        }

        /// <summary>The beach row answers with DisplayName, which falls back to Name for
        /// an entry with no pt-BR label — several places.json entries are like that.</summary>
        [Test]
        public void ABeachRefUsesTheDisplayName_FallingBackToTheKeyOnlyWhenTheyAreTheSame()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                BeachRef("Sueste Beach"), BeachRef("Baía dos Porcos")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var rows = Refs(screen);
            Assert.That(rows[0].Q<Label>(className: "mv-conteudo__ref-name").text,
                Is.EqualTo("Baía do Sueste"), "NOT the English machine key");
            Assert.That(rows[1].Q<Label>(className: "mv-conteudo__ref-name").text,
                Is.EqualTo("Baía dos Porcos"));
            Assert.That(rows[0].Q<Label>(className: "mv-conteudo__ref-label").text,
                Is.EqualTo(vm.BeachRefLabel));
        }

        /// <summary>
        /// The wiring, driven through the internal entry points the Clickables call.
        /// Crucially the subscriber attaches AFTER the constructor has rendered once —
        /// a row that captured the event's value at build time would be wired to null.
        /// </summary>
        [Test]
        public void TappingARefRaisesTheKey_ForASubscriberAddedAfterConstruction()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                SpeciesRef("tiger_shark"), BeachRef("Sueste Beach")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var speciesKeys = new List<string>();
            var beachNames = new List<string>();
            screen.SpeciesRequested += speciesKeys.Add;
            screen.BeachRequested += beachNames.Add;

            screen.RaiseSpeciesRequested("tiger_shark");
            screen.RaiseBeachRequested("Sueste Beach");

            Assert.That(speciesKeys, Is.EqualTo(new[] { "tiger_shark" }));
            Assert.That(beachNames, Is.EqualTo(new[] { "Sueste Beach" }));
        }

        [Test]
        public void ARefWithNoPayloadRaisesNothing()
        {
            var screen = NewScreen();
            int raised = 0;
            screen.SpeciesRequested += _ => raised++;
            screen.BeachRequested += _ => raised++;

            screen.RaiseSpeciesRequested(null);
            screen.RaiseSpeciesRequested("");
            screen.RaiseBeachRequested(null);

            Assert.That(raised, Is.Zero);
        }

        /// <summary>A run of cross-references is one list; USS has no adjacent-sibling
        /// combinator, so the tighter gutter is a modifier the screen adds — never to
        /// the first of the run, and never to a reference that follows prose.</summary>
        [Test]
        public void OnlyAReferenceFollowingAnotherReferenceGetsTheTightGutter()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Paragraph("Prosa."),
                SpeciesRef("tiger_shark"),
                SpeciesRef("hammerhead"),
                Paragraph("Mais prosa."),
                BeachRef("Sueste Beach")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var rows = Refs(screen);
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows[0].ClassListContains("mv-conteudo__block--tight"), Is.False,
                "the first of a run keeps the normal block rhythm");
            Assert.That(rows[1].ClassListContains("mv-conteudo__block--tight"), Is.True);
            Assert.That(rows[2].ClassListContains("mv-conteudo__block--tight"), Is.False,
                "it follows a paragraph, not another reference");
        }

        [Test]
        public void EveryRefRowIsFocusableAndCarriesAStateLayer()
        {
            catalog.Items.Add(NewArticle("a", null, null, SpeciesRef("tiger_shark")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var row = Refs(screen).Single();
            Assert.That(row.focusable, Is.True);
            Assert.That(row.Q(className: "md-state-layer"), Is.Not.Null);
        }

        // ---- Video -----------------------------------------------------------

        [Test]
        public void AVideoBlockBuildsACardWithItsCaption()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Video("https://example.test/a.mp4", "Tubarão-limão em ação")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            var card = screen.Q(className: "mv-conteudo__video-card");
            Assert.That(card, Is.Not.Null);
            var title = card.Q<Label>(className: "mv-conteudo__video-title");
            Assert.That(Visible(title), Is.True);
            Assert.That(title.text, Is.EqualTo("Tubarão-limão em ação"));
        }

        [Test]
        public void AVideoWithNoCaptionHidesTheCaptionLine()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(Visible(screen.Q<Label>(className: "mv-conteudo__video-title")), Is.False);
        }

        /// <summary>No player wired — the screenshot harness, or a host that built none.
        /// A play control that cannot play is worse than no card.</summary>
        [Test]
        public void WithNoPlayerAvailable_TheVideoBlockIsDropped()
        {
            playback.Available = false;
            catalog.Items.Add(NewArticle("a", null, null,
                Paragraph("Antes."), Video("https://example.test/a.mp4")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q(className: "mv-conteudo__video-card"), Is.Null);
            Assert.That(Blocks(screen).Count, Is.EqualTo(1), "only the paragraph");
        }

        /// <summary>The idle control says "Assistir" and shows a play glyph, from the
        /// formatter — the screen spells neither.</summary>
        [Test]
        public void AnIdleCardShowsTheWatchLabelAndAnEmptyTrack()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudo__video-action-label").text,
                Is.EqualTo(ArticleFormatter.WatchLabel));
            Assert.That(screen.Q<MdIcon>(className: "mv-conteudo__video-action-icon").Icon,
                Is.EqualTo("play_arrow"));
            Assert.That(screen.Q(className: "mv-conteudo__video-track").style.visibility.value,
                Is.EqualTo(Visibility.Hidden), "no track before anything is playing");
        }

        /// <summary>
        /// A tap plays, and the control switches to the pause wording — the state the
        /// Espécie card had to be fixed for, because hiding the overlay while a clip ran
        /// left the card with no control at all.
        /// </summary>
        [Test]
        public void TappingTheCardPlaysIt_AndTheControlBecomesPause()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            screen.ToggleVideo(0);

            Assert.That(playback.Played, Is.EqualTo(new[] { "https://example.test/a.mp4" }));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__video-action-label").text,
                Is.EqualTo(SpeciesMediaFormatter.PauseLabel));
            Assert.That(screen.Q<MdIcon>(className: "mv-conteudo__video-action-icon").Icon,
                Is.EqualTo("pause"));
            Assert.That(screen.Q(className: "mv-conteudo__video-track").style.visibility.value,
                Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void TappingAPlayingCardPausesIt_AndTheControlBecomesResume()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            screen.ToggleVideo(0);
            screen.ToggleVideo(0);

            Assert.That(playback.PauseCount, Is.EqualTo(1));
            Assert.That(playback.Played.Count, Is.EqualTo(1), "a pause must not restart the stream");
            Assert.That(screen.Q<Label>(className: "mv-conteudo__video-action-label").text,
                Is.EqualTo(SpeciesMediaFormatter.ResumeLabel));
        }

        /// <summary>
        /// Which card owns the player is read off <see cref="IVideoPlayback.Url"/>, so a
        /// second card is Idle by construction and the screen keeps no index of its own
        /// to disagree with.
        /// </summary>
        [Test]
        public void OnlyTheCardThatOwnsThePlayerShowsPlayingState()
        {
            catalog.Items.Add(NewArticle("a", null, null,
                Video("https://example.test/a.mp4"), Video("https://example.test/b.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            screen.ToggleVideo(1);

            var labels = screen.Query<Label>(className: "mv-conteudo__video-action-label").ToList();
            Assert.That(labels[0].text, Is.EqualTo(ArticleFormatter.WatchLabel));
            Assert.That(labels[1].text, Is.EqualTo(SpeciesMediaFormatter.PauseLabel));

            var tracks = screen.Query<VisualElement>(className: "mv-conteudo__video-track").ToList();
            Assert.That(tracks[0].style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(tracks[1].style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        /// <summary>A failure is an ordinary state with an ordinary line — and on a Linux
        /// editor it is the ONLY state, since Unity cannot decode H.264 there.</summary>
        [Test]
        public void AFailedStreamShowsTheInlineErrorAndARetryControl()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();

            screen.ToggleVideo(0);
            playback.State = VideoPlaybackState.Failed;
            playback.Raise();

            var error = screen.Q<Label>(className: "mv-conteudo__video-error");
            Assert.That(Visible(error), Is.True);
            Assert.That(error.text, Is.EqualTo(ArticleFormatter.VideoErrorText));
            Assert.That(screen.Q<Label>(className: "mv-conteudo__video-action-label").text,
                Is.EqualTo(SpeciesMediaFormatter.RetryLabel));
            Assert.That(screen.Q(className: "mv-conteudo__video-track").style.visibility.value,
                Is.EqualTo(Visibility.Hidden), "nothing to seek in a stream that failed");
        }

        [Test]
        public void ThePlaybackClockComesFromTheFormatter()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            playback.Duration = 125d;
            playback.Position = 30d;

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();
            screen.ToggleVideo(0);

            Assert.That(screen.Q<Label>(className: "mv-conteudo__video-time").text,
                Is.EqualTo(ArticleFormatter.VideoTimeText(30d, 125d)));
        }

        // ---- Lifecycle -------------------------------------------------------

        /// <summary>
        /// The load-bearing one. The router only toggles `display`, so a clip left
        /// playing keeps streaming behind a hidden screen — and the player is shared
        /// with the Espécie screen.
        /// </summary>
        [Test]
        public void OnExit_StopsTheStream()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();
            screen.ToggleVideo(0);
            Assert.That(playback.Url, Is.EqualTo("https://example.test/a.mp4"));

            screen.OnExit();

            Assert.That(playback.StopCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(playback.Url, Is.Null);
            Assert.That(playback.State, Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>
        /// An article can be opened FROM another article, so the body can change without
        /// the screen being left — and the clip playing belongs to the old body.
        /// </summary>
        [Test]
        public void ChangingArticleStopsTheClipAndRebuildsTheBody()
        {
            catalog.Items.Add(NewArticle("a", null, null, Video("https://example.test/a.mp4")));
            catalog.Items.Add(NewArticle("b", null, null, Paragraph("Outro texto.")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();
            screen.ToggleVideo(0);
            Assert.That(playback.State, Is.EqualTo(VideoPlaybackState.Playing));

            vm.Show("b");

            Assert.That(playback.State, Is.EqualTo(VideoPlaybackState.Idle));
            Assert.That(screen.Q(className: "mv-conteudo__video-card"), Is.Null);
            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text,
                Is.EqualTo("Outro texto."));
        }

        [Test]
        public void OnExit_UnsubscribesSoALaterChangeDoesNotRepaintAHiddenScreen()
        {
            catalog.Items.Add(NewArticle("a", null, null, Paragraph("Primeiro.")));
            catalog.Items.Add(NewArticle("b", null, null, Paragraph("Segundo.")));

            var screen = NewScreen();
            vm.Show("a");
            screen.OnEnter();
            screen.OnExit();

            vm.Show("b");
            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text,
                Is.EqualTo("Primeiro."), "the screen is not listening");

            screen.OnEnter();
            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text,
                Is.EqualTo("Segundo."), "and catches up on the next visit");
        }

        /// <summary>A screen shown before its first OnEnter must not render an empty
        /// page — the constructor paints once without subscribing.</summary>
        [Test]
        public void TheConstructorPaintsOnce_WithoutSubscribing()
        {
            catalog.Items.Add(NewArticle("a", null, null, Paragraph("Texto.")));
            vm = new ArticleViewModel(catalog);
            vm.Show("a");

            var screen = new ArticleScreen(vm, playback, species, beaches, _ => null);

            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text, Is.EqualTo("Texto."));
        }

        /// <summary>The whole screen is optional-dependency tolerant: no player, no
        /// catalogs, no sprite loader. It must still render the text.</summary>
        [Test]
        public void WithNoServicesAtAll_TheTextStillRenders()
        {
            catalog.Items.Add(NewArticle("a", "Animals/tiger_shark", "Foto: X / CC0",
                Paragraph("Texto."), SpeciesRef("tiger_shark"), Video("https://example.test/a.mp4"),
                Figure("Beaches/sancho", "Legenda.")));

            vm = new ArticleViewModel(catalog);
            vm.Show("a");
            var screen = new ArticleScreen(vm);
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudo__paragraph").text, Is.EqualTo("Texto."));
            Assert.That(Refs(screen).Count, Is.EqualTo(1), "the ref row survives without a catalog");
            Assert.That(Refs(screen)[0].Q(className: "mv-conteudo__ref-name"), Is.Null);
            Assert.That(screen.Q(className: "mv-conteudo__video-card"), Is.Null, "no player");
            Assert.That(screen.Q(className: "mv-conteudo__figure"), Is.Null, "no sprite loader");
        }
    }
}
