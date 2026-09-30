using System;
using System.Collections.Generic;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The Espécie sub-screen (Decision D1). Three things here are structural rather
    /// than cosmetic and none of them shows up in a screenshot:
    ///
    /// <list type="number">
    /// <item><b>Every block is gated on presence.</b> Four of the five shipped species
    /// have no videos and none has a spec row, so "the block is absent" is the normal
    /// rendering — an empty card or a heading over nothing would be the bug.</item>
    /// <item><b>OnExit releases the rig and the stream.</b> The router keeps screens
    /// alive and only toggles <c>display</c>, so a missing teardown leaves a camera
    /// rendering and a video streaming behind a hidden screen for the rest of the
    /// session. That is a battery and bandwidth regression that looks like nothing.</item>
    /// <item><b>The entry point carries a species key.</b> Pinned here, at the far end,
    /// by driving Praia detalhe's link: a bare route would open the screen on whatever
    /// animal happened to be there last.</item>
    /// </list>
    ///
    /// <para>As in <see cref="MergulhoScreenTests"/> and <see cref="ReportScreenTests"/>,
    /// taps are expressed as direct calls: EditMode has no panel, so a
    /// <c>Clickable</c> cannot be driven with a synthetic pointer event.</para>
    /// </summary>
    public class EspecieScreenTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 15, 17, 30, 0, DateTimeKind.Utc);

        // ---- Fakes -----------------------------------------------------------

        sealed class FakeSpeciesCatalog : ISpeciesCatalog
        {
            public readonly List<SpeciesInfo> Items = new List<SpeciesInfo>();
            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
            }
        }

        sealed class FakeModelViewer : ISpeciesModelViewer
        {
            public bool Available = true;
            public bool ShowSucceeds = true;
            public readonly List<string> Shown = new List<string>();
            public int HideCount;
            public readonly List<float> Rotations = new List<float>();

            public bool IsAvailable => Available;
            public Texture Texture => null;
#pragma warning disable 67
            public event Action TextureChanged;
#pragma warning restore 67

            public bool Show(string speciesKey)
            {
                if (!ShowSucceeds) return false;
                Shown.Add(speciesKey);
                return true;
            }

            public void Hide() => HideCount++;
            public void SetViewportSize(int widthPx, int heightPx) { }
            public void Rotate(float pixelsX) => Rotations.Add(pixelsX);
            public void Zoom(float metres) { }
        }

        sealed class FakePlayback : IVideoPlayback
        {
            public bool Available = true;
            public int StopCount;
            public bool IsAvailable => Available;
            public string Url { get; private set; }
            public VideoPlaybackState State { get; private set; } = VideoPlaybackState.Idle;
            public Texture Texture => null;
            public double PositionSeconds => 0d;
            public double DurationSeconds => 0d;
            public event Action Changed;

            public void Play(string url)
            {
                Url = url;
                State = VideoPlaybackState.Playing;
                Changed?.Invoke();
            }

            public void Pause() { }

            public void Stop()
            {
                StopCount++;
                Url = null;
                State = VideoPlaybackState.Idle;
                Changed?.Invoke();
            }

            public void Seek(double seconds) { }
        }

        static SpeciesInfo Lemon() => new SpeciesInfo
        {
            Key = "lemon_shark",
            DisplayName = "Tubarão-limão",
            Binomial = "Negaprion brevirostris",
            ImageName = "lemon_shark",
            Description = "Uma espécie de tubarão da família dos carcarrinídeos.",
            PhotoCredit = "Foto: Albert Kok / CC BY-SA 3.0",
            ModelCredit = "modelo de meshy.ai",
            HasModel = true,
            Videos = new[] { new SpeciesVideo("Em ação", "https://example.test/a.mp4") },
        };

        static SpeciesInfo Bare() => new SpeciesInfo { Key = "bare", DisplayName = "Sem dados" };

        FakeSpeciesCatalog catalog;
        FakeModelViewer viewer;
        FakePlayback playback;
        EspecieViewModel vm;

        EspecieScreen NewScreen(params SpeciesInfo[] species)
        {
            catalog = new FakeSpeciesCatalog();
            catalog.Items.AddRange(species.Length > 0 ? species : new[] { Lemon() });
            viewer = new FakeModelViewer();
            playback = new FakePlayback();
            vm = new EspecieViewModel(catalog, playback);
            return new EspecieScreen(vm, viewer, playback, _ => null);
        }

        static bool Visible(VisualElement e) => e != null && e.style.display.value == DisplayStyle.Flex;

        static VisualElement Section(EspecieScreen s, string cls) => s.Q(className: cls);

        // ---- Contract --------------------------------------------------------

        [Test]
        public void Key_IsTheEspecieRoute()
        {
            Assert.That(NewScreen().Key, Is.EqualTo(AppRoutes.Especie));
        }

        /// <summary>
        /// The root is transparent and non-pickable, like every other screen: the
        /// opaque page surface is the `__content` child, so the strip reserved for the
        /// navigation bar never paints over it or eats its taps.
        /// </summary>
        [Test]
        public void TheRootIsTransparentAndTheContentIsThePage()
        {
            var screen = NewScreen();
            Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(screen.Q(className: "mv-especie__content"), Is.Not.Null);
        }

        /// <summary>
        /// A species page has nothing to select — no beach pill — and it is always
        /// pushed, so it always has a way back. Compact because the species photos are
        /// wide lead shots that a 340dp hero would crop the animal out of.
        /// </summary>
        [Test]
        public void TheHeroIsABackButtonOverACompactPhoto_AndNothingElse()
        {
            var hero = NewScreen().Q<MvHeroHeader>();

            Assert.That(hero, Is.Not.Null);
            Assert.That(hero.ShowBackButton, Is.True);
            Assert.That(hero.ShowSelector, Is.False);
            Assert.That(hero.Compact, Is.True);
        }

        /// <summary>
        /// Presence only: what "back" does is the host's call (pop the stack), and
        /// driving the click needs a panel. Same split as
        /// <see cref="MergulhoScreenTests"/>.
        /// </summary>
        [Test]
        public void TheBackButtonExists_AndIsVisible()
        {
            var screen = NewScreen();
            int backs = 0;
            screen.BackRequested += () => backs++;

            var back = screen.Q<MdIconButton>(className: MvHeroHeader.BackClassName);

            Assert.That(back, Is.Not.Null);
            Assert.That(back.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(backs, Is.Zero, "nothing was tapped");
        }

        [Test]
        public void EdgeInsets_GoToTheHeroAndTheContent_NotAsAStripAboveThePhoto()
        {
            var screen = NewScreen();
            screen.SetEdgeInsets(47f, 3f, 4f, 34f);

            var hero = screen.Q<MvHeroHeader>();
            var content = screen.Q(className: "mv-especie__content");

            Assert.That(hero.TopInset, Is.EqualTo(47f), "the photo runs under the status bar");
            Assert.That(content.style.paddingLeft.value.value, Is.EqualTo(3f));
            Assert.That(content.style.paddingRight.value.value, Is.EqualTo(4f));
            Assert.That(screen.style.paddingBottom.value.value, Is.EqualTo(34f));
        }

        // ---- Presence gating -------------------------------------------------

        /// <summary>
        /// Only reachable before the first push — the host sets the ViewModel and only
        /// navigates if the key resolved. It still must not render half a page.
        /// </summary>
        [Test]
        public void WithNoSpecies_TheBodyIsNotDrawn()
        {
            var screen = NewScreen();
            screen.OnEnter();

            Assert.That(Visible(screen.Q(className: "mv-especie__body")), Is.False);
        }

        [Test]
        public void WithAFullSpecies_EveryBlockIsDrawn()
        {
            var screen = NewScreen();
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(Visible(screen.Q(className: "mv-especie__body")), Is.True);
            Assert.That(Visible(screen.Q(className: "mv-especie__about")), Is.True);
            Assert.That(Visible(Section(screen, "mv-especie__model-card").parent), Is.True, "the 3D section");
            Assert.That(Visible(screen.Q(className: "mv-especie__videos").parent), Is.True, "the videos section");
            Assert.That(Visible(screen.Q(className: "mv-especie__credits")), Is.True);
        }

        /// <summary>
        /// The shape four of the five shipped species actually have. Nothing is a
        /// placeholder: the blocks are absent from the layout.
        /// </summary>
        [Test]
        public void WithABareSpecies_EveryOptionalBlockIsAbsent()
        {
            var screen = NewScreen(Bare());
            vm.ShowSpecies("bare");
            screen.OnEnter();

            Assert.That(Visible(screen.Q(className: "mv-especie__body")), Is.True);
            Assert.That(Visible(screen.Q(className: "mv-especie__about")), Is.False);
            Assert.That(Visible(screen.Q(className: "mv-especie__specs")), Is.False);
            Assert.That(Visible(Section(screen, "mv-especie__model-card").parent), Is.False);
            Assert.That(Visible(screen.Q(className: "mv-especie__videos").parent), Is.False);
            Assert.That(Visible(screen.Q(className: "mv-especie__credits")), Is.False);
        }

        /// <summary>The spec table is empty for EVERY shipped species (Decision D8).</summary>
        [Test]
        public void TheSpecCard_IsAbsentUntilSomebodyAuthorsTheFields()
        {
            var screen = NewScreen();
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();
            Assert.That(Visible(screen.Q(className: "mv-especie__specs")), Is.False);

            var withSpecs = Lemon();
            withSpecs.Diet = "Peixes e moluscos";
            screen = NewScreen(withSpecs);
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            var card = screen.Q(className: "mv-especie__specs");
            Assert.That(Visible(card), Is.True);
            Assert.That(card.Query(className: "mv-especie__spec").ToList().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// No rig in this scene (or in the screenshot harness) means no section — never
        /// an empty black box, and never a control that cannot work.
        /// </summary>
        [Test]
        public void WithNoRig_TheModelSectionIsAbsentEvenThoughTheSpeciesHasAModel()
        {
            var screen = NewScreen();
            viewer.Available = false;
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(Visible(Section(screen, "mv-especie__model-card").parent), Is.False);
            Assert.That(viewer.Shown, Is.Empty, "nothing is mounted either");
        }

        /// <summary>
        /// The documented fallout of replacing a species' FBX: the prefab reference
        /// dangles, the rig refuses, and the section has to go rather than show a black
        /// rectangle.
        /// </summary>
        [Test]
        public void WhenTheRigRefusesToMount_TheSectionIsDropped()
        {
            var screen = NewScreen();
            viewer.ShowSucceeds = false;
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(Visible(Section(screen, "mv-especie__model-card").parent), Is.False);
        }

        [Test]
        public void WithNoPlayer_TheVideoSectionIsAbsent()
        {
            var screen = NewScreen();
            playback.Available = false;
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(Visible(screen.Q(className: "mv-especie__videos").parent), Is.False);
        }

        [Test]
        public void OneCardPerClip()
        {
            var species = Lemon();
            species.Videos = new[]
            {
                new SpeciesVideo("um", "https://example.test/a.mp4"),
                new SpeciesVideo("dois", "https://example.test/b.mp4"),
            };
            var screen = NewScreen(species);
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(screen.Query(className: "mv-especie__video-card").ToList().Count, Is.EqualTo(2));
        }

        // ---- Lifecycle -------------------------------------------------------

        [Test]
        public void OnEnter_MountsTheSpeciesModel()
        {
            var screen = NewScreen();
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();

            Assert.That(viewer.Shown, Is.EqualTo(new[] { "lemon_shark" }));
        }

        /// <summary>
        /// The one that would otherwise cost battery and bandwidth silently — see the
        /// class remarks.
        /// </summary>
        [Test]
        public void OnExit_ReleasesTheRigAndTheStream()
        {
            var screen = NewScreen();
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();
            vm.ToggleVideo(0);

            screen.OnExit();

            Assert.That(viewer.HideCount, Is.GreaterThan(0));
            Assert.That(playback.StopCount, Is.GreaterThan(0));
            Assert.That(vm.PlayingVideoIndex, Is.EqualTo(-1));
        }

        /// <summary>
        /// Changing species under the screen has to swap BOTH media blocks, or the
        /// turntable keeps the previous animal and the player keeps streaming a clip
        /// that is no longer on the page.
        /// </summary>
        [Test]
        public void ChangingSpecies_RemountsTheModelAndStopsTheClip()
        {
            var screen = NewScreen(Lemon(), Bare());
            vm.ShowSpecies("lemon_shark");
            screen.OnEnter();
            vm.ToggleVideo(0);
            int stopsBefore = playback.StopCount;

            vm.ShowSpecies("bare");

            Assert.That(viewer.HideCount, Is.GreaterThan(0));
            Assert.That(playback.StopCount, Is.GreaterThan(stopsBefore));
            Assert.That(Visible(Section(screen, "mv-especie__model-card").parent), Is.False,
                "the bare species has no model, so the section goes with it");
        }

        // ---- The entry point -------------------------------------------------

        sealed class FakeBeachCatalog : IBeachCatalog
        {
            public IReadOnlyList<BeachInfo> Beaches { get; } = new List<BeachInfo>
            {
                new BeachInfo { Name = "Praia do Sancho" },
            };
        }

        sealed class FakeBeachContent : IBeachContent
        {
            public BeachContent Entry;

            public BeachContent ForBeach(string beachName) => Entry ?? BeachContent.EmptyFor(beachName);

            public bool TryGetContent(string beachName, out BeachContent content)
            {
                content = ForBeach(beachName);
                return Entry != null;
            }
        }

        sealed class FakeTides : ITideService
        {
            public TideData Current { get; set; }
#pragma warning disable 67
            public event Action<TideData> Changed;
#pragma warning restore 67
        }

        sealed class FakeConditions : IConditionsService
        {
            public ConditionsData Current { get; set; }
            public bool LastFetchFailed { get; set; }
            public bool IsFetching { get; set; }
            public int RefreshCalls;
            public void Refresh() => RefreshCalls++;
#pragma warning disable 67
            public event Action<ConditionsData> Changed;
#pragma warning restore 67
        }

        sealed class FakeOverride : IBeachOverride
        {
            public void SetOverride(string beachName) { }
            public void ClearOverride() { }
        }

        sealed class FakeActiveBeach : IActiveBeach
        {
            public string ActiveBeachKey { get; set; } = "Praia do Sancho";
#pragma warning disable 67
            public event Action<string> ActiveBeachChanged;
#pragma warning restore 67
        }

        /// <summary>
        /// The whole payload contract, end to end: "Saiba mais sobre a espécie" must
        /// name WHICH species, not just the route. A bare
        /// <see cref="AppRoutes.Especie"/> — which is what this link used to raise —
        /// would open the screen on whatever animal was there last.
        /// </summary>
        [Test]
        public void PraiaDetalheLearnMore_RaisesTheSelectedSpeciesKey()
        {
            var species = new FakeSpeciesCatalog();
            species.Items.Add(Lemon());
            species.Items.Add(new SpeciesInfo { Key = "tiger_shark", DisplayName = "Tubarão-tigre" });

            var content = new FakeBeachContent
            {
                Entry = new BeachContent
                {
                    BeachName = "Praia do Sancho",
                    Species = new[]
                    {
                        new BeachSpeciesContent { SpeciesKey = "lemon_shark" },
                        new BeachSpeciesContent { SpeciesKey = "tiger_shark" },
                    },
                },
            };

            var beaches = new FakeBeachCatalog();
            var detail = new BeachDetailViewModel(beaches, content, species, new FakeTides(),
                utcNow: () => Now, toLocalTime: d => d);
            var list = new BeachesViewModel(beaches, new FakeConditions(), new FakeTides(), new FakeOverride(),
                utcNow: () => Now, toLocalTime: d => d);
            var praias = new PraiasViewModel(detail, list, new FakeActiveBeach());

            var screen = new PraiaDetalheScreen(praias, _ => null, _ => null);
            string raised = null;
            screen.SpeciesRequested += key => raised = key;
            screen.OnEnter();

            // The first chip is selected on entry.
            screen.OnLearnMoreClicked();
            Assert.That(raised, Is.EqualTo("lemon_shark"));

            // …and the link follows the chip row, because it reads the key at tap time.
            detail.SelectSpecies(1);
            screen.OnLearnMoreClicked();
            Assert.That(raised, Is.EqualTo("tiger_shark"));
        }

        [Test]
        public void PraiaDetalheLearnMore_RaisesNothingWithNoSpeciesSelected()
        {
            var species = new FakeSpeciesCatalog();
            var beaches = new FakeBeachCatalog();
            var detail = new BeachDetailViewModel(beaches, new FakeBeachContent(), species, new FakeTides(),
                utcNow: () => Now, toLocalTime: d => d);
            var list = new BeachesViewModel(beaches, new FakeConditions(), new FakeTides(), new FakeOverride(),
                utcNow: () => Now, toLocalTime: d => d);
            var praias = new PraiasViewModel(detail, list, new FakeActiveBeach());

            var screen = new PraiaDetalheScreen(praias, _ => null, _ => null);
            int raised = 0;
            screen.SpeciesRequested += _ => raised++;
            screen.OnEnter();

            screen.OnLearnMoreClicked();

            Assert.That(raised, Is.Zero);
        }
    }
}
