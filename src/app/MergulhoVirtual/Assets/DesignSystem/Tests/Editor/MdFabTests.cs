using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdFabTests
    {
        [Test]
        public void Hierarchy_HasContainerStateLayerAndIcon()
        {
            var fab = new MdFab();
            var container = fab.Q<VisualElement>(className: MdFab.ContainerClassName);
            Assert.That(container, Is.Not.Null);
            Assert.That(container.Q<VisualElement>(className: "md-state-layer"), Is.Not.Null);
            Assert.That(container.Q<MdIcon>(), Is.Not.Null);
            // State layer must be first so content renders above it.
            Assert.That(container.IndexOf(container.Q<VisualElement>(className: "md-state-layer")), Is.EqualTo(0));
        }

        [Test]
        public void Defaults_AreRegularPrimary()
        {
            var fab = new MdFab();
            Assert.That(fab.Size, Is.EqualTo(MdFabSize.Regular));
            Assert.That(fab.Color, Is.EqualTo(MdFabColor.Primary));
            Assert.That(fab.ClassListContains("md-fab--regular"));
            Assert.That(fab.ClassListContains("md-fab--primary"));
        }

        [Test]
        public void Size_TogglesSizeClass()
        {
            var fab = new MdFab();
            fab.Size = MdFabSize.Large;
            Assert.That(fab.ClassListContains("md-fab--large"));
            Assert.That(fab.ClassListContains("md-fab--regular"), Is.False);

            fab.Size = MdFabSize.Small;
            Assert.That(fab.ClassListContains("md-fab--small"));
            Assert.That(fab.ClassListContains("md-fab--large"), Is.False);
        }

        [Test]
        public void Color_TogglesColorClassIndependentlyOfSize()
        {
            var fab = new MdFab { Size = MdFabSize.Small };
            fab.Color = MdFabColor.Tertiary;
            Assert.That(fab.ClassListContains("md-fab--tertiary"));
            Assert.That(fab.ClassListContains("md-fab--primary"), Is.False);
            Assert.That(fab.ClassListContains("md-fab--small"), "size class untouched by color change");
        }

        [Test]
        public void Icon_RoutesToMdIcon()
        {
            var fab = new MdFab { Icon = "photo_camera" };
            Assert.That(fab.Icon, Is.EqualTo("photo_camera"));
            Assert.That(fab.Q<MdIcon>().text, Is.EqualTo(MdIconGlyphs.Map["photo_camera"]));
        }

        [Test]
        public void IsFocusableTouchTarget()
        {
            var fab = new MdFab();
            Assert.That(fab.focusable, Is.True);
            Assert.That(fab.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(fab.Q<VisualElement>(className: MdFab.ContainerClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }
    }
}
