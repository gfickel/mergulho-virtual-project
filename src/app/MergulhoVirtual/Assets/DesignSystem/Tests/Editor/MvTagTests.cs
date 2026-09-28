using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvTagTests
    {
        [Test]
        public void Defaults_AreNeutralMedium()
        {
            var tag = new MvTag();
            Assert.That(tag.Variant, Is.EqualTo(MvTagVariant.Neutral));
            Assert.That(tag.Size, Is.EqualTo(MvTagSize.Medium));
            Assert.That(tag.ClassListContains(MvTag.UssClassName));
            Assert.That(tag.ClassListContains("mv-tag--neutral"));
            Assert.That(tag.ClassListContains("mv-tag--medium"));
        }

        [Test]
        public void Variant_SwapsExactlyOneVariantClass()
        {
            var tag = new MvTag();
            tag.Variant = MvTagVariant.Success;
            Assert.That(tag.ClassListContains("mv-tag--success"));
            Assert.That(tag.ClassListContains("mv-tag--neutral"), Is.False);

            tag.Variant = MvTagVariant.OnImage;
            Assert.That(tag.ClassListContains("mv-tag--on-image"));
            Assert.That(tag.ClassListContains("mv-tag--success"), Is.False);
        }

        [Test]
        public void Size_SwapsExactlyOneSizeClass()
        {
            var tag = new MvTag();
            tag.Size = MvTagSize.Small;
            Assert.That(tag.ClassListContains("mv-tag--small"));
            Assert.That(tag.ClassListContains("mv-tag--medium"), Is.False);
        }

        [Test]
        public void VariantAndSize_AreIndependent()
        {
            // UXML applies attributes in unspecified order, so neither setter may
            // depend on the other having run.
            var tag = new MvTag { Size = MvTagSize.Small, Variant = MvTagVariant.OnImage };
            Assert.That(tag.ClassListContains("mv-tag--small"));
            Assert.That(tag.ClassListContains("mv-tag--on-image"));
        }

        [Test]
        public void Text_GoesToTheLabel_AndNullBecomesEmpty()
        {
            var tag = new MvTag { Text = "Risco: Baixo" };
            var label = tag.Q<Label>(className: MvTag.LabelClassName);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Is.EqualTo("Risco: Baixo"));

            tag.Text = null;
            Assert.That(tag.Text, Is.EqualTo(""));
        }

        [Test]
        public void FillLayer_IsASeparateChildUnderTheLabel()
        {
            // The whole point of the __surface child: `on-image` fades the fill
            // without fading its own text, which an rgba() root fill could not do
            // (and a literal rgba() is banned by TokenDisciplineTests anyway).
            var tag = new MvTag { Variant = MvTagVariant.OnImage, Text = "Mar de fora" };
            Assert.That(tag.childCount, Is.EqualTo(2));
            Assert.That(tag[0].ClassListContains(MvTag.SurfaceClassName), "surface paints under the label");
            Assert.That(tag[1].ClassListContains(MvTag.LabelClassName));
        }

        [Test]
        public void IsNotInteractive()
        {
            // A tag is a read-only badge — that is why it is not an MdChip variant.
            var tag = new MvTag { Text = "Pendente" };
            Assert.That(tag.focusable, Is.False);
            Assert.That(tag.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(tag.Q(className: "md-state-layer"), Is.Null, "tags carry no state layer");
        }
    }
}
