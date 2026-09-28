using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvMediaPickerTests
    {
        Texture2D[] _textures;

        [SetUp]
        public void SetUp() => _textures = Enumerable.Range(0, 5)
            .Select(_ => new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave })
            .ToArray();

        [TearDown]
        public void TearDown()
        {
            foreach (var texture in _textures)
                Object.DestroyImmediate(texture);
        }

        MvMediaPickerItem[] Items(int count) =>
            _textures.Take(count).Select(t => new MvMediaPickerItem(t)).ToArray();

        static VisualElement Empty(MvMediaPicker picker) =>
            picker.Q<VisualElement>(className: MvMediaPicker.EmptyClassName);

        static VisualElement Card(MvMediaPicker picker) =>
            picker.Q<VisualElement>(className: MvMediaPicker.CardClassName);

        static VisualElement Add(MvMediaPicker picker) =>
            picker.Q<VisualElement>(className: MvMediaPicker.AddClassName);

        static int ThumbCount(MvMediaPicker picker) =>
            picker.Query<VisualElement>(className: MvMediaPicker.ThumbClassName).ToList().Count;

        [Test]
        public void Defaults_AreEmptyStateAndUnlimited()
        {
            var picker = new MvMediaPicker();
            Assert.That(picker.ClassListContains(MvMediaPicker.UssClassName));
            Assert.That(picker.ItemCount, Is.Zero);
            Assert.That(picker.MaxItems, Is.Zero, "0 = unlimited");
            Assert.That(picker.AtCapacity, Is.False);
            Assert.That(Empty(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Card(picker).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void EmptyState_IsOneTappableDashedBox()
        {
            var picker = new MvMediaPicker();
            var empty = Empty(picker);
            Assert.That(empty.focusable, Is.True);
            Assert.That(empty[0].ClassListContains("md-state-layer"));
            Assert.That(empty.Q<VisualElement>(className: MvMediaPicker.DashClassName), Is.Not.Null,
                "USS cannot express a dashed border — it is painted by a layer");
            Assert.That(empty.Q<MdIcon>(className: MvMediaPicker.EmptyIconClassName).Icon, Is.EqualTo("add"));
        }

        [Test]
        public void SetItems_SwapsToTheGridAndBack()
        {
            var picker = new MvMediaPicker();
            picker.SetItems(Items(2));
            Assert.That(picker.ItemCount, Is.EqualTo(2));
            Assert.That(ThumbCount(picker), Is.EqualTo(2));
            Assert.That(Empty(picker).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Card(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));

            picker.SetItems(null);
            Assert.That(picker.ItemCount, Is.Zero);
            Assert.That(ThumbCount(picker), Is.Zero);
            Assert.That(Empty(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void SetItems_CopiesTheListAndSkipsNulls()
        {
            var picker = new MvMediaPicker();
            var source = new System.Collections.Generic.List<MvMediaPickerItem>(Items(2)) { null };
            picker.SetItems(source);
            Assert.That(picker.ItemCount, Is.EqualTo(2));

            source.Clear();
            Assert.That(picker.ItemCount, Is.EqualTo(2), "the caller may keep mutating its own list");
        }

        [Test]
        public void AddTile_IsTheGridsLastChildAndIsShownUnderTheCap()
        {
            var picker = new MvMediaPicker { MaxItems = 3 };
            picker.SetItems(Items(2));
            var grid = picker.Q<VisualElement>(className: MvMediaPicker.GridClassName);
            Assert.That(grid.childCount, Is.EqualTo(3), "2 thumbs + the add tile");
            Assert.That(grid[grid.childCount - 1], Is.EqualTo(Add(picker)));
            Assert.That(picker.AtCapacity, Is.False);
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void AtCapacity_HidesTheAddTile_AndCapOfOneIsTheSlice3Case()
        {
            var picker = new MvMediaPicker { MaxItems = 1 };
            Assert.That(picker.AtCapacity, Is.False, "empty is never at capacity");

            picker.SetItems(Items(1));
            Assert.That(picker.AtCapacity, Is.True);
            Assert.That(ThumbCount(picker), Is.EqualTo(1));
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.None),
                "at cap the user is offered no way to add a second photo");

            // Removing the only photo returns to the dashed call to action.
            picker.SetItems(null);
            Assert.That(picker.AtCapacity, Is.False);
            Assert.That(Empty(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void MaxItems_ReevaluatesCapacityWhenChanged()
        {
            var picker = new MvMediaPicker();
            picker.SetItems(Items(2));
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));

            picker.MaxItems = 2;
            Assert.That(picker.AtCapacity, Is.True);
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.None));

            picker.MaxItems = 0; // back to unlimited
            Assert.That(picker.AtCapacity, Is.False);
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void MaxItems_ClampsNegativesToUnlimited()
        {
            var picker = new MvMediaPicker { MaxItems = -3 };
            Assert.That(picker.MaxItems, Is.Zero);
        }

        [Test]
        public void OverCap_RendersEverythingAndKeepsTheAddTileHidden()
        {
            // The component never drops items: the caller owns the list, and a
            // silent drop would desync it from the ViewModel.
            // (SetItems also logs an editor warning here; warnings do not fail
            // Unity tests, and the warning is the point — see the class docs.)
            var picker = new MvMediaPicker { MaxItems = 2 };
            picker.SetItems(Items(4));

            Assert.That(picker.ItemCount, Is.EqualTo(4));
            Assert.That(ThumbCount(picker), Is.EqualTo(4));
            Assert.That(picker.AtCapacity, Is.True);
            Assert.That(Add(picker).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void EachTile_CarriesItsOwnRemoveButtonWithATranslucentFillUnderTheGlyph()
        {
            var picker = new MvMediaPicker();
            picker.SetItems(Items(2));
            var removes = picker.Query<VisualElement>(className: MvMediaPicker.RemoveClassName).ToList();
            Assert.That(removes.Count, Is.EqualTo(2));
            foreach (var remove in removes)
            {
                Assert.That(remove.focusable, Is.True);
                // Fill first, then the state layer, then the glyph: the #000@25%
                // disc is its own layer so `opacity` never fades the ✕ (MvTag's fix).
                Assert.That(remove[0].ClassListContains(MvMediaPicker.RemoveFillClassName));
                Assert.That(remove[1].ClassListContains("md-state-layer"));
                Assert.That(remove.Q<MdIcon>(className: MvMediaPicker.RemoveIconClassName).Icon,
                    Is.EqualTo("close"));
            }
        }

        [Test]
        public void HintText_ShowsInBothStates_AndEmptyTitleIsSettable()
        {
            var picker = new MvMediaPicker { HintText = "Até 20MB", EmptyTitle = "Escolher foto" };
            var hints = picker.Query<Label>(className: MvMediaPicker.HintClassName).ToList();
            Assert.That(hints.Count, Is.EqualTo(2), "one under the call to action, one under the grid");
            Assert.That(hints.All(h => h.text == "Até 20MB"));
            Assert.That(picker.Q<Label>(className: MvMediaPicker.EmptyTitleClassName).text,
                Is.EqualTo("Escolher foto"));
            Assert.That(picker.HintText, Is.EqualTo("Até 20MB"));
        }

        [Test]
        public void Defaults_CarryTheV2Copy()
        {
            var picker = new MvMediaPicker();
            Assert.That(picker.EmptyTitle, Is.EqualTo("Selecione arquivos do dispositivo"));
            Assert.That(picker.HintText, Is.EqualTo("Limite de tamanho: 20MB"));
        }
    }
}
