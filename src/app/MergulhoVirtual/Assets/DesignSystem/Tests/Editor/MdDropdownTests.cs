using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdDropdownTests
    {
        static readonly string[] Beaches = { "Sancho", "Cacimba do Padre", "Sueste" };

        [Test]
        public void Hierarchy_HasFieldStateLayerValueArrowAndRootLabel()
        {
            var dropdown = new MdDropdown();
            var field = dropdown.Q<VisualElement>(className: MdDropdown.FieldClassName);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.Q<VisualElement>(className: "md-state-layer"), Is.Not.Null);
            Assert.That(field.Q<Label>(className: MdDropdown.ValueClassName), Is.Not.Null);
            Assert.That(field.Q<MdIcon>(className: MdDropdown.ArrowClassName), Is.Not.Null);
            // The floating label lives on the ROOT (the field clips to its
            // rounded corners and would cut off a border-straddling label).
            var label = dropdown.Q<Label>(className: MdDropdown.LabelClassName);
            Assert.That(label.parent, Is.EqualTo(dropdown));
        }

        [Test]
        public void Index_SelectsUpdatesValueAndFloatsLabel()
        {
            var dropdown = new MdDropdown();
            dropdown.SetChoices(Beaches);
            Assert.That(dropdown.Index, Is.EqualTo(-1));
            Assert.That(dropdown.Value, Is.EqualTo(""));
            Assert.That(dropdown.ClassListContains(MdDropdown.FloatingClassName), Is.False);

            int lastIndex = -2;
            dropdown.SelectionChanged += i => lastIndex = i;
            dropdown.Index = 1;
            Assert.That(lastIndex, Is.EqualTo(1));
            Assert.That(dropdown.Value, Is.EqualTo("Cacimba do Padre"));
            Assert.That(dropdown.Q<Label>(className: MdDropdown.ValueClassName).text, Is.EqualTo("Cacimba do Padre"));
            Assert.That(dropdown.ClassListContains(MdDropdown.FloatingClassName));
        }

        [Test]
        public void Index_OutOfRangeClearsToMinusOne()
        {
            var dropdown = new MdDropdown();
            dropdown.SetChoices(Beaches);
            dropdown.Index = 1;

            dropdown.Index = 99;
            Assert.That(dropdown.Index, Is.EqualTo(-1));
            Assert.That(dropdown.Value, Is.EqualTo(""));
            Assert.That(dropdown.ClassListContains(MdDropdown.FloatingClassName), Is.False);
        }

        [Test]
        public void Index_SameValueDoesNotRaise()
        {
            var dropdown = new MdDropdown();
            dropdown.SetChoices(Beaches);
            dropdown.Index = 2;
            int raised = 0;
            dropdown.SelectionChanged += _ => raised++;
            dropdown.Index = 2;
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void SetChoices_ShrinkingBelowSelectionClears()
        {
            var dropdown = new MdDropdown();
            dropdown.SetChoices(Beaches);
            dropdown.Index = 2;

            dropdown.SetChoices(new[] { "Sancho" });
            Assert.That(dropdown.Index, Is.EqualTo(-1), "stale selection cleared");

            dropdown.SetChoices(null);
            Assert.That(dropdown.Choices, Is.Empty);
        }

        [Test]
        public void IsFocusableTouchTarget()
        {
            var dropdown = new MdDropdown();
            Assert.That(dropdown.focusable, Is.True);
            Assert.That(dropdown.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(dropdown.Q<VisualElement>(className: MdDropdown.FieldClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
