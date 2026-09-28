using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdNavigationBarTests
    {
        static readonly MdNavDestination[] Destinations =
        {
            new MdNavDestination("view_in_ar", "AR"),
            new MdNavDestination("beach_access", "Praias"),
            new MdNavDestination("scuba_diving", "Animais"),
            new MdNavDestination("info", "Sobre"),
        };

        static MdNavigationBar Bar()
        {
            var bar = new MdNavigationBar();
            bar.SetDestinations(Destinations);
            return bar;
        }

        [Test]
        public void SetDestinations_BuildsOneItemPerDestinationWithIconAndLabel()
        {
            var bar = Bar();
            Assert.That(bar.DestinationCount, Is.EqualTo(4));
            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items.Count, Is.EqualTo(4));

            var first = items[0];
            var indicator = first.Q<VisualElement>(className: MdNavigationBar.IndicatorClassName);
            Assert.That(indicator, Is.Not.Null);
            Assert.That(indicator.Q<VisualElement>(className: "md-state-layer"), Is.Not.Null);
            Assert.That(indicator.Q<MdIcon>(className: MdNavigationBar.IconClassName).Icon,
                Is.EqualTo("view_in_ar"));
            Assert.That(first.Q<Label>(className: MdNavigationBar.LabelClassName).text,
                Is.EqualTo("AR"));
        }

        [Test]
        public void SelectedIndex_MovesActiveClassAndRaises()
        {
            var bar = Bar();
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1));

            int lastIndex = -2;
            bar.SelectionChanged += i => lastIndex = i;
            bar.SelectedIndex = 1;
            Assert.That(lastIndex, Is.EqualTo(1));

            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName));

            bar.SelectedIndex = 3;
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName), Is.False);
            Assert.That(items[3].ClassListContains(MdNavigationBar.ItemActiveClassName));
        }

        [Test]
        public void SelectedIndex_SameValueDoesNotRaise()
        {
            var bar = Bar();
            bar.SelectedIndex = 2;
            int raised = 0;
            bar.SelectionChanged += _ => raised++;
            bar.SelectedIndex = 2;
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void SelectedIndex_OutOfRangeClearsToMinusOne()
        {
            var bar = Bar();
            bar.SelectedIndex = 2;
            bar.SelectedIndex = 99;
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1));
            Assert.That(bar.Query<VisualElement>(className: MdNavigationBar.ItemActiveClassName).ToList(),
                Is.Empty);
        }

        [Test]
        public void SetDestinations_KeepsSelectionInRangeClearsWhenShrunk()
        {
            var bar = Bar();
            bar.SelectedIndex = 1;

            bar.SetDestinations(new List<MdNavDestination>(Destinations));
            Assert.That(bar.SelectedIndex, Is.EqualTo(1), "same-size rebuild keeps selection");
            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName));

            bar.SetDestinations(new[] { Destinations[0] });
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1), "stale selection cleared");

            bar.SetDestinations(null);
            Assert.That(bar.DestinationCount, Is.Zero);
        }

        [Test]
        public void Items_AreFocusableTouchTargetsWithInertVisuals()
        {
            var bar = Bar();
            var item = bar.Q<VisualElement>(className: MdNavigationBar.ItemClassName);
            Assert.That(item.focusable, Is.True);
            Assert.That(item.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(item.Q<VisualElement>(className: MdNavigationBar.IndicatorClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(item.Q<Label>(className: MdNavigationBar.LabelClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
