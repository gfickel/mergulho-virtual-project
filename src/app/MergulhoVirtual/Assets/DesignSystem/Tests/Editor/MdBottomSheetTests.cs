using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdBottomSheetTests
    {
        [Test]
        public void Hierarchy_HasHandleAndContentSlot()
        {
            var sheet = new MdBottomSheet();
            Assert.That(sheet.ClassListContains(MdBottomSheet.UssClassName));
            Assert.That(sheet.Handle.ClassListContains(MdBottomSheet.HandleClassName));
            Assert.That(sheet.Handle.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "handle is a visual affordance only (drag deferred)");

            var content = sheet.Q<VisualElement>(className: MdBottomSheet.ContentClassName);
            Assert.That(content, Is.Not.Null);
            // The handle sits above the content slot.
            Assert.That(sheet.hierarchy.IndexOf(sheet.Handle),
                Is.LessThan(sheet.hierarchy.IndexOf(content)));
        }

        [Test]
        public void Add_RoutesChildrenIntoContentSlot()
        {
            var sheet = new MdBottomSheet();
            var child = new Label("Tubarão-martelo");
            sheet.Add(child);

            var content = sheet.Q<VisualElement>(className: MdBottomSheet.ContentClassName);
            Assert.That(content.Contains(child), "Add() lands in the content slot");
            Assert.That(child.hierarchy.parent, Is.EqualTo(content));
            Assert.That(sheet.contentContainer, Is.EqualTo(content));
        }

        [Test]
        public void ShowHandle_TogglesDisplay()
        {
            var sheet = new MdBottomSheet();
            Assert.That(sheet.ShowHandle, Is.True, "handle shown by default");

            sheet.ShowHandle = false;
            Assert.That(sheet.Handle.style.display.value, Is.EqualTo(DisplayStyle.None));

            sheet.ShowHandle = true;
            Assert.That(sheet.Handle.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Close_RaisesClosedExactlyOnce()
        {
            var sheet = new MdBottomSheet();
            int closed = 0;
            sheet.Closed += () => closed++;
            sheet.Close();
            sheet.Close();
            Assert.That(closed, Is.EqualTo(1));
        }
    }
}
