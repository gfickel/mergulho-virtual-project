using System;
using System.Collections.Generic;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Espécie — the species sub-screen that resolves **Decision D1**
    /// (DESIGN_IMPLEMENTATION.md §10): the Animais catalog loses its bottom-bar tab
    /// in V2, and its content — the description, the 3D turntable and the inline
    /// educational videos — moves here, reached from a species card rather than from
    /// a list. Pushed as <see cref="AppRoutes.Especie"/> and dismissed with the
    /// hero's back button, exactly like <see cref="PraiaDetalheScreen"/>.
    ///
    /// <para><b>⚠ THERE IS NO FIGMA FRAME FOR THIS SCREEN.</b> The V2 frame
    /// inventory (§1.1) has no species page — the designer never drew one, and D1
    /// settled only *where* the content lives, not what it looks like. Every number
    /// and every ordering decision below is therefore **designed, not transcribed**,
    /// and wants a designer's review. The rules it was designed under: reuse the
    /// components and idioms Praia detalhe already established (full-bleed hero,
    /// title row, white cards on the off-white page, title-medium section headings,
    /// muted credit text at the foot), invent no new visual pattern, and invent no
    /// content — every string comes from an <c>AnimalDef</c> or is a fixed label on
    /// <see cref="EspecieViewModel"/>.</para>
    ///
    /// <para><b>Every block is gated on presence</b>, the same discipline as Praia
    /// detalhe: a block whose data is missing is not built into the layout at all —
    /// no placeholder strings, no headings over nothing. Measured over the five
    /// shipped species: name, binomial, photo, description, photo credit, model and
    /// model credit are filled for <b>all five</b>; the three spec rows for
    /// <b>none</b> (Decision D8 — a blank beats an invented fact); videos for
    /// <b>one</b> (lemon_shark). So the honest page today is hero → name → description
    /// → 3D model → credits, with a video section on one species and a spec table on
    /// none.</para>
    ///
    /// <para><b>Two engine-side services are constructor arguments here</b>, and they
    /// are used differently. <see cref="ISpeciesModelViewer"/> (the scene's
    /// <c>AnimalViewerRig</c>) is this screen's alone — it has no presentation state,
    /// only a texture and four gesture calls. <see cref="IVideoPlayback"/> is shared
    /// with <see cref="EspecieViewModel"/>, which owns every decision about it (which
    /// card is playing, what its control says); the screen holds it for exactly one
    /// thing, the decoded frame, because a <c>UnityEngine.Texture</c> cannot pass
    /// through a ViewModel required to stay engine-free — the same reason the sprite
    /// loaders are constructor arguments on every other screen. Both are optional:
    /// unavailable (no rig, no player — the screenshot harness, for instance) hides
    /// the block rather than offering a control that cannot work.</para>
    ///
    /// <para><b>Teardown in OnExit is load-bearing.</b> The router keeps screens
    /// alive and only toggles <c>display</c>, so without an explicit
    /// <see cref="ISpeciesModelViewer.Hide"/> and <see cref="IVideoPlayback.Stop"/> a
    /// camera would go on rendering a shark and a video would go on streaming behind
    /// a hidden screen, for as long as the app is open. The legacy uGUI
    /// <c>AnimalsScreenController</c> did both in <c>OnDisable</c>; losing it here
    /// would be a battery and bandwidth regression, not a cosmetic one.</para>
    /// </summary>
    public sealed class EspecieScreen : VisualElement, IAppScreen
    {
        /// <summary>
        /// Playhead poll interval, ms. Fast enough that the clock and the track look
        /// live, slow enough not to lay out the tree 60 times a second for a label
        /// that changes once a second.
        /// </summary>
        const long ProgressPollMs = 250;

        /// <summary>
        /// How far back the pause control sits while a clip runs. Not zero: the media
        /// surface IS the pause target, and a target with nothing drawn on it is one
        /// the user has to guess at.
        /// </summary>
        const float PlayingOverlayOpacity = 0.8f;

        /// <summary>
        /// Set on the video overlay exactly while a decoded frame is on screen
        /// (playing or paused). The overlay's two backdrops — the light, empty media
        /// well and an arbitrary video frame — need opposite ink, and USS cannot tell
        /// them apart, so the screen says which one is underneath and
        /// <c>EspecieScreen.uss</c> owns every value. See the block above
        /// <c>.mv-especie__video-scrim</c> there.
        /// </summary>
        const string OverFrameClass = "mv-especie__video-overlay--over-frame";

        /// <summary>
        /// Metres per notch of mouse wheel — editor and desktop review build only; a
        /// phone pinches instead. Same value the uGUI <c>AnimalViewerInput</c> used.
        /// </summary>
        const float ScrollMetresPerNotch = 0.3f;

        /// <summary>
        /// Metres per pixel of pinch. Ported from <c>AnimalViewerInput</c>, where it
        /// was tuned against these same models at these same auto-framed distances.
        /// </summary>
        const float PinchMetresPerPixel = 0.005f;

        readonly EspecieViewModel vm;
        readonly ISpeciesModelViewer modelViewer;
        readonly IVideoPlayback videoPlayback;
        readonly Func<string, Sprite> loadSpeciesSprite;

        readonly VisualElement content;
        readonly ScrollView scroll;
        readonly MvHeroHeader hero;
        readonly VisualElement heroPlaceholder;

        /// <summary>MvHeroHeader's scrim, kept so this screen can keep it off — the
        /// component re-shows it on every <c>SetImage</c>. Null-safe: a component that
        /// ever stops shipping one just means there is nothing to suppress.</summary>
        readonly VisualElement heroScrim;
        readonly VisualElement body;

        readonly VisualElement titleRow;
        readonly Label titleLabel;
        readonly Label binomialLabel;

        readonly VisualElement aboutCard;
        readonly Label descriptionLabel;

        readonly VisualElement specsCard;

        readonly VisualElement modelSection;
        readonly VisualElement viewport;
        readonly Image modelImage;
        readonly VisualElement modelPlaceholder;

        readonly VisualElement videosSection;
        readonly VisualElement videoCards;

        readonly VisualElement credits;
        readonly Label photoCreditLabel;
        readonly Label modelCreditLabel;

        /// <summary>Live pointers over the viewport, by pointer id — one rotates, two pinch.</summary>
        readonly Dictionary<int, Vector2> viewportPointers = new Dictionary<int, Vector2>();
        float lastPinchDistance = -1f;

        readonly List<VideoCard> cards = new List<VideoCard>();

        IVisualElementScheduledItem progressTick;
        bool subscribed;
        int viewportWidthPx;
        int viewportHeightPx;

        /// <summary>The hero's back button was tapped — pop the back stack.</summary>
        public event Action BackRequested;

        public EspecieScreen(
            EspecieViewModel viewModel,
            ISpeciesModelViewer modelViewer,
            IVideoPlayback videoPlayback,
            Func<string, Sprite> speciesSpriteLoader)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            this.modelViewer = modelViewer;
            this.videoPlayback = videoPlayback;
            loadSpeciesSprite = speciesSpriteLoader;

            AddToClassList("mv-especie");
            pickingMode = PickingMode.Ignore;

            content = new VisualElement();
            content.AddToClassList("mv-especie__content");
            Add(content);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-especie__scroll");
            content.Add(scroll);

            // No selector pill: "which beach am I at" is meaningless on a species
            // page, and the hero's only control is the way back.
            // Compact (240dp) rather than Praia detalhe's 340dp, and it is the photos
            // that decide: the species images are Wikipedia lead shots at roughly 4:3
            // or 16:9, and MvHeroHeader cover-fits. Cropping one to 390x340 (1.15:1)
            // throws away ~40% of the width — i.e. the head or the tail of the animal
            // the reader came to identify. 390x240 (1.63:1) is close to the sources and
            // crops gently. It also lightens the scrim, which is right here: nothing is
            // written over this photo.
            hero = new MvHeroHeader { ShowBackButton = true, ShowSelector = false, Compact = true };
            hero.AddToClassList("mv-especie__hero");
            // MvHeroHeader shows its scrim whenever an image is set, which is right
            // on Praia detalhe — text and badges are written over that photo. Nothing
            // is written over THIS one, and its whole job is to show what the animal
            // looks like, so the veil (measured at x0.885 green / x0.893 blue against
            // the source JPEG) buys nothing and costs the identification. The back
            // button stays legible without it: it paints its own opaque `surface`
            // disc with an outline and a `primary` glyph.
            heroScrim = hero.Q<VisualElement>(className: MvHeroHeader.ScrimClassName);
            heroPlaceholder = MakeHeroPlaceholder();
            hero.Insert(0, heroPlaceholder); // behind photo, scrim and controls
            hero.BackClicked += () => BackRequested?.Invoke();
            scroll.Add(hero);

            body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("mv-especie__body");
            scroll.Add(body);

            body.Add(titleRow = BuildTitleRow(out titleLabel, out binomialLabel));
            body.Add(aboutCard = BuildAboutCard(out descriptionLabel));
            body.Add(specsCard = BuildSpecsCard());
            body.Add(modelSection = BuildModelSection(out viewport, out modelImage, out modelPlaceholder));
            body.Add(videosSection = BuildVideosSection(out videoCards));
            body.Add(credits = BuildCredits(out photoCreditLabel, out modelCreditLabel));

            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Especie;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += OnViewModelChanged;
                vm.VideoChanged += OnVideoChanged;
                if (modelViewer != null) modelViewer.TextureChanged += OnModelTextureChanged;
                subscribed = true;
            }
            Render();
            MountModel();
            scroll.scrollOffset = Vector2.zero;
        }

        /// <summary>
        /// Releases both engine-side resources before unsubscribing — see the class
        /// remarks. Unsubscribing first would be harmless here (neither Hide nor Stop
        /// re-enters Render through an event this screen needs) but the order matches
        /// <see cref="MergulhoScreen"/>'s, where it does matter.
        /// </summary>
        public void OnExit() => Teardown();

        /// <summary>
        /// Safe-area insets in panel units. The hero is full-bleed, so the TOP inset
        /// goes to <see cref="MvHeroHeader.TopInset"/> — which pads the controls plane
        /// only and leaves the photo running under the status bar — rather than to the
        /// content container, which would paint a blank strip above it. Identical to
        /// Praia detalhe; there is no floating button here, so nothing measures the
        /// bottom other than the navigation bar's own reservation.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            hero.TopInset = top;
            content.style.paddingLeft = left;
            content.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void Teardown()
        {
            StopProgressTick();
            UnmountModel();
            StopVideo();
            if (!subscribed) return;
            vm.Changed -= OnViewModelChanged;
            vm.VideoChanged -= OnVideoChanged;
            if (modelViewer != null) modelViewer.TextureChanged -= OnModelTextureChanged;
            subscribed = false;
        }

        /// <summary>
        /// The species changed underneath the screen. Both media blocks belong to the
        /// old animal, so they are torn down before the repaint and the new model is
        /// mounted after it — otherwise the turntable would keep the previous shark
        /// and the player would keep streaming a clip that is no longer on the page.
        /// </summary>
        void OnViewModelChanged()
        {
            UnmountModel();
            StopVideo();
            Render();
            MountModel();
        }

        // ---- Hero -----------------------------------------------------------

        /// <summary>
        /// Painted behind the photo so a species with no image gets a deliberate
        /// surface and a glyph rather than an empty frame. Never seen today — all five
        /// AnimalDefs carry a photo — but it is one blank field away from being the
        /// normal case, exactly as on Praia detalhe.
        /// </summary>
        static VisualElement MakeHeroPlaceholder()
        {
            var placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("mv-especie__hero-placeholder");
            var icon = new MdIcon { Icon = "waves" };
            icon.AddToClassList("mv-especie__hero-placeholder-icon");
            placeholder.Add(icon);
            return placeholder;
        }

        // ---- Title row ------------------------------------------------------

        /// <summary>
        /// Common name over binomial, in the shape of Praia detalhe's beach name over
        /// its region line. The binomial is italic here, which is the convention for a
        /// scientific name and what the AR card already does; the Praia detalhe
        /// species card sets it upright and is the odd one out.
        /// </summary>
        static VisualElement BuildTitleRow(out Label title, out Label binomial)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-especie__title-row");

            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-large");
            title.AddToClassList("mv-especie__title");
            row.Add(title);

            binomial = new Label { pickingMode = PickingMode.Ignore };
            binomial.AddToClassList("md-typescale-body-medium");
            binomial.AddToClassList("mv-especie__binomial");
            row.Add(binomial);
            return row;
        }

        // ---- Description ----------------------------------------------------

        static VisualElement BuildAboutCard(out Label description)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-especie__card");
            card.AddToClassList("mv-especie__about");

            description = new Label { pickingMode = PickingMode.Ignore };
            description.AddToClassList("md-typescale-body-large");
            description.AddToClassList("mv-especie__description");
            card.Add(description);
            return card;
        }

        // ---- Spec rows ------------------------------------------------------

        /// <summary>
        /// The AR card's three rows on a light card: label left, value right. No
        /// section heading — the row labels ARE the content, the same call Praia
        /// detalhe's stats card makes, and a heading would be a fourth invented
        /// string over a table that is empty on every species today.
        /// </summary>
        static VisualElement BuildSpecsCard()
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-especie__card");
            card.AddToClassList("mv-especie__specs");
            return card;
        }

        void RenderSpecs()
        {
            specsCard.Clear();
            var rows = vm.SpecRows;
            specsCard.style.display = rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("mv-especie__spec");
                if (i > 0) row.AddToClassList("mv-especie__spec--gutter"); // USS has no `gap`.

                var label = new Label(rows[i].Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-label-medium");
                label.AddToClassList("mv-especie__spec-label");
                row.Add(label);

                var value = new Label(rows[i].Value) { pickingMode = PickingMode.Ignore };
                value.AddToClassList("md-typescale-body-large");
                value.AddToClassList("mv-especie__spec-value");
                row.Add(value);

                specsCard.Add(row);
            }
        }

        // ---- 3D model -------------------------------------------------------

        /// <summary>
        /// The turntable. The rig renders into a RenderTexture that this viewport
        /// displays and drives: drag rotates, two fingers zoom.
        ///
        /// <para>The gesture hint under it is the one affordance this block cannot do
        /// without — a still frame of a shark says nothing about being spinnable, and
        /// the alternative (a gesture glyph floating over the photo) would be a new
        /// visual pattern in a design language that has none.</para>
        /// </summary>
        VisualElement BuildModelSection(out VisualElement view, out Image image, out VisualElement placeholder)
        {
            var section = MakeSection(EspecieViewModel.ModelSectionTitle);

            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-especie__card");
            card.AddToClassList("mv-especie__model-card");

            // Pickable (the default) — this is the gesture surface.
            view = new VisualElement();
            view.AddToClassList("mv-especie__viewport");

            // StretchToFill, not ScaleAndCrop: the render target is allocated AT the
            // viewport's own aspect, so there is nothing to letterbox or crop, and
            // any other mode would resample a pixel-exact image.
            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("mv-especie__model-image");
            view.Add(image);

            // Shown until the first frame arrives — the rig needs a render to happen
            // after the model is mounted, so there is a beat where the texture is
            // null and an unfilled box would read as a broken image.
            placeholder = new VisualElement { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("mv-especie__model-placeholder");
            var placeholderIcon = new MdIcon { Icon = "3d_rotation" };
            placeholderIcon.AddToClassList("mv-especie__model-placeholder-icon");
            placeholder.Add(placeholderIcon);
            view.Add(placeholder);

            view.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            view.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            view.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
            view.RegisterCallback<PointerCaptureOutEvent>(OnViewportPointerCaptureOut);
            view.RegisterCallback<WheelEvent>(OnViewportWheel);
            view.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            card.Add(view);

            var hint = new VisualElement { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("mv-especie__model-hint");
            var hintIcon = new MdIcon { Icon = "3d_rotation" };
            hintIcon.AddToClassList("mv-especie__model-hint-icon");
            hint.Add(hintIcon);
            var hintLabel = new Label(EspecieViewModel.ModelHint) { pickingMode = PickingMode.Ignore };
            hintLabel.AddToClassList("md-typescale-label-small");
            hintLabel.AddToClassList("mv-especie__model-hint-label");
            hint.Add(hintLabel);
            card.Add(hint);

            section.Add(card);
            return section;
        }

        bool ModelSectionVisible => vm.HasModel && modelViewer != null && modelViewer.IsAvailable;

        void MountModel()
        {
            if (!ModelSectionVisible) return;
            if (!modelViewer.Show(vm.SpeciesKey))
            {
                // The rig refused — a dangling prefab reference, the documented
                // fallout of replacing a species' FBX. Drop the section rather than
                // leave an empty black box on the page.
                modelSection.style.display = DisplayStyle.None;
                return;
            }
            PushViewportSize();
            ApplyModelTexture();
        }

        void UnmountModel()
        {
            viewportPointers.Clear();
            lastPinchDistance = -1f;
            // Hide() releases the rig's render target, so the size the adapter holds
            // is gone with it. Leaving the cache set made PushViewportSize early-return
            // on the NEXT visit — same dp, same scale — so no target was ever
            // recreated and the second open of a species showed the placeholder
            // instead of the model. The cache tracks the adapter's state, so it has to
            // be cleared with it.
            viewportWidthPx = 0;
            viewportHeightPx = 0;
            modelViewer?.Hide();
            modelImage.image = null;
            modelImage.style.display = DisplayStyle.None;
            modelPlaceholder.style.display = DisplayStyle.Flex;
        }

        void OnModelTextureChanged() => ApplyModelTexture();

        void ApplyModelTexture()
        {
            var texture = modelViewer?.Texture;
            modelImage.image = texture;
            modelImage.style.display = texture != null ? DisplayStyle.Flex : DisplayStyle.None;
            modelPlaceholder.style.display = texture != null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void OnViewportGeometryChanged(GeometryChangedEvent _) => PushViewportSize();

        /// <summary>
        /// Tells the rig how big to render, in DEVICE PIXELS — the viewport's dp size
        /// times the panel's scale, so the model is sharp on a 3× phone instead of
        /// being upsampled from 358 dp. The adapter ignores a size it already has, so
        /// calling this from every layout pass is free.
        /// </summary>
        void PushViewportSize()
        {
            if (modelViewer == null || !modelViewer.IsAvailable) return;

            float widthDp = viewport.resolvedStyle.width;
            float heightDp = viewport.resolvedStyle.height;
            if (float.IsNaN(widthDp) || float.IsNaN(heightDp) || widthDp <= 1f || heightDp <= 1f) return;

            float scale = viewport.panel != null ? viewport.panel.scaledPixelsPerPoint : 1f;
            if (float.IsNaN(scale) || scale <= 0f) scale = 1f;

            int w = Mathf.RoundToInt(widthDp * scale);
            int h = Mathf.RoundToInt(heightDp * scale);
            if (w == viewportWidthPx && h == viewportHeightPx) return;
            viewportWidthPx = w;
            viewportHeightPx = h;
            modelViewer.SetViewportSize(w, h);
        }

        // ---- Viewport gestures ----------------------------------------------
        //
        // Pointers are CAPTURED and the events are consumed, so a drag that starts on
        // the turntable rotates it and does not also scroll the page. That is the
        // usual contract for a 3D viewer and the same one the uGUI AnimalViewerInput
        // had inside its ScrollRect — the cost is that the viewport is not a place
        // you can flick the page from, which is why the block is ~260 dp of a page
        // over a thousand dp long.

        void OnViewportPointerDown(PointerDownEvent e)
        {
            if (!ModelSectionVisible) return;
            viewportPointers[e.pointerId] = e.position;
            if (viewportPointers.Count == 2) lastPinchDistance = CurrentPinchDistance();
            viewport.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnViewportPointerMove(PointerMoveEvent e)
        {
            if (!viewportPointers.ContainsKey(e.pointerId)) return;
            viewportPointers[e.pointerId] = e.position;

            if (viewportPointers.Count >= 2)
            {
                float distance = CurrentPinchDistance();
                if (lastPinchDistance > 0f)
                {
                    // Fingers apart = zoom in = camera closer = negative metres.
                    modelViewer?.Zoom(-(distance - lastPinchDistance) * PinchMetresPerPixel);
                }
                lastPinchDistance = distance;
            }
            else
            {
                modelViewer?.Rotate(e.deltaPosition.x);
            }
            e.StopPropagation();
        }

        void OnViewportPointerUp(PointerUpEvent e)
        {
            if (!viewportPointers.Remove(e.pointerId)) return;
            if (viewportPointers.Count < 2) lastPinchDistance = -1f;
            viewport.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        /// <summary>
        /// The capture went away without a PointerUp — the gesture was cancelled, or
        /// the panel took the pointer back. Without this the pointer stays in the map
        /// forever and the next single-finger drag is mistaken for a pinch.
        /// </summary>
        void OnViewportPointerCaptureOut(PointerCaptureOutEvent e)
        {
            if (!viewportPointers.Remove(e.pointerId)) return;
            if (viewportPointers.Count < 2) lastPinchDistance = -1f;
        }

        void OnViewportWheel(WheelEvent e)
        {
            if (!ModelSectionVisible) return;
            // Editor and the Windows review build only; a phone pinches. Scrolling
            // down (positive delta) pushes the camera away.
            modelViewer?.Zoom(e.delta.y * ScrollMetresPerNotch);
            e.StopPropagation();
        }

        float CurrentPinchDistance()
        {
            if (viewportPointers.Count < 2) return 0f;
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int seen = 0;
            foreach (var position in viewportPointers.Values)
            {
                if (seen == 0) a = position;
                else if (seen == 1) b = position;
                else break;
                seen++;
            }
            return seen < 2 ? 0f : Vector2.Distance(a, b);
        }

        // ---- Videos ---------------------------------------------------------

        /// <summary>One clip's card and the elements its state drives.</summary>
        sealed class VideoCard
        {
            public VisualElement Root;
            public VisualElement Media;
            public Image Frame;
            public VisualElement Overlay;
            public MdIcon ActionIcon;
            public Label ActionLabel;
            public MdCircularProgress Spinner;
            public Label Error;
            /// <summary>The 14 dp touch strip. The visible 3 dp rule is <see cref="Bar"/>,
            /// centred in it — a 3 dp seek target would be unhittable with a thumb.</summary>
            public VisualElement Track;
            public VisualElement Bar;
            public VisualElement Fill;
            public Label Title;
            public Label Time;
        }

        VisualElement BuildVideosSection(out VisualElement cardHost)
        {
            var section = MakeSection(EspecieViewModel.VideosSectionTitle);
            cardHost = new VisualElement { pickingMode = PickingMode.Ignore };
            cardHost.AddToClassList("mv-especie__videos");
            section.Add(cardHost);
            return section;
        }

        /// <summary>
        /// Cards are rebuilt rather than re-bound: the list changes only when the
        /// species changes, and rebuilding keeps the per-card click closures and their
        /// indices in one place — the same call <see cref="PraiaDetalheScreen"/> makes
        /// about its species chips.
        /// </summary>
        void RenderVideos()
        {
            bool visible = vm.ShowVideos;
            videosSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                videoCards.Clear();
                cards.Clear();
                return;
            }

            videoCards.Clear();
            cards.Clear();
            var items = vm.Videos;
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var card = BuildVideoCard(items[i]);
                card.Media.AddManipulator(new Clickable(() => ToggleVideo(index)));
                card.Track.RegisterCallback<PointerDownEvent>(e => OnTrackPointerDown(e, index));
                card.Track.RegisterCallback<PointerMoveEvent>(e => OnTrackPointerMove(e, index));
                card.Track.RegisterCallback<PointerUpEvent>(e => OnTrackPointerUp(e, index));
                if (i > 0) card.Root.AddToClassList("mv-especie__video-card--gutter"); // USS has no `gap`.
                cards.Add(card);
                videoCards.Add(card.Root);
            }
            RenderVideoState();
        }

        VideoCard BuildVideoCard(SpeciesVideoView item)
        {
            var card = new VideoCard();

            card.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Root.AddToClassList("mv-especie__card");
            card.Root.AddToClassList("mv-especie__video-card");

            // The whole poster is the play/pause target — the control bar the uGUI
            // card carried is replaced by the surface itself plus the overlay, which
            // is both a bigger target and one fewer row of chrome.
            card.Media = new VisualElement { focusable = true };
            card.Media.AddToClassList("mv-especie__video-media");

            card.Frame = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            card.Frame.AddToClassList("mv-especie__video-frame");
            card.Media.Add(card.Frame);

            card.Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Overlay.AddToClassList("mv-especie__video-overlay");

            // The scrim is a SIBLING behind the content, not `opacity` on the overlay
            // itself: UI Toolkit inherits opacity down the subtree, so dimming the
            // container dims the glyph and the label with it — which is exactly how
            // the first render came out, a 55% grey "Assistir" on a black poster.
            // It is transparent until the overlay wears OverFrameClass; no C# ever
            // touches it again, so it needs no handle on VideoCard.
            var scrim = new VisualElement { pickingMode = PickingMode.Ignore };
            scrim.AddToClassList("mv-especie__video-scrim");
            card.Overlay.Add(scrim);

            card.Spinner = new MdCircularProgress { Indeterminate = true };
            card.Spinner.AddToClassList("mv-especie__video-spinner");
            card.Overlay.Add(card.Spinner);

            card.ActionIcon = new MdIcon { Icon = "play_arrow" };
            card.ActionIcon.AddToClassList("mv-especie__video-action-icon");
            card.Overlay.Add(card.ActionIcon);

            card.ActionLabel = new Label { pickingMode = PickingMode.Ignore };
            card.ActionLabel.AddToClassList("md-typescale-label-large");
            card.ActionLabel.AddToClassList("mv-especie__video-action-label");
            card.Overlay.Add(card.ActionLabel);

            card.Media.Add(card.Overlay);
            card.Root.Add(card.Media);

            // Pickable: tap or drag anywhere along it to seek.
            card.Track = new VisualElement();
            card.Track.AddToClassList("mv-especie__video-track");
            card.Bar = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Bar.AddToClassList("mv-especie__video-bar");
            card.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Fill.AddToClassList("mv-especie__video-fill");
            card.Bar.Add(card.Fill);
            card.Track.Add(card.Bar);
            card.Root.Add(card.Track);

            var footer = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.AddToClassList("mv-especie__video-footer");

            card.Title = new Label(item.HasTitle ? item.Title : "") { pickingMode = PickingMode.Ignore };
            card.Title.AddToClassList("md-typescale-title-small");
            card.Title.AddToClassList("mv-especie__video-title");
            card.Title.style.display = item.HasTitle ? DisplayStyle.Flex : DisplayStyle.None;
            footer.Add(card.Title);

            card.Time = new Label { pickingMode = PickingMode.Ignore };
            card.Time.AddToClassList("md-typescale-label-small");
            card.Time.AddToClassList("mv-especie__video-time");
            footer.Add(card.Time);

            card.Root.Add(footer);

            card.Error = new Label(vm.VideoErrorText) { pickingMode = PickingMode.Ignore };
            card.Error.AddToClassList("md-typescale-body-medium");
            card.Error.AddToClassList("mv-especie__video-error");
            card.Root.Add(card.Error);

            return card;
        }

        /// <summary>
        /// Tap on the poster. What it MEANS — start, pause, resume, retry — is the
        /// ViewModel's decision; the screen's only addition is starting the playhead
        /// poll, which is a panel concern and has no business in a ViewModel.
        /// </summary>
        void ToggleVideo(int index)
        {
            vm.ToggleVideo(index);
            if (vm.PlayingVideoIndex >= 0) StartProgressTick();
            RenderVideoState();
        }

        void StopVideo()
        {
            vm.StopVideo();
            StopProgressTick();
            RenderVideoState();
        }

        /// <summary>
        /// The player moved on its own — prepared, failed, or the clip ran out (the
        /// adapter answers the end of a clip by unbinding). Nothing playing means
        /// nothing to poll, so the tick is parked; otherwise it would keep laying out
        /// two labels four times a second for the rest of the session.
        /// </summary>
        void OnVideoChanged()
        {
            if (vm.PlayingVideoIndex < 0) StopProgressTick();
            RenderVideoState();
        }

        // ---- Seeking ---------------------------------------------------------
        //
        // Scrubbing needs no "is the user dragging" flag and no preview state: the
        // position under the finger simply IS the playhead, so the tap and every move
        // of the drag run the identical seek. Capturing the pointer on the way down is
        // what makes the drag keep working past the ends of a 3 dp-tall track (and
        // stops the page scrolling under the finger); PointerUp releases it.

        void OnTrackPointerDown(PointerDownEvent e, int index)
        {
            if (!SeekFromTrack(index, e.localPosition.x)) return;
            cards[index].Track.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnTrackPointerMove(PointerMoveEvent e, int index)
        {
            if (!cards[index].Track.HasPointerCapture(e.pointerId)) return;
            if (!SeekFromTrack(index, e.localPosition.x)) return;
            e.StopPropagation();
        }

        void OnTrackPointerUp(PointerUpEvent e, int index)
        {
            var track = cards[index].Track;
            if (!track.HasPointerCapture(e.pointerId)) return;
            track.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        /// <summary>
        /// Moves the playhead to a horizontal position on the track. False when there
        /// is nothing to seek — a card that does not own the player, or a clip whose
        /// duration is not known yet — in which case the caller leaves the event alone.
        /// </summary>
        bool SeekFromTrack(int index, float localX)
        {
            if (index != vm.PlayingVideoIndex) return false;

            float width = cards[index].Track.contentRect.width;
            if (float.IsNaN(width) || width <= 0f) return false;

            vm.SeekVideo(index, Mathf.Clamp01(localX / width));
            RenderVideoProgress();
            return true;
        }

        void StartProgressTick()
        {
            if (progressTick != null)
            {
                progressTick.Resume();
                return;
            }
            progressTick = schedule.Execute(RenderVideoProgress).Every(ProgressPollMs);
        }

        void StopProgressTick() => progressTick?.Pause();

        /// <summary>
        /// The state half: which overlay, which glyph, which words, and whether the
        /// decoded frame is on screen. Cheap, and driven by events rather than the
        /// clock.
        /// </summary>
        void RenderVideoState()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                // Every card but the owner is Idle by definition — there is one
                // player, so there is one card that can be doing anything.
                var state = vm.VideoStateFor(i);

                bool loading = state == VideoPlaybackState.Loading;
                bool failed = state == VideoPlaybackState.Failed;
                bool playing = state == VideoPlaybackState.Playing;

                // The decoded frame is the ONE thing read off the service rather than
                // the ViewModel — it is a UnityEngine.Texture.
                var texture = i == vm.PlayingVideoIndex ? videoPlayback?.Texture : null;
                bool hasFrame = texture != null && (playing || state == VideoPlaybackState.Paused);
                card.Frame.image = hasFrame ? texture : null;
                card.Frame.style.display = hasFrame ? DisplayStyle.Flex : DisplayStyle.None;

                // Which backdrop the overlay is sitting on. It is the ONE thing USS
                // cannot work out for itself — a decoded frame is an inline `display`
                // written just above — and it flips the scrim on and the glyph and
                // its word from navy to white in one class. Every value is in
                // EspecieScreen.uss; this line only states the fact.
                card.Overlay.EnableInClassList(OverFrameClass, hasFrame);

                // The overlay stays up in EVERY state, playing included. Hiding it
                // while the clip ran left the card with a frame, a seek rule and a
                // clock and no control at all: the whole media surface pauses, but
                // nothing on screen said so, and SpeciesMediaFormatter's "Pausar" /
                // `pause` pair — the one it produces for exactly this state — was
                // never drawn. Prominence is dropped instead of the control, and one
                // lever does it: this opacity dims the scrim AND the glyph together,
                // so a running clip reads through a lighter veil behind a quieter
                // control, while a PAUSED frame keeps both at full. (Opacity on the
                // container is right here precisely because it is meant to take the
                // whole overlay down, unlike on the scrim itself.)
                card.Overlay.style.opacity = playing ? PlayingOverlayOpacity : 1f;
                card.Spinner.style.display = loading ? DisplayStyle.Flex : DisplayStyle.None;
                card.ActionIcon.style.display = loading ? DisplayStyle.None : DisplayStyle.Flex;
                card.ActionIcon.Icon = vm.VideoActionIconFor(i);
                card.ActionLabel.text = vm.VideoActionLabelFor(i);

                card.Error.style.display = failed ? DisplayStyle.Flex : DisplayStyle.None;

                // `visibility`, not `display`: the 14dp strip stays in the layout of
                // every card whether or not it is the one playing. Toggling `display`
                // made the track appear on play and vanish on stop, which reflowed
                // the title, the clock, the next card and the credits under the
                // finger that had just tapped — a 14dp jump at the exact moment the
                // user is looking somewhere else. A hidden element is not picked, so
                // dragging a track that is not the playing one still does nothing.
                card.Track.style.visibility =
                    i == vm.PlayingVideoIndex && !failed ? Visibility.Visible : Visibility.Hidden;
            }
            RenderVideoProgress();
        }

        /// <summary>The clock half, polled — see <see cref="ProgressPollMs"/>.</summary>
        void RenderVideoProgress()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                string clock = vm.VideoTimeTextFor(i);
                card.Time.text = clock ?? "";
                card.Time.style.display = clock != null ? DisplayStyle.Flex : DisplayStyle.None;
                card.Fill.style.width = new Length(vm.VideoProgressFor(i) * 100f, LengthUnit.Percent);
            }
        }

        // ---- Credits --------------------------------------------------------

        /// <summary>
        /// Photo and model attribution at the foot of the page, as muted label-small
        /// text with no card around it — the same treatment Praia detalhe gives the
        /// beach photo's credit, and light enough that two lines of Sketchfab licence
        /// text do not read as content.
        ///
        /// <para>No heading and no "Foto:"/"Modelo:" prefixes: the authored values are
        /// inconsistent about carrying their own (see
        /// <see cref="SpeciesMediaFormatter.Credit"/>), and a prefix would print
        /// "Foto: Foto: …" on some species.</para>
        /// </summary>
        static VisualElement BuildCredits(out Label photo, out Label model)
        {
            var block = new VisualElement { pickingMode = PickingMode.Ignore };
            block.AddToClassList("mv-especie__credits");

            photo = new Label { pickingMode = PickingMode.Ignore };
            photo.AddToClassList("md-typescale-label-small");
            photo.AddToClassList("mv-especie__credit");
            block.Add(photo);

            model = new Label { pickingMode = PickingMode.Ignore };
            model.AddToClassList("md-typescale-label-small");
            model.AddToClassList("mv-especie__credit");
            model.AddToClassList("mv-especie__credit--gutter"); // USS has no `gap`.
            block.Add(model);
            return block;
        }

        static VisualElement MakeSection(string title)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("mv-especie__section");
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("md-typescale-title-medium");
            heading.AddToClassList("mv-especie__section-title");
            section.Add(heading);
            return section;
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            // No species is only ever the state before the first push: the host sets
            // the ViewModel and only navigates if the key resolved, so this cannot be
            // reached by tapping anything. The hero keeps its back button so the
            // screen is never a dead end even so.
            bool has = vm.HasSpecies;
            body.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;

            var sprite = has ? loadSpeciesSprite?.Invoke(vm.ImageName) : null;
            hero.SetImage(sprite);
            // Re-suppressed after every SetImage, not once at build time: the
            // component keys the scrim off "has image" and turns it back on each
            // call. See the constructor for why this hero wants none.
            if (heroScrim != null) heroScrim.style.display = DisplayStyle.None;
            heroPlaceholder.style.display = sprite != null ? DisplayStyle.None : DisplayStyle.Flex;

            if (!has) return;

            titleLabel.text = vm.TitleText ?? "";
            binomialLabel.text = vm.HasBinomial ? vm.BinomialText : "";
            binomialLabel.style.display = vm.HasBinomial ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasDescription = vm.HasDescription;
            aboutCard.style.display = hasDescription ? DisplayStyle.Flex : DisplayStyle.None;
            descriptionLabel.text = hasDescription ? vm.DescriptionText : "";

            RenderSpecs();

            modelSection.style.display = ModelSectionVisible ? DisplayStyle.Flex : DisplayStyle.None;

            RenderVideos();

            bool hasCredits = vm.HasCredits;
            credits.style.display = hasCredits ? DisplayStyle.Flex : DisplayStyle.None;
            photoCreditLabel.text = vm.HasPhotoCredit ? vm.PhotoCreditText : "";
            photoCreditLabel.style.display = vm.HasPhotoCredit ? DisplayStyle.Flex : DisplayStyle.None;
            modelCreditLabel.text = vm.HasModelCredit ? vm.ModelCreditText : "";
            modelCreditLabel.style.display = vm.HasModelCredit ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
