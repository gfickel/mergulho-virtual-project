using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvHeroHeaderTests
    {
        static VisualElement TopRow(MvHeroHeader hero) =>
            hero.Q<VisualElement>(className: MvHeroHeader.TopRowClassName);

        static VisualElement Badges(MvHeroHeader hero) =>
            hero.Q<VisualElement>(className: MvHeroHeader.BadgesClassName);

        static MdIconButton Back(MvHeroHeader hero) =>
            hero.Q<MdIconButton>(className: MvHeroHeader.BackClassName);

        static Image Photo(MvHeroHeader hero) =>
            hero.Q<Image>(className: MvHeroHeader.ImageClassName);

        static VisualElement Scrim(MvHeroHeader hero) =>
            hero.Q<VisualElement>(className: MvHeroHeader.ScrimClassName);

        static Texture2D NewTexture() => new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };

        [Test]
        public void BareHero_HasNoOverlaysAndNoScrim()
        {
            // The default is "just a photo slot": nothing shown, nothing taking space.
            var hero = new MvHeroHeader();
            Assert.That(hero.ClassListContains(MvHeroHeader.UssClassName));
            Assert.That(hero.Compact, Is.False);
            Assert.That(hero.ShowBackButton, Is.False);
            Assert.That(hero.ShowSelector, Is.False);
            Assert.That(TopRow(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Badges(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Photo(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Scrim(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void BackButton_AndSelector_AreIndependentlyOptional()
        {
            var hero = new MvHeroHeader();

            hero.ShowBackButton = true;
            Assert.That(TopRow(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Back(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(hero.Selector.style.display.value, Is.EqualTo(DisplayStyle.None));

            hero.ShowSelector = true;
            Assert.That(hero.Selector.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            hero.ShowBackButton = false;
            Assert.That(Back(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(TopRow(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex),
                "the row stays while the selector is on");

            hero.ShowSelector = false;
            Assert.That(TopRow(hero).style.display.value, Is.EqualTo(DisplayStyle.None),
                "with neither control the row must eat no space");
        }

        [Test]
        public void Scrim_FollowsTheImage()
        {
            // The AR HUD uses the controls over a live camera: no image, so no
            // scrim — that is what keeps the overlay usable without a fourth flag.
            var hero = new MvHeroHeader();
            var texture = NewTexture();
            try
            {
                hero.SetImage(texture);
                Assert.That(Photo(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(Scrim(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex));

                hero.SetImage((Texture2D)null);
                Assert.That(Photo(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(Scrim(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SetImage_SpriteAndTexture_AreMutuallyExclusive()
        {
            var hero = new MvHeroHeader();
            var texture = NewTexture();
            try
            {
                hero.SetImage(texture);
                Assert.That(Photo(hero).image, Is.EqualTo(texture));
                Assert.That(Photo(hero).sprite, Is.Null);

                hero.SetImage((Sprite)null);
                Assert.That(Photo(hero).image, Is.Null, "setting a sprite clears the texture");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SetBadges_BuildsOnImageTagsAndMarksTheFirst()
        {
            var hero = new MvHeroHeader();
            hero.SetBadges(new[] { "Mar de fora", "Área do parque" });

            Assert.That(hero.BadgeCount, Is.EqualTo(2));
            Assert.That(Badges(hero).style.display.value, Is.EqualTo(DisplayStyle.Flex));

            var tags = hero.Query<MvTag>(className: MvHeroHeader.BadgeClassName).ToList();
            Assert.That(tags.Count, Is.EqualTo(2));
            Assert.That(tags.All(t => t.Variant == MvTagVariant.OnImage));
            Assert.That(tags[0].Text, Is.EqualTo("Mar de fora"));
            Assert.That(tags[0].ClassListContains(MvHeroHeader.FirstBadgeClassName));
            Assert.That(tags[1].ClassListContains(MvHeroHeader.FirstBadgeClassName), Is.False);
        }

        [Test]
        public void SetBadges_NullOrEmpty_HidesTheRow()
        {
            var hero = new MvHeroHeader();
            hero.SetBadges(new[] { "a" });
            hero.SetBadges(null);
            Assert.That(hero.BadgeCount, Is.Zero);
            Assert.That(Badges(hero).style.display.value, Is.EqualTo(DisplayStyle.None));

            hero.SetBadges(new string[0]);
            Assert.That(Badges(hero).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Compact_TogglesTheModifierClass()
        {
            var hero = new MvHeroHeader { Compact = true };
            Assert.That(hero.ClassListContains(MvHeroHeader.CompactClassName));
            hero.Compact = false;
            Assert.That(hero.ClassListContains(MvHeroHeader.CompactClassName), Is.False);
        }

        [Test]
        public void TopInset_PadsTheForegroundOnly_SoThePhotoStaysFullBleed()
        {
            var hero = new MvHeroHeader { TopInset = 44 };
            var foreground = hero.Q<VisualElement>(className: MvHeroHeader.ForegroundClassName);
            Assert.That(hero.TopInset, Is.EqualTo(44));
            Assert.That(foreground.style.paddingTop.value.value, Is.EqualTo(44));
            Assert.That(Photo(hero).style.paddingTop.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(hero.style.paddingTop.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Selector_IsTheTouchTargetRootAndCarriesAStateLayer()
        {
            var hero = new MvHeroHeader { ShowSelector = true, SelectorText = "Baía do Sueste" };
            Assert.That(hero.Selector.ClassListContains(MvHeroHeader.SelectorClassName));
            Assert.That(hero.Selector.focusable, Is.True);

            var container = hero.Q<VisualElement>(className: MvHeroHeader.SelectorContainerClassName);
            Assert.That(container, Is.Not.Null);
            Assert.That(container.IndexOf(container.Q<VisualElement>(className: "md-state-layer")), Is.EqualTo(0),
                "the state layer must be the container's first child");

            var label = hero.Q<Label>(className: MvHeroHeader.SelectorLabelClassName);
            Assert.That(label.text, Is.EqualTo("Baía do Sueste"));
        }

        [Test]
        public void SelectorIcon_EmptyHidesTheSlot()
        {
            var hero = new MvHeroHeader();
            var icon = hero.Q<MdIcon>(className: MvHeroHeader.SelectorIconClassName);
            Assert.That(icon.text, Is.EqualTo(MdIconGlyphs.Map[MvHeroHeader.DefaultSelectorIconName]));

            hero.SelectorIcon = "";
            Assert.That(icon.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void DefaultGlyphs_AreInTheShippedSubset()
        {
            Assert.That(MdIconGlyphs.Map.ContainsKey(MvHeroHeader.DefaultBackIconName));
            Assert.That(MdIconGlyphs.Map.ContainsKey(MvHeroHeader.DefaultSelectorIconName));
            Assert.That(MdIconGlyphs.Map.ContainsKey(MvHeroHeader.SelectorChevronIconName));
        }
    }
}
