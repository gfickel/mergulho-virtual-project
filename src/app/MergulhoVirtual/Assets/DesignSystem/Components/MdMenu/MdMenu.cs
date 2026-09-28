using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// The shared overlay layer for popups (menus, and later dialogs and
    /// snackbars): a full-document, hit-transparent element kept as the LAST
    /// child of the UIDocument root so popups draw above every screen.
    /// Created on demand from any attached element. Its geometry is inline
    /// (not USS) so it works on themeless panels (tests) too.
    /// </summary>
    public static class MdOverlay
    {
        public const string LayerClassName = "md-overlay-layer";

        public static VisualElement EnsureLayer(VisualElement context)
        {
            if (context.panel == null)
                throw new InvalidOperationException("MdOverlay.EnsureLayer needs an element attached to a panel.");

            // The UIDocument root is the ancestor whose parent is the panel's
            // internal visual-tree root (which itself has no parent).
            var docRoot = context;
            while (docRoot.hierarchy.parent != null && docRoot.hierarchy.parent.hierarchy.parent != null)
                docRoot = docRoot.hierarchy.parent;

            VisualElement layer = null;
            foreach (var child in docRoot.Children())
            {
                if (child.ClassListContains(LayerClassName))
                {
                    layer = child;
                    break;
                }
            }
            if (layer == null)
            {
                layer = new VisualElement { name = "md-overlay-layer", pickingMode = PickingMode.Ignore };
                layer.AddToClassList(LayerClassName);
                FillParent(layer);
                docRoot.Add(layer);
            }
            layer.BringToFront();
            return layer;
        }

        internal static void FillParent(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }
    }

    /// <summary>
    /// M3 menu: a popup list anchored to an element, shown on the overlay
    /// layer over a transparent tap-to-dismiss scrim. Opens below the anchor
    /// (above when there's no room), at least as wide as the anchor, clamped
    /// to the panel. Selecting an item invokes the callback and closes;
    /// tapping outside closes without selecting. One-shot: Open() per showing.
    /// </summary>
    public sealed class MdMenu
    {
        public const string UssClassName = "md-menu";
        public const string ScrimClassName = "md-menu__scrim";
        public const string ScrollClassName = "md-menu__scroll";
        public const string ItemClassName = "md-menu__item";
        public const string ItemLabelClassName = "md-menu__item-label";
        public const string ItemSelectedClassName = "md-menu__item--selected";

        const float AnchorGap = 4f;

        readonly VisualElement _layer;
        readonly VisualElement _scrim;
        readonly VisualElement _menu;
        readonly Rect _anchorRect;
        bool _closed;

        /// <summary>Raised exactly once, on any dismissal (selection or scrim).</summary>
        public event Action Closed;

        /// <summary>The popup element — exposed for tests and positioning checks.</summary>
        public VisualElement Root => _menu;

        public static MdMenu Open(VisualElement anchor, IReadOnlyList<string> items,
            int selectedIndex, Action<int> onSelect) =>
            new MdMenu(anchor, items, selectedIndex, onSelect);

        MdMenu(VisualElement anchor, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelect)
        {
            _layer = MdOverlay.EnsureLayer(anchor);
            _anchorRect = anchor.worldBound;

            _scrim = new VisualElement { name = "scrim" };
            _scrim.AddToClassList(ScrimClassName);
            MdOverlay.FillParent(_scrim);
            _scrim.AddManipulator(new Clickable(Close));

            _menu = new VisualElement { name = "menu" };
            _menu.AddToClassList(UssClassName);
            _menu.style.position = Position.Absolute;
            _menu.style.minWidth = _anchorRect.width;
            // Hidden until positioned, so it doesn't flash at (0,0) for a frame.
            _menu.style.visibility = Visibility.Hidden;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(ScrollClassName);
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = new VisualElement { name = $"item-{i}" };
                item.AddToClassList(ItemClassName);
                if (i == selectedIndex)
                    item.AddToClassList(ItemSelectedClassName);

                var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
                stateLayer.AddToClassList("md-state-layer");
                item.Add(stateLayer);

                var label = new Label(items[i]) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(ItemLabelClassName);
                item.Add(label);

                item.AddManipulator(new Clickable(() =>
                {
                    onSelect?.Invoke(index);
                    Close();
                }));
                scroll.Add(item);
            }
            _menu.Add(scroll);

            _layer.Add(_scrim);
            _layer.Add(_menu);
            _menu.RegisterCallback<GeometryChangedEvent>(_ => Reposition());
        }

        void Reposition()
        {
            var bounds = _layer.contentRect;
            float width = _menu.resolvedStyle.width;
            float height = _menu.resolvedStyle.height;
            float left = Mathf.Clamp(_anchorRect.xMin, 0f, Mathf.Max(0f, bounds.width - width));
            float top = _anchorRect.yMax + AnchorGap;
            if (top + height > bounds.height)
                top = Mathf.Max(0f, _anchorRect.yMin - height - AnchorGap);
            _menu.style.left = left;
            _menu.style.top = top;
            _menu.style.visibility = Visibility.Visible;
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _scrim.RemoveFromHierarchy();
            _menu.RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }
}
