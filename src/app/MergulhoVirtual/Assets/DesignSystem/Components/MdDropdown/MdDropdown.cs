using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 exposed dropdown menu: an outlined-text-field-style box showing the
    /// selected choice, opening an <see cref="MdMenu"/> of <see cref="Choices"/>
    /// on tap. Same floating-label pattern as MdTextField (floats while open
    /// or when something is selected). Choices are code-set
    /// (<see cref="SetChoices"/>) — the app populates them from data
    /// (e.g. ReverseGeocoding.GetAllPlaceNames), not from UXML.
    /// <see cref="Index"/> is -1 when nothing is selected.
    /// </summary>
    [UxmlElement]
    public partial class MdDropdown : VisualElement
    {
        public const string UssClassName = "md-dropdown";
        public const string FieldClassName = "md-dropdown__field";
        public const string LabelClassName = "md-dropdown__label";
        public const string ValueClassName = "md-dropdown__value";
        public const string ArrowClassName = "md-dropdown__arrow";
        public const string FloatingClassName = "md-dropdown--floating";
        public const string OpenClassName = "md-dropdown--open";

        const string ArrowClosedIcon = "expand_more";
        const string ArrowOpenIcon = "expand_less";

        readonly VisualElement _field;
        readonly Label _label;
        readonly Label _value;
        readonly MdIcon _arrow;
        readonly List<string> _choices = new();
        int _index = -1;
        MdMenu _menu;

        /// <summary>Raised when <see cref="Index"/> changes (menu pick or
        /// programmatic set) — -1 means cleared.</summary>
        public event Action<int> SelectionChanged;

        [UxmlAttribute("label")]
        public string LabelText
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        public IReadOnlyList<string> Choices => _choices;

        public void SetChoices(IEnumerable<string> choices)
        {
            _choices.Clear();
            if (choices != null)
                _choices.AddRange(choices);
            if (_index >= _choices.Count)
                Index = -1;
        }

        /// <summary>Selected choice index; out-of-range values clear to -1.</summary>
        public int Index
        {
            get => _index;
            set
            {
                int next = value < 0 || value >= _choices.Count ? -1 : value;
                if (next == _index)
                    return;
                _index = next;
                _value.text = _index >= 0 ? _choices[_index] : "";
                UpdateFloating();
                SelectionChanged?.Invoke(_index);
            }
        }

        public string Value => _index >= 0 ? _choices[_index] : "";

        /// <summary>The open menu, or null. Exposed for tests.</summary>
        public MdMenu OpenMenu => _menu;

        public MdDropdown()
        {
            AddToClassList(UssClassName);
            focusable = true;

            _field = new VisualElement { name = "field", pickingMode = PickingMode.Ignore };
            _field.AddToClassList(FieldClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _value = new Label { name = "value", pickingMode = PickingMode.Ignore };
            _value.AddToClassList(ValueClassName);

            _arrow = new MdIcon { name = "arrow", Icon = ArrowClosedIcon };
            _arrow.AddToClassList(ArrowClassName);

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            _field.Add(stateLayer);
            _field.Add(_value);
            _field.Add(_arrow);
            Add(_field);
            // Label lives on the root, not inside __field: the field clips to
            // its rounded corners (overflow: hidden for the state layer), which
            // would cut off the floated label straddling the border.
            Add(_label);

            // While the menu is open its scrim covers this element too, so a
            // second tap lands on the scrim and closes — no toggle logic here.
            this.AddManipulator(new Clickable(Open));
        }

        void Open()
        {
            if (_menu != null || _choices.Count == 0 || panel == null)
                return;
            AddToClassList(OpenClassName);
            _arrow.Icon = ArrowOpenIcon;
            UpdateFloating();
            _menu = MdMenu.Open(_field, _choices, _index, i => Index = i);
            _menu.Closed += () =>
            {
                _menu = null;
                RemoveFromClassList(OpenClassName);
                _arrow.Icon = ArrowClosedIcon;
                UpdateFloating();
            };
        }

        void UpdateFloating()
        {
            bool floating = _index >= 0 || ClassListContains(OpenClassName);
            EnableInClassList(FloatingClassName, floating);
        }
    }
}
