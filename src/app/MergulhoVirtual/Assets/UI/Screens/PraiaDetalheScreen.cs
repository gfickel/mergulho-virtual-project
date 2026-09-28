using System;
using System.Collections.Generic;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Praia detalhe — DESIGN_IMPLEMENTATION.md §8.3, Figma frames Tela 4
    /// (15:550) and Tela 9 (89:1930, the beach dropdown open). A sub-screen
    /// pushed from <see cref="PraiasScreen"/> and dismissed with the hero's back
    /// button; the bottom bar stays visible with Praias selected.
    ///
    /// <para>Built in code for the same reason as the other screens (the content
    /// APIs — SetImage/SetBadges/SetItems/MdMenu — are code-only) and
    /// router-agnostic for the same reason: it raises
    /// <see cref="BackRequested"/> and <see cref="AppRoutes"/> keys, the host
    /// maps them.</para>
    ///
    /// <para><b>The empty screen is the normal screen.</b> Measured over the 17
    /// beaches in places.json: risk level, best season, sighting peak, lifeguard
    /// hours and tips are filled for NONE of them; species for 3; environment
    /// tags for 11; advisories for 7; ideal tide for 4; and the sighting count
    /// and the gallery have no data source at all. So 14 of 17 beaches render
    /// nothing below the alert bar. Every block is therefore gated on the
    /// ViewModel's <c>Has*</c> flag and simply is not built into the layout when
    /// it is false — no placeholder strings, no empty cards, no headings over
    /// nothing. ("—" appears only inside the two stat columns, which are fixed
    /// slots whose label is the content.)</para>
    ///
    /// <para><b>What keeps a content-empty beach from looking broken</b> is the
    /// description card: places.json carries a 170–280 character description for
    /// all 17 beaches, so hero + title + description is a real page for every
    /// one of them. That card is NOT in the Figma frame — V2 assumes the
    /// editorial content exists — and it is the one deliberate addition here.
    /// The photo credit rides in the same card because it belongs to the hero
    /// photo and has nowhere else to go once the sections above it vanish.</para>
    /// </summary>
    public sealed class PraiaDetalheScreen : VisualElement, IAppScreen
    {
        readonly PraiasViewModel praias;
        readonly BeachDetailViewModel vm;
        readonly Func<string, Sprite> loadBeachSprite;
        readonly Func<string, Sprite> loadSpeciesSprite;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly MvHeroHeader hero;
        readonly VisualElement heroPlaceholder;

        readonly VisualElement titleRow;
        readonly Label titleLabel;
        readonly MvTag riskTag;

        readonly VisualElement aboutCard;
        readonly Label descriptionLabel;
        readonly Label creditLabel;

        readonly VisualElement statsCard;
        readonly Label seasonValue;
        readonly Label tideValue;
        readonly Label tideCaption;
        readonly VisualElement statsDivider;
        readonly VisualElement sightingsRow;
        readonly Label sightingsValue;

        readonly VisualElement alerts;

        readonly VisualElement speciesSection;
        readonly VisualElement chipsTrack;
        readonly VisualElement speciesCard;
        readonly VisualElement speciesMedia;
        readonly Image speciesImage;
        readonly VisualElement speciesPlaceholder;
        readonly MvTag speciesTag;
        readonly Label speciesName;
        readonly Label speciesBinomial;
        readonly Label speciesBehaviour;

        readonly VisualElement tipsSection;
        readonly MvNumberedList tips;

        readonly VisualElement gallerySection;
        readonly MvMediaCarousel gallery;

        bool subscribed;

        /// <summary>The hero's back button was tapped — pop the back stack.</summary>
        public event Action BackRequested;

        /// <summary>
        /// Raised with an <see cref="AppRoutes"/> key for the two surfaces that
        /// lead off this screen: the floating SOS button
        /// (<see cref="AppRoutes.Sos"/>) and "Saiba mais sobre a espécie"
        /// (<see cref="AppRoutes.Especie"/>). Neither route has a screen yet
        /// (Slice 5 / Decision D1), and that is a deliberate no-op: MdRouter logs
        /// one warning and leaves the screen exactly where it is.
        /// </summary>
        public event Action<string> NavigationRequested;

        public PraiaDetalheScreen(
            PraiasViewModel viewModel,
            Func<string, Sprite> beachSpriteLoader,
            Func<string, Sprite> speciesSpriteLoader)
        {
            praias = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            vm = praias.Detail;
            loadBeachSprite = beachSpriteLoader;
            loadSpeciesSprite = speciesSpriteLoader;

            AddToClassList("mv-praia");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-praia__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-praia__scroll");
            content.Add(scroll);

            hero = new MvHeroHeader { ShowBackButton = true, ShowSelector = true };
            hero.AddToClassList("mv-praia__hero");
            heroPlaceholder = MakeHeroPlaceholder();
            hero.Insert(0, heroPlaceholder); // behind photo, scrim and controls
            hero.BackClicked += () => BackRequested?.Invoke();
            hero.SelectorClicked += OpenBeachSelector;
            scroll.Add(hero);

            // Main Content Body: V, gap 24, padding 24/16/120/16. The 120dp bottom
            // is nav-bar clearance the shell already reserves, so only a tail is kept.
            var body = new VisualElement();
            body.AddToClassList("mv-praia__body");
            scroll.Add(body);

            body.Add(titleRow = BuildTitleRow(out titleLabel, out riskTag));
            body.Add(aboutCard = BuildAboutCard(out descriptionLabel, out creditLabel));
            body.Add(statsCard = BuildStatsCard(
                out seasonValue, out tideValue, out tideCaption,
                out statsDivider, out sightingsRow, out sightingsValue));

            alerts = new VisualElement { pickingMode = PickingMode.Ignore };
            alerts.AddToClassList("mv-praia__alerts");
            body.Add(alerts);

            body.Add(speciesSection = BuildSpeciesSection(
                out chipsTrack, out speciesCard, out speciesMedia, out speciesImage,
                out speciesPlaceholder, out speciesTag, out speciesName, out speciesBinomial,
                out speciesBehaviour));

            body.Add(tipsSection = BuildTipsSection(out tips));
            body.Add(gallerySection = BuildGallerySection(out gallery));

            Add(BuildSosButton());

            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.PraiaDetalhe;

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
        /// Safe-area insets in panel units. The hero is full-bleed, so the TOP
        /// inset goes to <see cref="MvHeroHeader.TopInset"/> — which pads the
        /// controls plane only, leaving the photo running under the status bar —
        /// rather than to the content container, which would paint a blank strip
        /// above it. Left/right are padding on the opaque container; bottom is
        /// the navigation-bar reservation on the transparent root, and is what
        /// the floating SOS button measures its offset from.
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

        static VisualElement MakeHeroPlaceholder()
        {
            var placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("mv-praia__hero-placeholder");
            var icon = new MdIcon { Icon = "waves" };
            icon.AddToClassList("mv-praia__hero-placeholder-icon");
            placeholder.Add(icon);
            return placeholder;
        }

        /// <summary>
        /// Tela 9: a stock M3 menu hung off the hero pill, listing "Automático
        /// (GPS)" plus every beach. This is the ONLY way to reach a beach other
        /// than the resolved one now that the list screen is gone (§7), which is
        /// why the pill is on the hero of every beach page and why the landing's
        /// location card opens this same menu when GPS has resolved nothing.
        /// </summary>
        void OpenBeachSelector() =>
            PraiasScreen.OpenBeachMenu(hero.Selector, praias, praias.SelectBeach);

        // ---- Title row ------------------------------------------------------

        VisualElement BuildTitleRow(out Label title, out MvTag risk)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-praia__title-row");

            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("mv-praia__title-column");

            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-large");
            title.AddToClassList("mv-praia__title");
            column.Add(title);

            var region = new Label(vm.RegionText) { pickingMode = PickingMode.Ignore };
            region.AddToClassList("md-typescale-body-medium");
            region.AddToClassList("mv-praia__region");
            column.Add(region);

            row.Add(column);

            risk = new MvTag { Size = MvTagSize.Medium };
            risk.AddToClassList("mv-praia__risk");
            row.Add(risk);
            return row;
        }

        // ---- Description (not in V2 — see the class remarks) -----------------

        VisualElement BuildAboutCard(out Label description, out Label credit)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praia__card");
            card.AddToClassList("mv-praia__about");

            description = new Label { pickingMode = PickingMode.Ignore };
            description.AddToClassList("md-typescale-body-large");
            description.AddToClassList("mv-praia__description");
            card.Add(description);

            credit = new Label { pickingMode = PickingMode.Ignore };
            credit.AddToClassList("md-typescale-label-small");
            credit.AddToClassList("mv-praia__credit");
            card.Add(credit);
            return card;
        }

        // ---- Stats card -----------------------------------------------------

        VisualElement BuildStatsCard(
            out Label season, out Label tide, out Label tideNext,
            out VisualElement divider, out VisualElement sightings, out Label sightingsText)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praia__card");
            card.AddToClassList("mv-praia__stats");

            var columns = new VisualElement { pickingMode = PickingMode.Ignore };
            columns.AddToClassList("mv-praia__stat-columns");

            var seasonColumn = MakeStatColumn(BeachDetailViewModel.BestSeasonLabel, out season);
            columns.Add(seasonColumn);

            var tideColumn = MakeStatColumn(BeachDetailViewModel.IdealTideLabel, out tide);
            tideColumn.AddToClassList("mv-praia__stat-column--gutter"); // USS has no `gap`.
            // V2 writes "Baixa (até 14h)", asserting a window the DHN table does not
            // publish; the ViewModel splits it into the tide word and a live
            // "próx. baixa 14:40" caption, so the two stack here instead.
            tideNext = new Label { pickingMode = PickingMode.Ignore };
            tideNext.AddToClassList("md-typescale-body-small");
            tideNext.AddToClassList("mv-praia__stat-caption");
            tideColumn.Add(tideNext);
            columns.Add(tideColumn);

            card.Add(columns);

            divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.AddToClassList("mv-praia__divider");
            card.Add(divider);

            sightings = new VisualElement { pickingMode = PickingMode.Ignore };
            sightings.AddToClassList("mv-praia__sightings");

            var icon = new MdIcon { Icon = "bar_chart" };
            icon.AddToClassList("mv-praia__sightings-icon");
            sightings.Add(icon);

            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("mv-praia__sightings-text");
            var label = new Label(BeachDetailViewModel.SightingsLabel) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("md-typescale-label-medium");
            label.AddToClassList("mv-praia__stat-label");
            column.Add(label);
            sightingsText = new Label { pickingMode = PickingMode.Ignore };
            sightingsText.AddToClassList("mv-praia__sightings-value");
            column.Add(sightingsText);
            sightings.Add(column);

            card.Add(sightings);
            return card;
        }

        static VisualElement MakeStatColumn(string label, out Label value)
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("mv-praia__stat-column");

            var caption = new Label(label) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("md-typescale-label-medium");
            caption.AddToClassList("mv-praia__stat-label");
            column.Add(caption);

            value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("md-typescale-title-medium");
            value.AddToClassList("mv-praia__stat-value");
            column.Add(value);
            return column;
        }

        // ---- Species --------------------------------------------------------

        VisualElement BuildSpeciesSection(
            out VisualElement chips, out VisualElement card, out VisualElement media,
            out Image image, out VisualElement placeholder, out MvTag tag,
            out Label name, out Label binomial, out Label behaviour)
        {
            var section = MakeSection(BeachDetailViewModel.SpeciesSectionTitle);

            // Horizontal ScrollView: V2 draws nine chips in a 360dp row that
            // overflows the frame. AppScrollView keeps the stock scrollbar off.
            var chipScroll = new AppScrollView(ScrollViewMode.Horizontal);
            chipScroll.AddToClassList("mv-praia__chips");
            chips = chipScroll.contentContainer;
            chips.AddToClassList("mv-praia__chips-track");
            section.Add(chipScroll);

            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praia__card");
            card.AddToClassList("mv-praia__species-card");

            media = new VisualElement { pickingMode = PickingMode.Ignore };
            media.AddToClassList("mv-praia__species-media");

            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleAndCrop };
            image.AddToClassList("mv-praia__species-image");
            media.Add(image);

            placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("mv-praia__species-placeholder");
            var placeholderIcon = new MdIcon { Icon = "waves" };
            placeholderIcon.AddToClassList("mv-praia__hero-placeholder-icon");
            placeholder.Add(placeholderIcon);
            media.Add(placeholder);

            tag = new MvTag { Variant = MvTagVariant.OnImage, Size = MvTagSize.Small };
            tag.AddToClassList("mv-praia__species-tag");
            media.Add(tag);
            card.Add(media);

            var cardContent = new VisualElement { pickingMode = PickingMode.Ignore };
            cardContent.AddToClassList("mv-praia__species-content");

            var nameRow = new VisualElement { pickingMode = PickingMode.Ignore };
            nameRow.AddToClassList("mv-praia__species-name-row");
            name = new Label { pickingMode = PickingMode.Ignore };
            name.AddToClassList("md-typescale-title-medium");
            name.AddToClassList("mv-praia__species-name");
            nameRow.Add(name);
            binomial = new Label { pickingMode = PickingMode.Ignore };
            binomial.AddToClassList("md-typescale-body-small");
            binomial.AddToClassList("mv-praia__species-binomial");
            nameRow.Add(binomial);
            cardContent.Add(nameRow);

            behaviour = new Label { pickingMode = PickingMode.Ignore };
            behaviour.AddToClassList("md-typescale-body-large");
            behaviour.AddToClassList("mv-praia__species-behaviour");
            cardContent.Add(behaviour);

            var learnMore = new Label(BeachDetailViewModel.SpeciesLearnMoreLabel) { focusable = true };
            learnMore.AddToClassList("md-typescale-label-large");
            learnMore.AddToClassList("mv-praia__species-link");
            learnMore.AddManipulator(new Clickable(
                () => NavigationRequested?.Invoke(AppRoutes.Especie)));
            cardContent.Add(learnMore);

            card.Add(cardContent);
            section.Add(card);
            return section;
        }

        void RenderSpecies()
        {
            bool has = vm.HasSpecies;
            speciesSection.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            RebuildChips(vm.SpeciesChips, vm.SelectedSpeciesIndex);

            var species = vm.SelectedSpecies;
            if (species == null)
            {
                speciesCard.style.display = DisplayStyle.None;
                return;
            }
            speciesCard.style.display = DisplayStyle.Flex;

            var sprite = species.HasImage ? loadSpeciesSprite?.Invoke(species.ImageName) : null;
            speciesImage.sprite = sprite;
            speciesImage.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            speciesPlaceholder.style.display = sprite != null ? DisplayStyle.None : DisplayStyle.Flex;

            speciesTag.style.display = species.HasTag ? DisplayStyle.Flex : DisplayStyle.None;
            if (species.HasTag) speciesTag.Text = species.TagText;

            speciesName.text = species.DisplayName ?? "";
            speciesBinomial.text = species.HasBinomial ? species.Binomial : "";
            speciesBinomial.style.display = species.HasBinomial ? DisplayStyle.Flex : DisplayStyle.None;

            speciesBehaviour.text = species.HasBehaviour ? species.BehaviourText : "";
            speciesBehaviour.style.display = species.HasBehaviour ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Chips are rebuilt rather than re-labelled: the list changes only when
        /// the beach changes, and rebuilding keeps the selected index, the chip
        /// order and the click closures in one place.
        /// </summary>
        void RebuildChips(IReadOnlyList<BeachSpeciesChip> species, int selectedIndex)
        {
            chipsTrack.Clear();
            for (int i = 0; i < species.Count; i++)
            {
                int index = i;
                // Assist, not Filter: the selection is owned by the ViewModel, so a
                // chip must never toggle itself — tapping always selects, never
                // deselects, which is what a single-choice row means.
                var chip = new MdChip
                {
                    Kind = MdChipKind.Assist,
                    Text = species[i].Label,
                    Selected = i == selectedIndex,
                };
                chip.AddToClassList("mv-praia__chip");
                chip.Clicked += () => vm.SelectSpecies(index);
                chipsTrack.Add(chip);
            }
        }

        // ---- Tips / gallery -------------------------------------------------

        VisualElement BuildTipsSection(out MvNumberedList list)
        {
            var section = MakeSection(BeachDetailViewModel.TipsSectionTitle);
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-praia__card");
            card.AddToClassList("mv-praia__tips-card");
            list = new MvNumberedList();
            card.Add(list);
            section.Add(card);
            return section;
        }

        VisualElement BuildGallerySection(out MvMediaCarousel carousel)
        {
            var section = MakeSection(BeachDetailViewModel.GallerySectionTitle);
            carousel = new MvMediaCarousel();
            carousel.AddToClassList("mv-praia__gallery");
            section.Add(carousel);
            return section;
        }

        static VisualElement MakeSection(string title)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("mv-praia__section");
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("md-typescale-title-medium");
            heading.AddToClassList("mv-praia__section-title");
            section.Add(heading);
            return section;
        }

        // ---- Floating SOS ---------------------------------------------------

        /// <summary>
        /// 97×52 pill over the page. Figma parks it at x=16 in the flattened
        /// frame, i.e. bottom-LEFT, which is also the hand that is free while the
        /// other one holds the phone. It is a sibling of the content container,
        /// not of the scroll content, so it stays put while the page scrolls, and
        /// its bottom offset is measured from the root — whose padding-bottom is
        /// the navigation bar's reservation.
        /// </summary>
        VisualElement BuildSosButton()
        {
            var button = new VisualElement { focusable = true };
            button.AddToClassList("mv-praia__sos");

            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            button.Add(stateLayer);

            var icon = new MdIcon { Icon = "warning" };
            icon.AddToClassList("mv-praia__sos-icon");
            button.Add(icon);

            var label = new Label(PraiasViewModel.SosButtonLabel) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("mv-praia__sos-label");
            button.Add(label);

            button.AddManipulator(new Clickable(() => NavigationRequested?.Invoke(AppRoutes.Sos)));
            return button;
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            // A beach can disappear underneath this screen: clearing the override
            // while GPS resolves nothing closes the detail. Rather than a blank
            // page, the hero keeps its selector (labelled with the same call to
            // action the landing uses) so the user can pick one.
            bool hasBeach = vm.HasBeach;
            hero.SelectorText = hasBeach ? vm.Title : PraiasViewModel.ChooseBeachLabel;

            var sprite = hasBeach ? loadBeachSprite?.Invoke(vm.ImageName) : null;
            hero.SetImage(sprite);
            heroPlaceholder.style.display = sprite != null ? DisplayStyle.None : DisplayStyle.Flex;
            hero.SetBadges(vm.HasEnvironmentTags ? vm.EnvironmentTags : null);

            titleRow.style.display = hasBeach ? DisplayStyle.Flex : DisplayStyle.None;
            titleLabel.text = vm.Title;

            bool hasRisk = vm.HasRisk;
            riskTag.style.display = hasRisk ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasRisk)
            {
                riskTag.Text = vm.RiskPillText;
                riskTag.Variant = PraiasScreen.RiskVariant(vm.RiskLevel);
            }

            RenderAbout();
            RenderStats();
            RenderAlerts();
            RenderSpecies();

            bool hasTips = vm.HasTips;
            tipsSection.style.display = hasTips ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasTips) tips.SetItems(vm.Tips);

            // Always false today: user photos live in a private bucket behind
            // signed URLs and there is no public per-beach endpoint (§5.1). The
            // section stays wired so it lights up without reshaping the screen.
            gallerySection.style.display = vm.HasGallery ? DisplayStyle.Flex : DisplayStyle.None;
            if (!vm.HasGallery) gallery.SetItems(null);
        }

        void RenderAbout()
        {
            bool hasDescription = vm.HasDescription;
            bool hasCredit = vm.HasPhotoCredit;
            aboutCard.style.display =
                hasDescription || hasCredit ? DisplayStyle.Flex : DisplayStyle.None;

            descriptionLabel.text = hasDescription ? vm.DescriptionText : "";
            descriptionLabel.style.display = hasDescription ? DisplayStyle.Flex : DisplayStyle.None;

            creditLabel.text = hasCredit ? vm.PhotoCreditText : "";
            creditLabel.style.display = hasCredit ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RenderStats()
        {
            bool hasStats = vm.HasStats;
            statsCard.style.display = hasStats ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasStats) return;

            seasonValue.text = vm.BestSeasonText;
            tideValue.text = vm.IdealTideText;

            bool hasNext = vm.HasIdealTideNextEvent;
            tideCaption.text = hasNext ? vm.IdealTideNextEventText : "";
            tideCaption.style.display = hasNext ? DisplayStyle.Flex : DisplayStyle.None;

            // The divider only separates things; with no count row there is
            // nothing below it, and a rule across the bottom of a card reads as a
            // rendering bug.
            bool hasCount = vm.HasSightingCount;
            statsDivider.style.display = hasCount ? DisplayStyle.Flex : DisplayStyle.None;
            sightingsRow.style.display = hasCount ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasCount) sightingsValue.text = vm.SightingCountText;
        }

        void RenderAlerts()
        {
            alerts.Clear();
            var lines = vm.AlertLines;
            alerts.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < lines.Count; i++)
            {
                // The lifeguard line always comes first (BeachDetailViewModel
                // builds the list that way) and is the one V2 draws, with a
                // medical glyph; the advisories that follow are rules and
                // restrictions, so they keep the default warning triangle.
                bool lifeguard = i == 0 && vm.HasLifeguard;
                var bar = new MvAlertBar
                {
                    Severity = MvAlertBarSeverity.Warning,
                    Icon = lifeguard ? "medical_services" : MvAlertBar.DefaultIconName,
                    Text = lines[i],
                };
                if (i > 0) bar.AddToClassList("mv-praia__alert--gutter"); // USS has no `gap`.
                alerts.Add(bar);
            }
        }
    }
}
