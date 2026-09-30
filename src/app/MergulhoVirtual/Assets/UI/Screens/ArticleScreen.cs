using System;
using System.Collections.Generic;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Conteúdo — one educational article, open for reading. Pushed as
    /// <see cref="AppRoutes.Conteudo"/> from an <see cref="ArticlesScreen"/> card (or,
    /// later, from a link inside another article) and dismissed with the back button.
    ///
    /// <para><b>⚠ THERE IS NO FIGMA FRAME FOR THIS SCREEN.</b> V2 predates the
    /// educational-content feature entirely, so every value here is <b>designed, not
    /// transcribed</b> and wants a designer's review. Designed under the same rules as
    /// <see cref="EspecieScreen"/>: reuse the idioms the other screens established
    /// (compact full-bleed hero, 16dp side gutters, <c>title-medium</c> section
    /// headings, white outlined cards, muted <c>label-small</c> credits), invent no new
    /// visual pattern, and invent no copy — every string comes from
    /// <see cref="ArticleViewModel"/>.</para>
    ///
    /// <para><b>This is a block renderer, and the block list is the API.</b> It binds
    /// <see cref="ArticleViewModel.Blocks"/> — <b>never</b> <c>Current.Blocks</c> —
    /// because the ViewModel is where the <see cref="ArticleBlockKind.Unknown"/> filter
    /// is applied, so a block type from a newer <c>articles.json</c> renders as nothing
    /// at all without this screen having to remember to skip it. One element per block,
    /// in authoring order; a kind this switch does not handle is skipped in silence for
    /// the same forward-compatibility reason.</para>
    ///
    /// <para><b>Every optional piece of a block is gated on presence.</b> One of the
    /// three shipped image blocks has neither a caption nor a credit, and one of the
    /// four articles has no hero at all — so "the line is absent" is a normal
    /// rendering, and an empty caption row or a 240dp blank hero would be the bug. The
    /// no-hero path swaps the hero for a plain back-button row rather than drawing an
    /// empty photo frame (see <see cref="navRow"/>).</para>
    ///
    /// <para><b>Why it holds two catalogs.</b> A <c>speciesRef</c> / <c>beachRef</c>
    /// block carries only a machine <i>key</i> ("tiger_shark", "Sueste Beach") which
    /// the contract forbids rendering, and the two labels
    /// <see cref="ArticleViewModel"/> offers are fixed strings ("Saiba mais sobre a
    /// espécie") that do not name the destination. The tubarões article has <b>five
    /// consecutive</b> species references, which with the fixed label alone would be
    /// five identical rows. So the screen takes the two catalogs as optional
    /// constructor arguments and renders the destination's own <c>DisplayName</c>
    /// beside the fixed label — a data value read straight off the catalog, exactly as
    /// <see cref="PraiaDetalheScreen"/> renders <c>species.DisplayName</c>, not a
    /// string this screen built. With no catalog wired, or a key the catalog does not
    /// have, the name line is simply absent and the fixed label stands alone.</para>
    ///
    /// <para><b>Teardown in OnExit is load-bearing.</b> The router keeps screens alive
    /// and only toggles <c>display</c>, so without an explicit
    /// <see cref="IVideoPlayback.Stop"/> a clip would go on streaming behind a hidden
    /// screen for as long as the app is open — a bandwidth and battery regression that
    /// looks like nothing. Same contract, and the same ordering, as
    /// <see cref="EspecieScreen.OnExit"/>. Note the player instance is <b>shared</b>
    /// with that screen; the router only ever shows one screen, so that is safe
    /// precisely because both release it on the way out.</para>
    /// </summary>
    public sealed class ArticleScreen : VisualElement, IAppScreen
    {
        /// <summary>
        /// Playhead poll interval, ms — the same value <see cref="EspecieScreen"/>
        /// uses: fast enough that the clock and the track look live, slow enough not to
        /// lay out the tree 60 times a second for a label that changes once a second.
        /// </summary>
        const long ProgressPollMs = 250;

        /// <summary>How far back the pause control sits while a clip runs. Not zero:
        /// the media surface IS the pause target.</summary>
        const float PlayingOverlayOpacity = 0.8f;

        /// <summary>Set on a video overlay exactly while a decoded frame is on screen,
        /// so USS can flip the ink for a backdrop it cannot see. Mirrors
        /// <c>EspecieScreen.OverFrameClass</c>.</summary>
        const string OverFrameClass = "mv-conteudo__video-overlay--over-frame";

        /// <summary>
        /// Leading glyph on a <c>speciesRef</c> row. <b>The project has no shark
        /// glyph</b> — Decision D7's custom fin is still owed and
        /// <c>material_symbols_icons.txt</c> deliberately refuses a plausible marine
        /// substitute for the Avistamentos tab. <c>waves</c> is not that substitute: it
        /// is already the app's stand-in mark for "a marine animal we cannot draw
        /// here", used as the species-photo placeholder on both Praia detalhe and
        /// Espécie, so reusing it adds no new claim.
        /// </summary>
        const string SpeciesRefIconName = "waves";

        /// <summary>Leading glyph on a <c>beachRef</c> row — the project's beach mark
        /// everywhere (the Início Praias tile, MvHeroHeader's selector pin).</summary>
        const string BeachRefIconName = "location_on";

        /// <summary>Trailing affordance on both cross-reference rows.</summary>
        const string RefChevronIconName = "chevron_right";

        /// <summary>Glyph per callout tone, paired with
        /// <see cref="ArticleViewModel.CalloutLabelFor"/> so the mark and the word can
        /// never disagree. Indexed by <see cref="ArticleCalloutTone"/>.</summary>
        static readonly string[] CalloutIconNames = { "info", "warning", "check_circle", "error" };

        /// <summary>BEM tone modifier per <see cref="ArticleCalloutTone"/>.</summary>
        static readonly string[] CalloutToneClasses =
        {
            "mv-conteudo__callout--info",
            "mv-conteudo__callout--warning",
            "mv-conteudo__callout--success",
            "mv-conteudo__callout--error",
        };

        readonly ArticleViewModel vm;
        readonly IVideoPlayback videoPlayback;
        readonly ISpeciesCatalog speciesCatalog;
        readonly IBeachCatalog beachCatalog;
        readonly Func<string, Sprite> loadArticleSprite;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly MvHeroHeader hero;

        /// <summary>
        /// The back affordance for an article with <b>no</b> hero photo — one of the
        /// four shipped articles. A <see cref="MvHeroHeader"/> with no image is a 240dp
        /// empty box (its foreground is absolutely positioned, so it cannot collapse to
        /// its content), and a blank photo frame above the headline reads as a failed
        /// image load. Exactly one of these two is visible at a time.
        /// </summary>
        readonly VisualElement navRow;

        readonly VisualElement body;
        readonly Label titleLabel;
        readonly Label summaryLabel;
        readonly VisualElement metaRow;
        readonly Label metaLabel;
        readonly Label updatedLabel;
        readonly Label heroCreditLabel;
        readonly VisualElement blocks;

        /// <summary>Every video card currently in the tree, in block order — the only
        /// per-block state the screen keeps, because a clip's controls are repainted
        /// from the player rather than rebuilt.</summary>
        readonly List<VideoCard> cards = new List<VideoCard>();

        IVisualElementScheduledItem progressTick;
        bool subscribed;

        /// <summary>The back button was tapped — pop the back stack.</summary>
        public event Action BackRequested;

        /// <summary>
        /// A <c>speciesRef</c> row was tapped, carrying the block's <b>species key</b>
        /// (an <c>AnimalDef</c> asset name — a key, never a label). The host routes it
        /// through the same handler Praia detalhe's "Saiba mais sobre a espécie" uses,
        /// so an article's species link lands on the same Espécie screen by the same
        /// path.
        /// </summary>
        public event Action<string> SpeciesRequested;

        /// <summary>
        /// A <c>beachRef</c> row was tapped, carrying the block's <b>beach name</b> —
        /// the <c>places.json</c> machine key ("Sueste Beach"), not the pt-BR label.
        /// </summary>
        public event Action<string> BeachRequested;

        public ArticleScreen(
            ArticleViewModel viewModel,
            IVideoPlayback videoPlayback = null,
            ISpeciesCatalog speciesCatalog = null,
            IBeachCatalog beachCatalog = null,
            Func<string, Sprite> articleSpriteLoader = null)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            this.videoPlayback = videoPlayback;
            this.speciesCatalog = speciesCatalog;
            this.beachCatalog = beachCatalog;
            loadArticleSprite = articleSpriteLoader;

            AddToClassList("mv-conteudo");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-conteudo__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-conteudo__scroll");
            content.Add(scroll);

            // Compact (240dp) rather than Praia detalhe's 340dp, for the same reason
            // Espécie is compact: the covers are landscape photographs and a 1.15:1
            // crop throws away ~40% of the width. Nothing is written over this photo,
            // but the component's own scrim is kept — unlike Espécie, where the photo
            // IS the identification and a veil costs it something. Here it is a cover.
            hero = new MvHeroHeader { ShowBackButton = true, ShowSelector = false, Compact = true };
            hero.AddToClassList("mv-conteudo__hero");
            hero.BackClicked += () => BackRequested?.Invoke();
            scroll.Add(hero);

            navRow = BuildNavRow();
            scroll.Add(navRow);

            body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("mv-conteudo__body");
            scroll.Add(body);

            body.Add(BuildHeader(
                out titleLabel, out summaryLabel, out metaRow, out metaLabel,
                out updatedLabel, out heroCreditLabel));

            blocks = new VisualElement { pickingMode = PickingMode.Ignore };
            blocks.AddToClassList("mv-conteudo__blocks");
            body.Add(blocks);

            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Conteudo;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += OnViewModelChanged;
                if (videoPlayback != null) videoPlayback.Changed += OnPlaybackChanged;
                subscribed = true;
            }
            Render();
            // A reader that opens mid-article is a bug — same reset Praia detalhe and
            // Espécie do, and it matters more here because the page is long.
            scroll.scrollOffset = Vector2.zero;
        }

        /// <summary>
        /// Releases the stream before unsubscribing — see the class remarks. The order
        /// matches <see cref="EspecieScreen.OnExit"/>'s, where it does matter.
        /// </summary>
        public void OnExit() => Teardown();

        /// <summary>
        /// Safe-area insets in panel units. The TOP inset goes to the hero (which pads
        /// its controls plane only, leaving the photo running under the status bar)
        /// <b>and</b> to the no-hero nav row — only one of the two is ever visible, so
        /// nothing is double-counted. Left/right are padding on the opaque container;
        /// bottom is the navigation bar's reservation on the transparent root.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            hero.TopInset = top;
            navRow.style.paddingTop = top;
            content.style.paddingLeft = left;
            content.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void Teardown()
        {
            StopProgressTick();
            StopVideo();
            if (!subscribed) return;
            vm.Changed -= OnViewModelChanged;
            if (videoPlayback != null) videoPlayback.Changed -= OnPlaybackChanged;
            subscribed = false;
        }

        /// <summary>
        /// The article changed underneath the screen — which can happen without leaving
        /// it, because an article may be opened from another article. Any clip playing
        /// belongs to the old body, so it is stopped before the repaint drops its card.
        /// </summary>
        void OnViewModelChanged()
        {
            StopVideo();
            Render();
        }

        /// <summary>
        /// The player moved on its own: prepared, failed, or the clip ran out. Nothing
        /// playing means nothing to poll, so the tick is parked — otherwise it would
        /// keep laying out two labels four times a second for the rest of the session.
        /// </summary>
        void OnPlaybackChanged()
        {
            if (PlayingCard() == null) StopProgressTick();
            RenderVideoState();
        }

        // ---- Chrome ---------------------------------------------------------

        /// <summary>
        /// The no-hero back row. Same control and same glyph constant
        /// <see cref="MvHeroHeader"/> uses, so the two back affordances on this screen
        /// cannot drift into two different arrows.
        /// </summary>
        VisualElement BuildNavRow()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-conteudo__nav-row");
            var back = new MdIconButton { Icon = MvHeroHeader.DefaultBackIconName };
            back.AddToClassList("mv-conteudo__nav-back");
            back.Clicked += () => BackRequested?.Invoke();
            row.Add(back);
            return row;
        }

        /// <summary>
        /// Category + reading estimate on one meta line, the headline, the standfirst,
        /// the revision date and the cover's credit.
        ///
        /// <para><see cref="ArticleViewModel.MetaText"/> rather than a separate category
        /// label plus a separate estimate: it already joins the two and drops an absent
        /// half with no stray separator, so two elements would print the category
        /// twice. The credit rides here, directly under the photo it belongs to, rather
        /// than at the page foot where Espécie puts its two.</para>
        /// </summary>
        VisualElement BuildHeader(
            out Label title, out Label summary, out VisualElement meta, out Label metaText,
            out Label updated, out Label heroCredit)
        {
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("mv-conteudo__header");

            meta = new VisualElement { pickingMode = PickingMode.Ignore };
            meta.AddToClassList("mv-conteudo__meta");
            var clock = new MdIcon { Icon = ArticlesScreen.ReadingTimeIconName };
            clock.AddToClassList("mv-conteudo__meta-icon");
            meta.Add(clock);
            metaText = new Label { pickingMode = PickingMode.Ignore };
            metaText.AddToClassList("md-typescale-label-medium");
            metaText.AddToClassList("mv-conteudo__meta-label");
            meta.Add(metaText);
            header.Add(meta);

            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-large-increased");
            title.AddToClassList("mv-conteudo__title");
            header.Add(title);

            summary = new Label { pickingMode = PickingMode.Ignore };
            summary.AddToClassList("md-typescale-body-large");
            summary.AddToClassList("mv-conteudo__summary");
            header.Add(summary);

            updated = new Label { pickingMode = PickingMode.Ignore };
            updated.AddToClassList("md-typescale-label-small");
            updated.AddToClassList("mv-conteudo__updated");
            header.Add(updated);

            heroCredit = new Label { pickingMode = PickingMode.Ignore };
            heroCredit.AddToClassList("md-typescale-label-small");
            heroCredit.AddToClassList("mv-conteudo__credit");
            header.Add(heroCredit);

            return header;
        }

        // ---- Blocks ---------------------------------------------------------

        /// <summary>
        /// Rebuilt whole on every render: the body changes only when the article
        /// changes, and rebuilding keeps each block's click closures and its payload
        /// key in one place — the same call <see cref="PraiaDetalheScreen"/> makes about
        /// its species chips.
        /// </summary>
        void RenderBlocks()
        {
            blocks.Clear();
            cards.Clear();

            var list = vm.Blocks;   // already filtered to IsRenderable — see the remarks
            bool previousWasRef = false;
            for (int i = 0; i < list.Count; i++)
            {
                var block = list[i];
                if (block == null) continue;
                var element = BuildBlock(block);
                // A kind this build does not draw contributes nothing at all: no
                // placeholder, no empty box. Same rule as ArticleBlockKind.Unknown.
                if (element == null) continue;
                element.AddToClassList("mv-conteudo__block");

                // A run of cross-references is one list, not a series of unrelated
                // blocks — the tubarões article has five in a row — so the rows after
                // the first close up. USS has no adjacent-sibling combinator, so the
                // "previous one was a reference too" test lives here.
                bool isRef = block.Kind == ArticleBlockKind.SpeciesRef
                          || block.Kind == ArticleBlockKind.BeachRef;
                if (isRef && previousWasRef) element.AddToClassList("mv-conteudo__block--tight");
                previousWasRef = isRef;

                blocks.Add(element);
            }

            RenderVideoState();
        }

        VisualElement BuildBlock(ArticleBlock block)
        {
            switch (block.Kind)
            {
                case ArticleBlockKind.Heading: return BuildHeading(block);
                case ArticleBlockKind.Paragraph: return BuildParagraph(block);
                case ArticleBlockKind.BulletList: return BuildBulletList(block);
                case ArticleBlockKind.NumberedList: return BuildNumberedList(block);
                case ArticleBlockKind.Callout: return BuildCallout(block);
                case ArticleBlockKind.Quote: return BuildQuote(block);
                case ArticleBlockKind.Image: return BuildImage(block);
                case ArticleBlockKind.Video: return BuildVideo(block);
                // The raiser is passed as a METHOD REFERENCE, not as the event's current
                // value: an event read at build time is a snapshot, and every subscriber
                // attaches after the constructor has already run Render() once — so
                // capturing `SpeciesRequested` here would wire the taps to null.
                case ArticleBlockKind.SpeciesRef:
                    return BuildRef(
                        SpeciesRefIconName, SpeciesName(block.SpeciesKey), vm.SpeciesRefLabel,
                        "mv-conteudo__ref--species", block.SpeciesKey, RaiseSpeciesRequested);
                case ArticleBlockKind.BeachRef:
                    return BuildRef(
                        BeachRefIconName, BeachName(block.BeachName), vm.BeachRefLabel,
                        "mv-conteudo__ref--beach", block.BeachName, RaiseBeachRequested);
                default: return null;
            }
        }

        /// <summary>
        /// Level 2 is a section heading and takes the app's section rung
        /// (<c>title-medium</c>, 18/700, what Praia detalhe and Espécie use); level 3
        /// drops to <c>title-small</c> (15/700, V2's workhorse heading). With the
        /// article title at <c>title-large-increased</c> (24) that is a 24 / 18 / 15
        /// ladder — no invented size, and each rung already earns its living elsewhere.
        /// The loader clamps the level into 2–3, so the else-branch is belt and braces.
        /// </summary>
        VisualElement BuildHeading(ArticleBlock block)
        {
            bool level3 = block.Level >= 3;
            var label = new Label(block.Text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(level3 ? "md-typescale-title-small" : "md-typescale-title-medium");
            label.AddToClassList("mv-conteudo__heading");
            label.AddToClassList(level3 ? "mv-conteudo__heading--3" : "mv-conteudo__heading--2");
            return label;
        }

        /// <summary>
        /// The one block that carries rich text. <b>Rendered exactly as it arrives</b>:
        /// the build script neutralises every author <c>&lt;</c> (wrapping each one in
        /// <c>&lt;noparse&gt;</c>) before inserting its own
        /// <c>&lt;b&gt; &lt;i&gt; &lt;u&gt; &lt;a href&gt;</c>, so touching the string
        /// here would either print those tags or reopen the injection the escaping
        /// closes. Note it does NOT escape <c>&amp;</c> or <c>&gt;</c>: neither can open
        /// a tag, and UI Toolkit decodes no HTML entities, so an escaped one would
        /// render as its literal characters.
        /// </summary>
        static VisualElement BuildParagraph(ArticleBlock block)
        {
            var label = new Label(block.Text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("md-typescale-body-large");
            label.AddToClassList("mv-conteudo__paragraph");
            return label;
        }

        /// <summary>
        /// One row per item: the marker, then the text. The marker is
        /// <see cref="ArticleViewModel.BulletMarker"/> — a screen may not spell even a
        /// bullet glyph — and it is <c>flex-shrink: 0</c> + <c>nowrap</c> beside a
        /// wrapping sibling, the house pattern for a label in a flex row.
        /// </summary>
        VisualElement BuildBulletList(ArticleBlock block)
        {
            var list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.AddToClassList("mv-conteudo__bullets");

            var items = block.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("mv-conteudo__bullet");
                if (i > 0) row.AddToClassList("mv-conteudo__bullet--gutter"); // USS has no `gap`.

                var marker = new Label(vm.BulletMarker) { pickingMode = PickingMode.Ignore };
                marker.AddToClassList("md-typescale-body-large");
                marker.AddToClassList("mv-conteudo__bullet-marker");
                row.Add(marker);

                var text = new Label(items[i] ?? "") { pickingMode = PickingMode.Ignore };
                text.AddToClassList("md-typescale-body-large");
                text.AddToClassList("mv-conteudo__bullet-text");
                row.Add(text);

                list.Add(row);
            }
            return list;
        }

        /// <summary>
        /// The design system's <see cref="MvNumberedList"/> — filled <c>primary</c>
        /// circles holding the index, body text beside them, which is how V2 draws
        /// "Dicas de convivência".
        ///
        /// <para>Note it <b>numbers its own rows</b> (<c>StartNumber</c> + the index),
        /// which is why <see cref="ArticleViewModel.ListNumberFor"/> is not called here:
        /// that helper returns "1." with a dot, for an inline numbered list drawn as
        /// plain text. Two renderings of the same thing existed and the component won —
        /// it is the designed pattern, and a bare digit in a circle is what it draws.
        /// </para>
        /// </summary>
        static VisualElement BuildNumberedList(ArticleBlock block)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-conteudo__card");
            card.AddToClassList("mv-conteudo__numbered");
            var list = new MvNumberedList();
            list.SetItems(block.Items);
            card.Add(list);
            return card;
        }

        /// <summary>
        /// A tinted, outlined notice carrying the tone's own name as well as its tint —
        /// "Informação" / "Atenção" / "Recomendação" / "Perigo" from
        /// <see cref="ArticleViewModel.CalloutLabelFor"/>.
        ///
        /// <para><b>Not <see cref="MvAlertBar"/>, and the reason is the label.</b> That
        /// component is icon + one text label on one bar, with nowhere to put a second
        /// string; and the tone label is exactly what makes the distinction survive for
        /// a reader who cannot see the tint (a screen reader, or colour blindness) — the
        /// author reached for "error" rather than "warning" on purpose. So this mirrors
        /// MvAlertBar's visual language exactly (12/16 padding, corner-large, 1px
        /// same-role border, container fill, on-container ink) and adds the label row.
        /// Tones map to the token families the brief fixes: info → <c>primary</c>,
        /// warning → <c>warning</c>, success → <c>success</c>, error → <c>error</c> —
        /// note that is NOT MvAlertBar's `info`, which is amber
        /// (<c>secondary-container</c>).</para>
        /// </summary>
        VisualElement BuildCallout(ArticleBlock block)
        {
            int tone = (int)block.Tone;
            if (tone < 0 || tone >= CalloutToneClasses.Length) tone = (int)ArticleCalloutTone.Info;

            var callout = new VisualElement { pickingMode = PickingMode.Ignore };
            callout.AddToClassList("mv-conteudo__callout");
            callout.AddToClassList(CalloutToneClasses[tone]);

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("mv-conteudo__callout-head");

            var icon = new MdIcon { Icon = CalloutIconNames[tone] };
            icon.AddToClassList("mv-conteudo__callout-icon");
            head.Add(icon);

            // Null only for a tone cast in from outside the enum, which the loader
            // cannot produce; the row then carries the glyph alone rather than "".
            string toneLabel = vm.CalloutLabelFor(block.Tone);
            if (!string.IsNullOrEmpty(toneLabel))
            {
                var label = new Label(toneLabel) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-label-medium");
                label.AddToClassList("mv-conteudo__callout-tone");
                head.Add(label);
            }
            callout.Add(head);

            var text = new Label(block.Text) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("md-typescale-body-large");
            text.AddToClassList("mv-conteudo__callout-text");
            callout.Add(text);

            return callout;
        }

        /// <summary>
        /// A pull quote: a 4dp accent rule down the left, the text in italic, and the
        /// attribution under it when there is one. One of the two shipped quotes is
        /// unattributed, so the attribution line is genuinely optional — and a bare em
        /// dash on its own line would be worse than no line
        /// (<see cref="ArticleFormatter.QuoteAttribution"/> returns null for it).
        /// </summary>
        VisualElement BuildQuote(ArticleBlock block)
        {
            var quote = new VisualElement { pickingMode = PickingMode.Ignore };
            quote.AddToClassList("mv-conteudo__quote");

            var text = new Label(block.Text) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("md-typescale-body-large");
            text.AddToClassList("mv-conteudo__quote-text");
            quote.Add(text);

            string attribution = vm.QuoteAttributionFor(block);
            if (attribution != null)
            {
                var author = new Label(attribution) { pickingMode = PickingMode.Ignore };
                author.AddToClassList("md-typescale-label-medium");
                author.AddToClassList("mv-conteudo__quote-author");
                quote.Add(author);
            }
            return quote;
        }

        /// <summary>
        /// A figure: the photograph, then its caption, then its credit — <b>each omitted
        /// entirely when absent</b>. One of the three shipped image blocks has neither,
        /// which is the case this gating exists for: two empty lines under a photo read
        /// as a rendering fault.
        ///
        /// <para>A path that does not resolve draws <b>nothing at all</b> rather than an
        /// empty frame with an orphaned caption — an image nobody can see is not a
        /// figure, and its caption describes something that is not on screen.</para>
        /// </summary>
        VisualElement BuildImage(ArticleBlock block)
        {
            var sprite = loadArticleSprite?.Invoke(block.Src);
            if (sprite == null) return null;

            var figure = new VisualElement { pickingMode = PickingMode.Ignore };
            figure.AddToClassList("mv-conteudo__figure");

            var media = new VisualElement { pickingMode = PickingMode.Ignore };
            media.AddToClassList("mv-conteudo__figure-media");
            var image = new Image
            {
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleAndCrop,
                sprite = sprite,
            };
            image.AddToClassList("mv-conteudo__figure-image");
            media.Add(image);
            figure.Add(media);

            string caption = vm.CaptionFor(block);
            if (caption != null)
            {
                var label = new Label(caption) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-body-small");
                label.AddToClassList("mv-conteudo__figure-caption");
                figure.Add(label);
            }

            string credit = vm.CreditFor(block);
            if (credit != null)
            {
                var label = new Label(credit) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-label-small");
                label.AddToClassList("mv-conteudo__credit");
                figure.Add(label);
            }
            return figure;
        }

        // ---- Cross-references -----------------------------------------------

        /// <summary>
        /// The destination's pt-BR label, or null when there is no catalog or no such
        /// key. Read straight off the catalog — the screen never spells it, and it
        /// never falls back to the machine key, which the contract forbids showing.
        /// </summary>
        string SpeciesName(string key) => speciesCatalog?.Find(key)?.DisplayName;

        /// <summary>
        /// Same for a beach. <see cref="IBeachCatalog"/> has no <c>Find</c>, so the
        /// lookup walks its list; it is 17 entries and runs once per block while an
        /// article is built, not per frame. Matched on <c>Name</c> — the machine key the
        /// content file writes — and answered with <c>DisplayName</c>, which falls back
        /// to Name inside <see cref="BeachInfo"/> when no pt-BR label is authored.
        /// </summary>
        string BeachName(string key)
        {
            if (string.IsNullOrEmpty(key) || beachCatalog == null) return null;
            var all = beachCatalog.Beaches;
            if (all == null) return null;
            foreach (var beach in all)
            {
                if (beach != null && string.Equals(beach.Name, key, StringComparison.Ordinal))
                    return beach.DisplayName;
            }
            return null;
        }

        /// <summary>
        /// A 48dp+ row that opens another screen: glyph, the destination's name over the
        /// fixed link label, and a chevron. The whole row is the target, which is both
        /// the biggest one available and what the rest of the app does with a card.
        /// </summary>
        VisualElement BuildRef(
            string iconName, string name, string label, string toneClass,
            string payload, Action<string> raise)
        {
            var row = new VisualElement { focusable = true };
            row.AddToClassList("mv-conteudo__ref");
            row.AddToClassList(toneClass);

            var icon = new MdIcon { Icon = iconName };
            icon.AddToClassList("mv-conteudo__ref-icon");
            row.Add(icon);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("mv-conteudo__ref-text");

            // Absent when the catalog cannot resolve the key: the fixed label then
            // stands alone, which says less but says nothing false.
            if (!string.IsNullOrEmpty(name))
            {
                var nameLabel = new Label(name) { pickingMode = PickingMode.Ignore };
                nameLabel.AddToClassList("md-typescale-title-small");
                nameLabel.AddToClassList("mv-conteudo__ref-name");
                text.Add(nameLabel);
            }

            var linkLabel = new Label(label ?? "") { pickingMode = PickingMode.Ignore };
            linkLabel.AddToClassList("md-typescale-label-small");
            linkLabel.AddToClassList("mv-conteudo__ref-label");
            text.Add(linkLabel);
            row.Add(text);

            var chevron = new MdIcon { Icon = RefChevronIconName };
            chevron.AddToClassList("mv-conteudo__ref-chevron");
            row.Add(chevron);

            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            row.Add(stateLayer);

            row.AddManipulator(new Clickable(() => raise?.Invoke(payload)));
            return row;
        }

        /// <summary>
        /// A species cross-reference was tapped. A block with no key cannot reach here
        /// (<see cref="ArticleBlock.IsRenderable"/> drops it), so the guard is belt and
        /// braces — and raising nothing is the right answer either way, since the host
        /// would refuse to navigate.
        ///
        /// <para>Internal rather than private so the EditMode suite can pin the entry
        /// point: a <c>Clickable</c> needs a panel and a synthetic pointer event. Same
        /// convention as <see cref="ArticlesScreen.RaiseArticleRequested"/>.</para>
        /// </summary>
        internal void RaiseSpeciesRequested(string speciesKey)
        {
            if (string.IsNullOrEmpty(speciesKey)) return;
            SpeciesRequested?.Invoke(speciesKey);
        }

        /// <summary>A beach cross-reference was tapped. See
        /// <see cref="RaiseSpeciesRequested"/> for why this is internal.</summary>
        internal void RaiseBeachRequested(string beachName)
        {
            if (string.IsNullOrEmpty(beachName)) return;
            BeachRequested?.Invoke(beachName);
        }

        // ---- Video ----------------------------------------------------------

        /// <summary>One clip's card and the elements its state drives — the same shape
        /// <see cref="EspecieScreen"/> uses.</summary>
        sealed class VideoCard
        {
            public string Url;
            public VisualElement Root;
            public VisualElement Media;
            public Image Frame;
            public VisualElement Overlay;
            public MdIcon ActionIcon;
            public Label ActionLabel;
            public MdCircularProgress Spinner;
            public Label Error;
            /// <summary>The 14dp touch strip; the visible 3dp rule is <see cref="Bar"/>,
            /// centred in it — a 3dp seek target is unhittable with a thumb.</summary>
            public VisualElement Track;
            public VisualElement Bar;
            public VisualElement Fill;
            public Label Title;
            public Label Time;
        }

        /// <summary>
        /// A tap-to-play card, built the way Espécie's is: the whole poster is the
        /// play/pause target, the overlay stays up in every state (a running clip that
        /// shows no pause control is a card with no controls at all), and the seek strip
        /// is toggled with <c>visibility</c> rather than <c>display</c> so starting a
        /// clip never reflows the page under the finger.
        ///
        /// <para><b>Which card owns the player is read off the player itself</b>, by URL
        /// — <see cref="IVideoPlayback.Url"/> is documented as exactly that — so this
        /// screen keeps no "playing index" of its own and cannot disagree with the
        /// service. Note there is one player for the whole app and
        /// <see cref="EspecieScreen"/> shares it.</para>
        /// </summary>
        VisualElement BuildVideo(ArticleBlock block)
        {
            // No player wired (the screenshot harness, a host that built none): a play
            // control that cannot play is worse than no card, so the block is dropped
            // — the same call EspecieScreen makes about its whole video section.
            if (videoPlayback == null || !videoPlayback.IsAvailable) return null;

            var card = new VideoCard { Url = block.Url };

            card.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Root.AddToClassList("mv-conteudo__card");
            card.Root.AddToClassList("mv-conteudo__video-card");

            card.Media = new VisualElement { focusable = true };
            card.Media.AddToClassList("mv-conteudo__video-media");

            card.Frame = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            card.Frame.AddToClassList("mv-conteudo__video-frame");
            card.Media.Add(card.Frame);

            card.Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Overlay.AddToClassList("mv-conteudo__video-overlay");

            // A SIBLING behind the content, not `opacity` on the overlay: UI Toolkit
            // inherits opacity down the subtree, so dimming the container would dim the
            // glyph and the word with it.
            var scrim = new VisualElement { pickingMode = PickingMode.Ignore };
            scrim.AddToClassList("mv-conteudo__video-scrim");
            card.Overlay.Add(scrim);

            card.Spinner = new MdCircularProgress { Indeterminate = true };
            card.Spinner.AddToClassList("mv-conteudo__video-spinner");
            card.Overlay.Add(card.Spinner);

            card.ActionIcon = new MdIcon { Icon = "play_arrow" };
            card.ActionIcon.AddToClassList("mv-conteudo__video-action-icon");
            card.Overlay.Add(card.ActionIcon);

            card.ActionLabel = new Label { pickingMode = PickingMode.Ignore };
            card.ActionLabel.AddToClassList("md-typescale-label-large");
            card.ActionLabel.AddToClassList("mv-conteudo__video-action-label");
            card.Overlay.Add(card.ActionLabel);

            card.Media.Add(card.Overlay);
            card.Root.Add(card.Media);

            card.Track = new VisualElement();
            card.Track.AddToClassList("mv-conteudo__video-track");
            card.Bar = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Bar.AddToClassList("mv-conteudo__video-bar");
            card.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Fill.AddToClassList("mv-conteudo__video-fill");
            card.Bar.Add(card.Fill);
            card.Track.Add(card.Bar);
            card.Root.Add(card.Track);

            var footer = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.AddToClassList("mv-conteudo__video-footer");

            string clipTitle = vm.VideoTitleFor(block);
            card.Title = new Label(clipTitle ?? "") { pickingMode = PickingMode.Ignore };
            card.Title.AddToClassList("md-typescale-title-small");
            card.Title.AddToClassList("mv-conteudo__video-title");
            card.Title.style.display = clipTitle != null ? DisplayStyle.Flex : DisplayStyle.None;
            footer.Add(card.Title);

            card.Time = new Label { pickingMode = PickingMode.Ignore };
            card.Time.AddToClassList("md-typescale-label-small");
            card.Time.AddToClassList("mv-conteudo__video-time");
            footer.Add(card.Time);
            card.Root.Add(footer);

            card.Error = new Label(ArticleFormatter.VideoErrorText) { pickingMode = PickingMode.Ignore };
            card.Error.AddToClassList("md-typescale-body-medium");
            card.Error.AddToClassList("mv-conteudo__video-error");
            card.Root.Add(card.Error);

            int index = cards.Count;
            cards.Add(card);
            card.Media.AddManipulator(new Clickable(() => ToggleVideo(index)));
            card.Track.RegisterCallback<PointerDownEvent>(e => OnTrackPointerDown(e, index));
            card.Track.RegisterCallback<PointerMoveEvent>(e => OnTrackPointerMove(e, index));
            card.Track.RegisterCallback<PointerUpEvent>(e => OnTrackPointerUp(e, index));

            return card.Root;
        }

        /// <summary>The card that currently owns the shared player, or null.</summary>
        VideoCard PlayingCard()
        {
            if (videoPlayback == null || string.IsNullOrEmpty(videoPlayback.Url)) return null;
            foreach (var card in cards)
            {
                if (string.Equals(card.Url, videoPlayback.Url, StringComparison.Ordinal)) return card;
            }
            return null;
        }

        VideoPlaybackState StateFor(VideoCard card) =>
            videoPlayback != null && ReferenceEquals(card, PlayingCard())
                ? videoPlayback.State
                : VideoPlaybackState.Idle;

        /// <summary>
        /// Tap on a poster. Start, pause, resume or retry — decided from the player's
        /// own state, which is the only authority on it. Exposed internally so the
        /// EditMode suite can drive it: a <c>Clickable</c> needs a panel.
        /// </summary>
        internal void ToggleVideo(int index)
        {
            if (videoPlayback == null || index < 0 || index >= cards.Count) return;
            var card = cards[index];

            if (ReferenceEquals(card, PlayingCard()))
            {
                switch (videoPlayback.State)
                {
                    case VideoPlaybackState.Playing:
                        videoPlayback.Pause();
                        break;
                    case VideoPlaybackState.Loading:
                        // Already on its way; a second tap must not restart the stream.
                        break;
                    default:
                        videoPlayback.Play(card.Url);
                        break;
                }
            }
            else
            {
                // Play() on a different URL implicitly stops whatever was playing —
                // there is one player, and that is its documented contract.
                videoPlayback.Play(card.Url);
            }

            if (PlayingCard() != null) StartProgressTick();
            RenderVideoState();
        }

        void StopVideo()
        {
            videoPlayback?.Stop();
            StopProgressTick();
            RenderVideoState();
        }

        // ---- Seeking --------------------------------------------------------
        //
        // No "is the user dragging" flag and no preview state: the position under the
        // finger simply IS the playhead, so the tap and every move of the drag run the
        // identical seek. Capturing the pointer on the way down is what keeps the drag
        // working past the ends of a 14dp strip and stops the page scrolling under it.

        void OnTrackPointerDown(PointerDownEvent e, int index)
        {
            if (!SeekFromTrack(index, e.localPosition.x)) return;
            cards[index].Track.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnTrackPointerMove(PointerMoveEvent e, int index)
        {
            if (index >= cards.Count) return;
            if (!cards[index].Track.HasPointerCapture(e.pointerId)) return;
            if (!SeekFromTrack(index, e.localPosition.x)) return;
            e.StopPropagation();
        }

        void OnTrackPointerUp(PointerUpEvent e, int index)
        {
            if (index >= cards.Count) return;
            var track = cards[index].Track;
            if (!track.HasPointerCapture(e.pointerId)) return;
            track.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        /// <summary>
        /// Moves the playhead to a horizontal position on the track. False when there is
        /// nothing to seek — a card that does not own the player, or a clip whose
        /// duration is not known yet — in which case the caller leaves the event alone.
        /// </summary>
        bool SeekFromTrack(int index, float localX)
        {
            if (videoPlayback == null || index < 0 || index >= cards.Count) return false;
            var card = cards[index];
            if (!ReferenceEquals(card, PlayingCard())) return false;

            double duration = videoPlayback.DurationSeconds;
            if (!(duration > 0d)) return false;

            float width = card.Track.contentRect.width;
            if (float.IsNaN(width) || width <= 0f) return false;

            videoPlayback.Seek(Mathf.Clamp01(localX / width) * duration);
            RenderVideoProgress();
            return true;
        }

        void StartProgressTick()
        {
            if (progressTick != null)
            {
                progressTick.Resume();
                return;
            }
            progressTick = schedule.Execute(RenderVideoProgress).Every(ProgressPollMs);
        }

        void StopProgressTick() => progressTick?.Pause();

        /// <summary>The state half: which overlay, which glyph, which words, and whether
        /// the decoded frame is on screen. Event-driven, not polled.</summary>
        void RenderVideoState()
        {
            var playing = PlayingCard();
            foreach (var card in cards)
            {
                var state = StateFor(card);
                bool loading = state == VideoPlaybackState.Loading;
                bool failed = state == VideoPlaybackState.Failed;
                bool isPlaying = state == VideoPlaybackState.Playing;

                // The decoded frame is the ONE thing read off the service rather than a
                // formatter — it is a UnityEngine.Texture.
                var texture = ReferenceEquals(card, playing) ? videoPlayback?.Texture : null;
                bool hasFrame = texture != null && (isPlaying || state == VideoPlaybackState.Paused);
                card.Frame.image = hasFrame ? texture : null;
                card.Frame.style.display = hasFrame ? DisplayStyle.Flex : DisplayStyle.None;

                // Which backdrop the overlay sits on — the one thing USS cannot work out
                // for itself, since the frame's visibility is an inline style written
                // just above. Every value lives in ArticleScreen.uss.
                card.Overlay.EnableInClassList(OverFrameClass, hasFrame);
                card.Overlay.style.opacity = isPlaying ? PlayingOverlayOpacity : 1f;

                card.Spinner.style.display = loading ? DisplayStyle.Flex : DisplayStyle.None;
                card.ActionIcon.style.display = loading ? DisplayStyle.None : DisplayStyle.Flex;
                card.ActionIcon.Icon = ArticleFormatter.VideoActionIcon(state);
                card.ActionLabel.text = ArticleFormatter.VideoActionLabel(state);

                card.Error.style.display = failed ? DisplayStyle.Flex : DisplayStyle.None;

                // `visibility`, not `display`: the strip stays in the layout of every
                // card whether or not it is the one playing, so starting a clip does not
                // reflow 14dp of page under the finger that just tapped. A hidden
                // element is not picked, so dragging an idle track still does nothing.
                card.Track.style.visibility =
                    ReferenceEquals(card, playing) && !failed ? Visibility.Visible : Visibility.Hidden;
            }
            RenderVideoProgress();
        }

        /// <summary>The clock half, polled — see <see cref="ProgressPollMs"/>.</summary>
        void RenderVideoProgress()
        {
            var playing = PlayingCard();
            double position = videoPlayback?.PositionSeconds ?? 0d;
            double duration = videoPlayback?.DurationSeconds ?? 0d;

            foreach (var card in cards)
            {
                bool owns = ReferenceEquals(card, playing);
                string clock = owns ? ArticleFormatter.VideoTimeText(position, duration) : null;
                card.Time.text = clock ?? "";
                card.Time.style.display = clock != null ? DisplayStyle.Flex : DisplayStyle.None;
                float progress = owns ? ArticleFormatter.VideoProgress(position, duration) : 0f;
                card.Fill.style.width = new Length(progress * 100f, LengthUnit.Percent);
            }
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            // No article is only the state before the first push: the host sets the
            // ViewModel and only navigates if the id resolved. The back affordance
            // stays either way, so the screen is never a dead end even so.
            bool has = vm.HasArticle;
            body.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;

            var sprite = has && vm.HasHeroImage ? loadArticleSprite?.Invoke(vm.HeroImage) : null;
            hero.SetImage(sprite);
            // Exactly one of the two back affordances is drawn — see navRow's remarks.
            hero.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            navRow.style.display = sprite != null ? DisplayStyle.None : DisplayStyle.Flex;

            if (!has)
            {
                blocks.Clear();
                cards.Clear();
                return;
            }

            metaRow.style.display = vm.HasMeta ? DisplayStyle.Flex : DisplayStyle.None;
            metaLabel.text = vm.HasMeta ? vm.MetaText : "";

            titleLabel.text = vm.TitleText ?? "";

            summaryLabel.text = vm.HasSummary ? vm.SummaryText : "";
            summaryLabel.style.display = vm.HasSummary ? DisplayStyle.Flex : DisplayStyle.None;

            updatedLabel.text = vm.HasUpdated ? vm.UpdatedText : "";
            updatedLabel.style.display = vm.HasUpdated ? DisplayStyle.Flex : DisplayStyle.None;

            // The credit belongs to the photo, so it is dropped with it: an article
            // whose hero did not resolve must not print a credit for an image nobody
            // can see. It is never dimmed — a licence condition, not decoration.
            bool showHeroCredit = sprite != null && vm.HasHeroCredit;
            heroCreditLabel.text = showHeroCredit ? vm.HeroCreditText : "";
            heroCreditLabel.style.display = showHeroCredit ? DisplayStyle.Flex : DisplayStyle.None;

            RenderBlocks();
        }
    }
}
