using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvOptionCardTests
    {
        [Test]
        public void Defaults_AreUnselectedFocusableAndIconless()
        {
            var card = new MvOptionCard();
            Assert.That(card.ClassListContains(MvOptionCard.UssClassName));
            Assert.That(card.Selected, Is.False);
            Assert.That(card.ClassListContains(MvOptionCard.SelectedClassName), Is.False);
            Assert.That(card.focusable, Is.True);

            var icon = card.Q<MdIcon>(className: MvOptionCard.IconClassName);
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None),
                "an empty icon collapses its slot instead of leaving a 32dp hole");
        }

        [Test]
        public void StateLayer_IsTheRootsFirstChild()
        {
            // The card IS the visible surface (93dp), so there is no __container
            // level — the state layer sits directly on the root, as in
            // MvMediaCarousel's cards.
            var card = new MvOptionCard();
            Assert.That(card[0].ClassListContains("md-state-layer"));
        }

        [Test]
        public void IconAndText_ReachTheirElements()
        {
            var card = new MvOptionCard { Icon = "person", Text = "Turista / Visitante" };
            var icon = card.Q<MdIcon>(className: MvOptionCard.IconClassName);
            var label = card.Q<Label>(className: MvOptionCard.LabelClassName);
            Assert.That(icon.Icon, Is.EqualTo("person"));
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(label.text, Is.EqualTo("Turista / Visitante"));

            card.Text = null;
            Assert.That(card.Text, Is.EqualTo(""));
        }

        [Test]
        public void Selected_TogglesExactlyOneClassAndRaisesNothing()
        {
            // Selection is written BY the caller, so writing it must not call the
            // caller back — that is the whole point of Clicked being the only event.
            var card = new MvOptionCard();
            int clicked = 0;
            card.Clicked += () => clicked++;

            card.Selected = true;
            Assert.That(card.ClassListContains(MvOptionCard.SelectedClassName));
            card.Selected = false;
            Assert.That(card.ClassListContains(MvOptionCard.SelectedClassName), Is.False);
            Assert.That(clicked, Is.Zero);
        }

        [Test]
        public void IconAndSelected_AreIndependent()
        {
            // UXML applies attributes in unspecified order, so neither setter may
            // depend on the other having run.
            var card = new MvOptionCard { Selected = true, Icon = "explore", Text = "Condutor / Guia" };
            Assert.That(card.ClassListContains(MvOptionCard.SelectedClassName));
            Assert.That(card.Q<MdIcon>(className: MvOptionCard.IconClassName).Icon, Is.EqualTo("explore"));
        }

        [Test]
        public void ThereIsNoImplicitGroup_SelectingOneCardDoesNotTouchAnother()
        {
            // The group is the caller's (see the class docs). Two cards that are
            // siblings in the same row must still be fully independent, or a
            // ViewModel could not seed "both off" or "both on" for a screen that
            // wants multi-select. Exclusivity enforced by the caller is tested in
            // PlayMode, where there is a pointer to click with.
            var parent = new VisualElement();
            var tourist = new MvOptionCard { Icon = "person", Text = "Turista" };
            var guide = new MvOptionCard { Icon = "explore", Text = "Condutor" };
            parent.Add(tourist);
            parent.Add(guide);

            tourist.Selected = true;
            Assert.That(guide.Selected, Is.False);
            guide.Selected = true;
            Assert.That(tourist.Selected, Is.True, "no card deselects a sibling on its own");
        }
    }
}
