using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdTopAppBarVariant
    {
        Small,
        CenterAligned,
        Medium,
        Large,
    }

    /// <summary>
    /// M3 top app bar (small / center-aligned / medium / large). Static — the
    /// collapse-on-scroll behavior comes later, per the plan. One icon-button
    /// leading slot (<see cref="NavigationIcon"/> + <see cref="NavigationClicked"/>),
    /// trailing action icon buttons via <see cref="AddAction"/>. The title is
    /// rendered in-row for Small/CenterAligned and on a second line for
    /// Medium/Large; both labels always exist and USS shows the right one, so
    /// switching variants never reparents anything.
    /// </summary>
    [UxmlElement]
    public partial class MdTopAppBar : VisualElement
    {
        public const string UssClassName = "md-top-app-bar";
        public const string RowClassName = "md-top-app-bar__row";
        public const string NavClassName = "md-top-app-bar__nav";
        public const string TitleClassName = "md-top-app-bar__title";
        public const string ActionsClassName = "md-top-app-bar__actions";
        public const string ActionClassName = "md-top-app-bar__action";
        public const string HeadlineClassName = "md-top-app-bar__headline";

        static readonly string[] VariantClassNames =
        {
            "md-top-app-bar--small",
            "md-top-app-bar--center-aligned",
            "md-top-app-bar--medium",
            "md-top-app-bar--large",
        };

        readonly MdIconButton _nav;
        readonly Label _title;
        readonly VisualElement _actions;
        readonly Label _headline;
        MdTopAppBarVariant _variant;
        string _navigationIcon = "";

        /// <summary>Raised when the leading navigation icon is tapped.</summary>
        public event Action NavigationClicked;

        [UxmlAttribute("variant")]
        public MdTopAppBarVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        [UxmlAttribute("title")]
        public string Title
        {
            get => _title.text;
            set
            {
                _title.text = value ?? "";
                _headline.text = value ?? "";
            }
        }

        /// <summary>Leading icon (Material Symbols name); empty = no nav slot.</summary>
        [UxmlAttribute("navigation-icon")]
        public string NavigationIcon
        {
            get => _navigationIcon;
            set
            {
                _navigationIcon = value ?? "";
                _nav.Icon = _navigationIcon;
                _nav.style.display = _navigationIcon.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public MdTopAppBar()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);

            var row = new VisualElement { name = "row", pickingMode = PickingMode.Ignore };
            row.AddToClassList(RowClassName);

            _nav = new MdIconButton { name = "nav", Variant = MdIconButtonVariant.Standard };
            _nav.AddToClassList(NavClassName);
            _nav.style.display = DisplayStyle.None;
            _nav.Clicked += () => NavigationClicked?.Invoke();

            _title = new Label { name = "title", pickingMode = PickingMode.Ignore };
            _title.AddToClassList(TitleClassName);

            _actions = new VisualElement { name = "actions", pickingMode = PickingMode.Ignore };
            _actions.AddToClassList(ActionsClassName);

            _headline = new Label { name = "headline", pickingMode = PickingMode.Ignore };
            _headline.AddToClassList(HeadlineClassName);

            row.Add(_nav);
            row.Add(_title);
            row.Add(_actions);
            Add(row);
            Add(_headline);
        }

        /// <summary>
        /// Appends a trailing action icon button and returns it (for later
        /// SetEnabled/icon swaps). M3 allows up to three; not enforced.
        /// </summary>
        public MdIconButton AddAction(string icon, Action clicked = null)
        {
            var action = new MdIconButton { Icon = icon, Variant = MdIconButtonVariant.Standard };
            action.AddToClassList(ActionClassName);
            if (clicked != null)
                action.Clicked += clicked;
            _actions.Add(action);
            return action;
        }

        public void ClearActions() => _actions.Clear();
    }
}
