using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>One educational clip as the screen draws it: a caption that may be
    /// absent, and the URL that is never rendered.</summary>
    public sealed class SpeciesVideoView
    {
        /// <summary>Caption, or null when the clip was authored without one.</summary>
        public readonly string Title;

        /// <summary>Stream URL — handed to <see cref="IVideoPlayback.Play"/>, never shown.</summary>
        public readonly string Url;

        public SpeciesVideoView(string title, string url)
        {
            Title = title;
            Url = url;
        }

        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    }

    /// <summary>
    /// State + presentation logic for one species — the Espécie sub-screen
    /// (Decision D1). Plain C#, no UnityEngine, same contract as every other
    /// ViewModel here.
    ///
    /// <para><b>There is no Figma frame for this screen.</b> The V2 frame inventory
    /// (DESIGN_IMPLEMENTATION.md §1.1) has none — the designer never drew it, and
    /// Decision D1 only says *where* the species content goes ("a sub-screen from
    /// the species cards"), not what it looks like. So the copy below and the block
    /// order in <see cref="EspecieScreen"/> are designed against the established
    /// language of the other screens rather than transcribed, and want a designer's
    /// eyes. Everything that is not a fixed label comes from an
    /// <c>AnimalDef</c>.</para>
    ///
    /// <para><b>It owns which species is open, and the video player's presentation
    /// state.</b> The two media services are treated deliberately differently, and
    /// the difference is whether they have presentation state at all:</para>
    /// <list type="bullet">
    /// <item><see cref="IVideoPlayback"/> <b>is here</b>: which card owns the shared
    /// player, and what its control says and shows, are exactly the kind of derived
    /// presentation state every other ViewModel in this layer holds — and putting it
    /// here is also what lets a test and the screenshot harness drive a clip into
    /// playing without a panel or a decoder. The screen still reads the decoded
    /// <c>Texture</c> off the same instance, because a <c>UnityEngine.Texture</c>
    /// cannot pass through a ViewModel required to stay engine-free.</item>
    /// <item><see cref="ISpeciesModelViewer"/> <b>is not</b>: it has no state to
    /// present. Its whole surface is a texture and four gesture calls, so routing it
    /// through here would add a layer of pass-through methods and no testable logic.
    /// The screen holds it, and this type contributes only whether the block should
    /// exist at all (<see cref="HasModel"/>, from the data).</item>
    /// </list>
    ///
    /// <para><b>The screen is entered with a payload, and this is where it lands.</b>
    /// The species key travels from whichever screen raised it to
    /// <c>AppUiHost</c>, which calls <see cref="ShowSpecies"/> and only then pushes
    /// the route — so by the time <c>OnEnter</c> runs the state is already correct.
    /// An unknown key returns false and changes nothing, and the host does not
    /// navigate, which is why the screen never has to render a "species not found"
    /// state.</para>
    /// </summary>
    public sealed class EspecieViewModel : IDisposable
    {
        // ---- Copy ------------------------------------------------------------
        // Fixed labels live here as consts, the convention BeachDetailViewModel and
        // PraiasViewModel set; the data-dependent strings all go through
        // SpeciesCardFormatter (identity + the three spec rows, shared verbatim with
        // the AR card) and SpeciesMediaFormatter (credits + the player).

        /// <summary>
        /// Heading over the turntable. <b>Invented copy</b> — there is no frame to
        /// transcribe it from. Factual rather than promotional on purpose: it names
        /// what the block is, so it reads correctly next to "Vídeos" below it.
        /// </summary>
        public const string ModelSectionTitle = "Modelo 3D";

        /// <summary>
        /// The one-line gesture hint under the viewport. <b>Invented copy.</b> A 3D
        /// turntable in a scrolling page has no affordance of its own — nothing about
        /// a still image says it can be spun — and the alternative (an overlaid
        /// gesture glyph) would be a new visual pattern. Kept to one line and
        /// jargon-free: "pinça" reads as tooling, "dois dedos" does not.
        /// </summary>
        public const string ModelHint = "Arraste para girar · dois dedos para aproximar";

        /// <summary>
        /// Heading over the clips. <b>Not invented</b> — this is the existing
        /// project copy, the same word the uGUI <c>VideoSectionBuilder</c> puts over
        /// the same clips on the legacy Animais screen.
        /// </summary>
        public const string VideosSectionTitle = "Vídeos";

        readonly ISpeciesCatalog catalog;
        readonly IVideoPlayback playback;
        IReadOnlyList<SpeciesSpecRow> specRows = Array.Empty<SpeciesSpecRow>();
        IReadOnlyList<SpeciesVideoView> videos = Array.Empty<SpeciesVideoView>();
        bool disposed;

        /// <summary>
        /// Raised when the open species changed (including to none). <b>Not</b> raised
        /// for playback — that is <see cref="VideoChanged"/>, because the screen answers
        /// this one by tearing down and remounting the 3D model, which a video state
        /// change must not do.
        /// </summary>
        public event Action Changed;

        /// <summary>Raised when the shared player's state, playhead or owning card changed.</summary>
        public event Action VideoChanged;

        /// <summary>The species being shown, or null before the first
        /// <see cref="ShowSpecies"/>.</summary>
        public SpeciesInfo Species { get; private set; }

        public EspecieViewModel(ISpeciesCatalog catalog, IVideoPlayback playback = null)
        {
            this.catalog = catalog;
            this.playback = playback;
            if (this.playback != null) this.playback.Changed += OnPlaybackChanged;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (playback == null) return;
            playback.Changed -= OnPlaybackChanged;
            playback.Stop();
        }

        // ---- Selection -------------------------------------------------------

        /// <summary>
        /// Opens a species by catalog key (the AnimalDef asset file name — the same
        /// key <c>beaches_content.json</c> and the AR hit source use).
        ///
        /// <para>Returns false, having changed nothing, for an empty or unknown key.
        /// That is the caller's cue not to navigate: an unknown key means a typo in
        /// the content file or a prefab that was never catalogued, and a blank
        /// species page is worse than staying put. Re-opening the species already
        /// shown is a no-op that still returns true.</para>
        /// </summary>
        public bool ShowSpecies(string key)
        {
            var found = string.IsNullOrEmpty(key) ? null : catalog?.Find(key);
            if (found == null) return false;
            if (ReferenceEquals(found, Species)) return true;

            // The clips on screen are about to be a different animal's.
            StopVideo();

            Species = found;
            // Built once per selection, not once per render: the screen reads both
            // lists and their counts, and re-entering the route repaints.
            specRows = SpeciesCardFormatter.SpecRows(found);
            videos = BuildVideos(found);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Closes the species. Idempotent; exists so a host can reset the
        /// screen rather than leaving a stale animal behind it.</summary>
        public void Clear()
        {
            if (Species == null) return;
            StopVideo();
            Species = null;
            specRows = Array.Empty<SpeciesSpecRow>();
            videos = Array.Empty<SpeciesVideoView>();
            Changed?.Invoke();
        }

        /// <summary>
        /// Clips with a URL, in authoring order. A clip with no URL is dropped here
        /// rather than rendered as a card that cannot play — the same rule the
        /// catalog adapter applies on the way in, repeated because this list is also
        /// reachable from a hand-built <see cref="SpeciesInfo"/> in a test.
        /// </summary>
        static IReadOnlyList<SpeciesVideoView> BuildVideos(SpeciesInfo species)
        {
            if (species == null || !species.HasVideos) return Array.Empty<SpeciesVideoView>();
            var list = new List<SpeciesVideoView>(species.Videos.Count);
            foreach (var video in species.Videos)
            {
                if (video == null || !video.HasUrl) continue;
                list.Add(new SpeciesVideoView(SpeciesMediaFormatter.VideoTitle(video.Title), video.Url));
            }
            return list;
        }

        // ---- Identity (shared verbatim with the AR species card) -------------

        public bool HasSpecies => Species != null;

        /// <summary>The catalog key, for <see cref="ISpeciesModelViewer.Show"/>. Not a label.</summary>
        public string SpeciesKey => Species?.Key;

        public string TitleText => SpeciesCardFormatter.Title(Species);

        /// <summary>Null hides the line — the identification is still open (reef_shark).</summary>
        public string BinomialText => SpeciesCardFormatter.Binomial(Species);

        public bool HasBinomial => BinomialText != null;

        /// <summary>Sprite file name under Resources/Animals, resolved by the screen's loader.</summary>
        public string ImageName => Species != null && Species.HasImage ? Species.ImageName : null;

        public bool HasImage => ImageName != null;

        // ---- Spec rows -------------------------------------------------------

        /// <summary>
        /// Size / diet / behaviour, minus the rows with no value — the AR card's
        /// three rows, through the AR card's formatter, so the two can never print
        /// the same facts differently.
        ///
        /// <para><b>Empty for all five shipped species</b> (Decision D8): the fields
        /// exist on AnimalDef and nobody has authored them, and a blank beats a
        /// guess. So this block is absent in practice and the page is currently
        /// hero → name → description → 3D model → videos → credits.</para>
        /// </summary>
        public IReadOnlyList<SpeciesSpecRow> SpecRows => specRows;

        public bool HasSpecRows => specRows.Count > 0;

        // ---- Description -----------------------------------------------------

        /// <summary>Verbatim from AnimalDef; null hides the card. Filled on all five species.</summary>
        public string DescriptionText => SpeciesMediaFormatter.Description(Species?.Description);

        public bool HasDescription => DescriptionText != null;

        // ---- 3D model --------------------------------------------------------

        /// <summary>
        /// The species has a model to show. This is the DATA half only — whether a
        /// rig exists to show it in is <see cref="ISpeciesModelViewer.IsAvailable"/>,
        /// which the screen ands in, because there is no rig in the screenshot
        /// harness and none in a scene the builder has not run over.
        /// </summary>
        public bool HasModel => Species != null && Species.HasModel;

        // ---- Videos ----------------------------------------------------------

        /// <summary>
        /// Playable clips. <b>Empty for four of the five species</b> — only
        /// lemon_shark has any — so an absent video section is the common case.
        /// </summary>
        public IReadOnlyList<SpeciesVideoView> Videos => videos;

        public bool HasVideos => videos.Count > 0;

        /// <summary>
        /// There is a player to play them with. False in the screenshot harness and in
        /// any host that built none, and the screen then hides the whole section rather
        /// than offering a play button that cannot play.
        /// </summary>
        public bool HasVideoPlayer => playback != null && playback.IsAvailable;

        /// <summary>The section is worth drawing: clips AND something to play them with.</summary>
        public bool ShowVideos => HasVideos && HasVideoPlayer;

        /// <summary>
        /// Which card owns the shared player, or -1.
        ///
        /// <para><b>An index, not a URL comparison</b>, and that is not incidental: two
        /// clips on one species may legitimately carry the same URL — lemon_shark's two
        /// entries do today — and matching on the URL would light both cards up at
        /// once. This ViewModel is the player's only caller, so its index is the
        /// authority.</para>
        /// </summary>
        public int PlayingVideoIndex { get; private set; } = -1;

        /// <summary>
        /// What the tap on a card's poster means, given what the player is doing:
        /// start, pause, resume, or retry after a failure. A tap on a card that does
        /// not own the player always starts it, implicitly stopping whatever was
        /// playing — there is one player, so there is one clip.
        /// </summary>
        public void ToggleVideo(int index)
        {
            if (playback == null || index < 0 || index >= videos.Count) return;
            string url = videos[index].Url;

            if (PlayingVideoIndex == index)
            {
                switch (playback.State)
                {
                    case VideoPlaybackState.Playing:
                        playback.Pause();
                        return;
                    case VideoPlaybackState.Loading:
                        // Already buffering; a second tap must not restart the stream.
                        return;
                    default:
                        playback.Play(url); // resume, or retry after a failure
                        return;
                }
            }

            PlayingVideoIndex = index;
            playback.Play(url);
            VideoChanged?.Invoke();
        }

        /// <summary>Unbinds the player and releases its stream. Idempotent.</summary>
        public void StopVideo()
        {
            bool wasPlaying = PlayingVideoIndex >= 0;
            PlayingVideoIndex = -1;
            playback?.Stop();
            if (wasPlaying) VideoChanged?.Invoke();
        }

        /// <summary>
        /// Scrub to a fraction of the clip, 0–1. Ignored for a card that does not own
        /// the player, or before the duration is known — there is nothing to seek
        /// within yet.
        /// </summary>
        public void SeekVideo(int index, float fraction)
        {
            if (playback == null || index != PlayingVideoIndex) return;
            double duration = playback.DurationSeconds;
            if (!(duration > 0d)) return;
            if (fraction < 0f) fraction = 0f;
            else if (fraction > 1f) fraction = 1f;
            playback.Seek(fraction * duration);
        }

        /// <summary>
        /// What card <paramref name="index"/> is doing. Every card but the owner is
        /// <see cref="VideoPlaybackState.Idle"/> by definition.
        /// </summary>
        public VideoPlaybackState VideoStateFor(int index) =>
            playback != null && index == PlayingVideoIndex ? playback.State : VideoPlaybackState.Idle;

        public string VideoActionLabelFor(int index) =>
            SpeciesMediaFormatter.ActionLabel(VideoStateFor(index));

        public string VideoActionIconFor(int index) =>
            SpeciesMediaFormatter.ActionIcon(VideoStateFor(index));

        /// <summary>"0:42 / 2:15", or null while the duration is unknown — see
        /// <see cref="SpeciesMediaFormatter.PlaybackTime"/>.</summary>
        public string VideoTimeTextFor(int index) =>
            index == PlayingVideoIndex && playback != null
                ? SpeciesMediaFormatter.PlaybackTime(playback.PositionSeconds, playback.DurationSeconds)
                : null;

        /// <summary>Fraction played, 0–1, for the seek track.</summary>
        public float VideoProgressFor(int index) =>
            index == PlayingVideoIndex && playback != null
                ? SpeciesMediaFormatter.Progress(playback.PositionSeconds, playback.DurationSeconds)
                : 0f;

        /// <summary>The one fixed pt-BR line a failed stream shows. Constant, so the
        /// screen need not know which of the states carries an error.</summary>
        public string VideoErrorText => SpeciesMediaFormatter.ErrorText;

        /// <summary>
        /// A state change from the player itself — prepared, failed, or the clip ran
        /// out (the adapter answers the end of a clip by unbinding, which is what
        /// returns the card to its poster).
        /// </summary>
        void OnPlaybackChanged()
        {
            if (playback != null && playback.State == VideoPlaybackState.Idle) PlayingVideoIndex = -1;
            VideoChanged?.Invoke();
        }

        // ---- Credits ---------------------------------------------------------

        /// <summary>
        /// Attribution for the hero photo, verbatim. A <b>licence condition</b> on
        /// every shipped image (CC-BY-SA or public domain), not decoration — so it
        /// rides at the foot of the page wherever the photo is shown, exactly as
        /// Praia detalhe does with the beach photo's credit.
        /// </summary>
        public string PhotoCreditText => SpeciesMediaFormatter.Credit(Species?.PhotoCredit);

        public bool HasPhotoCredit => PhotoCreditText != null;

        /// <summary>Attribution for the 3D model, verbatim. Same licence reasoning; often
        /// several lines of Sketchfab/Meshy text, so the screen lets it wrap.</summary>
        public string ModelCreditText => SpeciesMediaFormatter.Credit(Species?.ModelCredit);

        public bool HasModelCredit => ModelCreditText != null;

        public bool HasCredits => HasPhotoCredit || HasModelCredit;
    }
}
