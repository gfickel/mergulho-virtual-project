using System;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Início (Home) — DESIGN_IMPLEMENTATION.md §8.1, Figma frames Tela 7
    /// (29:2154) and Tela 6 (15:746, the first-run welcome card).
    ///
    /// Built in code, like <see cref="PraiasScreen"/>: the pieces that carry the
    /// actual content (MdSparkline.SetSamples, MdIcon.Icon) are code-only APIs, so
    /// a UXML file would hold nothing but empty boxes.
    ///
    /// Router-agnostic on purpose: every tappable surface raises
    /// <see cref="NavigationRequested"/> with an <see cref="AppRoutes"/> key and the
    /// host maps it, so the screen needs no router reference and stays unit-testable.
    ///
    /// The root is transparent and non-pickable; the opaque page surface lives on an
    /// inner container which takes the top/left/right safe-area insets as PADDING —
    /// so its background still paints under the status bar / notch while the content
    /// clears it. The bottom inset goes on the transparent root so the navigation bar
    /// strip stays unpainted and tappable. (Deliberate deviation from SafeAreaElement,
    /// which would leave that top strip transparent — raw camera feed — and is skipped
    /// entirely in editor panels, breaking the Device Simulator.)
    /// </summary>
    public sealed class HomeScreen : VisualElement, IAppScreen
    {
        readonly HomeViewModel vm;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly VisualElement welcomeCard;
        readonly Label waveValue, tideValue, moonValue, windValue, waterValue, freshnessLabel;
        readonly MdSparkline sparkline;

        /// <summary>The five rows, the freshness line and the chart — everything the
        /// conditions state view stands in for when there is nothing to show.</summary>
        readonly VisualElement[] conditionsData;
        readonly MvStateView conditionsState;

        bool subscribed;

        /// <summary>
        /// Raised with an <see cref="AppRoutes"/> key when the user taps a feature
        /// card (Praias / Mergulho / Avistamentos / Sos / Sobre) or the conditions
        /// card header. The host turns it into a route change.
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// "Baixar a tábua de maré do mês" was tapped. Not a route — the host owns
        /// what it opens. No source is wired yet (see the report / §8.1).
        /// </summary>
        public event Action TideTableRequested;

        public HomeScreen(HomeViewModel viewModel)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            AddToClassList("mv-home");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-home__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-home__scroll");
            content.Add(scroll);

            // Main Content Body: V, gap 16, padding 20/16/100/16. Figma's 100dp
            // bottom is clearance for the 98dp pinned navigation bar, which the
            // shell reserves below this screen (and SetEdgeInsets tops up with
            // anything it did not) — so the body keeps only a short scroll tail.
            var body = new VisualElement();
            body.AddToClassList("mv-home__body");
            scroll.Add(body);

            body.Add(welcomeCard = BuildWelcomeCard());

            var conditions = BuildConditionsCard(
                out waveValue, out tideValue, out moonValue, out windValue, out waterValue,
                out freshnessLabel, out sparkline, out conditionsData, out conditionsState);
            body.Add(conditions);

            // 2 rows × 2 cards, 173×138 each, gap 12 (the cards flex to fill, which
            // reproduces 173 at the 390dp design width and adapts on other widths).
            var grid = new VisualElement();
            grid.AddToClassList("mv-home__grid");
            var features = vm.GridFeatures;
            for (int i = 0; i < features.Count; i += 2)
            {
                var row = new VisualElement();
                row.AddToClassList("mv-home__grid-row");
                if (i > 0) row.AddToClassList("mv-home__grid-row--gutter"); // USS has no `gap`/:first-child.
                row.Add(MakeFeatureCard(features[i], wide: false));
                if (i + 1 < features.Count)
                {
                    var second = MakeFeatureCard(features[i + 1], wide: false);
                    second.AddToClassList("mv-feature-card--gutter"); // USS has no `gap`.
                    row.Add(second);
                }
                grid.Add(row);
            }
            body.Add(grid);

            // Decision D2: Sobre (and the Instagram widget with it) lost its bottom-bar
            // tab, so it becomes a Home entry. This card is NOT in the Figma frame — it
            // is a deliberate addition, full-width in the same card language so it reads
            // as its own section rather than as an orphaned fifth grid cell.
            if (vm.WideFeature != null) body.Add(MakeFeatureCard(vm.WideFeature, wide: true));

            // "Conteúdo educativo" — also NOT in the Figma frame, and for the same
            // reason as the card above it: the feature gets no bottom-bar tab (V2 has
            // four destinations and that is fixed), so it needs a discoverable entry
            // point, and this is the established language for one. It follows Sobre
            // rather than leading it because Sobre is the older entry and the grid
            // above already sets the reading order.
            if (vm.LearnFeature != null) body.Add(MakeFeatureCard(vm.LearnFeature, wide: true));

            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());

            // First paint without subscribing: OnEnter owns the subscriptions, but a
            // screen must never render empty rows if it is shown before its first
            // OnEnter (or by a host that only calls it on later visits).
            RenderWelcome();
            RenderData();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Home;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.DataChanged += RenderData;
                vm.WelcomeChanged += RenderWelcome;
                subscribed = true;
            }
            RenderWelcome();
            RenderData();
            scroll.scrollOffset = Vector2.zero;
        }

        public void OnExit() => Unsubscribe();

        /// <summary>
        /// Safe-area insets in panel units. Top/left/right are padding on the opaque
        /// content container so the page surface still paints under the status bar;
        /// bottom is the navigation-bar reservation on the transparent root, so that
        /// strip stays unpainted and taps reach the bar.
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
            vm.DataChanged -= RenderData;
            vm.WelcomeChanged -= RenderWelcome;
            subscribed = false;
        }

        // ---- Welcome card (Tela 6) ------------------------------------------

        VisualElement BuildWelcomeCard()
        {
            var card = new VisualElement();
            card.AddToClassList("mv-home__card");
            card.AddToClassList("mv-home__welcome");

            var title = new Label(HomeViewModel.WelcomeTitle);
            title.AddToClassList("md-typescale-title-large");
            title.AddToClassList("mv-home__welcome-title");
            card.Add(title);

            var text = new Label(HomeViewModel.WelcomeBody);
            text.AddToClassList("md-typescale-body-large");
            text.AddToClassList("mv-home__welcome-body");
            card.Add(text);

            // V2's mark is a filled navy disc with a white cross, not the outlined
            // `cancel` ring — filled variant + a plain `close` glyph; the 24dp disc
            // and 16dp glyph are sized in HomeScreen.uss.
            var close = new MdIconButton { Icon = "close", Variant = MdIconButtonVariant.Filled };
            close.AddToClassList("mv-home__welcome-close");
            close.Clicked += vm.DismissWelcome;
            card.Add(close);

            return card;
        }

        // ---- Conditions card ------------------------------------------------

        /// <summary>
        /// 234dp navy card: "Hoje" + chevron, the tide-table link, the five shared
        /// condition rows and the tide sparkline. Figma flattens this to a bitmap
        /// (image 13) — there is nothing to transcribe, so it is rebuilt from the
        /// ViewModel rows + MdSparkline (§1, §8.1).
        /// </summary>
        VisualElement BuildConditionsCard(
            out Label wave, out Label tide, out Label moon, out Label wind, out Label water,
            out Label freshness, out MdSparkline chart,
            out VisualElement[] dataBlock, out MvStateView state)
        {
            var card = new VisualElement();
            card.AddToClassList("mv-home__card");
            card.AddToClassList("mv-conditions");

            var header = new VisualElement();
            header.AddToClassList("mv-conditions__header");
            card.Add(header);

            var titleGroup = new VisualElement { focusable = true };
            titleGroup.AddToClassList("mv-conditions__title-group");
            var title = new Label(HomeViewModel.ConditionsCardTitle) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-small");
            title.AddToClassList("mv-conditions__title");
            titleGroup.Add(title);
            var chevron = new MdIcon { Icon = "chevron_right" };
            chevron.AddToClassList("mv-conditions__chevron");
            titleGroup.Add(chevron);
            // The header drills into the full conditions view, which lives on Praias.
            titleGroup.AddManipulator(new Clickable(() => NavigationRequested?.Invoke(AppRoutes.Praias)));
            header.Add(titleGroup);

            // Two elements, not one, and the split is the whole point: the design's
            // underline is a 1px BOTTOM BORDER on the label (UI Toolkit has no
            // text-decoration — same fidelity trade as §3.6's shadows), so the label
            // cannot also be the 48dp touch target. Growing its own box either pushes
            // the rule ~17dp clear of the text or drops the ink ~15dp below "Hoje",
            // which shares this row. The wrapper takes the height and the Clickable
            // and paints nothing; the label keeps its rule and stops being pickable.
            // Measured before: ~14dp of hit box against the library's 48dp minimum.
            var linkHit = new VisualElement { focusable = true };
            linkHit.AddToClassList("mv-conditions__link-hit");
            linkHit.AddManipulator(new Clickable(() => TideTableRequested?.Invoke()));

            var link = new Label(HomeViewModel.TideTableLinkText) { pickingMode = PickingMode.Ignore };
            link.AddToClassList("md-typescale-label-small");
            link.AddToClassList("mv-conditions__link");
            linkHit.Add(link);
            header.Add(linkHit);

            var rows = new VisualElement[5];
            wave = AddConditionsRow(card, "Onda", out rows[0]);
            tide = AddConditionsRow(card, "Maré", out rows[1]);
            moon = AddConditionsRow(card, "Lua", out rows[2]);
            wind = AddConditionsRow(card, "Vento", out rows[3]);
            water = AddConditionsRow(card, "Água", out rows[4]);

            freshness = new Label();
            freshness.AddToClassList("md-typescale-label-small");
            freshness.AddToClassList("mv-conditions__freshness");
            card.Add(freshness);

            chart = new MdSparkline
            {
                Baseline = HomeViewModel.TideBaselineM,
                ExtremumLabelFormatter = vm.FormatTideExtremumLabel,
            };
            chart.AddToClassList("mv-conditions__sparkline");
            card.Add(chart);

            // §8.7's state, inline inside the card rather than over the screen: the
            // header above it is still true and still drills into Praias, and the
            // rest of Início is unaffected by the sea forecast being unreachable.
            // The block is built once and hidden; it carries no copy of its own, so
            // every string below comes from the ViewModel (StateViewCopy).
            state = new MvStateView();
            state.AddToClassList("mv-conditions__state");
            state.ActionInvoked += () =>
            {
                vm.RetryConditions();
                RenderData();
            };
            state.style.display = DisplayStyle.None;
            card.Add(state);

            dataBlock = new VisualElement[rows.Length + 2];
            Array.Copy(rows, dataBlock, rows.Length);
            dataBlock[rows.Length] = freshness;
            dataBlock[rows.Length + 1] = chart;

            return card;
        }

        static Label AddConditionsRow(VisualElement parent, string rowName, out VisualElement rowElement)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            rowElement = row;
            row.AddToClassList("mv-conditions__row");

            var nameLabel = new Label(rowName) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("md-typescale-label-large");
            nameLabel.AddToClassList("mv-conditions__row-label");
            row.Add(nameLabel);

            var valueLabel = new Label(ConditionsFormatter.NoValue) { pickingMode = PickingMode.Ignore };
            valueLabel.AddToClassList("md-typescale-body-large");
            valueLabel.AddToClassList("mv-conditions__row-value");
            row.Add(valueLabel);

            parent.Add(row);
            return valueLabel;
        }

        // ---- Feature cards --------------------------------------------------

        /// <summary>
        /// 173×138 grid card (or the full-width Sobre variant): 36×36 navy icon tile
        /// with a 24dp amber glyph, title, body. Raises the feature's route key.
        /// </summary>
        VisualElement MakeFeatureCard(HomeFeature feature, bool wide)
        {
            var card = new VisualElement { focusable = true };
            card.AddToClassList("mv-feature-card");
            if (wide) card.AddToClassList("mv-feature-card--wide");

            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("mv-feature-card__tile");
            var icon = new MdIcon { Icon = feature.Icon };
            icon.AddToClassList("mv-feature-card__glyph");
            tile.Add(icon);
            card.Add(tile);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("mv-feature-card__text");

            var title = new Label(feature.Title) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-small");
            title.AddToClassList("mv-feature-card__title");
            text.Add(title);

            var body = new Label(feature.Body) { pickingMode = PickingMode.Ignore };
            body.AddToClassList("md-typescale-body-small");
            body.AddToClassList("mv-feature-card__body");
            text.Add(body);

            card.Add(text);

            // Last child so the press tint covers the whole card.
            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            card.Add(stateLayer);

            string route = feature.Route;
            card.AddManipulator(new Clickable(() => NavigationRequested?.Invoke(route)));
            return card;
        }

        // ---- Rendering ------------------------------------------------------

        void RenderWelcome() =>
            welcomeCard.style.display = vm.ShowWelcome ? DisplayStyle.Flex : DisplayStyle.None;

        void RenderData()
        {
            // Nothing fetched and the service says it tried: the rows would be five
            // em dashes over an empty chart, which reads as "the sea is blank"
            // rather than "we could not load it". Swap in the state view instead.
            bool unavailable = vm.ConditionsUnavailable;
            foreach (var element in conditionsData)
                element.style.display = unavailable ? DisplayStyle.None : DisplayStyle.Flex;
            conditionsState.style.display = unavailable ? DisplayStyle.Flex : DisplayStyle.None;

            if (unavailable)
            {
                conditionsState.Variant = vm.IsOffline ? MvStateViewVariant.Offline : MvStateViewVariant.Error;
                conditionsState.Title = vm.ConditionsStateTitle;
                conditionsState.Body = vm.ConditionsStateBody;
                conditionsState.ActionText = vm.ConditionsStateAction;
                conditionsState.ActionEnabled = vm.ConditionsRetryEnabled;
                return;
            }

            waveValue.text = vm.WaveText;
            tideValue.text = vm.TideText;
            moonValue.text = vm.MoonText;
            windValue.text = vm.WindText;
            waterValue.text = vm.WaterText;
            freshnessLabel.text = vm.FreshnessText;

            var tide = vm.CurrentTide;
            sparkline.SetSamples(tide.Valid ? tide.Next24hHeights : null);
        }
    }
}
