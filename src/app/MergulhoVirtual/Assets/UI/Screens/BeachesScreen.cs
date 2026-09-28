using System;
using MergulhoVirtual.DesignSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// UI Toolkit Beaches screen (list ↔ detail), composed from design-system
    /// components and driven by <see cref="BeachesViewModel"/>. Built in code —
    /// the components' data APIs (SetChoices, SetLeadingImage, SetSamples) are
    /// code-only, so a UXML file would carry no real content.
    ///
    /// The root is transparent and non-pickable; the opaque surface lives on an
    /// inner container that stops <see cref="BottomInset"/> panel units above the
    /// screen bottom, so the legacy uGUI BottomNav stays visible and tappable
    /// regardless of uGUI ↔ UI Toolkit draw order.
    /// </summary>
    public sealed class BeachesScreen : VisualElement
    {
        readonly BeachesViewModel vm;
        readonly Func<string, Sprite> loadBeachSprite;

        readonly VisualElement content;
        readonly VisualElement listPane;
        readonly VisualElement detailPane;

        ScrollView detailScroll;
        MdTopAppBar detailBar;
        VisualElement heroFrame;
        Image heroImage;
        Label descriptionLabel;
        Label creditLabel;
        Label conditionsTitleLabel;
        Label freshnessLabel;
        Label waveValue, tideValue, moonValue, windValue, waterValue;
        MdSparkline sparkline;

        public BeachesScreen(BeachesViewModel viewModel, Func<string, Sprite> beachSpriteLoader)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            loadBeachSprite = beachSpriteLoader;

            AddToClassList("mv-beaches");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-beaches__content");
            Add(content);

            content.Add(listPane = BuildListPane());
            content.Add(detailPane = BuildDetailPane());

            vm.NavigationChanged += OnNavigationChanged;
            vm.DataChanged += RenderData;
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            OnNavigationChanged();
        }

        /// <summary>
        /// Edge insets in panel units. Top/left/right are safe-area insets applied
        /// as padding on the opaque content container — so its surface color still
        /// paints under the status bar/notch, with content pushed clear of it.
        /// Bottom is the uGUI BottomNav reservation, applied on the transparent
        /// root so that strip stays unpainted and tappable.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            content.style.paddingTop = top;
            content.style.paddingLeft = left;
            content.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void OnDetachFromPanel(DetachFromPanelEvent _)
        {
            vm.NavigationChanged -= OnNavigationChanged;
            vm.DataChanged -= RenderData;
        }

        // ---- List pane ------------------------------------------------------

        VisualElement BuildListPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("mv-beaches__pane");

            pane.Add(new MdTopAppBar { Variant = MdTopAppBarVariant.Medium, Title = "Praias" });

            var selector = new MdDropdown { LabelText = "Praia ativa" };
            selector.SetChoices(vm.OverrideChoices);
            selector.Index = vm.OverrideIndex;
            selector.SelectionChanged += index => vm.SelectOverride(index);
            var selectorWrap = new VisualElement();
            selectorWrap.AddToClassList("mv-beaches__selector");
            selectorWrap.Add(selector);
            pane.Add(selectorWrap);

            var scroll = new ScrollView();
            scroll.AddToClassList("mv-beaches__scroll");
            foreach (var beach in vm.Beaches)
            {
                scroll.Add(MakeBeachCard(beach));
            }
            pane.Add(scroll);
            return pane;
        }

        /// <summary>
        /// Full-width photo card (M3 card-with-media): cover image (or a
        /// token-colored placeholder so imageless beaches keep the same shape),
        /// title + one-line teaser, pressed/hover state layer on top.
        /// </summary>
        VisualElement MakeBeachCard(BeachInfo beach)
        {
            var card = new VisualElement { focusable = true };
            card.AddToClassList("mv-beach-card");

            var sprite = loadBeachSprite?.Invoke(beach.ImageName);
            if (sprite != null)
            {
                var photo = new Image
                {
                    sprite = sprite,
                    scaleMode = ScaleMode.ScaleAndCrop,
                    pickingMode = PickingMode.Ignore,
                };
                photo.AddToClassList("mv-beach-card__media");
                card.Add(photo);
            }
            else
            {
                var placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
                placeholder.AddToClassList("mv-beach-card__media");
                placeholder.AddToClassList("mv-beach-card__media--placeholder");
                var icon = new MdIcon { Icon = "waves" };
                icon.AddToClassList("mv-beach-card__placeholder-icon");
                placeholder.Add(icon);
                card.Add(placeholder);
            }

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("mv-beach-card__text");
            var title = new Label(beach.Name) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-medium");
            title.AddToClassList("mv-beach-card__title");
            text.Add(title);
            if (!string.IsNullOrEmpty(beach.Description))
            {
                var subtitle = new Label(beach.Description) { pickingMode = PickingMode.Ignore };
                subtitle.AddToClassList("md-typescale-body-medium");
                subtitle.AddToClassList("mv-beach-card__subtitle");
                text.Add(subtitle);
            }
            card.Add(text);

            // Last child so the press tint also covers the photo.
            var stateLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            card.Add(stateLayer);

            string capturedName = beach.Name;
            card.AddManipulator(new Clickable(() => vm.ShowDetail(capturedName)));
            return card;
        }

        // ---- Detail pane ----------------------------------------------------

        VisualElement BuildDetailPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("mv-beaches__pane");

            detailBar = new MdTopAppBar { Variant = MdTopAppBarVariant.Small, NavigationIcon = "arrow_back" };
            detailBar.NavigationClicked += vm.ShowList;
            pane.Add(detailBar);

            detailScroll = new ScrollView();
            detailScroll.AddToClassList("mv-beaches__detail-scroll");
            pane.Add(detailScroll);

            var body = new VisualElement();
            body.AddToClassList("mv-beaches__detail-content");
            detailScroll.Add(body);

            heroFrame = new VisualElement();
            heroFrame.AddToClassList("mv-beaches__hero");
            heroImage = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            heroImage.AddToClassList("mv-beaches__hero-image");
            heroFrame.Add(heroImage);
            body.Add(heroFrame);

            descriptionLabel = new Label();
            descriptionLabel.AddToClassList("md-typescale-body-medium");
            descriptionLabel.AddToClassList("mv-beaches__description");
            body.Add(descriptionLabel);

            creditLabel = new Label();
            creditLabel.AddToClassList("md-typescale-label-small");
            creditLabel.AddToClassList("mv-beaches__credit");
            body.Add(creditLabel);

            var conditionsCard = new MdCard { Variant = MdCardVariant.Filled };
            conditionsCard.AddToClassList("mv-beaches__card");
            conditionsTitleLabel = new Label("Condições");
            conditionsTitleLabel.AddToClassList("md-typescale-title-medium");
            conditionsTitleLabel.AddToClassList("mv-beaches__card-title");
            conditionsCard.Add(conditionsTitleLabel);
            waveValue = AddConditionsRow(conditionsCard, "Onda");
            tideValue = AddConditionsRow(conditionsCard, "Maré");
            moonValue = AddConditionsRow(conditionsCard, "Lua");
            windValue = AddConditionsRow(conditionsCard, "Vento");
            waterValue = AddConditionsRow(conditionsCard, "Água");
            freshnessLabel = new Label();
            freshnessLabel.AddToClassList("md-typescale-label-small");
            freshnessLabel.AddToClassList("mv-beaches__freshness");
            conditionsCard.Add(freshnessLabel);
            body.Add(conditionsCard);

            var tideCard = new MdCard { Variant = MdCardVariant.Filled };
            tideCard.AddToClassList("mv-beaches__card");
            var tideTitle = new Label("Maré — próximas 24 h");
            tideTitle.AddToClassList("md-typescale-title-medium");
            tideTitle.AddToClassList("mv-beaches__card-title");
            tideCard.Add(tideTitle);
            sparkline = new MdSparkline
            {
                Baseline = BeachesViewModel.TideBaselineM,
                ExtremumLabelFormatter = vm.FormatTideExtremumLabel,
            };
            sparkline.AddToClassList("mv-beaches__sparkline");
            tideCard.Add(sparkline);
            body.Add(tideCard);

            return pane;
        }

        static Label AddConditionsRow(VisualElement parent, string rowName)
        {
            var row = new VisualElement();
            row.AddToClassList("mv-beaches__row");
            var nameLabel = new Label(rowName);
            nameLabel.AddToClassList("md-typescale-label-large");
            nameLabel.AddToClassList("mv-beaches__row-label");
            row.Add(nameLabel);
            var valueLabel = new Label("—");
            valueLabel.AddToClassList("md-typescale-body-medium");
            valueLabel.AddToClassList("mv-beaches__row-value");
            row.Add(valueLabel);
            parent.Add(row);
            return valueLabel;
        }

        // ---- Rendering ------------------------------------------------------

        void OnNavigationChanged()
        {
            bool detail = vm.SelectedBeach != null;
            listPane.style.display = detail ? DisplayStyle.None : DisplayStyle.Flex;
            detailPane.style.display = detail ? DisplayStyle.Flex : DisplayStyle.None;
            if (detail)
            {
                RenderDetail(vm.SelectedBeach);
                detailScroll.scrollOffset = Vector2.zero;
            }
            RenderData();
        }

        void RenderDetail(BeachInfo beach)
        {
            detailBar.Title = beach.Name;

            var sprite = loadBeachSprite?.Invoke(beach.ImageName);
            heroImage.sprite = sprite;
            heroFrame.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;

            descriptionLabel.text = beach.Description ?? "";
            descriptionLabel.style.display =
                string.IsNullOrEmpty(beach.Description) ? DisplayStyle.None : DisplayStyle.Flex;

            creditLabel.text = beach.PhotoCredit ?? "";
            creditLabel.style.display =
                string.IsNullOrEmpty(beach.PhotoCredit) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RenderData()
        {
            conditionsTitleLabel.text = vm.ConditionsTitle;
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
