using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// One card in an <see cref="MvMediaCarousel"/>: a photo, a title and a
    /// credit line. Carries either a <see cref="Sprite"/> or a
    /// <see cref="Texture2D"/> (a downloaded photo arrives as a texture, a
    /// bundled one as a sprite) — whichever is non-null is shown.
    /// </summary>
    public sealed class MvMediaItem
    {
        public readonly string Title;
        public readonly string Caption;
        public readonly Sprite Sprite;
        public readonly Texture2D Texture;

        /// <summary>A card with no photo (or one that has not loaded yet).</summary>
        public MvMediaItem(string title, string caption)
        {
            Title = title ?? "";
            Caption = caption ?? "";
        }

        public MvMediaItem(string title, string caption, Sprite sprite)
            : this(title, caption)
        {
            Sprite = sprite;
        }

        public MvMediaItem(string title, string caption, Texture2D texture)
            : this(title, caption)
        {
            Texture = texture;
        }
    }

    /// <summary>
    /// A horizontally scrolling strip of photo cards — "Galeria de avistamentos"
    /// (§8.3). Items come from code via <see cref="SetItems"/> (UXML cannot
    /// express a list); a tap raises <see cref="ItemClicked"/> with the index.
    /// <para>
    /// <b>The stock scrollbar is turned off in C#, not USS, and that is not a
    /// style choice.</b> <see cref="ScrollView"/> writes its scrollers' `display`
    /// as an INLINE style on every layout pass — that is how
    /// <see cref="ScrollerVisibility.Auto"/> appears and disappears — and inline
    /// styles beat stylesheets, so `display: none` on `.unity-scroller` in
    /// MvMediaCarousel.uss would simply be overwritten.
    /// <see cref="ScrollerVisibility.Hidden"/> is the supported way to say
    /// "no scrollbar, still scrollable".
    /// </para>
    /// <para>
    /// The app layer has the same fix as a reusable <c>AppScrollView</c> subclass
    /// in <c>Assets/UI/Controls/</c>. This component cannot use it: the
    /// DesignSystem asmdef must not reference the app assembly (the graph only
    /// flows the other way), so the two-line setting is duplicated here on
    /// purpose. If a third caller ever appears inside the design system, promote
    /// it to a DS-side <c>MdScrollView</c> and let the app subclass THAT.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvMediaCarousel : VisualElement
    {
        public const string UssClassName = "mv-media-carousel";
        public const string ScrollClassName = "mv-media-carousel__scroll";
        public const string TrackClassName = "mv-media-carousel__track";
        public const string CardClassName = "mv-media-carousel__card";
        public const string FirstCardClassName = "mv-media-carousel__card--first";
        public const string MediaClassName = "mv-media-carousel__media";
        public const string ImageClassName = "mv-media-carousel__image";
        public const string CaptionClassName = "mv-media-carousel__caption";
        public const string TitleClassName = "mv-media-carousel__title";
        public const string CreditClassName = "mv-media-carousel__credit";

        readonly ScrollView _scroll;

        /// <summary>Raised with the index of the tapped card.</summary>
        public event Action<int> ItemClicked;

        public int ItemCount => _scroll.contentContainer.childCount;

        public MvMediaCarousel()
        {
            AddToClassList(UssClassName);

            _scroll = new ScrollView(ScrollViewMode.Horizontal);
            // See the class remarks: this MUST be the C# property, USS cannot do it.
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.AddToClassList(ScrollClassName);
            _scroll.contentContainer.AddToClassList(TrackClassName);
            Add(_scroll);
        }

        /// <summary>
        /// Replaces the strip. Null or empty clears it and hides the whole
        /// element, so a screen can leave the carousel in the tree for a beach
        /// with no photos and it contributes no height.
        /// </summary>
        public void SetItems(IReadOnlyList<MvMediaItem> items)
        {
            _scroll.contentContainer.Clear();
            int count = items?.Count ?? 0;
            for (int i = 0; i < count; i++)
                _scroll.contentContainer.Add(BuildCard(items[i], i));
            style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement BuildCard(MvMediaItem item, int index)
        {
            var card = new VisualElement { name = $"card-{index}", focusable = true };
            card.AddToClassList(CardClassName);
            // Same no-`gap`/no-structural-pseudo-class workaround as MvNumberedList:
            // margin on every card, zeroed on the first, so the strip has no
            // leading indent and no trailing dead space.
            if (index == 0)
                card.AddToClassList(FirstCardClassName);

            // First child, per the state-layer contract — which means the ripple
            // reads on the caption area and behind (not over) the photo, exactly
            // as MdListItem's does behind its leading image.
            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");

            var media = new VisualElement { name = "media", pickingMode = PickingMode.Ignore };
            media.AddToClassList(MediaClassName);

            var image = new Image { name = "image", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleAndCrop };
            image.AddToClassList(ImageClassName);
            if (item.Texture != null)
                image.image = item.Texture;
            else
                image.sprite = item.Sprite;
            media.Add(image);

            var caption = new VisualElement { name = "caption", pickingMode = PickingMode.Ignore };
            caption.AddToClassList(CaptionClassName);

            var title = new Label(item.Title) { name = "title", pickingMode = PickingMode.Ignore };
            title.AddToClassList(TitleClassName);

            var credit = new Label(item.Caption) { name = "credit", pickingMode = PickingMode.Ignore };
            credit.AddToClassList(CreditClassName);
            credit.style.display = item.Caption.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            caption.Add(title);
            caption.Add(credit);

            card.Add(stateLayer);
            card.Add(media);
            card.Add(caption);
            card.AddManipulator(new Clickable(() => ItemClicked?.Invoke(index)));
            return card;
        }
    }
}
