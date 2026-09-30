using System;
using System.Collections.Generic;
using System.Linq;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The "Conteúdo educativo" index. Four things here are structural rather than
    /// cosmetic, and <b>none of them shows up in a screenshot</b>:
    ///
    /// <list type="number">
    /// <item><b>The card carries the article id, not the label.</b> A card that raised
    /// its title, or a bare route, would open the reader on the wrong article — or on
    /// whatever was there last.</item>
    /// <item><b>Category grouping is the ViewModel's order.</b> The screen must walk
    /// <c>Groups</c> and never sort, or the author's one ordering lever
    /// (front-matter <c>order</c>) silently becomes the Portuguese alphabet.</item>
    /// <item><b>The state view replaces the list and draws no button.</b> Neither empty
    /// nor unavailable is retryable, so a button would be a lie the user pays for with
    /// a tap.</item>
    /// <item><b>A cover that does not resolve produces a TEXT-ONLY card</b>, not an
    /// empty grey box — and its credit goes with it, since a credit for an image nobody
    /// can see credits nothing.</item>
    /// </list>
    ///
    /// <para><b>What these tests cannot see: layout.</b> EditMode has no panel, so
    /// nothing below knows whether the header collides with the first card or whether
    /// the 24px headline wraps at 360dp. <c>make ds-shots SHOT=conteudos</c> is the
    /// check for that, not this file. Taps are likewise expressed as direct calls — a
    /// <c>Clickable</c> needs a panel and a synthetic pointer event — which is the same
    /// convention <see cref="EspecieScreenTests"/> and <see cref="ReportScreenTests"/>
    /// follow.</para>
    /// </summary>
    public class ArticlesScreenTests
    {
        // ---- Fakes -----------------------------------------------------------

        sealed class FakeCatalog : IArticleCatalog
        {
            public readonly List<ArticleSummary> Items = new List<ArticleSummary>();
            public bool Available = true;

            public IReadOnlyList<ArticleSummary> All() => Items;

            public Article Find(string id)
            {
                foreach (var item in Items)
                {
                    if (item.Id == id) return Article.With(item, Array.Empty<ArticleBlock>());
                }
                return null;
            }

            public bool IsAvailable => Available;
        }

        static ArticleSummary Summary(
            string id, string category, string hero = null, string heroCredit = null,
            int wordCount = 400, string summary = "Um resumo de uma frase.") =>
            new ArticleSummary
            {
                Id = id,
                Title = "Título de " + id,
                Summary = summary,
                Category = category,
                HeroImage = hero,
                HeroCredit = heroCredit,
                Order = 10,
                WordCount = wordCount,
            };

        FakeCatalog catalog;
        ArticlesViewModel vm;

        /// <summary>Textures made for the cover-branch tests, destroyed in TearDown so
        /// EditMode does not accumulate leaked objects across the suite.</summary>
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp() => catalog = new FakeCatalog();

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

        /// <summary>
        /// A minimal real <see cref="UnityEngine.Sprite"/>. The cover branch turns on
        /// "the loader returned something", so 1×1 is enough — nothing in EditMode
        /// measures or draws it.
        /// </summary>
        UnityEngine.Sprite NewSprite()
        {
            var texture = new UnityEngine.Texture2D(1, 1);
            var sprite = UnityEngine.Sprite.Create(
                texture, new UnityEngine.Rect(0, 0, 1, 1), new UnityEngine.Vector2(0.5f, 0.5f));
            owned.Add(texture);
            owned.Add(sprite);
            return sprite;
        }

        /// <summary>
        /// The default loader answers null for every path, which makes the "no cover"
        /// branch the default one — the honest default, since one of the four shipped
        /// articles has no hero and a path that does not resolve is the same case.
        /// </summary>
        ArticlesScreen NewScreen()
        {
            vm = new ArticlesViewModel(catalog);
            return new ArticlesScreen(vm, _ => null);
        }

        /// <summary><paramref name="resolvingPaths"/> names the paths the loader should
        /// answer with an image; everything else still comes back null.</summary>
        ArticlesScreen NewScreenWithCovers(params string[] resolvingPaths)
        {
            var resolving = new HashSet<string>(resolvingPaths, StringComparer.Ordinal);
            vm = new ArticlesViewModel(catalog);
            return new ArticlesScreen(vm, path =>
                path != null && resolving.Contains(path) ? NewSprite() : null);
        }

        static bool Visible(VisualElement e) => e != null && e.style.display.value == DisplayStyle.Flex;

        List<VisualElement> Cards(ArticlesScreen s) =>
            s.Query<VisualElement>(className: "mv-conteudos__card").ToList();

        List<Label> Headings(ArticlesScreen s) =>
            s.Query<Label>(className: "mv-conteudos__group-heading").ToList();

        // ---- Contract --------------------------------------------------------

        [Test]
        public void Key_IsTheConteudosRoute()
        {
            catalog.Items.Add(Summary("a", "Espécies"));
            Assert.That(NewScreen().Key, Is.EqualTo(AppRoutes.Conteudos));
        }

        /// <summary>
        /// The root is transparent and non-pickable like every other screen: the opaque
        /// page surface is the `__content` child, so the strip reserved for the
        /// navigation bar never paints over it or eats its taps.
        /// </summary>
        [Test]
        public void TheRootIsTransparentAndTheContentIsThePage()
        {
            var screen = NewScreen();
            Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(screen.Q(className: "mv-conteudos__content"), Is.Not.Null);
        }

        /// <summary>
        /// A pushed screen with no hero photo still needs a way out, and it is the same
        /// control and the same glyph MvHeroHeader floats over a photo — so the two
        /// cannot drift into two different arrows.
        /// </summary>
        [Test]
        public void TheHeaderHasABackButton_WithTheSameGlyphTheHeroUses()
        {
            var screen = NewScreen();
            var back = screen.Q<MdIconButton>(className: "mv-conteudos__back");

            Assert.That(back, Is.Not.Null);
            Assert.That(back.Icon, Is.EqualTo(MvHeroHeader.DefaultBackIconName));
        }

        /// <summary>
        /// Presence only, and nothing fires on its own: what "back" means is the host's
        /// call (pop the stack), and driving an <c>MdIconButton</c>'s click needs a
        /// panel. Same split <see cref="EspecieScreenTests"/> documents.
        /// </summary>
        [Test]
        public void NothingRaisesBackRequestedWithoutATap()
        {
            var screen = NewScreen();
            int backs = 0;
            screen.BackRequested += () => backs++;
            screen.OnEnter();

            Assert.That(backs, Is.Zero);
        }

        [Test]
        public void EdgeInsets_PadTheOpaqueContainer_SoTheSurfacePaintsUnderTheStatusBar()
        {
            var screen = NewScreen();
            screen.SetEdgeInsets(47f, 3f, 4f, 34f);

            var content = screen.Q(className: "mv-conteudos__content");
            Assert.That(content.style.paddingTop.value.value, Is.EqualTo(47f));
            Assert.That(content.style.paddingLeft.value.value, Is.EqualTo(3f));
            Assert.That(content.style.paddingRight.value.value, Is.EqualTo(4f));
            Assert.That(screen.style.paddingBottom.value.value, Is.EqualTo(34f),
                "the bottom inset stays on the transparent root — the nav bar's strip");
        }

        // ---- Chrome ----------------------------------------------------------

        /// <summary>Every string on this screen comes from the ViewModel, which takes
        /// them from ArticleFormatter — the screen spells nothing.</summary>
        [Test]
        public void TheHeaderCopyComesFromTheViewModel()
        {
            catalog.Items.Add(Summary("a", "Espécies"));
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(screen.Q<Label>(className: "mv-conteudos__title").text,
                Is.EqualTo(ArticleFormatter.IndexTitle));
            Assert.That(screen.Q<Label>(className: "mv-conteudos__subtitle").text,
                Is.EqualTo(ArticleFormatter.IndexSubtitle));
            Assert.That(screen.Q<Label>(className: "mv-conteudos__count").text,
                Is.EqualTo(ArticleFormatter.ArticleCount(1)));
        }

        /// <summary>The Início entry card and this page carry the same mark; one
        /// constant, two sites.</summary>
        [Test]
        public void TheHeaderGlyphIsTheSameOneTheInicioCardUses()
        {
            var screen = NewScreen();
            var icon = screen.Q<MdIcon>(className: "mv-conteudos__title-icon");

            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.Icon, Is.EqualTo(ArticlesScreen.HeaderIconName));
        }

        /// <summary>At zero the state view says it better, so the caption hides rather
        /// than printing "0 conteúdos" above it.</summary>
        [Test]
        public void WithNoArticles_TheCountCaptionIsAbsent()
        {
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Visible(screen.Q<Label>(className: "mv-conteudos__count")), Is.False);
        }

        // ---- Grouping --------------------------------------------------------

        /// <summary>
        /// One heading per category, in the ViewModel's first-appearance order, and the
        /// cards under the heading they belong to. The screen must not sort.
        /// </summary>
        [Test]
        public void GroupsRenderInTheViewModelsOrder_OneHeadingEach()
        {
            catalog.Items.Add(Summary("um", "O projeto"));
            catalog.Items.Add(Summary("dois", "Espécies"));
            catalog.Items.Add(Summary("tres", "Espécies"));
            catalog.Items.Add(Summary("quatro", "Boas práticas"));

            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Headings(screen).Select(l => l.text),
                Is.EqualTo(new[] { "O projeto", "Espécies", "Boas práticas" }));
            Assert.That(Cards(screen).Count, Is.EqualTo(4));
            // The two Espécies articles are in the same group element.
            var groups = screen.Query<VisualElement>(className: "mv-conteudos__group").ToList();
            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[1].Query<VisualElement>(className: "mv-conteudos__card").ToList().Count,
                Is.EqualTo(2));
        }

        /// <summary>A blank category is a content bug; the cards still render, under no
        /// heading, rather than disappearing with it.</summary>
        [Test]
        public void ABlankCategory_DropsTheHeadingAndKeepsTheCard()
        {
            catalog.Items.Add(Summary("sem-categoria", "   "));
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Headings(screen), Is.Empty);
            Assert.That(Cards(screen).Count, Is.EqualTo(1));
        }

        /// <summary>USS has no `gap` or :first-child, so the between-groups and
        /// between-cards spacing is a modifier the screen adds — and never to the
        /// first one, which would double the gap above it.</summary>
        [Test]
        public void OnlyTheRowsAfterTheFirstCarryTheGutterModifier()
        {
            catalog.Items.Add(Summary("um", "Espécies"));
            catalog.Items.Add(Summary("dois", "Espécies"));
            catalog.Items.Add(Summary("tres", "O projeto"));

            var screen = NewScreen();
            screen.OnEnter();

            var groups = screen.Query<VisualElement>(className: "mv-conteudos__group").ToList();
            Assert.That(groups[0].ClassListContains("mv-conteudos__group--gutter"), Is.False);
            Assert.That(groups[1].ClassListContains("mv-conteudos__group--gutter"), Is.True);

            var speciesCards = groups[0].Query<VisualElement>(className: "mv-conteudos__card").ToList();
            Assert.That(speciesCards[0].ClassListContains("mv-conteudos__card--gutter"), Is.False);
            Assert.That(speciesCards[1].ClassListContains("mv-conteudos__card--gutter"), Is.True);
        }

        // ---- Card contents ---------------------------------------------------

        [Test]
        public void ACardShowsTheTitle_TheSummaryAndTheReadingEstimate()
        {
            catalog.Items.Add(Summary("um", "Espécies", wordCount: 400));
            var screen = NewScreen();
            screen.OnEnter();

            var card = Cards(screen)[0];
            Assert.That(card.Q<Label>(className: "mv-conteudos__card-title").text,
                Is.EqualTo("Título de um"));
            Assert.That(card.Q<Label>(className: "mv-conteudos__card-summary").text,
                Is.EqualTo("Um resumo de uma frase."));
            Assert.That(card.Q<Label>(className: "mv-conteudos__card-meta-label").text,
                Is.EqualTo(ArticleFormatter.ReadingTime(400)));
        }

        /// <summary>
        /// The card prints the reading estimate ALONE, not MetaText — the category is
        /// already the heading directly above it, and MetaText joins the two.
        /// </summary>
        [Test]
        public void TheCardsMetaLineDoesNotRepeatTheCategoryHeading()
        {
            catalog.Items.Add(Summary("um", "Espécies", wordCount: 400));
            var screen = NewScreen();
            screen.OnEnter();

            var meta = Cards(screen)[0].Q<Label>(className: "mv-conteudos__card-meta-label").text;
            Assert.That(meta, Does.Not.Contain("Espécies"));
            Assert.That(meta, Is.Not.EqualTo(ArticleFormatter.MetaLine(
                ArticleFormatter.ReadingTime(400), "Espécies")));
        }

        /// <summary>An article with no word count cannot support an estimate, so the
        /// whole row is absent rather than printing "0 min".</summary>
        [Test]
        public void WithNoWordCount_TheMetaRowIsAbsent()
        {
            catalog.Items.Add(Summary("um", "Espécies", wordCount: 0));
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Cards(screen)[0].Q(className: "mv-conteudos__card-meta"), Is.Null);
        }

        [Test]
        public void WithNoSummary_TheTeaserLineIsAbsent()
        {
            catalog.Items.Add(Summary("um", "Espécies", summary: null));
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Cards(screen)[0].Q(className: "mv-conteudos__card-summary"), Is.Null);
        }

        /// <summary>
        /// One of the four shipped articles has no hero at all, and a hero path that
        /// does not resolve is the same case: a text-only card, not an empty 16:9 box.
        /// </summary>
        [Test]
        public void ACoverThatDoesNotResolve_LeavesATextOnlyCard()
        {
            catalog.Items.Add(Summary("um", "Espécies", hero: "Beaches/nao_existe"));
            var screen = NewScreen();   // the loader answers null for everything
            screen.OnEnter();

            var card = Cards(screen)[0];
            Assert.That(card.Q(className: "mv-conteudos__card-media"), Is.Null);
            Assert.That(card.Q<Label>(className: "mv-conteudos__card-title"), Is.Not.Null,
                "the card is still a card");
        }

        /// <summary>A credit is a licence condition WHEREVER the photo is shown — so it
        /// is drawn with the cover and dropped with it.</summary>
        [Test]
        public void TheCoverCreditIsShownWithTheCover_AndDroppedWithIt()
        {
            catalog.Items.Add(Summary("com", "Espécies", hero: "Beaches/ok", heroCredit: "Foto: Alguém / CC BY-SA 4.0"));
            catalog.Items.Add(Summary("sem", "Espécies", hero: "Beaches/faltando", heroCredit: "Foto: Ninguém / CC0"));

            var screen = NewScreenWithCovers("Beaches/ok");
            screen.OnEnter();

            var cards = Cards(screen);
            Assert.That(cards[0].Q(className: "mv-conteudos__card-media"), Is.Not.Null);
            Assert.That(cards[0].Q<Label>(className: "mv-conteudos__card-credit").text,
                Is.EqualTo("Foto: Alguém / CC BY-SA 4.0"));

            Assert.That(cards[1].Q(className: "mv-conteudos__card-media"), Is.Null);
            Assert.That(cards[1].Q(className: "mv-conteudos__card-credit"), Is.Null,
                "a credit for an image nobody can see credits nothing");
        }

        /// <summary>A credit must never be dimmed — see the USS. Asserted here as "the
        /// screen sets no inline opacity", which is the only half a structure test can
        /// see; the stylesheet half is a comment and a review item.</summary>
        [Test]
        public void TheCoverCreditCarriesNoInlineOpacity()
        {
            catalog.Items.Add(Summary("com", "Espécies", hero: "Beaches/ok", heroCredit: "Foto: Alguém / CC BY 4.0"));
            var screen = NewScreenWithCovers("Beaches/ok");
            screen.OnEnter();

            var credit = Cards(screen)[0].Q<Label>(className: "mv-conteudos__card-credit");
            Assert.That(credit.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // ---- Tapping ---------------------------------------------------------

        /// <summary>
        /// The whole card is the target and it carries the article ID — the route
        /// payload — not the title. Driven through the internal entry point the
        /// Clickable calls, because EditMode has no panel.
        /// </summary>
        [Test]
        public void TappingACardRaisesTheArticleId()
        {
            catalog.Items.Add(Summary("tubaroes-de-noronha", "Espécies"));
            var screen = NewScreen();
            screen.OnEnter();

            var raised = new List<string>();
            screen.ArticleRequested += raised.Add;
            screen.RaiseArticleRequested(vm.Articles[0].Id);

            Assert.That(raised, Is.EqualTo(new[] { "tubaroes-de-noronha" }));
        }

        /// <summary>A card with no id could only come from an entry the loader should
        /// have skipped; raising nothing beats pushing a reader that will clear itself.</summary>
        [Test]
        public void ACardWithNoId_RaisesNothing()
        {
            var screen = NewScreen();
            int raised = 0;
            screen.ArticleRequested += _ => raised++;

            screen.RaiseArticleRequested(null);
            screen.RaiseArticleRequested("");

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void EveryCardIsFocusableAndCarriesAStateLayer()
        {
            catalog.Items.Add(Summary("um", "Espécies"));
            var screen = NewScreen();
            screen.OnEnter();

            var card = Cards(screen)[0];
            Assert.That(card.focusable, Is.True);
            Assert.That(card.Q(className: "md-state-layer"), Is.Not.Null);
        }

        // ---- States ----------------------------------------------------------

        /// <summary>
        /// A readable catalog with nothing in it. Not a failure — content work nobody
        /// has done — so the glyph and the wording must not read as one.
        /// </summary>
        [Test]
        public void WithAnEmptyCatalog_TheStateViewReplacesTheList()
        {
            var screen = NewScreen();
            screen.OnEnter();

            var state = screen.Q<MvStateView>(className: "mv-conteudos__state");
            Assert.That(Visible(state), Is.True);
            Assert.That(Visible(screen.Q(className: "mv-conteudos__groups")), Is.False);
            Assert.That(Cards(screen), Is.Empty);

            Assert.That(state.Variant, Is.EqualTo(MvStateViewVariant.Empty));
            Assert.That(state.Icon, Is.EqualTo(ArticlesScreen.HeaderIconName));
            Assert.That(state.Title, Is.EqualTo(StateViewCopy.ArticlesEmptyTitle));
            Assert.That(state.Body, Is.EqualTo(StateViewCopy.ArticlesEmptyBody));
        }

        /// <summary>A catalog that could not be read at all — a build shipped without
        /// its generated articles.json. A different statement, differently worded.</summary>
        [Test]
        public void WithAnUnreadableCatalog_TheStateViewSaysSomethingElse()
        {
            catalog.Available = false;
            var screen = NewScreen();
            screen.OnEnter();

            var state = screen.Q<MvStateView>(className: "mv-conteudos__state");
            Assert.That(Visible(state), Is.True);
            Assert.That(state.Variant, Is.EqualTo(MvStateViewVariant.Error));
            Assert.That(state.Title, Is.EqualTo(StateViewCopy.ErrorTitle));
            Assert.That(state.Body, Is.EqualTo(StateViewCopy.ArticlesErrorBody));
        }

        /// <summary>Neither state is retryable — articles.json ships inside the APK — so
        /// the footer draws nothing. A button here would be a lie.</summary>
        [Test]
        public void NeitherStateDrawsAnActionButton()
        {
            var screen = NewScreen();
            screen.OnEnter();
            Assert.That(screen.Q<MvStateView>(className: "mv-conteudos__state").ActionText, Is.Empty);

            catalog.Available = false;
            vm.Refresh();
            Assert.That(screen.Q<MvStateView>(className: "mv-conteudos__state").ActionText, Is.Empty);
        }

        [Test]
        public void WithArticles_TheStateViewIsAbsentAndTheListIsDrawn()
        {
            catalog.Items.Add(Summary("um", "Espécies"));
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Visible(screen.Q<MvStateView>(className: "mv-conteudos__state")), Is.False,
                "built once and hidden, not created on demand");
            Assert.That(Visible(screen.Q(className: "mv-conteudos__groups")), Is.True);
        }

        // ---- Lifecycle -------------------------------------------------------

        /// <summary>Per-visit work lives in OnEnter/OnExit, never a constructor: the
        /// router keeps every screen alive and only toggles `display`.</summary>
        [Test]
        public void OnExit_UnsubscribesSoALaterChangeDoesNotRepaintAHiddenScreen()
        {
            var screen = NewScreen();
            screen.OnEnter();
            Assert.That(Cards(screen), Is.Empty);

            screen.OnExit();
            catalog.Items.Add(Summary("novo", "Espécies"));
            vm.Refresh();

            Assert.That(Cards(screen), Is.Empty, "the screen is not listening");

            screen.OnEnter();
            Assert.That(Cards(screen).Count, Is.EqualTo(1), "and picks it up on the next visit");
        }

        /// <summary>A screen shown before its first OnEnter must not render an empty
        /// page — the constructor paints once without subscribing.</summary>
        [Test]
        public void TheConstructorPaintsOnce_WithoutSubscribing()
        {
            catalog.Items.Add(Summary("um", "Espécies"));
            var screen = NewScreen();

            Assert.That(Cards(screen).Count, Is.EqualTo(1));
        }
    }
}
