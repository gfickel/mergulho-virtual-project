using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdChipTests
    {
        [Test]
        public void Selected_TogglesClassAndShowsCheckIcon()
        {
            var chip = new MdChip { Kind = MdChipKind.Filter, Text = "Sancho", Icon = "waves" };
            var icon = chip.Q<MdIcon>();
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["waves"]));

            chip.Selected = true;
            Assert.That(chip.ClassListContains(MdChip.SelectedClassName));
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["check"]), "selected filter chip shows check");

            chip.Selected = false;
            Assert.That(chip.ClassListContains(MdChip.SelectedClassName), Is.False);
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["waves"]), "custom icon restored");
        }

        [Test]
        public void SelectedChanged_FiresOncePerChange()
        {
            var chip = new MdChip { Kind = MdChipKind.Filter, Text = "Sueste" };
            int fired = 0;
            bool last = false;
            chip.SelectedChanged += v => { fired++; last = v; };

            chip.Selected = true;
            chip.Selected = true; // no-op, must not re-fire
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(last, Is.True);

            chip.Selected = false;
            Assert.That(fired, Is.EqualTo(2));
            Assert.That(last, Is.False);
        }

        [Test]
        public void Selected_IsIndependentOfAttributeOrder()
        {
            // UXML applies attributes in unspecified order; Selected must stick
            // even when set before Kind.
            var chip = new MdChip();
            chip.Selected = true;
            chip.Kind = MdChipKind.Filter;
            Assert.That(chip.Selected, Is.True);
            Assert.That(chip.ClassListContains(MdChip.SelectedClassName));
        }

        /// <summary>
        /// The check is a FILTER affordance. An assist chip (what the Reportar
        /// species / size / behaviour rows build) must keep exactly the same
        /// width selected and unselected, or the row re-flows under the finger.
        /// </summary>
        [Test]
        public void SelectedAssistChip_DoesNotGrowACheckIcon()
        {
            var chip = new MdChip { Kind = MdChipKind.Assist, Text = "Tubarão-tigre" };
            var icon = chip.Q<MdIcon>();

            chip.Selected = true;
            Assert.That(chip.ClassListContains(MdChip.SelectedClassName), "still paints as selected");
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None), "no check on an assist chip");
            Assert.That(chip.ClassListContains(MdChip.WithIconClassName), Is.False);

            // An assist chip with its OWN leading icon keeps it, selected or not.
            var withIcon = new MdChip { Kind = MdChipKind.Assist, Text = "Marés", Icon = "waves" };
            withIcon.Selected = true;
            Assert.That(withIcon.Q<MdIcon>().text, Is.EqualTo(MdIconGlyphs.Map["waves"]));
        }

        [Test]
        public void ChipWithoutIcon_HidesLeadingIconUntilSelected()
        {
            // Filter kind: this is the one path on which selection adds the check.
            var chip = new MdChip { Kind = MdChipKind.Filter, Text = "Porto" };
            var icon = chip.Q<MdIcon>();
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(chip.ClassListContains(MdChip.WithIconClassName), Is.False);

            chip.Selected = true;
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(chip.ClassListContains(MdChip.WithIconClassName));
        }
    }
}
