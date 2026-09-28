using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvAlertBarTests
    {
        [Test]
        public void Defaults_AreWarningWithTheWarningGlyph()
        {
            var bar = new MvAlertBar();
            Assert.That(bar.Severity, Is.EqualTo(MvAlertBarSeverity.Warning));
            Assert.That(bar.ClassListContains(MvAlertBar.UssClassName));
            Assert.That(bar.ClassListContains("mv-alert-bar--warning"));

            var icon = bar.Q<MdIcon>(className: MvAlertBar.IconClassName);
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map[MvAlertBar.DefaultIconName]));
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Severity_SwapsExactlyOneClass()
        {
            var bar = new MvAlertBar();
            bar.Severity = MvAlertBarSeverity.Error;
            Assert.That(bar.ClassListContains("mv-alert-bar--error"));
            Assert.That(bar.ClassListContains("mv-alert-bar--warning"), Is.False);

            bar.Severity = MvAlertBarSeverity.Success;
            Assert.That(bar.ClassListContains("mv-alert-bar--success"));
            Assert.That(bar.ClassListContains("mv-alert-bar--error"), Is.False);
        }

        [Test]
        public void Icon_EmptyHidesTheSlot_AndAKnownNameShowsIt()
        {
            var bar = new MvAlertBar();
            var icon = bar.Q<MdIcon>(className: MvAlertBar.IconClassName);

            bar.Icon = "";
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None));

            bar.Icon = "medical_services";
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["medical_services"]));
        }

        [Test]
        public void Text_GoesToTheLabel_AndNullBecomesEmpty()
        {
            var bar = new MvAlertBar { Text = "Salva-vidas: Das 08h às 17h" };
            var label = bar.Q<Label>(className: MvAlertBar.LabelClassName);
            Assert.That(label.text, Is.EqualTo("Salva-vidas: Das 08h às 17h"));

            bar.Text = null;
            Assert.That(bar.Text, Is.EqualTo(""));
        }

        [Test]
        public void IconComesBeforeTheLabel()
        {
            var bar = new MvAlertBar();
            Assert.That(bar.childCount, Is.EqualTo(2));
            Assert.That(bar[0].ClassListContains(MvAlertBar.IconClassName));
            Assert.That(bar[1].ClassListContains(MvAlertBar.LabelClassName));
        }
    }
}
