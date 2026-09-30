using System;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Mergulho (AR) — DESIGN_IMPLEMENTATION.md §8.4, Figma frame Tela 8
    /// (31:3979). The bottom-bar destination that replaced the uGUI AR HUD
    /// (<c>ScreenUI/MainScreen/TopBar</c>) in Slice 4.
    ///
    /// <para><b>This screen is a HUD, not a page.</b> Everything behind it is the
    /// live camera, so — unlike every other screen here — it paints no opaque
    /// surface at all: the root and every container are transparent and
    /// <see cref="PickingMode.Ignore"/>, and only the two real surfaces (the top
    /// controls and the species card) are pickable. That is what lets a tap on
    /// open water fall through the UI Toolkit panel to the uGUI canvas and the AR
    /// scene underneath, which is where it has to land for
    /// <c>ObjectInteraction</c>'s raycast to see it.</para>
    ///
    /// <para><b>AR itself is not this screen's business.</b> The session, the
    /// camera feed, the per-beach spawning and the raycast all stay in
    /// Assembly-CSharp; the only thing that crosses the assembly boundary is a
    /// species key on <see cref="IArSelection"/> (Slice 4's rule: keep
    /// ObjectInteraction as the hit source, move only the presentation). The AR
    /// session being enabled on this route is <c>ScreenManager</c>'s job, driven
    /// by the router's route key — not by anything here.</para>
    ///
    /// <para><b>What survived underneath — nothing, now.</b>
    /// <c>ScreenUI/MainScreen</c> stayed active on this route for one reason: the
    /// on-beach <c>ArTuning</c> pill and panel, the field-tuning UI for the AR
    /// stabilisation filters, which was uGUI and which this slice deliberately did
    /// not port. It sat at 72% of the screen height on the right, clear of both the
    /// control strip and the card. <b>It was deleted on 2026-09-29</b>, so nothing
    /// paints beneath this screen any more and MainScreen is an empty shell awaiting
    /// deletion — see AppUiHost.OnRouteChanged.</para>
    ///
    /// <para>Router-agnostic like the rest: it raises <see cref="BackRequested"/>
    /// and the host decides what "back" means from a tab root.</para>
    /// </summary>
    public sealed class MergulhoScreen : VisualElement, IAppScreen
    {
        readonly MergulhoViewModel vm;
        readonly PraiasViewModel praias;

        readonly MvHeroHeader hero;
        readonly VisualElement dock;
        readonly VisualElement card;
        readonly Label titleLabel;
        readonly Label binomialLabel;
        readonly VisualElement divider;
        readonly VisualElement specs;

        bool subscribed;

        /// <summary>
        /// The hero's back arrow was tapped. Tela 8 draws one even though Mergulho
        /// is a tab root with nothing on the back stack, so the host resolves it:
        /// pop if there is anything to pop, otherwise leave the AR view for the
        /// landing route. The screen does not decide that.
        /// </summary>
        public event Action BackRequested;

        public MergulhoScreen(MergulhoViewModel viewModel, PraiasViewModel praiasViewModel)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            praias = praiasViewModel ?? throw new ArgumentNullException(nameof(praiasViewModel));

            AddToClassList("mv-mergulho");
            pickingMode = PickingMode.Ignore;

            // Image-less hero: MvHeroHeader hides its scrim when there is no photo
            // (a grey veil over a live camera would be exactly wrong), which is the
            // case this component was built to cover — see its class remarks.
            // pickingMode Ignore on the hero ROOT only: in UI Toolkit that excludes
            // the element itself, not its children, so the back button and the pill
            // stay tappable while the transparent rest of the strip does not eat
            // taps meant for an animal behind it.
            hero = new MvHeroHeader { ShowBackButton = true, ShowSelector = true };
            hero.AddToClassList("mv-mergulho__hero");
            hero.pickingMode = PickingMode.Ignore;
            hero.BackClicked += () => BackRequested?.Invoke();
            hero.SelectorClicked += OpenBeachSelector;
            Add(hero);

            // The card is bottom-anchored ABOVE the navigation bar, not a bottom
            // sheet (§8.4). The dock is the flexible space between the control
            // strip and the bar; it is transparent and non-pickable, so the whole
            // middle of the screen is the AR scene's.
            dock = new VisualElement { pickingMode = PickingMode.Ignore };
            dock.AddToClassList("mv-mergulho__dock");
            Add(dock);

            dock.Add(card = BuildCard(out titleLabel, out binomialLabel, out divider, out specs));

            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Mergulho;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += Render;
                praias.Changed += Render;
                subscribed = true;
            }
            // The override may have changed from the Praias screens while this one
            // was hidden, and GPS may have moved. Idempotent.
            praias.SyncToActiveBeach();
            // Only now does a tap on the AR scene mean anything: the hit source is
            // at the scene root and would otherwise raycast from every screen.
            vm.SetListening(true);
            Render();
        }

        public void OnExit()
        {
            // Before Unsubscribe, deliberately: stopping the listen also closes the
            // card, and that has to reach this screen's Render while it is still
            // subscribed — otherwise the tree keeps a visible card that the
            // ViewModel says is closed, and the next OnEnter shows it for a frame.
            vm.SetListening(false);
            Unsubscribe();
        }

        /// <summary>
        /// Safe-area insets in panel units. The TOP inset goes to
        /// <see cref="MvHeroHeader.TopInset"/>, which pads the controls plane only
        /// — there is no page surface here to pad, and the camera is meant to run
        /// under the status bar. Left/right pad the dock so the card clears a
        /// notch's side cut-outs in landscape-ish cases; bottom is the navigation
        /// bar's reservation on the root, which is what the card's margin is
        /// measured from.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            hero.TopInset = top;
            dock.style.paddingLeft = left;
            dock.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            vm.Changed -= Render;
            praias.Changed -= Render;
            subscribed = false;
        }

        /// <summary>
        /// The same selector as Tela 9, opened from the same helper so the two
        /// screens cannot drift. Picking a beach here sets the GPS override, which
        /// re-points the AR spawner, the conditions fetch and the Praias screens
        /// together — there is deliberately no separate "beach I am looking at".
        /// </summary>
        void OpenBeachSelector() =>
            PraiasScreen.OpenBeachMenu(hero.Selector, praias, praias.SelectBeach);

        // ---- Species info card ----------------------------------------------

        /// <summary>
        /// 342×176, V, pad 16, gap 12, over the camera (§8.4). Built once and
        /// shown/hidden; only the spec rows are rebuilt, since they are the only
        /// part whose shape depends on the species.
        /// </summary>
        VisualElement BuildCard(
            out Label title, out Label binomial, out VisualElement rule, out VisualElement specRows)
        {
            // Pickable (the default): the card is an opaque surface, and a tap that
            // lands on it must not also reach the animal behind it.
            var root = new VisualElement();
            root.AddToClassList("mv-mergulho__card");

            var titleBlock = new VisualElement { pickingMode = PickingMode.Ignore };
            titleBlock.AddToClassList("mv-mergulho__title-block");

            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-large");
            title.AddToClassList("mv-mergulho__title");
            titleBlock.Add(title);

            binomial = new Label { pickingMode = PickingMode.Ignore };
            binomial.AddToClassList("md-typescale-body-medium");
            binomial.AddToClassList("mv-mergulho__binomial");
            titleBlock.Add(binomial);

            root.Add(titleBlock);

            rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.AddToClassList("mv-mergulho__rule");
            root.Add(rule);

            specRows = new VisualElement { pickingMode = PickingMode.Ignore };
            specRows.AddToClassList("mv-mergulho__specs");
            root.Add(specRows);

            // Last child so it paints over the title block, whose 310dp measure
            // runs under the disc in the Figma frame too.
            var close = new MdIconButton { Icon = "close", tooltip = vm.CloseLabel };
            close.AddToClassList("mv-mergulho__close");
            close.Clicked += vm.CloseCard;
            root.Add(close);

            return root;
        }

        void RenderSpecs()
        {
            specs.Clear();
            var rows = vm.SpecRows;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("mv-mergulho__spec");
                if (i > 0) row.AddToClassList("mv-mergulho__spec--gutter"); // USS has no `gap`.

                var label = new Label(rows[i].Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-label-medium");
                label.AddToClassList("mv-mergulho__spec-label");
                row.Add(label);

                var value = new Label(rows[i].Value) { pickingMode = PickingMode.Ignore };
                value.AddToClassList("md-typescale-body-medium");
                value.AddToClassList("mv-mergulho__spec-value");
                row.Add(value);

                specs.Add(row);
            }
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            hero.SelectorText = praias.LocationTitleText;

            bool open = vm.HasSelection;
            card.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open)
            {
                specs.Clear();
                return;
            }

            titleLabel.text = vm.TitleText ?? "";

            bool hasBinomial = vm.HasBinomial;
            binomialLabel.text = hasBinomial ? vm.BinomialText : "";
            binomialLabel.style.display = hasBinomial ? DisplayStyle.Flex : DisplayStyle.None;

            RenderSpecs();

            // The rule only separates the name from the spec table. With no rows
            // below it there is nothing to separate, and a line across the bottom
            // of a card reads as a rendering bug — the same call Praia detalhe
            // makes about its stats divider. Empty is the state of EVERY species
            // today: the three AnimalDef fields ship blank on purpose (D8).
            bool hasSpecs = vm.HasSpecRows;
            divider.style.display = hasSpecs ? DisplayStyle.Flex : DisplayStyle.None;
            specs.style.display = hasSpecs ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
