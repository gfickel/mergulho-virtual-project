using System;
using System.Collections.Generic;
using System.Linq;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// MergulhoScreen is a HUD over a live camera, and the two things that make it
    /// one are structural, not visual: nothing but its two real surfaces may be
    /// pickable (or taps never reach the animals behind it), and nothing may paint
    /// a page background. Both are one careless line away from breaking and
    /// neither shows up in a screenshot, so they are pinned here.
    ///
    /// <para>The rest is the card's own behaviour — opened by a tap, closed by the
    /// ⨯, with the rows nobody has authored simply absent. As in
    /// <see cref="ReportScreenTests"/>, taps are expressed as ViewModel calls:
    /// EditMode has no panel, so a <c>Clickable</c> cannot be driven with a
    /// synthetic pointer event.</para>
    /// </summary>
    public class MergulhoScreenTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 15, 17, 30, 0, DateTimeKind.Utc);

        // ---- Fakes (mirrors of the other suites' — each set stays free to serve
        // its own purpose, the same reason the shot fixtures are mirrored) -------

        sealed class FakeCatalog : IBeachCatalog
        {
            public IReadOnlyList<BeachInfo> Beaches { get; set; } = new List<BeachInfo>
            {
                new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
                new BeachInfo { Name = "Praia do Sancho" },
            };
        }

        sealed class FakeContent : IBeachContent
        {
            public BeachContent ForBeach(string beachName) => BeachContent.EmptyFor(beachName);

            public bool TryGetContent(string beachName, out BeachContent content)
            {
                content = BeachContent.EmptyFor(beachName);
                return false;
            }
        }

        sealed class FakeSpecies : ISpeciesCatalog
        {
            public List<SpeciesInfo> Items = new List<SpeciesInfo>
            {
                new SpeciesInfo { Key = "tiger_shark", DisplayName = "Tubarão-tigre", Binomial = "Galeocerdo cuvier" },
            };

            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
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
            public string LastSet;
            public void SetOverride(string beachName) => LastSet = beachName;
            public void ClearOverride() => LastSet = null;
        }

        sealed class FakeActiveBeach : IActiveBeach
        {
            public string ActiveBeachKey { get; private set; }
#pragma warning disable 67
            public event Action<string> ActiveBeachChanged;
#pragma warning restore 67
            public FakeActiveBeach(string initial) => ActiveBeachKey = initial;
        }

        sealed class FakeArSelection : IArSelection
        {
            public bool Listening;
            public event Action<string> SpeciesSelected;
            public void SetListening(bool listening) => Listening = listening;
            public void Tap(string key) => SpeciesSelected?.Invoke(key);
        }

        FakeSpecies species;
        FakeArSelection ar;
        MergulhoViewModel vm;
        PraiasViewModel praias;

        MergulhoScreen NewScreen(string activeBeach = "Sueste Beach")
        {
            species = new FakeSpecies();
            ar = new FakeArSelection();
            vm = new MergulhoViewModel(species, ar);

            var catalog = new FakeCatalog();
            var detail = new BeachDetailViewModel(catalog, new FakeContent(), species, new FakeTides(),
                utcNow: () => Now, toLocalTime: d => d);
            var beaches = new BeachesViewModel(catalog, new FakeConditions(), new FakeTides(), new FakeOverride(),
                utcNow: () => Now, toLocalTime: d => d);
            praias = new PraiasViewModel(detail, beaches, new FakeActiveBeach(activeBeach));

            return new MergulhoScreen(vm, praias);
        }

        static VisualElement Card(MergulhoScreen screen) => screen.Q(className: "mv-mergulho__card");

        // ---- The HUD contract ------------------------------------------------

        [Test]
        public void Key_IsTheMergulhoRoute()
        {
            Assert.That(NewScreen().Key, Is.EqualTo(AppRoutes.Mergulho));
        }

        /// <summary>
        /// The whole reason this screen can sit on top of the AR scene. Anything
        /// pickable outside the two real surfaces swallows a tap meant for an
        /// animal — invisibly, because the element is transparent.
        /// </summary>
        [Test]
        public void OnlyTheCardAndTheHeroControls_ArePickable()
        {
            var screen = NewScreen();

            Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(screen.Q(className: "mv-mergulho__hero").pickingMode, Is.EqualTo(PickingMode.Ignore),
                "the hero is a 160dp strip of mostly empty space over the camera");
            Assert.That(screen.Q(className: "mv-mergulho__dock").pickingMode, Is.EqualTo(PickingMode.Ignore),
                "the dock is the whole middle of the screen");

            // The card itself must NOT be ignored: a tap that lands on it must not
            // also reach the animal behind it.
            Assert.That(Card(screen).pickingMode, Is.EqualTo(PickingMode.Position));
        }

        /// <summary>
        /// Every other screen wraps its content in an opaque page surface (the
        /// `__content` container that takes the safe-area padding and paints the
        /// background). This one must not have one at all, or the camera feed is
        /// covered by a flat colour — so its children are exactly the control strip
        /// and the dock, with nothing in between.
        /// </summary>
        [Test]
        public void ThereIsNoOpaquePageContainer()
        {
            var screen = NewScreen();
            var children = screen.Children().ToList();

            Assert.That(children.Count, Is.EqualTo(2));
            Assert.That(children[0].ClassListContains("mv-mergulho__hero"), Is.True);
            Assert.That(children[1].ClassListContains("mv-mergulho__dock"), Is.True);
        }

        [Test]
        public void TheHeroShowsTheActiveBeach_AndOffersTheSelector()
        {
            var screen = NewScreen();
            var hero = screen.Q<MvHeroHeader>();

            Assert.That(hero, Is.Not.Null);
            Assert.That(hero.ShowSelector, Is.True);
            Assert.That(hero.ShowBackButton, Is.True, "Tela 8 draws a back arrow even on a tab root");
            Assert.That(hero.SelectorText, Is.EqualTo("Baía do Sueste"),
                "the pill shows the displayName, never the machine key");
        }

        [Test]
        public void TheHeroFallsBackToTheSelectorCallToAction_WhenNoBeachIsResolved()
        {
            var screen = NewScreen(activeBeach: null);
            Assert.That(screen.Q<MvHeroHeader>().SelectorText, Is.EqualTo(PraiasViewModel.ChooseBeachLabel));
        }

        // ---- The species card ------------------------------------------------

        [Test]
        public void TheCardIsHidden_UntilSomethingIsTapped()
        {
            var screen = NewScreen();
            Assert.That(Card(screen).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void ATap_ShowsTheNameAndBinomial()
        {
            var screen = NewScreen();
            screen.OnEnter();

            ar.Tap("tiger_shark");

            Assert.That(Card(screen).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.Q<Label>(className: "mv-mergulho__title").text, Is.EqualTo("Tubarão-tigre"));
            var binomial = screen.Q<Label>(className: "mv-mergulho__binomial");
            Assert.That(binomial.text, Is.EqualTo("Galeocerdo cuvier"));
            Assert.That(binomial.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void TheRuleAndTheSpecTable_AreAbsentWhenNothingIsAuthored()
        {
            var screen = NewScreen();
            screen.OnEnter();

            ar.Tap("tiger_shark");

            // The state of every species today: name + binomial, nothing else — and
            // NOT a divider hanging over an empty block.
            Assert.That(screen.Q(className: "mv-mergulho__rule").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Q(className: "mv-mergulho__specs").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Query(className: "mv-mergulho__spec").ToList(), Is.Empty);
        }

        [Test]
        public void AuthoredSpecs_RenderOneRowEach_LabelAndValue()
        {
            var screen = NewScreen();
            species.Items[0].ApproximateSize = "3 a 4 metros";
            species.Items[0].Behaviour = "Solitário e noturno";
            screen.OnEnter();

            ar.Tap("tiger_shark");

            var rows = screen.Query(className: "mv-mergulho__spec").ToList();
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(screen.Q(className: "mv-mergulho__rule").style.display.value, Is.EqualTo(DisplayStyle.Flex));

            var labels = screen.Query<Label>(className: "mv-mergulho__spec-label").ToList().Select(l => l.text).ToList();
            var values = screen.Query<Label>(className: "mv-mergulho__spec-value").ToList().Select(l => l.text).ToList();
            Assert.That(labels, Is.EqualTo(new[] { SpeciesCardFormatter.SizeLabel, SpeciesCardFormatter.BehaviourLabel }));
            Assert.That(values, Is.EqualTo(new[] { "3 a 4 metros", "Solitário e noturno" }));

            // USS has no `gap`, so every row but the first carries the 10dp margin.
            Assert.That(rows[0].ClassListContains("mv-mergulho__spec--gutter"), Is.False);
            Assert.That(rows[1].ClassListContains("mv-mergulho__spec--gutter"), Is.True);
        }

        [Test]
        public void TheCloseButton_DismissesTheCard()
        {
            var screen = NewScreen();
            screen.OnEnter();
            ar.Tap("tiger_shark");

            // The handler is vm.CloseCard; EditMode cannot synthesise the tap.
            vm.CloseCard();

            Assert.That(Card(screen).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Q<MdIconButton>(className: "mv-mergulho__close"), Is.Not.Null,
                "the card's only dismiss affordance must exist — a miss does not close it");
        }

        // ---- Route lifecycle -------------------------------------------------

        [Test]
        public void EnteringAndLeaving_TogglesTheArHitSource()
        {
            var screen = NewScreen();

            screen.OnEnter();
            Assert.That(ar.Listening, Is.True);

            screen.OnExit();
            Assert.That(ar.Listening, Is.False, "the raycaster must not fire from the other three tabs");
        }

        [Test]
        public void LeavingTheRoute_ClosesTheCard()
        {
            var screen = NewScreen();
            screen.OnEnter();
            ar.Tap("tiger_shark");

            screen.OnExit();

            Assert.That(Card(screen).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        /// <summary>
        /// The back arrow exists and is visible. What it DOES is the host's call
        /// (pop, else leave AR for Início) — the screen only raises
        /// <see cref="MergulhoScreen.BackRequested"/>, and driving the click needs
        /// a panel, so this is the presence half.
        /// </summary>
        [Test]
        public void TheBackButtonExists_AndIsVisible()
        {
            var screen = NewScreen();
            var back = screen.Q<MdIconButton>(className: MvHeroHeader.BackClassName);

            Assert.That(back, Is.Not.Null);
            Assert.That(back.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        // ---- Insets ----------------------------------------------------------

        /// <summary>
        /// The top inset goes to the hero's controls plane, NOT to a content
        /// container — there is no page surface to pad here, and the camera is
        /// meant to run under the status bar.
        /// </summary>
        [Test]
        public void TheTopInset_PadsTheControlsOnly()
        {
            var screen = NewScreen();
            screen.SetEdgeInsets(47f, 0f, 0f, 34f);

            Assert.That(screen.Q<MvHeroHeader>().TopInset, Is.EqualTo(47f));
            Assert.That(screen.style.paddingTop.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(screen.style.paddingBottom.value.value, Is.EqualTo(34f));
        }
    }
}
