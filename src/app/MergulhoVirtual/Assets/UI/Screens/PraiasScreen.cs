using System;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Praias landing — DESIGN_IMPLEMENTATION.md §8.2, Figma frame Tela 1
    /// (9:109). The Praias tab root, replacing the browsable beach list (§7).
    ///
    /// <para>Built in code like <see cref="HomeScreen"/>, for the same reason:
    /// the pieces that carry content (MvHeroHeader.SetImage/SetBadges, MdMenu)
    /// are code-only APIs, so a UXML file would hold nothing but empty boxes.
    /// Router-agnostic on purpose — it raises <see cref="AppRoutes"/> keys and
    /// <see cref="DetailRequested"/>, and the host decides Navigate vs Push.</para>
    ///
    /// <para><b>The screen is short by design and shorter still in practice.</b>
    /// Tela 1 itself ends with ~200dp of bare page below the CTA card, and most
    /// of what it draws is editorial content nobody has written yet: the risk
    /// pill is filled for 0 of 17 beaches and the sighting peak for 0 of 17. So
    /// the landing is built from the three things that are always true — the
    /// beach you are at, the live tide, and the invitation to report — and the
    /// risk pill simply is not drawn when there is no risk level. The two stat
    /// columns keep their "—" because a stat column is a fixed slot whose label
    /// is the content (BeachContentFormatter's two conventions), and dropping
    /// one would leave a lone half-width card.</para>
    ///
    /// <para><b>No beach resolved is a first-class state</b>, not an error: the
    /// hero falls back to its placeholder, the badges disappear, and the location
    /// card becomes the beach selector ("Escolha uma praia"). That is also the
    /// screen's escape hatch — without it, a user whose GPS is off every polygon
    /// could reach no beach at all now that the list is gone.</para>
    /// </summary>
    public sealed class PraiasScreen : VisualElement, IAppScreen
    {
        readonly PraiasViewModel vm;
        readonly Func<string, Sprite> loadBeachSprite;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly MvHeroHeader hero;
        readonly VisualElement heroPlaceholder;
        readonly VisualElement locationCard;
        readonly Label youAreAtLabel;
        readonly Label beachNameLabel;
        readonly MvTag riskTag;
        readonly Label tideValue;
        readonly Label peakValue;

        bool subscribed;

        /// <summary>
        /// Raised with an <see cref="AppRoutes"/> key when a surface that leads
        /// somewhere else is tapped (today: the CTA's "Reportar" button →
        /// <see cref="AppRoutes.Avistamentos"/>).
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// The user asked to open the active beach's page. Separate from
        /// <see cref="NavigationRequested"/> because it is a push onto this tab's
        /// stack rather than a destination change, and because it is only ever
        /// raised when a beach is actually open.
        /// </summary>
        public event Action DetailRequested;

        public PraiasScreen(PraiasViewModel viewModel, Func<string, Sprite> beachSpriteLoader)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            loadBeachSprite = beachSpriteLoader;

            AddToClassList("mv-praias");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-praias__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-praias__scroll");
            content.Add(scroll);

            // Hero: 240dp compact, photo + scrim + environment badges, no controls
            // (Tela 1 has none — the selector lives on the location card below,
            // where it also covers the no-beach state).
            hero = new MvHeroHeader { Compact = true };
            hero.AddToClassList("mv-praias__hero");
            heroPlaceholder = MakeHeroPlaceholder();
            // Index 0 so it paints BEHIND the photo, the scrim and the controls;
            // MvHeroHeader has no contentContainer override, so this is the raw
            // child order the component itself uses.
            hero.Insert(0, heroPlaceholder);
            scroll.Add(hero);

            // Main Content Body: V, gap 16, padding 20/16/100/16. The 100dp bottom
            // is clearance for the pinned navigation bar, which the shell already
            // reserves below this screen, so only a short scroll tail remains.
            var body = new VisualElement();
            body.AddToClassList("mv-praias__body");
            scroll.Add(body);

            body.Add(locationCard = BuildLocationCard(
                out youAreAtLabel, out beachNameLabel, out riskTag));
            body.Add(BuildStatsRow(out tideValue, out peakValue));
            body.Add(BuildCtaCard());

            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());

            // First paint without subscribing — a screen must never render empty
            // rows if it is shown before its first OnEnter.
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Praias;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += Render;
                subscribed = true;
            }
            // Re-resolve on entry: GPS may have moved (or the override changed
            // from the detail screen) while this screen was hidden. Idempotent,
            // and it raises Changed, which repaints.
            vm.SyncToActiveBeach();
            Render();
            scroll.scrollOffset = Vector2.zero;
        }

        public void OnExit() => Unsubscribe();

        /// <summary>
        /// Safe-area insets in panel units. The hero is full-bleed, so the TOP
        /// inset goes to <see cref="MvHeroHeader.TopInset"/> (which pads the
        /// controls plane, not the photo) instead of to the content container —
        /// otherwise the page would paint a blank strip above the photo. Left and
        /// right are padding on the opaque container, as elsewhere; bottom is the
        /// navigation-bar reservation on the transparent root.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            hero.TopInset = top;
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

        // ---- Hero -----------------------------------------------------------

        /// <summary>
        /// What the three beaches with no cover photo get (Quixaba, Enseada dos
        /// Tubarões, Praia do Americano): the same token fill + glyph the list
        /// cards used, so an imageless hero keeps the screen's shape instead of
        /// leaving a hole where a 240dp photo should be.
        /// </summary>
        static VisualElement MakeHeroPlaceholder()
        {
            var placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("mv-praias__hero-placeholder");
            var icon = new MdIcon { Icon = "waves" };
            icon.AddToClassList("mv-praias__hero-placeholder-icon");
            placeholder.Add(icon);
            return placeholder;
        }

        // ---- Location card --------------------------------------------------

        VisualElement BuildLocationCard(out Label caption, out Label name, out MvTag risk)
        {
            var card = new VisualElement { focusable = true };
            card.AddToClassList("mv-praias__card");
            card.AddToClassList("mv-praias__location");

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("mv-praias__location-head");

            caption = new Label(vm.YouAreAtText) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("md-typescale-label-medium");
            caption.AddToClassList("mv-praias__location-caption");
            head.Add(caption);

            var nameRow = new VisualElement { pickingMode = PickingMode.Ignore };
            nameRow.AddToClassList("mv-praias__location-name-row");
            name = new Label { pickingMode = PickingMode.Ignore };
            name.AddToClassList("md-typescale-headline-small");
            name.AddToClassList("mv-praias__location-name");
            nameRow.Add(name);
            // Not in Tela 1, which gives the card no affordance at all. It needs
            // one: the card is the only way into a beach page now that the list
            // is gone, and in the no-beach state it is the only way to the
            // selector.
            var chevron = new MdIcon { Icon = "chevron_right" };
            chevron.AddToClassList("mv-praias__location-chevron");
            nameRow.Add(chevron);
            head.Add(nameRow);
            card.Add(head);

            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.AddToClassList("mv-praias__divider");
            card.Add(divider);

            var footRow = new VisualElement { pickingMode = PickingMode.Ignore };
            footRow.AddToClassList("mv-praias__location-foot");
            var region = new Label(vm.RegionText) { pickingMode = PickingMode.Ignore };
            region.AddToClassList("md-typescale-body-large");
            region.AddToClassList("mv-praias__location-region");
            footRow.Add(region);
            risk = new MvTag { Size = MvTagSize.Medium };
            risk.AddToClassList("mv-praias__risk");
            footRow.Add(risk);
            card.Add(footRow);

            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            card.Add(stateLayer);

            card.AddManipulator(new Clickable(OnLocationCardClicked));
            return card;
        }

        /// <summary>
        /// One target, two meanings, decided by whether a beach is open: open its
        /// page, or — when nothing is resolved — open the selector, which pins a
        /// beach, makes it the active one (through the ViewModel) and opens its
        /// page in the same gesture.
        /// </summary>
        void OnLocationCardClicked()
        {
            if (vm.HasActiveBeach) DetailRequested?.Invoke();
            else OpenBeachSelector();
        }

        /// <summary>
        /// Picking from the landing card goes straight into the beach's page: the
        /// card the user tapped said "escolha uma praia", so choosing one has to
        /// land somewhere. "Automático (GPS)" is the exception — it hands control
        /// back, and if GPS still resolves nothing there is nothing to open.
        /// </summary>
        void OpenBeachSelector() => OpenBeachMenu(locationCard, vm, index =>
        {
            vm.SelectBeach(index);
            if (vm.HasActiveBeach) DetailRequested?.Invoke();
        });

        /// <summary>
        /// The beach selector, opened from the landing card here and from the hero
        /// pill on Praia detalhe — one place so the two cannot drift.
        ///
        /// <para>The scroller is hidden the way <see cref="AppScrollView"/> hides
        /// it everywhere else: MdMenu builds a stock ScrollView, and with 18
        /// entries the list always overflows, so without this the one place in the
        /// app that shows UI Toolkit's desktop scrollbar-with-steppers would be
        /// the beach picker. It is set from C# because scroller visibility is an
        /// inline style USS cannot reach. This belongs in MdMenu itself — it is
        /// here because the menu is a design-system component and this slice does
        /// not own it.</para>
        /// </summary>
        internal static void OpenBeachMenu(VisualElement anchor, PraiasViewModel vm, Action<int> onSelect)
        {
            var menu = MdMenu.Open(anchor, vm.SelectorChoices, vm.SelectorIndex, onSelect);
            menu.Root.AddToClassList("mv-beach-menu");
            var scroll = menu.Root.Q<ScrollView>();
            if (scroll != null)
            {
                scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
        }

        // ---- Stats ----------------------------------------------------------

        VisualElement BuildStatsRow(out Label tide, out Label peak)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-praias__stats-row");
            row.Add(MakeStatCard(BeachDetailViewModel.TideNowLabel, out tide));
            var second = MakeStatCard(BeachDetailViewModel.SightingPeakLabel, out peak);
            second.AddToClassList("mv-praias__stat--gutter"); // USS has no `gap`.
            row.Add(second);
            return row;
        }

        static VisualElement MakeStatCard(string caption, out Label value)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praias__card");
            card.AddToClassList("mv-praias__stat");

            var captionLabel = new Label(caption) { pickingMode = PickingMode.Ignore };
            captionLabel.AddToClassList("md-typescale-label-medium");
            captionLabel.AddToClassList("mv-praias__stat-caption");
            card.Add(captionLabel);

            value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("md-typescale-title-medium");
            value.AddToClassList("mv-praias__stat-value");
            card.Add(value);
            return card;
        }

        // ---- CTA ------------------------------------------------------------

        VisualElement BuildCtaCard()
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praias__cta");

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("mv-praias__cta-text");

            var title = new Label(PraiasViewModel.CtaTitle) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-small");
            title.AddToClassList("mv-praias__cta-title");
            text.Add(title);

            var bodyLabel = new Label(PraiasViewModel.CtaBody) { pickingMode = PickingMode.Ignore };
            bodyLabel.AddToClassList("md-typescale-body-medium");
            bodyLabel.AddToClassList("mv-praias__cta-body");
            text.Add(bodyLabel);

            card.Add(text);

            var button = new MdButton
            {
                Variant = MdButtonVariant.Filled,
                Text = PraiasViewModel.CtaButtonLabel,
            };
            button.AddToClassList("mv-praias__cta-button");
            button.Clicked += () => NavigationRequested?.Invoke(AppRoutes.Avistamentos);
            card.Add(button);

            return card;
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            var detail = vm.Detail;

            // The placeholder follows the RESOLVED sprite, not HasImage: a beach can
            // name a photo that is not in Resources, and that must not leave a blank hero.
            var cover = detail.HasImage ? loadBeachSprite?.Invoke(detail.ImageName) : null;
            hero.SetImage(cover);
            heroPlaceholder.style.display = cover != null ? DisplayStyle.None : DisplayStyle.Flex;
            hero.SetBadges(detail.HasEnvironmentTags ? detail.EnvironmentTags : null);

            youAreAtLabel.style.display = vm.HasYouAreAtLabel ? DisplayStyle.Flex : DisplayStyle.None;
            beachNameLabel.text = vm.LocationTitleText;

            // No risk level is filled for any beach today, so this pill is absent
            // in practice — and absent is the point: an unknown risk must never
            // render as a reassuring "Baixo".
            bool hasRisk = detail.HasRisk;
            riskTag.style.display = hasRisk ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasRisk)
            {
                riskTag.Text = detail.RiskPillText;
                riskTag.Variant = RiskVariant(detail.RiskLevel);
            }

            tideValue.text = detail.TideNowText;
            peakValue.text = detail.SightingPeakText;
        }

        /// <summary>
        /// Risk level → tag color. V2 only ever draws the green "Baixo" pill; the
        /// other two levels take the warning and error roles, which is what those
        /// roles are for, rather than a third brand color nobody has specified.
        /// </summary>
        internal static MvTagVariant RiskVariant(BeachRiskLevel level)
        {
            switch (level)
            {
                case BeachRiskLevel.Low: return MvTagVariant.Success;
                case BeachRiskLevel.Medium: return MvTagVariant.Warning;
                case BeachRiskLevel.High: return MvTagVariant.Error;
                default: return MvTagVariant.Neutral;
            }
        }
    }
}
