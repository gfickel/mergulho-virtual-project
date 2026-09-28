using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdTopAppBarTests
    {
        [Test]
        public void Hierarchy_HasRowNavTitleActionsAndHeadline()
        {
            var bar = new MdTopAppBar();
            var row = bar.Q<VisualElement>(className: MdTopAppBar.RowClassName);
            Assert.That(row, Is.Not.Null);
            Assert.That(row.Q<MdIconButton>(className: MdTopAppBar.NavClassName), Is.Not.Null);
            Assert.That(row.Q<Label>(className: MdTopAppBar.TitleClassName), Is.Not.Null);
            Assert.That(row.Q<VisualElement>(className: MdTopAppBar.ActionsClassName), Is.Not.Null);
            // The medium/large headline is a sibling of the row, on the root.
            var headline = bar.Q<Label>(className: MdTopAppBar.HeadlineClassName);
            Assert.That(headline.parent, Is.EqualTo(bar));
        }

        [Test]
        public void Variant_SwapsModifierClass()
        {
            var bar = new MdTopAppBar();
            Assert.That(bar.ClassListContains("md-top-app-bar--small"));

            bar.Variant = MdTopAppBarVariant.Large;
            Assert.That(bar.ClassListContains("md-top-app-bar--large"));
            Assert.That(bar.ClassListContains("md-top-app-bar--small"), Is.False);

            bar.Variant = MdTopAppBarVariant.CenterAligned;
            Assert.That(bar.ClassListContains("md-top-app-bar--center-aligned"));
            Assert.That(bar.ClassListContains("md-top-app-bar--large"), Is.False);
        }

        [Test]
        public void Title_SetsBothInRowTitleAndHeadline()
        {
            var bar = new MdTopAppBar { Title = "Praias" };
            Assert.That(bar.Q<Label>(className: MdTopAppBar.TitleClassName).text, Is.EqualTo("Praias"));
            Assert.That(bar.Q<Label>(className: MdTopAppBar.HeadlineClassName).text, Is.EqualTo("Praias"));

            bar.Title = null;
            Assert.That(bar.Q<Label>(className: MdTopAppBar.TitleClassName).text, Is.EqualTo(""));
        }

        [Test]
        public void NavigationIcon_TogglesNavButtonVisibility()
        {
            var bar = new MdTopAppBar();
            var nav = bar.Q<MdIconButton>(className: MdTopAppBar.NavClassName);
            Assert.That(nav.style.display.value, Is.EqualTo(DisplayStyle.None), "hidden by default");

            bar.NavigationIcon = "arrow_back";
            Assert.That(nav.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(nav.Icon, Is.EqualTo("arrow_back"));

            bar.NavigationIcon = "";
            Assert.That(nav.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void AddAction_AppendsIconButtonsAndClearRemovesThem()
        {
            var bar = new MdTopAppBar();
            var actions = bar.Q<VisualElement>(className: MdTopAppBar.ActionsClassName);

            var search = bar.AddAction("search");
            bar.AddAction("more_vert");
            Assert.That(actions.childCount, Is.EqualTo(2));
            Assert.That(search.Icon, Is.EqualTo("search"));
            Assert.That(search.ClassListContains(MdTopAppBar.ActionClassName));
            Assert.That(search.parent, Is.EqualTo(actions));

            bar.ClearActions();
            Assert.That(actions.childCount, Is.Zero);
        }

        [Test]
        public void NonInteractiveParts_DoNotPick()
        {
            var bar = new MdTopAppBar();
            // The bar blocks taps from bleeding through to AR content behind it…
            Assert.That(bar.pickingMode, Is.EqualTo(PickingMode.Position));
            // …but its layout parts don't steal picks from the nav/action buttons.
            Assert.That(bar.Q<VisualElement>(className: MdTopAppBar.RowClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(bar.Q<Label>(className: MdTopAppBar.TitleClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(bar.Q<VisualElement>(className: MdTopAppBar.ActionsClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(bar.Q<Label>(className: MdTopAppBar.HeadlineClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
