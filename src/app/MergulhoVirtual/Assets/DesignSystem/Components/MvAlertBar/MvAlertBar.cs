using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>Semantic color of an <see cref="MvAlertBar"/>.</summary>
    public enum MvAlertBarSeverity
    {
        /// <summary>Amber — the V2 default ("Salva-vidas: Das 08h às 17h").</summary>
        Warning,
        /// <summary>Neutral-informational (amber-container's cooler sibling).</summary>
        Info,
        /// <summary>Green — a reassuring notice.</summary>
        Success,
        /// <summary>Red — a blocking or dangerous condition.</summary>
        Error,
    }

    /// <summary>
    /// A single-line-ish inline notice: leading icon + wrapping text on a tinted,
    /// outlined, rounded bar. Non-interactive by default (the root stays pickable
    /// so a screen can wrap it in a <c>Clickable</c> if a notice ever needs to
    /// open something, but nothing here raises events).
    /// <para>
    /// V2 uses exactly one of these — the lifeguard hours bar on Praia detalhe
    /// (§8.3) — but the three extra severities are the same four USS lines each
    /// against tokens that already exist, and Slice 5's error/offline states want
    /// them. Set <see cref="Icon"/> to the empty string for a text-only bar.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvAlertBar : VisualElement
    {
        public const string UssClassName = "mv-alert-bar";
        public const string IconClassName = "mv-alert-bar__icon";
        public const string LabelClassName = "mv-alert-bar__label";

        /// <summary>Material Symbols name used when nothing else is set.</summary>
        public const string DefaultIconName = "warning";

        static readonly string[] SeverityClassNames =
        {
            "mv-alert-bar--warning",
            "mv-alert-bar--info",
            "mv-alert-bar--success",
            "mv-alert-bar--error",
        };

        readonly MdIcon _icon;
        readonly Label _label;
        MvAlertBarSeverity _severity = MvAlertBarSeverity.Warning;

        [UxmlAttribute("text")]
        public string Text
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        /// <summary>Leading icon (Material Symbols name); empty = no icon slot.</summary>
        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon.Icon;
            set
            {
                _icon.Icon = value ?? "";
                _icon.style.display = _icon.Icon.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        [UxmlAttribute("severity")]
        public MvAlertBarSeverity Severity
        {
            get => _severity;
            set
            {
                RemoveFromClassList(SeverityClassNames[(int)_severity]);
                _severity = value;
                AddToClassList(SeverityClassNames[(int)_severity]);
            }
        }

        public MvAlertBar()
        {
            AddToClassList(UssClassName);
            AddToClassList(SeverityClassNames[(int)_severity]);

            _icon = new MdIcon { name = "icon", Icon = DefaultIconName };
            _icon.AddToClassList(IconClassName);

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            Add(_icon);
            Add(_label);
        }
    }
}
