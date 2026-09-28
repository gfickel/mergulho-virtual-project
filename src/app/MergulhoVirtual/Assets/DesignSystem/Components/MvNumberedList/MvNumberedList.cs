using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// An ordered list drawn as V2 draws it: a filled <c>primary</c> circle
    /// holding the index, and the item's body text beside it. Used by
    /// "Dicas de convivência" (§8.3) and any other short, numbered guidance.
    /// <para>
    /// Rows come from code — <see cref="SetItems"/> — for the same reason
    /// <c>MdNavigationBar.SetDestinations</c> and <c>MdDropdown.SetChoices</c> do:
    /// UXML cannot express a list. <see cref="StartNumber"/> is the one thing that
    /// IS a UXML attribute (a continued list can start at 4).
    /// </para>
    /// <para>
    /// Container-less on purpose: V2 puts these rows inside a white 24dp-radius
    /// card, but that card is an <c>MdCard</c> the screen owns. This component is
    /// the rows and their spacing, nothing else — the same split as
    /// <c>MdListItem</c> vs. the list around it.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvNumberedList : VisualElement
    {
        public const string UssClassName = "mv-numbered-list";
        public const string ItemClassName = "mv-numbered-list__item";
        public const string FirstItemClassName = "mv-numbered-list__item--first";
        public const string MarkerClassName = "mv-numbered-list__marker";
        public const string NumberClassName = "mv-numbered-list__number";
        public const string TextClassName = "mv-numbered-list__text";

        readonly List<string> _items = new();
        int _startNumber = 1;

        /// <summary>Number shown on the first row (default 1).</summary>
        [UxmlAttribute("start-number")]
        public int StartNumber
        {
            get => _startNumber;
            set
            {
                if (_startNumber == value)
                    return;
                _startNumber = value;
                Rebuild();
            }
        }

        public int ItemCount => _items.Count;

        public MvNumberedList()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
        }

        /// <summary>
        /// Replaces the rows. Null or empty clears the list (and the element then
        /// contributes no height, so a screen can leave it in the tree unconditionally).
        /// </summary>
        public void SetItems(IReadOnlyList<string> items)
        {
            _items.Clear();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                    _items.Add(items[i] ?? "");
            }
            Rebuild();
        }

        /// <summary>The body text of a row — exposed for tests and for screens
        /// that re-read what they set.</summary>
        public string ItemTextAt(int index) => _items[index];

        void Rebuild()
        {
            Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = new VisualElement { name = $"item-{i}", pickingMode = PickingMode.Ignore };
                item.AddToClassList(ItemClassName);
                // USS has no `gap` and no structural pseudo-classes, so the row
                // spacing is a margin on every row and a modifier zeroes the first.
                if (i == 0)
                    item.AddToClassList(FirstItemClassName);

                var marker = new VisualElement { name = "marker", pickingMode = PickingMode.Ignore };
                marker.AddToClassList(MarkerClassName);

                var number = new Label((_startNumber + i).ToString())
                    { name = "number", pickingMode = PickingMode.Ignore };
                number.AddToClassList(NumberClassName);
                marker.Add(number);

                var text = new Label(_items[i]) { name = "text", pickingMode = PickingMode.Ignore };
                text.AddToClassList(TextClassName);

                item.Add(marker);
                item.Add(text);
                Add(item);
            }
        }
    }
}
