using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// The full-bleed photo header: cover image, scrim, and up to three optional
    /// overlays — a floating back button, a pill "place selector", and a row of
    /// <see cref="MvTag"/> badges pinned to the bottom.
    /// <para>
    /// <b>Every overlay is optional and each is independent.</b> Praia detalhe
    /// (§8.3) uses all four layers; Praias landing (§8.2) uses image + scrim +
    /// badges and no controls; the AR HUD (§8.4) will use the back button and the
    /// selector over a live camera with no image at all. So: the back button and
    /// the selector are gated by <see cref="ShowBackButton"/> /
    /// <see cref="ShowSelector"/> (both default OFF — a bare hero is just a photo),
    /// the top row hides itself when neither is on so it eats no space, the badge
    /// row hides when <see cref="SetBadges"/> gets nothing, and the SCRIM hides
    /// when there is no image — which is what makes the AR case work without a
    /// fourth flag, since a scrim over a live camera would just be a grey veil.
    /// </para>
    /// <para>
    /// The image and scrim are absolutely positioned and the controls live in a
    /// separate absolutely-positioned foreground, so <see cref="TopInset"/> (the
    /// status-bar safe area, which the HOST measures — §1) pushes the controls
    /// down without moving the photo. That separation is the whole reason the
    /// three layers are siblings rather than nested.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvHeroHeader : VisualElement
    {
        public const string UssClassName = "mv-hero-header";
        public const string CompactClassName = "mv-hero-header--compact";
        public const string ImageClassName = "mv-hero-header__image";
        public const string ScrimClassName = "mv-hero-header__scrim";
        public const string ForegroundClassName = "mv-hero-header__foreground";
        public const string TopRowClassName = "mv-hero-header__top-row";
        public const string BackClassName = "mv-hero-header__back";
        public const string SelectorClassName = "mv-hero-header__selector";
        public const string SelectorContainerClassName = "mv-hero-header__selector-container";
        public const string SelectorIconClassName = "mv-hero-header__selector-icon";
        public const string SelectorLabelClassName = "mv-hero-header__selector-label";
        public const string SelectorChevronClassName = "mv-hero-header__selector-chevron";
        public const string BadgesClassName = "mv-hero-header__badges";
        public const string BadgeClassName = "mv-hero-header__badge";
        public const string FirstBadgeClassName = "mv-hero-header__badge--first";

        /// <summary>Material Symbols name for the default back glyph.</summary>
        public const string DefaultBackIconName = "arrow_back";
        /// <summary>Material Symbols name for the default selector leading glyph.</summary>
        public const string DefaultSelectorIconName = "location_on";
        /// <summary>Material Symbols name for the selector's trailing glyph.</summary>
        public const string SelectorChevronIconName = "expand_more";

        readonly Image _image;
        readonly VisualElement _scrim;
        readonly VisualElement _foreground;
        readonly VisualElement _topRow;
        readonly MdIconButton _back;
        readonly VisualElement _selector;
        readonly MdIcon _selectorIcon;
        readonly Label _selectorLabel;
        readonly VisualElement _badges;

        bool _compact;
        bool _showBackButton;
        bool _showSelector;
        float _topInset;

        /// <summary>Raised when the floating back button is tapped.</summary>
        public event Action BackClicked;

        /// <summary>Raised when the selector pill is tapped (open a menu on
        /// <see cref="Selector"/>, which is the element to anchor it to).</summary>
        public event Action SelectorClicked;

        /// <summary>
        /// The 240dp hero of the Praias landing (§8.2) instead of the 340dp hero of
        /// Praia detalhe (§8.3). Also lightens the scrim to V2's landing value.
        /// </summary>
        [UxmlAttribute("compact")]
        public bool Compact
        {
            get => _compact;
            set
            {
                _compact = value;
                EnableInClassList(CompactClassName, _compact);
            }
        }

        [UxmlAttribute("show-back-button")]
        public bool ShowBackButton
        {
            get => _showBackButton;
            set
            {
                _showBackButton = value;
                UpdateTopRow();
            }
        }

        [UxmlAttribute("show-selector")]
        public bool ShowSelector
        {
            get => _showSelector;
            set
            {
                _showSelector = value;
                UpdateTopRow();
            }
        }

        [UxmlAttribute("selector-text")]
        public string SelectorText
        {
            get => _selectorLabel.text;
            set => _selectorLabel.text = value ?? "";
        }

        /// <summary>Selector leading icon (Material Symbols name); empty = none.</summary>
        [UxmlAttribute("selector-icon")]
        public string SelectorIcon
        {
            get => _selectorIcon.Icon;
            set
            {
                _selectorIcon.Icon = value ?? "";
                _selectorIcon.style.display =
                    _selectorIcon.Icon.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        [UxmlAttribute("back-icon")]
        public string BackIcon
        {
            get => _back.Icon;
            set => _back.Icon = value ?? "";
        }

        /// <summary>
        /// Extra space above the controls, in dp — the status-bar safe-area inset
        /// the host measures. Applied as padding on the foreground only, so the
        /// photo keeps running edge to edge behind the status bar.
        /// </summary>
        public float TopInset
        {
            get => _topInset;
            set
            {
                _topInset = value;
                _foreground.style.paddingTop = value;
            }
        }

        /// <summary>The selector pill — anchor an <c>MdMenu</c> to this.</summary>
        public VisualElement Selector => _selector;

        public int BadgeCount => _badges.childCount;

        public MvHeroHeader()
        {
            AddToClassList(UssClassName);

            _image = new Image { name = "image", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleAndCrop };
            _image.AddToClassList(ImageClassName);
            _image.style.display = DisplayStyle.None;

            _scrim = new VisualElement { name = "scrim", pickingMode = PickingMode.Ignore };
            _scrim.AddToClassList(ScrimClassName);
            _scrim.style.display = DisplayStyle.None;

            // Picking Ignore on the containers, NOT on their interactive leaves:
            // in UI Toolkit `Ignore` excludes only the element itself, so children
            // still receive pointer events while taps on the empty photo fall
            // through to whatever the screen puts behind the hero.
            _foreground = new VisualElement { name = "foreground", pickingMode = PickingMode.Ignore };
            _foreground.AddToClassList(ForegroundClassName);

            _topRow = new VisualElement { name = "top-row", pickingMode = PickingMode.Ignore };
            _topRow.AddToClassList(TopRowClassName);
            _topRow.style.display = DisplayStyle.None;

            _back = new MdIconButton { name = "back", Icon = DefaultBackIconName };
            _back.AddToClassList(BackClassName);
            _back.style.display = DisplayStyle.None;
            _back.Clicked += () => BackClicked?.Invoke();

            _selector = new VisualElement { name = "selector", focusable = true };
            _selector.AddToClassList(SelectorClassName);
            _selector.style.display = DisplayStyle.None;

            var selectorContainer = new VisualElement { name = "selector-container", pickingMode = PickingMode.Ignore };
            selectorContainer.AddToClassList(SelectorContainerClassName);

            var selectorStateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            selectorStateLayer.AddToClassList("md-state-layer");

            _selectorIcon = new MdIcon { name = "selector-icon", Icon = DefaultSelectorIconName };
            _selectorIcon.AddToClassList(SelectorIconClassName);

            _selectorLabel = new Label { name = "selector-label", pickingMode = PickingMode.Ignore };
            _selectorLabel.AddToClassList(SelectorLabelClassName);

            var chevron = new MdIcon { name = "selector-chevron", Icon = SelectorChevronIconName };
            chevron.AddToClassList(SelectorChevronClassName);

            selectorContainer.Add(selectorStateLayer);
            selectorContainer.Add(_selectorIcon);
            selectorContainer.Add(_selectorLabel);
            selectorContainer.Add(chevron);
            _selector.Add(selectorContainer);
            _selector.AddManipulator(new Clickable(() => SelectorClicked?.Invoke()));

            _badges = new VisualElement { name = "badges", pickingMode = PickingMode.Ignore };
            _badges.AddToClassList(BadgesClassName);
            _badges.style.display = DisplayStyle.None;

            _topRow.Add(_back);
            _topRow.Add(_selector);
            _foreground.Add(_topRow);
            _foreground.Add(_badges);

            Add(_image);
            Add(_scrim);
            Add(_foreground);
        }

        /// <summary>Show a sprite as the cover photo (null clears it, which also
        /// hides the scrim — see the class remarks).</summary>
        public void SetImage(Sprite sprite)
        {
            _image.sprite = sprite;
            _image.image = null;
            UpdateImage();
        }

        /// <summary>Show a texture as the cover photo (null clears it).</summary>
        public void SetImage(Texture2D texture)
        {
            _image.image = texture;
            _image.sprite = null;
            UpdateImage();
        }

        /// <summary>
        /// Replaces the badge row with one <see cref="MvTag"/> per string, styled
        /// <see cref="MvTagVariant.OnImage"/>. Null or empty hides the row.
        /// </summary>
        public void SetBadges(IReadOnlyList<string> badges)
        {
            _badges.Clear();
            int count = badges?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var tag = new MvTag
                {
                    name = $"badge-{i}",
                    Text = badges[i],
                    Variant = MvTagVariant.OnImage,
                    Size = MvTagSize.Medium,
                };
                tag.AddToClassList(BadgeClassName);
                // Same no-`gap`/no-structural-pseudo-class workaround as
                // MvNumberedList: margin on every badge, zeroed on the first.
                if (i == 0)
                    tag.AddToClassList(FirstBadgeClassName);
                _badges.Add(tag);
            }
            _badges.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateImage()
        {
            bool hasImage = _image.sprite != null || _image.image != null;
            _image.style.display = hasImage ? DisplayStyle.Flex : DisplayStyle.None;
            _scrim.style.display = hasImage ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateTopRow()
        {
            _back.style.display = _showBackButton ? DisplayStyle.Flex : DisplayStyle.None;
            _selector.style.display = _showSelector ? DisplayStyle.Flex : DisplayStyle.None;
            _topRow.style.display =
                _showBackButton || _showSelector ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
