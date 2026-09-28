using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdTextFieldTests
    {
        [Test]
        public void Hierarchy_HasContainerLabelInputAndSupporting()
        {
            var field = new MdTextField();
            var container = field.Q<VisualElement>(className: MdTextField.ContainerClassName);
            Assert.That(container, Is.Not.Null);
            Assert.That(container.Q<Label>(className: MdTextField.LabelClassName), Is.Not.Null);
            Assert.That(container.Q<TextField>(className: MdTextField.InputClassName), Is.Not.Null);
            Assert.That(field.Q<Label>(className: MdTextField.SupportingClassName), Is.Not.Null);
        }

        [Test]
        public void Variant_TogglesVariantClass()
        {
            var field = new MdTextField();
            Assert.That(field.ClassListContains("md-text-field--filled"), "defaults to filled");

            field.Variant = MdTextFieldVariant.Outlined;
            Assert.That(field.ClassListContains("md-text-field--outlined"));
            Assert.That(field.ClassListContains("md-text-field--filled"), Is.False);
        }

        [Test]
        public void Value_FloatsTheLabel()
        {
            var field = new MdTextField { LabelText = "Espécie" };
            Assert.That(field.ClassListContains(MdTextField.FloatingClassName), Is.False);

            field.Value = "Tubarão-limão";
            Assert.That(field.ClassListContains(MdTextField.FloatingClassName));
            Assert.That(field.Q<TextField>().value, Is.EqualTo("Tubarão-limão"));

            field.Value = "";
            Assert.That(field.ClassListContains(MdTextField.FloatingClassName), Is.False);
        }

        [Test]
        public void ValueChanged_RaisedOnChange_NotOnSameValue()
        {
            var field = new MdTextField();
            int raised = 0;
            string last = null;
            field.ValueChanged += v => { raised++; last = v; };

            field.Value = "abc";
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(last, Is.EqualTo("abc"));

            field.Value = "abc";
            Assert.That(raised, Is.EqualTo(1), "same-value set is ignored");

            field.Value = null;
            Assert.That(raised, Is.EqualTo(2));
            Assert.That(last, Is.EqualTo(""));
        }

        [Test]
        public void ErrorText_EntersErrorStateAndOverridesSupportingText()
        {
            var field = new MdTextField { SupportingText = "Nome popular" };
            var supporting = field.Q<Label>(className: MdTextField.SupportingClassName);
            Assert.That(field.IsError, Is.False);
            Assert.That(supporting.text, Is.EqualTo("Nome popular"));

            field.ErrorText = "Campo obrigatório";
            Assert.That(field.IsError);
            Assert.That(field.ClassListContains(MdTextField.ErrorClassName));
            Assert.That(supporting.text, Is.EqualTo("Campo obrigatório"));

            field.ErrorText = "";
            Assert.That(field.IsError, Is.False);
            Assert.That(field.ClassListContains(MdTextField.ErrorClassName), Is.False);
            Assert.That(supporting.text, Is.EqualTo("Nome popular"), "supporting text restored");
        }

        [Test]
        public void SupportingLabel_HiddenWhenEmpty()
        {
            var field = new MdTextField();
            var supporting = field.Q<Label>(className: MdTextField.SupportingClassName);
            Assert.That(supporting.style.display.value, Is.EqualTo(DisplayStyle.None));

            field.SupportingText = "Ajuda";
            Assert.That(supporting.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            field.SupportingText = "";
            Assert.That(supporting.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void LabelText_RoundTrips()
        {
            var field = new MdTextField { LabelText = "Praia" };
            Assert.That(field.LabelText, Is.EqualTo("Praia"));
            field.LabelText = null;
            Assert.That(field.LabelText, Is.EqualTo(""));
        }
    }
}
