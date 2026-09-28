using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 checkbox — a standalone 18dp box inside a 48dp touch target.
    /// Drives the four "Comportamento observado" rows on Reportar (§8.5).
    /// <para>
    /// <b>It carries no label.</b> V2 draws the row as
    /// <c>label ... space-between ... checkbox</c>, i.e. the text sits at the far
    /// side of a 52dp row the SCREEN owns, not beside the box. A built-in label
    /// could only ever be adjacent, so the screen would have to hide it and place
    /// its own anyway. Same split as <c>MvNumberedList</c> (rows, not the card
    /// around them) and <c>MdChip</c> (chip, not the chip row).
    /// </para>
    /// <para>
    /// <b>Two-way binding is safe</b>: the setter is guarded by an equality check,
    /// so a ViewModel that writes <see cref="Checked"/> back from its
    /// <see cref="ValueChanged"/> handler terminates after one hop instead of
    /// looping. Raising on a programmatic set (rather than only on a tap) is the
    /// <c>MdChip.Selected</c> contract, kept so the two toggles behave alike.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MdCheckbox : VisualElement
    {
        public const string UssClassName = "md-checkbox";
        public const string ContainerClassName = "md-checkbox__container";
        public const string BoxClassName = "md-checkbox__box";
        public const string CheckClassName = "md-checkbox__check";
        public const string CheckedClassName = "md-checkbox--checked";

        const string CheckIconName = "check";

        readonly VisualElement _container;
        readonly VisualElement _box;
        readonly MdIcon _check;
        bool _checked;

        /// <summary>Raised whenever <see cref="Checked"/> actually changes — by a
        /// tap or by code. Not raised while disabled (the click never lands).</summary>
        public event Action<bool> ValueChanged;

        [UxmlAttribute("checked")]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;
                _checked = value;
                EnableInClassList(CheckedClassName, _checked);
                ValueChanged?.Invoke(_checked);
            }
        }

        /// <summary>Flips <see cref="Checked"/> — what a tap does.</summary>
        public void Toggle() => Checked = !_checked;

        public MdCheckbox()
        {
            AddToClassList(UssClassName);
            focusable = true;

            // Root = 48dp touch target; __container = the 40dp state-layer disc;
            // __box = the 18dp square V2 actually draws. Same three-level split as
            // MdIconButton (48 target / 40 circle / glyph).
            _container = new VisualElement { name = "container", pickingMode = PickingMode.Ignore };
            _container.AddToClassList(ContainerClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _box = new VisualElement { name = "box", pickingMode = PickingMode.Ignore };
            _box.AddToClassList(BoxClassName);

            _check = new MdIcon { name = "check", Icon = CheckIconName };
            _check.AddToClassList(CheckClassName);
            _box.Add(_check);

            _container.Add(stateLayer);
            _container.Add(_box);
            Add(_container);

            this.AddManipulator(new Clickable(Toggle));
        }
    }
}
