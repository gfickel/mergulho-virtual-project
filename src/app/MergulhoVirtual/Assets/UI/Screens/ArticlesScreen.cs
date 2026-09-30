using System;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Conteúdo educativo — the index of educational articles, grouped by category.
    /// Pushed as <see cref="AppRoutes.Conteudos"/> from the Início "Conteúdo
    /// educativo" card and dismissed with the header's back button.
    ///
    /// <para><b>⚠ THERE IS NO FIGMA FRAME FOR THIS SCREEN.</b> V2's frame inventory
    /// has no educational-content surface at all — the feature postdates the
    /// prototype — so every number and every ordering decision below is
    /// <b>designed, not transcribed</b>, and wants a designer's review. It was
    /// designed under the same rules <see cref="EspecieScreen"/> was: reuse the
    /// idioms Início and Praia detalhe already established (16dp side gutters, white
    /// outlined cards on the off-white page, <c>title-medium</c> section headings,
    /// muted <c>label-small</c> credits), invent no new visual pattern, and invent no
    /// copy — every string comes from <see cref="ArticlesViewModel"/>.</para>
    ///
    /// <para><b>The back button is an <see cref="MdIconButton"/>, not a hero.</b>
    /// <see cref="PraiaDetalheScreen"/> and <see cref="EspecieScreen"/> get theirs
    /// from <see cref="MvHeroHeader"/>, which is right for a page that opens on a
    /// photo; an index has no cover image, and a hero with no image is a 240–340dp
    /// empty box. So this reuses the same control and the same glyph constant
    /// (<see cref="MvHeroHeader.DefaultBackIconName"/>) in an ordinary header row —
    /// the affordance is identical, the 240dp of empty photo is not.</para>
    ///
    /// <para><b>Category order is the ViewModel's, not this screen's.</b>
    /// <see cref="ArticlesViewModel.Groups"/> arrives in first-appearance order over
    /// the author's <c>order</c> field, so the screen walks it and never sorts:
    /// sorting here would take the author's one ordering lever away and hand it to
    /// the Portuguese alphabet.</para>
    ///
    /// <para><b>The state view draws no button, deliberately.</b>
    /// <c>articles.json</c> ships inside the APK, so neither "nothing authored yet"
    /// nor "the file is missing" is retryable and
    /// <see cref="ArticlesViewModel.StateActionLabel"/> is always null. The screen
    /// binds it anyway rather than hard-coding the absence, so the day a retry
    /// becomes real it appears without a change here.</para>
    /// </summary>
    public sealed class ArticlesScreen : VisualElement, IAppScreen
    {
        /// <summary>
        /// The index's own leading glyph, shared with the Início entry card
        /// (<c>HomeViewModel.LearnFeature</c>) so the tile the user taps and the page
        /// it opens carry the same mark.
        /// </summary>
        public const string HeaderIconName = "menu_book";

        /// <summary>Material Symbols name on the reading-time meta row.</summary>
        public const string ReadingTimeIconName = "schedule";

        readonly ArticlesViewModel vm;
        readonly Func<string, Sprite> loadArticleSprite;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly VisualElement body;
        readonly Label titleLabel;
        readonly Label subtitleLabel;
        readonly Label countLabel;
        readonly VisualElement groups;
        readonly MvStateView state;

        bool subscribed;

        /// <summary>
        /// A card was tapped, with the tapped article's <b>id</b> (the stable
        /// kebab-case front-matter key — a key, never a label).
        ///
        /// <para>A payload event rather than a bare <see cref="AppRoutes.Conteudo"/>
        /// key, for exactly the reason <see cref="PraiaDetalheScreen.SpeciesRequested"/>
        /// is one: a route alone cannot say <i>which</i> article to open. The screen
        /// still knows nothing about navigation — it raises the key and the host sets
        /// the reader's ViewModel before pushing.</para>
        /// </summary>
        public event Action<string> ArticleRequested;

        /// <summary>The header's back button was tapped — pop the back stack.</summary>
        public event Action BackRequested;

        public ArticlesScreen(ArticlesViewModel viewModel, Func<string, Sprite> articleSpriteLoader = null)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            loadArticleSprite = articleSpriteLoader;

            AddToClassList("mv-conteudos");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-conteudos__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-conteudos__scroll");
            content.Add(scroll);

            body = new VisualElement();
            body.AddToClassList("mv-conteudos__body");
            scroll.Add(body);

            body.Add(BuildHeader(out titleLabel, out subtitleLabel, out countLabel));

            groups = new VisualElement { pickingMode = PickingMode.Ignore };
            groups.AddToClassList("mv-conteudos__groups");
            body.Add(groups);

            state = new MvStateView();
            state.AddToClassList("mv-conteudos__state");
            state.style.display = DisplayStyle.None;
            body.Add(state);

            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());

            // First paint without subscribing: OnEnter owns the subscription, but the
            // screen must never render an empty page if it is shown before its first
            // OnEnter. Same contract as HomeScreen.
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Conteudos;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += Render;
                subscribed = true;
            }
            Render();
            scroll.scrollOffset = Vector2.zero;
        }

        public void OnExit() => Unsubscribe();

        /// <summary>
        /// Safe-area insets in panel units. There is no full-bleed hero here, so the
        /// TOP inset is padding on the opaque content container — the page surface
        /// still paints under the status bar while the header clears it. Bottom goes
        /// on the transparent root, which is the navigation bar's reservation.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            content.style.paddingTop = top;
            content.style.paddingLeft = left;
            content.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            vm.Changed -= Render;
            subscribed = false;
        }

        // ---- Header ---------------------------------------------------------

        /// <summary>
        /// Back button on its own row, then the glyph + headline, the subtitle and the
        /// count. The back button gets a row of its own rather than sharing one with
        /// the title: at 360dp a 48dp control plus a 24px headline leaves the headline
        /// under 260dp, which wraps "Conteúdo educativo" beside the arrow and reads as
        /// a cramped app bar rather than a page opening.
        /// </summary>
        VisualElement BuildHeader(out Label title, out Label subtitle, out Label count)
        {
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("mv-conteudos__header");

            var navRow = new VisualElement { pickingMode = PickingMode.Ignore };
            navRow.AddToClassList("mv-conteudos__nav-row");
            // Same control and same glyph constant MvHeroHeader uses for its floating
            // back button, so the two cannot drift into two different arrows.
            var back = new MdIconButton { Icon = MvHeroHeader.DefaultBackIconName };
            back.AddToClassList("mv-conteudos__back");
            back.Clicked += () => BackRequested?.Invoke();
            navRow.Add(back);
            header.Add(navRow);

            var titleRow = new VisualElement { pickingMode = PickingMode.Ignore };
            titleRow.AddToClassList("mv-conteudos__title-row");

            var icon = new MdIcon { Icon = HeaderIconName };
            icon.AddToClassList("mv-conteudos__title-icon");
            titleRow.Add(icon);

            title = new Label(vm.TitleText) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-large-increased");
            title.AddToClassList("mv-conteudos__title");
            titleRow.Add(title);
            header.Add(titleRow);

            subtitle = new Label(vm.SubtitleText) { pickingMode = PickingMode.Ignore };
            subtitle.AddToClassList("md-typescale-body-large");
            subtitle.AddToClassList("mv-conteudos__subtitle");
            header.Add(subtitle);

            count = new Label { pickingMode = PickingMode.Ignore };
            count.AddToClassList("md-typescale-label-medium");
            count.AddToClassList("mv-conteudos__count");
            header.Add(count);

            return header;
        }

        // ---- Cards ----------------------------------------------------------

        /// <summary>
        /// One index row: a 16:9 cover, the headline, the teaser, the reading estimate
        /// and — wherever the cover is shown — its credit. <b>The whole card is the tap
        /// target</b>, which is both the biggest possible one and what a list of
        /// articles behaves like everywhere else; there is no separate "ler" button to
        /// aim at.
        ///
        /// <para>Every block below the cover is gated on the card's own <c>Has*</c>
        /// flag, the same discipline Praia detalhe uses: a teaser-less or estimate-less
        /// article simply has fewer lines, never an empty one.</para>
        /// </summary>
        VisualElement BuildCard(ArticleCardView card)
        {
            var root = new MdCard { Variant = MdCardVariant.Outlined, focusable = true };
            root.AddToClassList("mv-conteudos__card");

            // The cover is only built when there is a sprite to put in it. A path that
            // does not resolve (a hero the build script named but never installed) is
            // therefore a TEXT-ONLY card, not an empty grey 16:9 box — and one of the
            // four shipped articles has no hero at all, so text-only is a normal row
            // rather than a degraded one.
            var sprite = card.HasHeroImage ? loadArticleSprite?.Invoke(card.HeroImage) : null;
            if (sprite != null)
            {
                var media = new VisualElement { pickingMode = PickingMode.Ignore };
                media.AddToClassList("mv-conteudos__card-media");
                var image = new Image
                {
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleAndCrop,
                    sprite = sprite,
                };
                image.AddToClassList("mv-conteudos__card-image");
                media.Add(image);
                root.Add(media);
            }

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("mv-conteudos__card-body");

            var title = new Label(card.TitleText ?? "") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-small-increased");
            title.AddToClassList("mv-conteudos__card-title");
            text.Add(title);

            if (card.HasSummary)
            {
                var summary = new Label(card.SummaryText) { pickingMode = PickingMode.Ignore };
                summary.AddToClassList("md-typescale-body-medium");
                summary.AddToClassList("mv-conteudos__card-summary");
                text.Add(summary);
            }

            if (card.HasReadingTime)
            {
                var meta = new VisualElement { pickingMode = PickingMode.Ignore };
                meta.AddToClassList("mv-conteudos__card-meta");

                var clock = new MdIcon { Icon = ReadingTimeIconName };
                clock.AddToClassList("mv-conteudos__card-meta-icon");
                meta.Add(clock);

                // ReadingTimeText, not MetaText: the category is already the heading
                // directly above this card, so MetaText would print it twice.
                var metaLabel = new Label(card.ReadingTimeText) { pickingMode = PickingMode.Ignore };
                metaLabel.AddToClassList("md-typescale-label-small");
                metaLabel.AddToClassList("mv-conteudos__card-meta-label");
                meta.Add(metaLabel);

                text.Add(meta);
            }

            // A credit is a LICENCE CONDITION wherever the photo it belongs to is
            // shown, so it rides with the cover and is dropped with it — and it is
            // never dimmed with opacity (see the USS).
            if (sprite != null && card.HasHeroCredit)
            {
                var credit = new Label(card.HeroCreditText) { pickingMode = PickingMode.Ignore };
                credit.AddToClassList("md-typescale-label-small");
                credit.AddToClassList("mv-conteudos__card-credit");
                text.Add(credit);
            }

            root.Add(text);

            // Last child so the press tint covers the whole card; MdCard already
            // carries `overflow: hidden`, so it follows the rounded corners.
            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            root.Add(stateLayer);

            string id = card.Id;
            root.AddManipulator(new Clickable(() => RaiseArticleRequested(id)));
            return root;
        }

        /// <summary>
        /// A card with no id could only come from a catalog entry the loader should
        /// have skipped; raising nothing is then correct, and the host would have
        /// refused to navigate anyway.
        ///
        /// <para>Internal rather than private so the EditMode suite can pin the entry
        /// point: a <c>Clickable</c> needs a panel and a synthetic pointer event, which
        /// EditMode has neither of — the same convention every other screen suite here
        /// follows.</para>
        /// </summary>
        internal void RaiseArticleRequested(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ArticleRequested?.Invoke(id);
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            countLabel.text = vm.HasCountText ? vm.CountText : "";
            countLabel.style.display = vm.HasCountText ? DisplayStyle.Flex : DisplayStyle.None;

            bool showState = vm.ShowStateView;
            groups.style.display = showState ? DisplayStyle.None : DisplayStyle.Flex;
            state.style.display = showState ? DisplayStyle.Flex : DisplayStyle.None;

            if (showState)
            {
                groups.Clear();
                // Empty is not a failure — it is content work nobody has done yet — and
                // the two must not read the same. `menu_book` replaces MvStateView's
                // neutral `list` default for the empty case so the page still names what
                // is missing.
                //
                // The glyph is set EXPLICITLY in both branches, not left to the variant:
                // MvStateView.Icon latches `_iconOverridden`, so setting it once would
                // pin `menu_book` over a later Error variant's own glyph. Writing both
                // sides means the two states cannot get stuck wearing each other's mark.
                state.Variant = vm.IsUnavailable ? MvStateViewVariant.Error : MvStateViewVariant.Empty;
                state.Icon = vm.IsUnavailable
                    ? MvStateView.DefaultIconNames[(int)MvStateViewVariant.Error]
                    : HeaderIconName;
                state.Title = vm.StateTitleText;
                state.Body = vm.StateBodyText;
                // Always null today, and binding it rather than assuming so is what
                // makes a future retry appear with no change here.
                state.ActionText = vm.HasStateAction ? vm.StateActionLabel : "";
                return;
            }

            RenderGroups();
        }

        /// <summary>
        /// Groups and cards are rebuilt rather than re-bound: the list changes only
        /// when the catalog is reloaded (an editor-only event), and rebuilding keeps
        /// the per-card click closures and their ids in one place — the same call
        /// <see cref="PraiaDetalheScreen"/> makes about its species chips.
        /// </summary>
        void RenderGroups()
        {
            groups.Clear();
            var sections = vm.Groups;
            for (int i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                if (section == null) continue;

                var group = new VisualElement { pickingMode = PickingMode.Ignore };
                group.AddToClassList("mv-conteudos__group");
                if (i > 0) group.AddToClassList("mv-conteudos__group--gutter"); // USS has no `gap`.

                // A blank category is a content bug, and the cards still render under
                // no heading rather than disappearing — see ArticleFormatter.CategoryHeading.
                if (section.HasHeading)
                {
                    var heading = new Label(section.HeadingText) { pickingMode = PickingMode.Ignore };
                    heading.AddToClassList("md-typescale-title-medium");
                    heading.AddToClassList("mv-conteudos__group-heading");
                    group.Add(heading);
                }

                var rows = section.Articles;
                for (int j = 0; j < rows.Count; j++)
                {
                    if (rows[j] == null) continue;
                    var card = BuildCard(rows[j]);
                    if (j > 0) card.AddToClassList("mv-conteudos__card--gutter"); // USS has no `gap`.
                    group.Add(card);
                }

                groups.Add(group);
            }
        }
    }
}
