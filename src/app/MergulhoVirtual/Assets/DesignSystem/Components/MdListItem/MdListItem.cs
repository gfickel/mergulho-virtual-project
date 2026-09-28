using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 list item (1/2/3-line). The whole row is the touch target, so the
    /// state layer sits directly on the root (no inner container — list items
    /// are full-bleed rectangles). Line count is derived: no supporting text =
    /// one-line, supporting text = two-line, plus <see cref="ThreeLine"/> to
    /// let the supporting text wrap. Leading slot shows either an icon
    /// (<see cref="LeadingIcon"/>, UXML-able) or an image
    /// (<see cref="SetLeadingImage(Sprite)"/>, code-only since it's an asset
    /// reference) — the image wins when both are set.
    /// </summary>
    [UxmlElement]
    public partial class MdListItem : VisualElement
    {
        public const string UssClassName = "md-list-item";
        public const string LeadingIconClassName = "md-list-item__leading-icon";
        public const string LeadingImageClassName = "md-list-item__leading-image";
        public const string LeadingImageInnerClassName = "md-list-item__leading-image-inner";
        public const string ContentClassName = "md-list-item__content";
        public const string HeadlineClassName = "md-list-item__headline";
        public const string SupportingClassName = "md-list-item__supporting";
        public const string TrailingIconClassName = "md-list-item__trailing-icon";
        public const string OneLineClassName = "md-list-item--one-line";
        public const string TwoLineClassName = "md-list-item--two-line";
        public const string ThreeLineClassName = "md-list-item--three-line";

        readonly MdIcon _leadingIcon;
        readonly VisualElement _leadingImageBox;
        readonly Image _leadingImage;
        readonly Label _headline;
        readonly Label _supporting;
        readonly MdIcon _trailingIcon;
        string _leadingIconName = "";
        string _trailingIconName = "";
        bool _threeLine;

        /// <summary>Raised on click/tap. Not raised while disabled.</summary>
        public event Action Clicked;

        [UxmlAttribute("headline")]
        public string Headline
        {
            get => _headline.text;
            set => _headline.text = value ?? "";
        }

        [UxmlAttribute("supporting-text")]
        public string SupportingText
        {
            get => _supporting.text;
            set
            {
                _supporting.text = value ?? "";
                UpdateLines();
            }
        }

        /// <summary>Lets the supporting text wrap to a second line (88dp row).
        /// Only takes effect when there IS supporting text.</summary>
        [UxmlAttribute("three-line")]
        public bool ThreeLine
        {
            get => _threeLine;
            set
            {
                _threeLine = value;
                UpdateLines();
            }
        }

        /// <summary>Leading icon (Material Symbols name); empty = none.
        /// Hidden while a leading image is set.</summary>
        [UxmlAttribute("leading-icon")]
        public string LeadingIcon
        {
            get => _leadingIconName;
            set
            {
                _leadingIconName = value ?? "";
                _leadingIcon.Icon = _leadingIconName;
                UpdateLeading();
            }
        }

        [UxmlAttribute("trailing-icon")]
        public string TrailingIcon
        {
            get => _trailingIconName;
            set
            {
                _trailingIconName = value ?? "";
                _trailingIcon.Icon = _trailingIconName;
                _trailingIcon.style.display =
                    _trailingIconName.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public MdListItem()
        {
            AddToClassList(UssClassName);
            AddToClassList(OneLineClassName);
            focusable = true;

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _leadingIcon = new MdIcon { name = "leading-icon" };
            _leadingIcon.AddToClassList(LeadingIconClassName);
            _leadingIcon.style.display = DisplayStyle.None;

            // Wrapper clips the cover-fit image to the rounded corners.
            _leadingImageBox = new VisualElement { name = "leading-image", pickingMode = PickingMode.Ignore };
            _leadingImageBox.AddToClassList(LeadingImageClassName);
            _leadingImageBox.style.display = DisplayStyle.None;
            _leadingImage = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleAndCrop };
            _leadingImage.AddToClassList(LeadingImageInnerClassName);
            _leadingImageBox.Add(_leadingImage);

            var content = new VisualElement { name = "content", pickingMode = PickingMode.Ignore };
            content.AddToClassList(ContentClassName);

            _headline = new Label { name = "headline", pickingMode = PickingMode.Ignore };
            _headline.AddToClassList(HeadlineClassName);

            _supporting = new Label { name = "supporting", pickingMode = PickingMode.Ignore };
            _supporting.AddToClassList(SupportingClassName);
            _supporting.style.display = DisplayStyle.None;

            _trailingIcon = new MdIcon { name = "trailing-icon" };
            _trailingIcon.AddToClassList(TrailingIconClassName);
            _trailingIcon.style.display = DisplayStyle.None;

            content.Add(_headline);
            content.Add(_supporting);

            Add(stateLayer);
            Add(_leadingIcon);
            Add(_leadingImageBox);
            Add(content);
            Add(_trailingIcon);

            this.AddManipulator(new Clickable(() => Clicked?.Invoke()));
        }

        /// <summary>Show a sprite in the leading slot (null clears it).</summary>
        public void SetLeadingImage(Sprite sprite)
        {
            _leadingImage.sprite = sprite;
            _leadingImage.image = null;
            UpdateLeading();
        }

        /// <summary>Show a texture in the leading slot (null clears it).</summary>
        public void SetLeadingImage(Texture2D texture)
        {
            _leadingImage.image = texture;
            _leadingImage.sprite = null;
            UpdateLeading();
        }

        void UpdateLeading()
        {
            bool hasImage = _leadingImage.sprite != null || _leadingImage.image != null;
            bool hasIcon = !hasImage && _leadingIconName.Length > 0;
            _leadingImageBox.style.display = hasImage ? DisplayStyle.Flex : DisplayStyle.None;
            _leadingIcon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateLines()
        {
            bool hasSupporting = _supporting.text.Length > 0;
            _supporting.style.display = hasSupporting ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList(OneLineClassName, !hasSupporting);
            EnableInClassList(TwoLineClassName, hasSupporting && !_threeLine);
            EnableInClassList(ThreeLineClassName, hasSupporting && _threeLine);
        }
    }
}
