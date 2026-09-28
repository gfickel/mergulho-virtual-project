using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdChipKind
    {
        Assist,
        Filter,
    }

    /// <summary>
    /// M3 chip (assist + filter). Filter chips toggle <see cref="Selected"/> on
    /// click and show a leading check when selected; assist chips just raise
    /// <see cref="Clicked"/>. Root = ≥48dp touch target, __container = the
    /// visible 32dp chip.
    /// </summary>
    [UxmlElement]
    public partial class MdChip : VisualElement
    {
        public const string UssClassName = "md-chip";
        public const string ContainerClassName = "md-chip__container";
        public const string IconClassName = "md-chip__icon";
        public const string LabelClassName = "md-chip__label";
        public const string SelectedClassName = "md-chip--selected";
        public const string WithIconClassName = "md-chip--with-icon";

        const string CheckIconName = "check";

        readonly VisualElement _container;
        readonly MdIcon _leadingIcon;
        readonly Label _label;
        MdChipKind _kind;
        bool _selected;
        string _iconName = "";

        /// <summary>Raised on click/tap (both kinds). Not raised while disabled.</summary>
        public event Action Clicked;

        /// <summary>Raised when a filter chip's <see cref="Selected"/> changes.</summary>
        public event Action<bool> SelectedChanged;

        [UxmlAttribute("kind")]
        public MdChipKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                UpdateLeadingIcon();
            }
        }

        [UxmlAttribute("text")]
        public string Text
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        /// <summary>Optional leading icon (Material Symbols name); empty = none.
        /// A selected filter chip always shows the check instead.</summary>
        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _iconName;
            set
            {
                _iconName = value ?? "";
                UpdateLeadingIcon();
            }
        }

        /// <summary>Selection state. Clicking toggles it only for filter chips,
        /// but it can be set programmatically on any kind (UXML attribute order
        /// is unspecified, so this setter must not depend on <see cref="Kind"/>).</summary>
        [UxmlAttribute("selected")]
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                EnableInClassList(SelectedClassName, _selected);
                UpdateLeadingIcon();
                SelectedChanged?.Invoke(_selected);
            }
        }

        public MdChip()
        {
            AddToClassList(UssClassName);
            focusable = true;

            _container = new VisualElement { name = "container", pickingMode = PickingMode.Ignore };
            _container.AddToClassList(ContainerClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _leadingIcon = new MdIcon { name = "icon" };
            _leadingIcon.AddToClassList(IconClassName);
            _leadingIcon.style.display = DisplayStyle.None;

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            _container.Add(stateLayer);
            _container.Add(_leadingIcon);
            _container.Add(_label);
            Add(_container);

            this.AddManipulator(new Clickable(OnClick));
        }

        void OnClick()
        {
            if (_kind == MdChipKind.Filter)
                Selected = !Selected;
            Clicked?.Invoke();
        }

        void UpdateLeadingIcon()
        {
            string icon = _selected ? CheckIconName : _iconName;
            _leadingIcon.Icon = icon;
            bool hasIcon = icon.Length > 0;
            _leadingIcon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList(WithIconClassName, hasIcon);
        }
    }
}
