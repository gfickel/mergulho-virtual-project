using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdDialogTests
    {
        [Test]
        public void Hierarchy_HasIconHeadlineSupportingAndTwoActions()
        {
            var dialog = new MdDialog();
            Assert.That(dialog.ClassListContains(MdDialog.UssClassName));
            Assert.That(dialog.Q<MdIcon>(className: MdDialog.IconClassName), Is.Not.Null);
            Assert.That(dialog.Q<Label>(className: MdDialog.HeadlineClassName), Is.Not.Null);
            Assert.That(dialog.Q<Label>(className: MdDialog.SupportingClassName), Is.Not.Null);
            var actions = dialog.Q<VisualElement>(className: MdDialog.ActionsClassName);
            Assert.That(actions, Is.Not.Null);
            Assert.That(dialog.DismissButton.parent, Is.EqualTo(actions));
            Assert.That(dialog.ConfirmButton.parent, Is.EqualTo(actions));
            // Confirm is the trailing (rightmost) action, per M3.
            Assert.That(actions.IndexOf(dialog.ConfirmButton),
                Is.GreaterThan(actions.IndexOf(dialog.DismissButton)));
        }

        [Test]
        public void Defaults_IconSupportingAndDismissHidden()
        {
            var dialog = new MdDialog();
            Assert.That(dialog.Q<MdIcon>(className: MdDialog.IconClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(dialog.Q<Label>(className: MdDialog.SupportingClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(dialog.DismissButton.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(dialog.ClassListContains(MdDialog.WithIconClassName), Is.False);
        }

        [Test]
        public void TextProperties_RoundTripAndToggleVisibility()
        {
            var dialog = new MdDialog
            {
                Headline = "Descartar registro?",
                SupportingText = "As informações serão perdidas.",
                ConfirmText = "Descartar",
                DismissText = "Cancelar",
            };
            Assert.That(dialog.Headline, Is.EqualTo("Descartar registro?"));
            Assert.That(dialog.Q<Label>(className: MdDialog.SupportingClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(dialog.ConfirmButton.Text, Is.EqualTo("Descartar"));
            Assert.That(dialog.DismissButton.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            dialog.SupportingText = "";
            dialog.DismissText = null;
            Assert.That(dialog.Q<Label>(className: MdDialog.SupportingClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(dialog.DismissButton.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Icon_TogglesClassAndVisibility()
        {
            var dialog = new MdDialog { Icon = "add_a_photo" };
            Assert.That(dialog.ClassListContains(MdDialog.WithIconClassName));
            Assert.That(dialog.Q<MdIcon>(className: MdDialog.IconClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.Flex));

            dialog.Icon = "";
            Assert.That(dialog.ClassListContains(MdDialog.WithIconClassName), Is.False);
            Assert.That(dialog.Q<MdIcon>(className: MdDialog.IconClassName)
                .style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Close_RaisesClosedExactlyOnce()
        {
            var dialog = new MdDialog();
            int closed = 0;
            dialog.Closed += () => closed++;
            dialog.Close();
            dialog.Close();
            Assert.That(closed, Is.EqualTo(1));
        }
    }
}
