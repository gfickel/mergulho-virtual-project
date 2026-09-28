using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdIconTests
    {
        [Test]
        public void KnownIcon_SetsGlyphText()
        {
            var icon = new MdIcon { Icon = "waves" };
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map["waves"]));
            Assert.That(icon.Icon, Is.EqualTo("waves"));
        }

        [Test]
        public void UnknownIcon_RendersNothingAndWarns()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown icon 'nope_not_real'"));
            var icon = new MdIcon { Icon = "nope_not_real" };
            Assert.That(icon.text, Is.EqualTo(""));
        }

        [Test]
        public void EmptyIcon_RendersNothing()
        {
            var icon = new MdIcon { Icon = "" };
            Assert.That(icon.text, Is.EqualTo(""));
            icon.Icon = null;
            Assert.That(icon.Icon, Is.EqualTo(""));
        }

        [Test]
        public void GlyphMap_IsNonEmptyAndGlyphsAreSingleCodepoints()
        {
            Assert.That(MdIconGlyphs.Map.Count, Is.GreaterThan(50));
            foreach (var pair in MdIconGlyphs.Map)
            {
                Assert.That(char.ConvertToUtf32(pair.Value, 0), Is.GreaterThan(0xE000),
                    $"{pair.Key} should map into the icon PUA range");
            }
        }

        [Test]
        public void IsDecorative_ByDefault()
        {
            Assert.That(new MdIcon().pickingMode, Is.EqualTo(PickingMode.Ignore));
        }
    }
}
