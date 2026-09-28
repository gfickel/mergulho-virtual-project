using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdButtonVariant
    {
        Filled,
        Tonal,
        Outlined,
        Text,
    }

    /// <summary>
    /// M3 common button (filled / tonal / outlined / text).
    /// Reference implementation of the component contract: data in through
    /// properties, events out through <see cref="Clicked"/>, styling through
    /// tokens only. The root element is the ≥48dp touch target; the visible
    /// 40dp pill is the __container child holding the state layer.
    /// </summary>
    [UxmlElement]
    public partial class MdButton : VisualElement
    {
        public const string UssClassName = "md-button";
        public const string ContainerClassName = "md-button__container";
        public const string IconClassName = "md-button__icon";
        public const string LabelClassName = "md-button__label";
        public const string WithIconClassName = "md-button--with-icon";

        static readonly string[] VariantClassNames =
        {
            "md-button--filled",
            "md-button--tonal",
            "md-button--outlined",
            "md-button--text",
        };

        readonly VisualElement _container;
        readonly VisualElement _stateLayer;
        readonly MdIcon _icon;
        readonly Label _label;
        MdButtonVariant _variant;
        string _iconName = "";

        /// <summary>Raised on click/tap. Not raised while disabled.</summary>
        public event Action Clicked;

        [UxmlAttribute("variant")]
        public MdButtonVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        [UxmlAttribute("text")]
        public string Text
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        /// <summary>Optional leading icon (Material Symbols name); empty = no icon.</summary>
        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _iconName;
            set
            {
                _iconName = value ?? "";
                _icon.Icon = _iconName;
                bool hasIcon = _iconName.Length > 0;
                _icon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
                EnableInClassList(WithIconClassName, hasIcon);
            }
        }

        public MdButton()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);
            focusable = true;

            _container = new VisualElement { name = "container", pickingMode = PickingMode.Ignore };
            _container.AddToClassList(ContainerClassName);

            _stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            _stateLayer.AddToClassList("md-state-layer");

            _icon = new MdIcon { name = "icon" };
            _icon.AddToClassList(IconClassName);
            _icon.style.display = DisplayStyle.None;

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            _container.Add(_stateLayer);
            _container.Add(_icon);
            _container.Add(_label);
            Add(_container);

            this.AddManipulator(new Clickable(() => Clicked?.Invoke()));
        }
    }
}
