using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdFabSize
    {
        Regular,
        Small,
        Large,
    }

    public enum MdFabColor
    {
        Primary,
        Secondary,
        Tertiary,
        Surface,
    }

    /// <summary>
    /// M3 floating action button (small 40dp / regular 56dp / large 96dp,
    /// container colors primary/secondary/tertiary/surface). Root = ≥48dp
    /// touch target, __container = the visible rounded square. Same
    /// state-layer pattern as <see cref="MdButton"/>. Positioning (bottom-end
    /// float) is the host screen's job — the component is just the button.
    /// </summary>
    [UxmlElement]
    public partial class MdFab : VisualElement
    {
        public const string UssClassName = "md-fab";
        public const string ContainerClassName = "md-fab__container";
        public const string IconClassName = "md-fab__icon";

        static readonly string[] SizeClassNames =
        {
            "md-fab--regular",
            "md-fab--small",
            "md-fab--large",
        };

        static readonly string[] ColorClassNames =
        {
            "md-fab--primary",
            "md-fab--secondary",
            "md-fab--tertiary",
            "md-fab--surface",
        };

        readonly VisualElement _container;
        readonly MdIcon _icon;
        MdFabSize _size;
        MdFabColor _color;

        /// <summary>Raised on click/tap. Not raised while disabled.</summary>
        public event Action Clicked;

        [UxmlAttribute("size")]
        public MdFabSize Size
        {
            get => _size;
            set
            {
                RemoveFromClassList(SizeClassNames[(int)_size]);
                _size = value;
                AddToClassList(SizeClassNames[(int)_size]);
            }
        }

        [UxmlAttribute("color")]
        public MdFabColor Color
        {
            get => _color;
            set
            {
                RemoveFromClassList(ColorClassNames[(int)_color]);
                _color = value;
                AddToClassList(ColorClassNames[(int)_color]);
            }
        }

        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon.Icon;
            set => _icon.Icon = value;
        }

        public MdFab()
        {
            AddToClassList(UssClassName);
            AddToClassList(SizeClassNames[(int)_size]);
            AddToClassList(ColorClassNames[(int)_color]);
            focusable = true;

            _container = new VisualElement { name = "container", pickingMode = PickingMode.Ignore };
            _container.AddToClassList(ContainerClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _icon = new MdIcon { name = "icon" };
            _icon.AddToClassList(IconClassName);

            _container.Add(stateLayer);
            _container.Add(_icon);
            Add(_container);

            this.AddManipulator(new Clickable(() => Clicked?.Invoke()));
        }
    }
}
