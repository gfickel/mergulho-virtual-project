using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdIconButtonVariant
    {
        Standard,
        Filled,
        Tonal,
        Outlined,
    }

    /// <summary>
    /// M3 icon button. Root = 48dp touch target, __container = the visible
    /// 40dp circle. Same state-layer pattern as <see cref="MdButton"/>.
    /// </summary>
    [UxmlElement]
    public partial class MdIconButton : VisualElement
    {
        public const string UssClassName = "md-icon-button";
        public const string ContainerClassName = "md-icon-button__container";

        static readonly string[] VariantClassNames =
        {
            "md-icon-button--standard",
            "md-icon-button--filled",
            "md-icon-button--tonal",
            "md-icon-button--outlined",
        };

        readonly VisualElement _container;
        readonly MdIcon _icon;
        MdIconButtonVariant _variant;

        /// <summary>Raised on click/tap. Not raised while disabled.</summary>
        public event Action Clicked;

        [UxmlAttribute("variant")]
        public MdIconButtonVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon.Icon;
            set => _icon.Icon = value;
        }

        public MdIconButton()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);
            focusable = true;

            _container = new VisualElement { name = "container", pickingMode = PickingMode.Ignore };
            _container.AddToClassList(ContainerClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _icon = new MdIcon { name = "icon" };

            _container.Add(stateLayer);
            _container.Add(_icon);
            Add(_container);

            this.AddManipulator(new Clickable(() => Clicked?.Invoke()));
        }
    }
}
