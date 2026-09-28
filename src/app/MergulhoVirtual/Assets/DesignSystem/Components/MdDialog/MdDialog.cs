using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 basic dialog: a modal card centered on the shared overlay layer over
    /// a visible scrim. Headline + optional icon + optional supporting text +
    /// up to two text-button actions (confirm, and an optional dismiss).
    /// Action taps raise <see cref="Confirmed"/>/<see cref="Dismissed"/> and
    /// close; tapping the scrim closes without raising either (only
    /// <see cref="Closed"/>, which fires exactly once on any dismissal).
    /// The element is constructible standalone (structure tests); showing it
    /// goes through <see cref="Open"/>, one-shot per instance like MdMenu.
    /// </summary>
    public sealed class MdDialog : VisualElement
    {
        public const string UssClassName = "md-dialog";
        public const string ScrimClassName = "md-dialog__scrim";
        public const string CenterClassName = "md-dialog__center";
        public const string IconClassName = "md-dialog__icon";
        public const string HeadlineClassName = "md-dialog__headline";
        public const string SupportingClassName = "md-dialog__supporting";
        public const string ActionsClassName = "md-dialog__actions";
        public const string ActionClassName = "md-dialog__action";
        public const string WithIconClassName = "md-dialog--with-icon";

        readonly MdIcon _icon;
        readonly Label _headline;
        readonly Label _supporting;
        readonly MdButton _dismissButton;
        readonly MdButton _confirmButton;
        string _iconName = "";
        VisualElement _scrim;
        VisualElement _center;
        bool _closed;

        /// <summary>Raised when the confirm action is tapped (before closing).</summary>
        public event Action Confirmed;

        /// <summary>Raised when the dismiss action is tapped (before closing).</summary>
        public event Action Dismissed;

        /// <summary>Raised exactly once, on any dismissal (action or scrim).</summary>
        public event Action Closed;

        /// <summary>The confirm text button — exposed for tests.</summary>
        public MdButton ConfirmButton => _confirmButton;

        /// <summary>The dismiss text button — exposed for tests.</summary>
        public MdButton DismissButton => _dismissButton;

        public string Headline
        {
            get => _headline.text;
            set => _headline.text = value ?? "";
        }

        /// <summary>Optional supporting text; empty hides the label.</summary>
        public string SupportingText
        {
            get => _supporting.text;
            set
            {
                _supporting.text = value ?? "";
                _supporting.style.display =
                    _supporting.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Optional hero icon (Material Symbols name); empty = none.
        /// With an icon, M3 centers icon and headline.</summary>
        public string Icon
        {
            get => _iconName;
            set
            {
                _iconName = value ?? "";
                _icon.Icon = _iconName;
                bool hasIcon = _iconName.Length > 0;
                _icon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
                EnableInClassList(WithIconClassName, hasIcon);
            }
        }

        public string ConfirmText
        {
            get => _confirmButton.Text;
            set => _confirmButton.Text = value ?? "";
        }

        /// <summary>Optional dismiss action label; empty hides the button
        /// (single-action dialog).</summary>
        public string DismissText
        {
            get => _dismissButton.Text;
            set
            {
                _dismissButton.Text = value ?? "";
                _dismissButton.style.display =
                    _dismissButton.Text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public MdDialog()
        {
            AddToClassList(UssClassName);

            _icon = new MdIcon { name = "icon" };
            _icon.AddToClassList(IconClassName);
            _icon.style.display = DisplayStyle.None;

            _headline = new Label { name = "headline", pickingMode = PickingMode.Ignore };
            _headline.AddToClassList(HeadlineClassName);

            _supporting = new Label { name = "supporting", pickingMode = PickingMode.Ignore };
            _supporting.AddToClassList(SupportingClassName);
            _supporting.style.display = DisplayStyle.None;

            var actions = new VisualElement { name = "actions" };
            actions.AddToClassList(ActionsClassName);

            _dismissButton = new MdButton { name = "dismiss", Variant = MdButtonVariant.Text };
            _dismissButton.AddToClassList(ActionClassName);
            _dismissButton.style.display = DisplayStyle.None;
            _dismissButton.Clicked += () =>
            {
                Dismissed?.Invoke();
                Close();
            };

            _confirmButton = new MdButton { name = "confirm", Variant = MdButtonVariant.Text };
            _confirmButton.AddToClassList(ActionClassName);
            _confirmButton.Clicked += () =>
            {
                Confirmed?.Invoke();
                Close();
            };

            actions.Add(_dismissButton);
            actions.Add(_confirmButton);

            Add(_icon);
            Add(_headline);
            Add(_supporting);
            Add(actions);
        }

        /// <summary>Show a dialog on the overlay layer of <paramref name="context"/>'s
        /// panel (any attached element works as context).</summary>
        public static MdDialog Open(VisualElement context, string headline, string supportingText,
            string confirmText, Action onConfirm = null,
            string dismissText = null, Action onDismiss = null, string icon = null)
        {
            var dialog = new MdDialog
            {
                Headline = headline,
                SupportingText = supportingText,
                ConfirmText = confirmText,
                DismissText = dismissText,
                Icon = icon,
            };
            if (onConfirm != null)
                dialog.Confirmed += onConfirm;
            if (onDismiss != null)
                dialog.Dismissed += onDismiss;
            dialog.Show(context);
            return dialog;
        }

        void Show(VisualElement context)
        {
            var layer = MdOverlay.EnsureLayer(context);

            _scrim = new VisualElement { name = "scrim" };
            _scrim.AddToClassList(ScrimClassName);
            MdOverlay.FillParent(_scrim);
            _scrim.AddManipulator(new Clickable(Close));

            // A hit-transparent centering holder: the dialog itself is pickable
            // (blocking taps on its own surface); everything around it falls
            // through to the scrim.
            _center = new VisualElement { name = "center", pickingMode = PickingMode.Ignore };
            _center.AddToClassList(CenterClassName);
            MdOverlay.FillParent(_center);
            _center.style.alignItems = Align.Center;
            _center.style.justifyContent = Justify.Center;
            _center.Add(this);

            layer.Add(_scrim);
            layer.Add(_center);
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _scrim?.RemoveFromHierarchy();
            _center?.RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }
}
