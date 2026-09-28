using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>One navigation-bar destination: an icon name + a label.</summary>
    public readonly struct MdNavDestination
    {
        public readonly string Icon;
        public readonly string Label;

        public MdNavDestination(string icon, string label)
        {
            Icon = icon ?? "";
            Label = label ?? "";
        }
    }

    /// <summary>
    /// Navigation bar: 3–5 destinations, each an icon in an active-indicator
    /// pill plus a label. Destinations are set from code via
    /// <see cref="SetDestinations"/> (same pattern as MdDropdown.SetChoices —
    /// no UXML item support). Tapping a destination sets
    /// <see cref="SelectedIndex"/> and raises <see cref="SelectionChanged"/>;
    /// re-tapping the selected one is a no-op.
    /// <para>
    /// Styled to the V2 dark bar (DESIGN_IMPLEMENTATION.md §4 "Bar spec"): an
    /// inverse-surface container, an amber pill behind the active icon and
    /// white-at-50% icon+label on the inactive ones. That is entirely
    /// <c>MdNavigationBar.uss</c> — the element tree below is identical for
    /// active and inactive destinations, which is what lets the restyle be a
    /// stylesheet change rather than a component rewrite.
    /// </para>
    /// <para>
    /// The 34dp OS home-indicator strip below the bar is safe-area padding the
    /// HOST applies; the bar itself is the 64dp tab row and nothing else.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MdNavigationBar : VisualElement
    {
        public const string UssClassName = "md-nav-bar";
        public const string ItemClassName = "md-nav-bar__item";
        public const string ItemActiveClassName = "md-nav-bar__item--active";
        public const string IndicatorClassName = "md-nav-bar__indicator";
        public const string IconClassName = "md-nav-bar__icon";
        public const string LabelClassName = "md-nav-bar__label";

        readonly List<VisualElement> _items = new();
        int _selectedIndex = -1;

        /// <summary>Raised whenever the selection actually changes (tap or code); -1 = none.</summary>
        public event Action<int> SelectionChanged;

        public int DestinationCount => _items.Count;

        /// <summary>
        /// Rebuilds the destinations. An existing selection is kept if still in
        /// range, silently cleared to -1 otherwise (initialization API — no event).
        /// </summary>
        public void SetDestinations(IReadOnlyList<MdNavDestination> destinations)
        {
            Clear();
            _items.Clear();

            int count = destinations?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var destination = destinations[i];

                var item = new VisualElement { name = $"destination-{i}", focusable = true };
                item.AddToClassList(ItemClassName);

                var indicator = new VisualElement { name = "indicator", pickingMode = PickingMode.Ignore };
                indicator.AddToClassList(IndicatorClassName);

                var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
                stateLayer.AddToClassList("md-state-layer");

                var icon = new MdIcon { name = "icon", Icon = destination.Icon };
                icon.AddToClassList(IconClassName);

                var label = new Label(destination.Label) { name = "label", pickingMode = PickingMode.Ignore };
                label.AddToClassList(LabelClassName);

                indicator.Add(stateLayer);
                indicator.Add(icon);
                item.Add(indicator);
                item.Add(label);
                item.AddManipulator(new Clickable(() => SelectedIndex = index));
                _items.Add(item);
                Add(item);
            }

            if (_selectedIndex >= _items.Count)
                _selectedIndex = -1;
            else if (_selectedIndex >= 0)
                _items[_selectedIndex].AddToClassList(ItemActiveClassName);
        }

        /// <summary>The active destination, or -1 for none. Out-of-range clears to -1.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = (value < 0 || value >= _items.Count) ? -1 : value;
                if (next == _selectedIndex)
                    return;
                if (_selectedIndex >= 0)
                    _items[_selectedIndex].RemoveFromClassList(ItemActiveClassName);
                _selectedIndex = next;
                if (next >= 0)
                    _items[next].AddToClassList(ItemActiveClassName);
                SelectionChanged?.Invoke(_selectedIndex);
            }
        }

        public MdNavigationBar()
        {
            AddToClassList(UssClassName);
        }
    }
}
