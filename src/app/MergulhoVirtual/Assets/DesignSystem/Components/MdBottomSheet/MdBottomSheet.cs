using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 modal bottom sheet: a surface docked to the bottom of the shared
    /// overlay layer over a visible scrim, with the standard drag-handle
    /// affordance on top and free-form slotted content below (children added
    /// to the sheet land in the content slot). Tapping the scrim closes;
    /// <see cref="Closed"/> fires exactly once on any dismissal.
    /// Drag-to-dismiss is deliberately deferred (plan note) — the handle is
    /// visual-only for now. One-shot per instance, like MdDialog/MdMenu; the
    /// element is also constructible standalone for structure tests.
    /// </summary>
    public sealed class MdBottomSheet : VisualElement
    {
        public const string UssClassName = "md-bottom-sheet";
        public const string ScrimClassName = "md-bottom-sheet__scrim";
        public const string HolderClassName = "md-bottom-sheet__holder";
        public const string HandleClassName = "md-bottom-sheet__handle";
        public const string ContentClassName = "md-bottom-sheet__content";

        readonly VisualElement _handle;
        readonly VisualElement _content;
        VisualElement _scrim;
        VisualElement _holder;
        bool _closed;

        /// <summary>Raised exactly once, on any dismissal (scrim or Close()).</summary>
        public event Action Closed;

        /// <summary>The drag-handle affordance — exposed for tests.</summary>
        public VisualElement Handle => _handle;

        /// <summary>Children added to the sheet go into the content slot,
        /// below the drag handle.</summary>
        public override VisualElement contentContainer => _content ?? this;

        /// <summary>The M3 drag-handle affordance; hide it for sheets that
        /// should read as non-draggable surfaces.</summary>
        public bool ShowHandle
        {
            get => _handle.style.display.value != DisplayStyle.None;
            set => _handle.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public MdBottomSheet()
        {
            AddToClassList(UssClassName);

            _handle = new VisualElement { name = "handle", pickingMode = PickingMode.Ignore };
            _handle.AddToClassList(HandleClassName);
            hierarchy.Add(_handle);

            _content = new VisualElement { name = "content" };
            _content.AddToClassList(ContentClassName);
            hierarchy.Add(_content);
        }

        /// <summary>Show an empty sheet on the overlay layer of
        /// <paramref name="context"/>'s panel (any attached element works as
        /// context); add content to the returned sheet.</summary>
        public static MdBottomSheet Open(VisualElement context)
        {
            var sheet = new MdBottomSheet();
            sheet.Show(context);
            return sheet;
        }

        void Show(VisualElement context)
        {
            var layer = MdOverlay.EnsureLayer(context);

            _scrim = new VisualElement { name = "scrim" };
            _scrim.AddToClassList(ScrimClassName);
            MdOverlay.FillParent(_scrim);
            _scrim.AddManipulator(new Clickable(Close));

            // A hit-transparent bottom-docking holder: the sheet itself is
            // pickable (blocking taps on its own surface); everything above it
            // falls through to the scrim.
            _holder = new VisualElement { name = "holder", pickingMode = PickingMode.Ignore };
            _holder.AddToClassList(HolderClassName);
            MdOverlay.FillParent(_holder);
            _holder.style.justifyContent = Justify.FlexEnd;
            _holder.style.alignItems = Align.Center;
            _holder.Add(this);

            layer.Add(_scrim);
            layer.Add(_holder);
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _scrim?.RemoveFromHierarchy();
            if (_holder != null)
                _holder.RemoveFromHierarchy();
            else
                RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }
}
