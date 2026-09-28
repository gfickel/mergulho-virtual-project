using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvNumberedListTests
    {
        static string[] Numbers(MvNumberedList list) =>
            list.Query<Label>(className: MvNumberedList.NumberClassName).ToList()
                .Select(l => l.text).ToArray();

        static string[] Texts(MvNumberedList list) =>
            list.Query<Label>(className: MvNumberedList.TextClassName).ToList()
                .Select(l => l.text).ToArray();

        [Test]
        public void SetItems_BuildsOneNumberedRowPerItem()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "Mantenha distância.", "Evite movimentos bruscos.", "Respeite a sinalização." });

            Assert.That(list.ItemCount, Is.EqualTo(3));
            Assert.That(list.childCount, Is.EqualTo(3));
            Assert.That(Numbers(list), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(Texts(list), Is.EqualTo(new[]
                { "Mantenha distância.", "Evite movimentos bruscos.", "Respeite a sinalização." }));
        }

        [Test]
        public void FirstRow_IsTheOnlyOneMarkedFirst()
        {
            // USS has no structural pseudo-classes, so the row gap is a margin on
            // every row plus a modifier that zeroes it on row 0.
            var list = new MvNumberedList();
            list.SetItems(new[] { "a", "b", "c" });

            var items = list.Query<VisualElement>(className: MvNumberedList.ItemClassName).ToList();
            Assert.That(items.Count, Is.EqualTo(3));
            Assert.That(items[0].ClassListContains(MvNumberedList.FirstItemClassName));
            Assert.That(items[1].ClassListContains(MvNumberedList.FirstItemClassName), Is.False);
            Assert.That(items[2].ClassListContains(MvNumberedList.FirstItemClassName), Is.False);
        }

        [Test]
        public void StartNumber_RenumbersExistingRows()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "a", "b" });
            list.StartNumber = 8;

            Assert.That(list.StartNumber, Is.EqualTo(8));
            Assert.That(Numbers(list), Is.EqualTo(new[] { "8", "9" }));
            Assert.That(Texts(list), Is.EqualTo(new[] { "a", "b" }), "renumbering must not lose the text");
        }

        [Test]
        public void SetItems_ReplacesRatherThanAppends()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "a", "b", "c" });
            list.SetItems(new[] { "x" });

            Assert.That(list.ItemCount, Is.EqualTo(1));
            Assert.That(Texts(list), Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void SetItems_NullOrEmpty_ClearsEveryRow()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "a", "b" });

            list.SetItems(null);
            Assert.That(list.ItemCount, Is.Zero);
            Assert.That(list.childCount, Is.Zero, "an empty list must contribute no height");

            list.SetItems(new string[0]);
            Assert.That(list.childCount, Is.Zero);
        }

        [Test]
        public void MarkerHoldsTheNumber_AndTextIsItsSibling()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "uma dica" });

            var item = list[0];
            Assert.That(item.childCount, Is.EqualTo(2));
            var marker = item[0];
            Assert.That(marker.ClassListContains(MvNumberedList.MarkerClassName));
            Assert.That(marker.childCount, Is.EqualTo(1));
            Assert.That(marker[0].ClassListContains(MvNumberedList.NumberClassName));
            Assert.That(item[1].ClassListContains(MvNumberedList.TextClassName));
        }

        [Test]
        public void IsNotInteractive()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "a" });
            Assert.That(list.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(list.Q(className: "md-state-layer"), Is.Null);
        }

        [Test]
        public void ItemTextAt_ReadsBackWhatWasSet()
        {
            var list = new MvNumberedList();
            list.SetItems(new[] { "primeira", null, "terceira" });
            Assert.That(list.ItemTextAt(0), Is.EqualTo("primeira"));
            Assert.That(list.ItemTextAt(1), Is.EqualTo(""), "null items become empty strings");
            Assert.That(list.ItemTextAt(2), Is.EqualTo("terceira"));
        }
    }
}
