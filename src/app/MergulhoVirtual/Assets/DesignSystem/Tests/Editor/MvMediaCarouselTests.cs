using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvMediaCarouselTests
    {
        static MvMediaItem[] ThreeItems() => new[]
        {
            new MvMediaItem("Tubarão-limão", "Foto: Bianca Rangel"),
            new MvMediaItem("Tartaruga-verde", "Foto: Marcos Lima"),
            new MvMediaItem("Raia Pintada", ""),
        };

        static System.Collections.Generic.List<VisualElement> Cards(MvMediaCarousel carousel) =>
            carousel.Query<VisualElement>(className: MvMediaCarousel.CardClassName).ToList();

        [Test]
        public void ScrollersAreHiddenFromCode()
        {
            // The regression this component exists to not have: ScrollView writes
            // scroller `display` as an INLINE style every layout pass, so USS can
            // never hide it — only ScrollerVisibility.Hidden can.
            var carousel = new MvMediaCarousel();
            var scroll = carousel.Q<ScrollView>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.mode, Is.EqualTo(ScrollViewMode.Horizontal));
            Assert.That(scroll.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
            Assert.That(scroll.verticalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
            Assert.That(scroll.contentContainer.ClassListContains(MvMediaCarousel.TrackClassName));
        }

        [Test]
        public void SetItems_BuildsOneCardPerItem()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());

            Assert.That(carousel.ItemCount, Is.EqualTo(3));
            var cards = Cards(carousel);
            Assert.That(cards.Count, Is.EqualTo(3));

            var titles = carousel.Query<Label>(className: MvMediaCarousel.TitleClassName).ToList()
                .Select(l => l.text).ToArray();
            Assert.That(titles, Is.EqualTo(new[] { "Tubarão-limão", "Tartaruga-verde", "Raia Pintada" }));
        }

        [Test]
        public void FirstCard_IsTheOnlyOneMarkedFirst()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());

            var cards = Cards(carousel);
            Assert.That(cards[0].ClassListContains(MvMediaCarousel.FirstCardClassName));
            Assert.That(cards[1].ClassListContains(MvMediaCarousel.FirstCardClassName), Is.False);
            Assert.That(cards[2].ClassListContains(MvMediaCarousel.FirstCardClassName), Is.False);
        }

        [Test]
        public void EmptyCaption_HidesTheCreditLine()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());

            var credits = carousel.Query<Label>(className: MvMediaCarousel.CreditClassName).ToList();
            Assert.That(credits.Count, Is.EqualTo(3));
            Assert.That(credits[0].style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(credits[2].style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void SetItems_NullOrEmpty_ClearsAndHidesTheStrip()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());
            Assert.That(carousel.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            carousel.SetItems(null);
            Assert.That(carousel.ItemCount, Is.Zero);
            Assert.That(carousel.style.display.value, Is.EqualTo(DisplayStyle.None));

            carousel.SetItems(new MvMediaItem[0]);
            Assert.That(carousel.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void SetItems_ReplacesRatherThanAppends()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());
            carousel.SetItems(new[] { new MvMediaItem("Barracuda", "Foto: —") });

            Assert.That(carousel.ItemCount, Is.EqualTo(1));
            Assert.That(Cards(carousel)[0].ClassListContains(MvMediaCarousel.FirstCardClassName),
                "the surviving card must be renumbered as the first");
        }

        [Test]
        public void Card_IsInteractive_WithAFirstChildStateLayer()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(ThreeItems());

            var card = Cards(carousel)[0];
            Assert.That(card.focusable, Is.True);
            Assert.That(card.IndexOf(card.Q<VisualElement>(className: "md-state-layer")), Is.EqualTo(0));
            Assert.That(card.Q<VisualElement>(className: MvMediaCarousel.MediaClassName), Is.Not.Null);
            Assert.That(card.Q<VisualElement>(className: MvMediaCarousel.CaptionClassName), Is.Not.Null);
        }

        [Test]
        public void MediaItem_CarriesEitherASpriteOrATexture()
        {
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var carousel = new MvMediaCarousel();
                carousel.SetItems(new[]
                {
                    new MvMediaItem("Com textura", "", texture),
                    new MvMediaItem("Sem imagem", ""),
                });

                var images = carousel.Query<Image>(className: MvMediaCarousel.ImageClassName).ToList();
                Assert.That(images[0].image, Is.EqualTo(texture));
                Assert.That(images[1].image, Is.Null);
                Assert.That(images[1].sprite, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void MediaItem_NullStrings_BecomeEmpty()
        {
            var item = new MvMediaItem(null, null);
            Assert.That(item.Title, Is.EqualTo(""));
            Assert.That(item.Caption, Is.EqualTo(""));
        }
    }
}
