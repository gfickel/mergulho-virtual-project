using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 snackbar: a brief message docked at the bottom of the shared overlay
    /// layer, with an optional text action. Auto-dismisses after
    /// <see cref="DefaultDurationSeconds"/> (pass 0 to keep it until acted on
    /// or replaced); consecutive <see cref="Show"/> calls replace the current
    /// one, per M3. Non-modal: no scrim, and the overlay layer is
    /// hit-transparent, so only the snackbar's own surface intercepts taps.
    /// The element is constructible standalone (structure tests); showing it
    /// goes through <see cref="Show"/>, one-shot per instance like MdMenu.
    /// </summary>
    public sealed class MdSnackbar : VisualElement
    {
        public const string UssClassName = "md-snackbar";
        public const string LabelClassName = "md-snackbar__label";
        public const string ActionClassName = "md-snackbar__action";
        public const string WithActionClassName = "md-snackbar--with-action";

        public const float DefaultDurationSeconds = 4f;
        const float ScreenMargin = 16f;

        static MdSnackbar _current;

        readonly Label _label;
        readonly MdButton _action;
        VisualElement _holder;
        bool _closed;

        /// <summary>Raised when the action is tapped (before closing).</summary>
        public event Action ActionClicked;

        /// <summary>Raised exactly once, on any dismissal (action, timeout,
        /// replacement, or explicit <see cref="Close"/>).</summary>
        public event Action Closed;

        /// <summary>The action text button — exposed for tests.</summary>
        public MdButton ActionButton => _action;

        public string Message
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        /// <summary>Optional action label; empty hides the action button.</summary>
        public string ActionText
        {
            get => _action.Text;
            set
            {
                _action.Text = value ?? "";
                bool hasAction = _action.Text.Length > 0;
                _action.style.display = hasAction ? DisplayStyle.Flex : DisplayStyle.None;
                EnableInClassList(WithActionClassName, hasAction);
            }
        }

        public MdSnackbar()
        {
            AddToClassList(UssClassName);

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            _action = new MdButton { name = "action", Variant = MdButtonVariant.Text };
            _action.AddToClassList(ActionClassName);
            _action.style.display = DisplayStyle.None;
            _action.Clicked += () =>
            {
                ActionClicked?.Invoke();
                Close();
            };

            Add(_label);
            Add(_action);
        }

        /// <summary>Show a snackbar on the overlay layer of
        /// <paramref name="context"/>'s panel (any attached element works as
        /// context), replacing the currently visible one, if any.</summary>
        public static MdSnackbar Show(VisualElement context, string message,
            string actionText = null, Action onAction = null,
            float durationSeconds = DefaultDurationSeconds)
        {
            _current?.Close();

            var snackbar = new MdSnackbar { Message = message, ActionText = actionText };
            if (onAction != null)
                snackbar.ActionClicked += onAction;

            var layer = MdOverlay.EnsureLayer(context);
            // Bottom-docked holder (inline like MdMenu's geometry): fills the
            // layer, hit-transparent, and pads by the screen margin so the
            // snackbar's USS width/max-width center within it.
            var holder = new VisualElement { name = "snackbar-holder", pickingMode = PickingMode.Ignore };
            MdOverlay.FillParent(holder);
            holder.style.justifyContent = Justify.FlexEnd;
            holder.style.alignItems = Align.Center;
            holder.style.paddingLeft = ScreenMargin;
            holder.style.paddingRight = ScreenMargin;
            holder.style.paddingBottom = ScreenMargin;
            holder.Add(snackbar);
            layer.Add(holder);
            snackbar._holder = holder;

            _current = snackbar;
            if (durationSeconds > 0)
                snackbar.schedule.Execute(snackbar.Close)
                    .StartingIn((long)(durationSeconds * 1000f));
            return snackbar;
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            if (_current == this)
                _current = null;
            // _holder is null when the element was never shown.
            (_holder ?? this).RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }
}
