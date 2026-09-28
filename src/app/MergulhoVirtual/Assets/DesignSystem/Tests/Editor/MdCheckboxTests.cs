using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdCheckboxTests
    {
        [Test]
        public void Defaults_AreUncheckedAndFocusable()
        {
            var box = new MdCheckbox();
            Assert.That(box.Checked, Is.False);
            Assert.That(box.ClassListContains(MdCheckbox.UssClassName));
            Assert.That(box.ClassListContains(MdCheckbox.CheckedClassName), Is.False);
            Assert.That(box.focusable, Is.True, "keyboard focus, like every other interactive component");
        }

        [Test]
        public void Structure_IsTouchTargetThenDiscThenBox()
        {
            // 48dp root / 40dp state-layer disc / 18dp box — the MdIconButton split.
            var box = new MdCheckbox();
            var container = box.Q<VisualElement>(className: MdCheckbox.ContainerClassName);
            Assert.That(container, Is.Not.Null);
            Assert.That(container.parent, Is.EqualTo(box));
            Assert.That(container[0].ClassListContains("md-state-layer"),
                "the state layer must be the container's first child");
            Assert.That(container.Q<VisualElement>(className: MdCheckbox.BoxClassName), Is.Not.Null);
        }

        [Test]
        public void CheckGlyph_IsAnMdIconInsideTheBox()
        {
            var box = new MdCheckbox();
            var visual = box.Q<VisualElement>(className: MdCheckbox.BoxClassName);
            var check = box.Q<MdIcon>(className: MdCheckbox.CheckClassName);
            Assert.That(check, Is.Not.Null);
            Assert.That(check.parent, Is.EqualTo(visual));
            Assert.That(check.Icon, Is.EqualTo("check"));
        }

        [Test]
        public void Checked_TogglesExactlyOneClass()
        {
            var box = new MdCheckbox { Checked = true };
            Assert.That(box.ClassListContains(MdCheckbox.CheckedClassName));
            box.Checked = false;
            Assert.That(box.ClassListContains(MdCheckbox.CheckedClassName), Is.False);
        }

        [Test]
        public void ValueChanged_RaisesOnChangeOnly()
        {
            var box = new MdCheckbox();
            int raised = 0;
            bool? last = null;
            box.ValueChanged += v => { raised++; last = v; };

            box.Checked = true;
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(last, Is.True);

            // Re-setting the same value is a no-op: this guard is what makes a
            // ViewModel writing back from the handler terminate after one hop.
            box.Checked = true;
            Assert.That(raised, Is.EqualTo(1));

            box.Checked = false;
            Assert.That(raised, Is.EqualTo(2));
            Assert.That(last, Is.False);
        }

        [Test]
        public void Toggle_FlipsAndRaises()
        {
            var box = new MdCheckbox();
            int raised = 0;
            box.ValueChanged += _ => raised++;

            box.Toggle();
            Assert.That(box.Checked, Is.True);
            box.Toggle();
            Assert.That(box.Checked, Is.False);
            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void TwoWayBinding_DoesNotLoop()
        {
            // The screen pattern: VM <- component, and the VM writes back.
            var box = new MdCheckbox();
            bool model = false;
            int raised = 0;
            box.ValueChanged += v =>
            {
                raised++;
                model = v;
                box.Checked = model; // the echo the equality guard has to absorb
            };

            box.Toggle();
            Assert.That(model, Is.True);
            Assert.That(raised, Is.EqualTo(1), "the write-back must not re-raise");
        }
    }
}
