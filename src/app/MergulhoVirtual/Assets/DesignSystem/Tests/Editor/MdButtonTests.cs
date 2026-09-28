using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdButtonTests
    {
        [Test]
        public void Hierarchy_HasContainerStateLayerIconAndLabel()
        {
            var button = new MdButton();
            var container = button.Q<VisualElement>(className: MdButton.ContainerClassName);
            Assert.That(container, Is.Not.Null);
            Assert.That(container.Q<VisualElement>(className: "md-state-layer"), Is.Not.Null);
            Assert.That(container.Q<MdIcon>(), Is.Not.Null);
            Assert.That(container.Q<Label>(className: MdButton.LabelClassName), Is.Not.Null);
            // State layer must be first so content renders above it.
            Assert.That(container.IndexOf(container.Q<VisualElement>(className: "md-state-layer")), Is.EqualTo(0));
        }

        [Test]
        public void Variant_TogglesVariantClass()
        {
            var button = new MdButton();
            Assert.That(button.ClassListContains("md-button--filled"), "defaults to filled");

            button.Variant = MdButtonVariant.Outlined;
            Assert.That(button.ClassListContains("md-button--outlined"));
            Assert.That(button.ClassListContains("md-button--filled"), Is.False);

            button.Variant = MdButtonVariant.Text;
            Assert.That(button.ClassListContains("md-button--text"));
            Assert.That(button.ClassListContains("md-button--outlined"), Is.False);
        }

        [Test]
        public void Text_RoundTrips()
        {
            var button = new MdButton { Text = "Enviar" };
            Assert.That(button.Text, Is.EqualTo("Enviar"));
            Assert.That(button.Q<Label>(className: MdButton.LabelClassName).text, Is.EqualTo("Enviar"));

            button.Text = null;
            Assert.That(button.Text, Is.EqualTo(""));
        }

        [Test]
        public void Icon_TogglesDisplayAndClass()
        {
            var button = new MdButton();
            var icon = button.Q<MdIcon>();
            Assert.That(button.ClassListContains(MdButton.WithIconClassName), Is.False);
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None));

            button.Icon = "photo_camera";
            Assert.That(button.ClassListContains(MdButton.WithIconClassName));
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["photo_camera"]));

            button.Icon = "";
            Assert.That(button.ClassListContains(MdButton.WithIconClassName), Is.False);
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void IsFocusableTouchTarget()
        {
            var button = new MdButton();
            Assert.That(button.focusable, Is.True);
            Assert.That(button.pickingMode, Is.EqualTo(PickingMode.Position));
            // Children must not steal the pointer from the root target.
            Assert.That(button.Q<VisualElement>(className: MdButton.ContainerClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
