using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>Which kind of nothing an <see cref="MvStateView"/> is reporting.</summary>
    public enum MvStateViewVariant
    {
        /// <summary>Something failed. Default glyph <c>error</c>.</summary>
        Error,

        /// <summary>The device has no connectivity. Default glyph <c>wifi_off</c>.</summary>
        Offline,

        /// <summary>
        /// Nothing went wrong — there is simply nothing to show yet. Default
        /// glyph <c>list</c>: V2 draws no empty frame (§8.7 only has error and
        /// offline), so this is a neutral stand-in rather than a transcription,
        /// and callers with a better glyph set <see cref="Icon"/>.
        /// </summary>
        Empty,
    }

    /// <summary>
    /// The full-bleed "there is nothing here" state: a status icon over an
    /// optional illustration, a title, a body line, and one action button.
    /// Drives frames <c>79:1304</c> (error) and <c>81:1403</c> (offline), which
    /// are identical but for the glyph — hence one component with variants
    /// (DESIGN_IMPLEMENTATION.md §6 row 6, §8.7).
    ///
    /// <para>
    /// <b>It carries no copy of its own.</b> Every pt-BR string is supplied by
    /// the caller, because in this project user-visible strings live in a
    /// formatter under <c>Assets/UI/Domain/</c> and are pinned by a test — the
    /// design system must not be a second place they can be written. The gallery
    /// passes literals; the app passes <c>StateViewCopy</c>.
    /// </para>
    ///
    /// <para>
    /// <b>It works full-screen and inline, from one layout.</b> The content block
    /// is <c>flex-grow: 1</c> and centred, and the footer is
    /// <c>flex-shrink: 0</c>. Given a parent with a definite height it fills it
    /// and the button lands bottom-anchored, which is the Figma frame; given an
    /// auto-height parent (a card, a scroll column) there is no free space to
    /// grow into and the button simply follows the body at its authored gap. One
    /// component, both placements, no modifier.
    /// </para>
    ///
    /// <para>
    /// <b>The action is optional and the caller owns it.</b> Leave
    /// <see cref="ActionText"/> empty and the whole footer is hidden — a state
    /// with no honest retry path must not draw a button that does nothing.
    /// Nothing here retries anything: the view raises
    /// <see cref="ActionInvoked"/> and the screen decides what that means.
    /// </para>
    ///
    /// <para>
    /// <b>The illustration slot is empty until the designer ships the asset.</b>
    /// V2 draws a line-art shark-in-the-waves raster behind the glyph; it is an
    /// image in the Figma file with no exported counterpart in this repo, so the
    /// slot exists (<see cref="SetIllustration(Sprite)"/>) and renders nothing
    /// until one is supplied. No stand-in art is invented.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvStateView : VisualElement
    {
        public const string UssClassName = "mv-state-view";
        public const string ContentClassName = "mv-state-view__content";
        public const string IconClassName = "mv-state-view__icon";
        public const string IllustrationClassName = "mv-state-view__illustration";
        public const string TitleClassName = "mv-state-view__title";
        public const string BodyClassName = "mv-state-view__body";
        public const string FooterClassName = "mv-state-view__footer";
        public const string ActionClassName = "mv-state-view__action";

        /// <summary>Default Material Symbols glyph per variant, indexed by <see cref="MvStateViewVariant"/>.</summary>
        public static readonly string[] DefaultIconNames = { "error", "wifi_off", "list" };

        static readonly string[] VariantClassNames =
        {
            "mv-state-view--error",
            "mv-state-view--offline",
            "mv-state-view--empty",
        };

        readonly VisualElement _content;
        readonly MdIcon _icon;
        readonly Image _illustration;
        readonly Label _title;
        readonly Label _body;
        readonly VisualElement _footer;
        readonly MdButton _action;

        MvStateViewVariant _variant = MvStateViewVariant.Error;
        bool _iconOverridden;

        /// <summary>Raised when the action button is tapped. Never raised when
        /// the button is hidden (<see cref="ActionText"/> empty).</summary>
        public event Action ActionInvoked;

        /// <summary>
        /// Swaps the BEM modifier and, unless <see cref="Icon"/> was set
        /// explicitly, the default glyph. Setting the variant after a manual
        /// icon leaves the manual icon alone — the caller's choice wins.
        /// </summary>
        [UxmlAttribute("variant")]
        public MvStateViewVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
                if (!_iconOverridden) ApplyIcon(DefaultIconNames[(int)_variant]);
            }
        }

        /// <summary>Material Symbols name for the 46dp status glyph. Empty hides
        /// the slot; setting it pins the glyph against later variant changes.</summary>
        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon.Icon;
            set
            {
                _iconOverridden = true;
                ApplyIcon(value ?? "");
            }
        }

        [UxmlAttribute("title-text")]
        public string Title
        {
            get => _title.text;
            set
            {
                _title.text = value ?? "";
                _title.style.display = _title.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        [UxmlAttribute("body-text")]
        public string Body
        {
            get => _body.text;
            set
            {
                _body.text = value ?? "";
                _body.style.display = _body.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Label of the action button. Empty (the default) hides the
        /// whole footer, so a state with no retry draws no button.</summary>
        [UxmlAttribute("action-text")]
        public string ActionText
        {
            get => _action.Text;
            set
            {
                _action.Text = value ?? "";
                _footer.style.display = _action.Text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Enables/disables the action button without hiding it — for a retry that
        /// is currently in flight, where removing the button would read as the
        /// state having changed. Meaningless while the footer is hidden.
        /// </summary>
        [UxmlAttribute("action-enabled")]
        public bool ActionEnabled
        {
            get => _action.enabledSelf;
            set => _action.SetEnabled(value);
        }

        public MvStateView()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);
            // Nothing outside the button is interactive, so the root is not a
            // touch target and carries no state layer — MdButton owns both.
            pickingMode = PickingMode.Ignore;

            _content = new VisualElement { name = "content", pickingMode = PickingMode.Ignore };
            _content.AddToClassList(ContentClassName);

            _icon = new MdIcon { name = "icon", Icon = DefaultIconNames[(int)_variant] };
            _icon.AddToClassList(IconClassName);

            _illustration = new Image { name = "illustration", pickingMode = PickingMode.Ignore };
            _illustration.AddToClassList(IllustrationClassName);
            _illustration.style.display = DisplayStyle.None;

            _title = new Label { name = "title", pickingMode = PickingMode.Ignore };
            _title.AddToClassList(TitleClassName);
            _title.style.display = DisplayStyle.None;

            _body = new Label { name = "body", pickingMode = PickingMode.Ignore };
            _body.AddToClassList(BodyClassName);
            _body.style.display = DisplayStyle.None;

            _content.Add(_icon);
            _content.Add(_illustration);
            _content.Add(_title);
            _content.Add(_body);

            _footer = new VisualElement { name = "footer" };
            _footer.AddToClassList(FooterClassName);
            _footer.style.display = DisplayStyle.None;

            _action = new MdButton { name = "action", Variant = MdButtonVariant.Filled };
            _action.AddToClassList(ActionClassName);
            _action.Clicked += () => ActionInvoked?.Invoke();
            _footer.Add(_action);

            Add(_content);
            Add(_footer);
        }

        /// <summary>Show a sprite as the illustration (null clears it and hides the slot).</summary>
        public void SetIllustration(Sprite sprite)
        {
            _illustration.sprite = sprite;
            _illustration.image = null;
            UpdateIllustration(sprite != null);
        }

        /// <summary>Show a texture as the illustration (null clears it and hides the slot).</summary>
        public void SetIllustration(Texture2D texture)
        {
            _illustration.image = texture;
            _illustration.sprite = null;
            UpdateIllustration(texture != null);
        }

        void UpdateIllustration(bool visible) =>
            _illustration.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        void ApplyIcon(string name)
        {
            _icon.Icon = name;
            _icon.style.display = _icon.Icon.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
