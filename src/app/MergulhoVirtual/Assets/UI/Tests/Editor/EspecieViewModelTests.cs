using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The Espécie sub-screen's ViewModel (Decision D1): which species is open, and
    /// the shared video player's presentation state.
    ///
    /// <para>Two things here are worth more than the usual property round-trips. The
    /// first is that <see cref="EspecieViewModel.ShowSpecies"/> REFUSES an unknown key
    /// — that refusal is what stops the host navigating to a blank species page, so it
    /// is the whole payload contract in one boolean. The second is that "which card is
    /// playing" is an INDEX: two clips on one species may legitimately share a URL
    /// (lemon_shark's two entries do today), and a URL-keyed player would light both
    /// cards at once.</para>
    /// </summary>
    public class EspecieViewModelTests
    {
        // ---- Fakes -----------------------------------------------------------

        sealed class FakeCatalog : ISpeciesCatalog
        {
            public readonly List<SpeciesInfo> Items = new List<SpeciesInfo>();
            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
            }
        }

        /// <summary>
        /// A player with no decoder: every transition is explicit, so a test can park
        /// it in any state the real one can reach — including Failed, which on a Linux
        /// editor is the only state a real clip ever reaches.
        /// </summary>
        sealed class FakePlayback : IVideoPlayback
        {
            public bool Available = true;
            public double Duration;
            public double Position;
            public readonly List<string> Played = new List<string>();
            public int StopCount;
            public readonly List<double> Seeks = new List<double>();

            public bool IsAvailable => Available;
            public string Url { get; private set; }
            public VideoPlaybackState State { get; private set; } = VideoPlaybackState.Idle;
            public UnityEngine.Texture Texture => null;
            public double PositionSeconds => Position;
            public double DurationSeconds => Duration;

            public event Action Changed;

            public void Play(string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                Played.Add(url);
                Url = url;
                State = VideoPlaybackState.Loading;
                Changed?.Invoke();
            }

            public void Pause()
            {
                if (State != VideoPlaybackState.Playing) return;
                State = VideoPlaybackState.Paused;
                Changed?.Invoke();
            }

            public void Stop()
            {
                StopCount++;
                Url = null;
                State = VideoPlaybackState.Idle;
                Position = 0d;
                Changed?.Invoke();
            }

            public void Seek(double seconds) => Seeks.Add(seconds);

            /// <summary>Drive the state the way the real adapter's callbacks would.</summary>
            public void Settle(VideoPlaybackState state, double duration = 0d, double position = 0d)
            {
                State = state;
                if (duration > 0d) Duration = duration;
                Position = position;
                Changed?.Invoke();
            }
        }

        static SpeciesInfo Lemon() => new SpeciesInfo
        {
            Key = "lemon_shark",
            DisplayName = "Tubarão-limão",
            Binomial = "Negaprion brevirostris",
            ImageName = "lemon_shark",
            Description = "Uma espécie de tubarão da família dos carcarrinídeos.",
            PhotoCredit = "Foto: Albert Kok / CC BY-SA 3.0",
            ModelCredit = "chenukabro2k19, modelo de meshy.ai",
            HasModel = true,
            Videos = new[]
            {
                new SpeciesVideo("Tubarão-limão em ação", "https://example.test/a.mp4"),
                // Deliberately the SAME url as the first, which is what the shipped
                // asset carries — see the class remarks.
                new SpeciesVideo("Tubarão-limão (vídeo 2)", "https://example.test/a.mp4"),
            },
        };

        /// <summary>A species with nothing optional filled — four of the five shipped
        /// ones are close to this, and every block has to vanish rather than blank.</summary>
        static SpeciesInfo Bare() => new SpeciesInfo { Key = "bare", DisplayName = "Espécie sem dados" };

        FakeCatalog catalog;
        FakePlayback playback;

        EspecieViewModel NewVm(params SpeciesInfo[] species)
        {
            catalog = new FakeCatalog();
            catalog.Items.AddRange(species);
            playback = new FakePlayback();
            return new EspecieViewModel(catalog, playback);
        }

        // ---- The payload contract -------------------------------------------

        [Test]
        public void ShowSpecies_OpensAKnownKey()
        {
            var vm = NewVm(Lemon());

            Assert.That(vm.ShowSpecies("lemon_shark"), Is.True);
            Assert.That(vm.HasSpecies, Is.True);
            Assert.That(vm.SpeciesKey, Is.EqualTo("lemon_shark"));
            Assert.That(vm.TitleText, Is.EqualTo("Tubarão-limão"));
            Assert.That(vm.BinomialText, Is.EqualTo("Negaprion brevirostris"));
        }

        /// <summary>
        /// The refusal the whole entry point rests on: the host only pushes the route
        /// when this returns true, so a typo in beaches_content.json leaves the user
        /// where they were instead of on an empty page.
        /// </summary>
        [Test]
        public void ShowSpecies_RefusesAnUnknownKey_AndChangesNothing()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            int changes = 0;
            vm.Changed += () => changes++;

            Assert.That(vm.ShowSpecies("nao_existe"), Is.False);
            Assert.That(vm.ShowSpecies(null), Is.False);
            Assert.That(vm.ShowSpecies(""), Is.False);
            Assert.That(vm.SpeciesKey, Is.EqualTo("lemon_shark"), "the open species is untouched");
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void ShowSpecies_IsIdempotentForTheSpeciesAlreadyOpen()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            int changes = 0;
            vm.Changed += () => changes++;

            Assert.That(vm.ShowSpecies("lemon_shark"), Is.True, "true: the caller's request IS satisfied");
            Assert.That(changes, Is.Zero, "but nothing changed, so nothing repaints");
        }

        [Test]
        public void Clear_ClosesTheSpecies()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            vm.Clear();

            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.HasVideos, Is.False);
            Assert.That(vm.HasSpecRows, Is.False);
            Assert.That(vm.TitleText, Is.Null);
        }

        // ---- Presence gating -------------------------------------------------

        [Test]
        public void EveryOptionalBlockIsAbsentForABareSpecies()
        {
            var vm = NewVm(Bare());
            vm.ShowSpecies("bare");

            Assert.That(vm.HasBinomial, Is.False);
            Assert.That(vm.HasImage, Is.False);
            Assert.That(vm.HasDescription, Is.False);
            Assert.That(vm.HasSpecRows, Is.False);
            Assert.That(vm.HasModel, Is.False);
            Assert.That(vm.HasVideos, Is.False);
            Assert.That(vm.HasPhotoCredit, Is.False);
            Assert.That(vm.HasModelCredit, Is.False);
            Assert.That(vm.HasCredits, Is.False);
            // The common name is the ONE thing that is always there.
            Assert.That(vm.TitleText, Is.EqualTo("Espécie sem dados"));
        }

        /// <summary>
        /// The three spec rows are blank on every shipped AnimalDef (Decision D8), so
        /// an empty table is the normal result — and when they ARE filled they come
        /// through SpeciesCardFormatter, the AR card's formatter, so the two surfaces
        /// can never print the same facts differently.
        /// </summary>
        [Test]
        public void SpecRows_DropTheRowsWithNoValue()
        {
            var species = Lemon();
            species.Diet = "Peixes e moluscos";
            var vm = NewVm(species);
            vm.ShowSpecies("lemon_shark");

            Assert.That(vm.HasSpecRows, Is.True);
            Assert.That(vm.SpecRows.Count, Is.EqualTo(1));
            Assert.That(vm.SpecRows[0].Label, Is.EqualTo(SpeciesCardFormatter.DietLabel));
            Assert.That(vm.SpecRows[0].Value, Is.EqualTo("Peixes e moluscos"));
        }

        [Test]
        public void CreditsAreVerbatim()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            Assert.That(vm.HasCredits, Is.True);
            Assert.That(vm.PhotoCreditText, Is.EqualTo("Foto: Albert Kok / CC BY-SA 3.0"));
            Assert.That(vm.ModelCreditText, Is.EqualTo("chenukabro2k19, modelo de meshy.ai"));
        }

        /// <summary>A clip with no URL cannot be played, so it is not offered.</summary>
        [Test]
        public void VideosWithoutAUrlAreDropped()
        {
            var species = Lemon();
            species.Videos = new[]
            {
                new SpeciesVideo("sem url", ""),
                new SpeciesVideo("boa", "https://example.test/b.mp4"),
                null,
            };
            var vm = NewVm(species);
            vm.ShowSpecies("lemon_shark");

            Assert.That(vm.Videos.Count, Is.EqualTo(1));
            Assert.That(vm.Videos[0].Title, Is.EqualTo("boa"));
        }

        // ---- The video player ------------------------------------------------

        [Test]
        public void ShowVideos_NeedsBothClipsAndAPlayer()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            Assert.That(vm.ShowVideos, Is.True);

            playback.Available = false;
            Assert.That(vm.HasVideos, Is.True, "the clips are still in the data");
            Assert.That(vm.ShowVideos, Is.False, "but there is nothing to play them with");
        }

        [Test]
        public void ToggleVideo_StartsTheTappedClip()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            vm.ToggleVideo(0);

            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(0));
            Assert.That(playback.Played, Is.EqualTo(new[] { "https://example.test/a.mp4" }));
            Assert.That(vm.VideoStateFor(0), Is.EqualTo(VideoPlaybackState.Loading));
            Assert.That(vm.VideoStateFor(1), Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>
        /// The reason the owner is an index. Both clips carry the SAME url in the
        /// shipped lemon_shark asset; tapping the second must move the highlight to
        /// the second card, not light up both.
        /// </summary>
        [Test]
        public void TwoClipsSharingAUrl_AreStillDistinctCards()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            vm.ToggleVideo(0);
            vm.ToggleVideo(1);

            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(1));
            Assert.That(vm.VideoStateFor(0), Is.EqualTo(VideoPlaybackState.Idle));
            Assert.That(vm.VideoStateFor(1), Is.EqualTo(VideoPlaybackState.Loading));
        }

        [Test]
        public void ToggleVideo_PausesWhilePlaying_AndResumesWhenPaused()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d);

            vm.ToggleVideo(0);
            Assert.That(vm.VideoStateFor(0), Is.EqualTo(VideoPlaybackState.Paused));
            Assert.That(vm.VideoActionLabelFor(0), Is.EqualTo("Continuar"));

            vm.ToggleVideo(0);
            Assert.That(playback.Played.Count, Is.EqualTo(2), "resuming goes back through Play");
        }

        /// <summary>A second tap while buffering must not restart the stream.</summary>
        [Test]
        public void ToggleVideo_IsInertWhileLoading()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);

            vm.ToggleVideo(0);

            Assert.That(playback.Played.Count, Is.EqualTo(1));
        }

        [Test]
        public void ToggleVideo_RetriesAfterAFailure()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Failed);

            Assert.That(vm.VideoActionLabelFor(0), Is.EqualTo("Tentar de novo"));
            vm.ToggleVideo(0);
            Assert.That(playback.Played.Count, Is.EqualTo(2));
        }

        [Test]
        public void ToggleVideo_IgnoresAnIndexOutsideTheList()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            vm.ToggleVideo(-1);
            vm.ToggleVideo(99);

            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(-1));
            Assert.That(playback.Played, Is.Empty);
        }

        /// <summary>
        /// The adapter answers the end of a clip by unbinding, which is what returns
        /// the card to its poster. The ViewModel has to let go of the owning index at
        /// the same moment, or the finished card keeps a seek track and a clock.
        /// </summary>
        [Test]
        public void ThePlayerGoingIdle_ReleasesTheOwningCard()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d);

            playback.Stop(); // what OnReachedEnd does

            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(-1));
            Assert.That(vm.VideoTimeTextFor(0), Is.Null);
            Assert.That(vm.VideoProgressFor(0), Is.EqualTo(0f));
        }

        [Test]
        public void OpeningAnotherSpecies_StopsWhatWasPlaying()
        {
            var bare = Bare();
            var vm = NewVm(Lemon(), bare);
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);

            vm.ShowSpecies("bare");

            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(-1));
            Assert.That(playback.StopCount, Is.GreaterThan(0));
        }

        [Test]
        public void TimeAndProgress_ComeFromThePlayer_ForTheOwningCardOnly()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d, position: 21d);

            Assert.That(vm.VideoTimeTextFor(0), Is.EqualTo("0:21 / 0:42"));
            Assert.That(vm.VideoProgressFor(0), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(vm.VideoTimeTextFor(1), Is.Null);
            Assert.That(vm.VideoProgressFor(1), Is.EqualTo(0f));
        }

        [Test]
        public void SeekVideo_MapsAFractionOntoTheDuration()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d);

            vm.SeekVideo(0, 0.5f);
            vm.SeekVideo(0, 2f);   // clamped
            vm.SeekVideo(0, -1f);  // clamped

            Assert.That(playback.Seeks, Is.EqualTo(new[] { 21d, 42d, 0d }));
        }

        [Test]
        public void SeekVideo_IgnoresACardThatDoesNotOwnThePlayer()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d);

            vm.SeekVideo(1, 0.5f);

            Assert.That(playback.Seeks, Is.Empty);
        }

        /// <summary>
        /// Playback must not raise the species-changed event: the screen answers that
        /// one by tearing down and remounting the 3D model, which would make the
        /// turntable jump every time a clip buffered.
        /// </summary>
        [Test]
        public void PlaybackRaisesVideoChanged_NeverChanged()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");

            int speciesChanges = 0, videoChanges = 0;
            vm.Changed += () => speciesChanges++;
            vm.VideoChanged += () => videoChanges++;

            vm.ToggleVideo(0);
            playback.Settle(VideoPlaybackState.Playing, duration: 42d);

            Assert.That(speciesChanges, Is.Zero);
            Assert.That(videoChanges, Is.GreaterThan(0));
        }

        /// <summary>A host that built no player must still get a working page.</summary>
        [Test]
        public void WithNoPlayerAtAll_NothingThrows()
        {
            var cat = new FakeCatalog();
            cat.Items.Add(Lemon());
            var vm = new EspecieViewModel(cat, null);

            Assert.That(vm.ShowSpecies("lemon_shark"), Is.True);
            Assert.That(vm.HasVideoPlayer, Is.False);
            Assert.That(vm.ShowVideos, Is.False);
            Assert.DoesNotThrow(() => vm.ToggleVideo(0));
            Assert.DoesNotThrow(() => vm.SeekVideo(0, 0.5f));
            Assert.DoesNotThrow(vm.StopVideo);
            Assert.DoesNotThrow(vm.Dispose);
        }

        [Test]
        public void Dispose_ReleasesThePlayer()
        {
            var vm = NewVm(Lemon());
            vm.ShowSpecies("lemon_shark");
            vm.ToggleVideo(0);

            vm.Dispose();

            Assert.That(playback.StopCount, Is.GreaterThan(0));
        }
    }
}
