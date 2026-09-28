using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdListItemTests
    {
        [Test]
        public void Hierarchy_StateLayerFirstThenLeadingContentTrailing()
        {
            var item = new MdListItem();
            Assert.That(item.IndexOf(item.Q<VisualElement>(className: "md-state-layer")), Is.EqualTo(0),
                "state layer renders under everything");
            Assert.That(item.Q<VisualElement>(className: MdListItem.ContentClassName), Is.Not.Null);
            Assert.That(item.Q<Label>(className: MdListItem.HeadlineClassName), Is.Not.Null);
            Assert.That(item.Q<Label>(className: MdListItem.SupportingClassName), Is.Not.Null);
        }

        [Test]
        public void Lines_DerivedFromSupportingTextAndThreeLineFlag()
        {
            var item = new MdListItem { Headline = "Sancho" };
            var supporting = item.Q<Label>(className: MdListItem.SupportingClassName);
            Assert.That(item.ClassListContains(MdListItem.OneLineClassName));
            Assert.That(supporting.style.display.value, Is.EqualTo(DisplayStyle.None));

            item.SupportingText = "Mar de dentro";
            Assert.That(item.ClassListContains(MdListItem.TwoLineClassName));
            Assert.That(item.ClassListContains(MdListItem.OneLineClassName), Is.False);
            Assert.That(supporting.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            item.ThreeLine = true;
            Assert.That(item.ClassListContains(MdListItem.ThreeLineClassName));
            Assert.That(item.ClassListContains(MdListItem.TwoLineClassName), Is.False);

            // Three-line only means anything with supporting text present.
            item.SupportingText = "";
            Assert.That(item.ClassListContains(MdListItem.OneLineClassName));
            Assert.That(item.ClassListContains(MdListItem.ThreeLineClassName), Is.False);
        }

        [Test]
        public void Leading_ImageWinsOverIcon()
        {
            var item = new MdListItem { LeadingIcon = "waves" };
            var icon = item.Q<MdIcon>(className: MdListItem.LeadingIconClassName);
            var imageBox = item.Q<VisualElement>(className: MdListItem.LeadingImageClassName);
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(imageBox.style.display.value, Is.EqualTo(DisplayStyle.None));

            var texture = new Texture2D(1, 1);
            item.SetLeadingImage(texture);
            Assert.That(imageBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None), "image wins over icon");

            item.SetLeadingImage((Texture2D)null);
            Assert.That(imageBox.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex), "icon returns when image cleared");
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void TrailingIcon_TogglesDisplay()
        {
            var item = new MdListItem();
            var trailing = item.Q<MdIcon>(className: MdListItem.TrailingIconClassName);
            Assert.That(trailing.style.display.value, Is.EqualTo(DisplayStyle.None));

            item.TrailingIcon = "chevron_right";
            Assert.That(trailing.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(trailing.text, Is.EqualTo(MdIconGlyphs.Map["chevron_right"]));

            item.TrailingIcon = "";
            Assert.That(trailing.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Headline_RoundTrips()
        {
            var item = new MdListItem { Headline = "Praia do Sancho" };
            Assert.That(item.Headline, Is.EqualTo("Praia do Sancho"));
            item.Headline = null;
            Assert.That(item.Headline, Is.EqualTo(""));
        }

        [Test]
        public void IsFocusableTouchTarget()
        {
            var item = new MdListItem();
            Assert.That(item.focusable, Is.True);
            Assert.That(item.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(item.Q<VisualElement>(className: MdListItem.ContentClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
