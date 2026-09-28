using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdCardTests
    {
        [Test]
        public void DefaultsToElevated()
        {
            var card = new MdCard();
            Assert.That(card.Variant, Is.EqualTo(MdCardVariant.Elevated));
            Assert.That(card.ClassListContains("md-card--elevated"));
        }

        [Test]
        public void Variant_TogglesVariantClass()
        {
            var card = new MdCard();
            card.Variant = MdCardVariant.Outlined;
            Assert.That(card.ClassListContains("md-card--outlined"));
            Assert.That(card.ClassListContains("md-card--elevated"), Is.False);

            card.Variant = MdCardVariant.Filled;
            Assert.That(card.ClassListContains("md-card--filled"));
            Assert.That(card.ClassListContains("md-card--outlined"), Is.False);
        }

        [Test]
        public void Children_SlotDirectlyIntoTheCard()
        {
            var card = new MdCard();
            var child = new Label("content");
            card.Add(child);
            Assert.That(child.parent, Is.EqualTo(card));
            Assert.That(card.childCount, Is.EqualTo(1));
        }
    }
}
