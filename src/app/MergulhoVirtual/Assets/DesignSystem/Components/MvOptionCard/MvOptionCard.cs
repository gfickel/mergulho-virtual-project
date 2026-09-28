using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// A large selectable card: a 32dp icon slot over a short label, with
    /// selection expressed as a 2dp <c>primary</c> border. V2 uses a pair of
    /// them for "Selecione seu perfil" — Turista / Visitante and Condutor / Guia
    /// (§8.5) — and the shape generalises to any 2–3 way pictorial choice.
    ///
    /// <para>
    /// <b>The group lives in the CALLER, not here.</b> The card owns one bool
    /// (<see cref="Selected"/>) and raises <see cref="Clicked"/>; it never
    /// toggles itself. Three reasons, in order of weight:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   The screen binds to a ViewModel that is the single source of truth for
    ///   the chosen profile — it must be able to seed the selection (restoring a
    ///   draft), clear it (form reset) and be told when the user changes it. A
    ///   component-owned group would be a second authority over the same value,
    ///   which is the classic two-way-binding echo.
    ///   </description></item>
    ///   <item><description>
    ///   A group needs a container, and the container here is the screen's:
    ///   V2's "Cards Container" is a plain 12dp-gap row inside the form column.
    ///   Owning the group would drag that row's geometry into the design system,
    ///   exactly what <c>MvNumberedList</c> (rows, not the card) and
    ///   <c>MdChip</c> (chip, not the chip row) deliberately avoid.
    ///   </description></item>
    ///   <item><description>
    ///   <c>MdChip</c> already sets the precedent one section up the same form:
    ///   the "Tamanho aproximado" chips are a single-select row whose exclusivity
    ///   the screen enforces.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Not self-toggling is the deliberate difference from MdChip.</b> A
    /// filter chip is independently on/off, so it flips itself. An option card is
    /// one arm of a radio group, so a self-flip could produce "two selected" or
    /// "none selected" states the group does not allow. Raising
    /// <see cref="Clicked"/> and leaving the write to the caller makes the
    /// invariant the caller's one-liner and structurally unreachable from input:
    /// </para>
    /// <code>
    /// foreach (var c in cards)
    ///     c.Clicked += () => { foreach (var o in cards) o.Selected = (o == c); };
    /// </code>
    /// </summary>
    [UxmlElement]
    public partial class MvOptionCard : VisualElement
    {
        public const string UssClassName = "mv-option-card";
        public const string IconClassName = "mv-option-card__icon";
        public const string LabelClassName = "mv-option-card__label";
        public const string SelectedClassName = "mv-option-card--selected";

        readonly MdIcon _icon;
        readonly Label _label;
        bool _selected;

        /// <summary>Raised on tap. Not raised while disabled. Selection is NOT
        /// changed — see the class remarks.</summary>
        public event Action Clicked;

        /// <summary>Material Symbols name for the 32dp icon slot; empty hides it.</summary>
        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon.Icon;
            set
            {
                _icon.Icon = value;
                _icon.style.display = _icon.Icon.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        [UxmlAttribute("text")]
        public string Text
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        /// <summary>Pure state — setting it paints the selected border and swaps
        /// the label face, and raises nothing (the caller is the one writing it).</summary>
        [UxmlAttribute("selected")]
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                EnableInClassList(SelectedClassName, _selected);
            }
        }

        public MvOptionCard()
        {
            AddToClassList(UssClassName);
            focusable = true;

            // The card IS the visible surface (93dp tall, far past the 48dp touch
            // minimum), so there is no __container level: the state layer is the
            // root's first child, as in MvMediaCarousel's cards.
            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            _icon = new MdIcon { name = "icon" };
            _icon.AddToClassList(IconClassName);
            _icon.style.display = DisplayStyle.None;

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            Add(stateLayer);
            Add(_icon);
            Add(_label);

            this.AddManipulator(new Clickable(() => Clicked?.Invoke()));
        }
    }
}
