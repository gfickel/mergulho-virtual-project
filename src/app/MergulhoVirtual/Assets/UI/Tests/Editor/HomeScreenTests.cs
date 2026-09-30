using System;
using System.Collections.Generic;
using System.Linq;
using MergulhoVirtual.DesignSystem;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The half of Início that has consequences: what the conditions card draws
    /// when there is nothing to draw. EditMode has no panel, so the retry tap is
    /// expressed as the ViewModel call the handler makes — same convention as
    /// <see cref="ReportScreenTests"/>.
    /// </summary>
    public class HomeScreenTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        sealed class FakeConditions : IConditionsService
        {
            public ConditionsData Current { get; set; }
            public bool LastFetchFailed { get; set; }
            public bool IsFetching { get; set; }
            public int RefreshCalls;
            public void Refresh() => RefreshCalls++;
            public event Action<ConditionsData> Changed;
            public void Raise() => Changed?.Invoke(Current);
        }

        sealed class FakeTides : ITideService
        {
            public TideData Current { get; set; }
#pragma warning disable 67
            public event Action<TideData> Changed;
#pragma warning restore 67
        }

        sealed class FakeConnectivity : IConnectivity
        {
            public bool IsOnline { get; set; } = true;
        }

        sealed class FakeOnboarding : IOnboardingState
        {
            public bool WelcomeDismissed { get; set; } = true;
            public void DismissWelcome() => WelcomeDismissed = true;
        }

        FakeConditions conditions;
        FakeTides tides;
        FakeConnectivity connectivity;
        HomeViewModel vm;
        HomeScreen screen;

        [SetUp]
        public void SetUp()
        {
            conditions = new FakeConditions();
            tides = new FakeTides();
            connectivity = new FakeConnectivity();
        }

        [TearDown]
        public void TearDown() => vm?.Dispose();

        void Build()
        {
            vm = new HomeViewModel(conditions, tides, new FakeOnboarding(), connectivity,
                utcNow: () => Now, toLocalTime: d => d);
            screen = new HomeScreen(vm);
            screen.OnEnter();
        }

        MvStateView State() => screen.Q<MvStateView>(className: "mv-conditions__state");
        List<VisualElement> Rows() =>
            screen.Query<VisualElement>(className: "mv-conditions__row").ToList();
        MdSparkline Sparkline() => screen.Q<MdSparkline>(className: "mv-conditions__sparkline");
        Label Freshness() => screen.Q<Label>(className: "mv-conditions__freshness");

        static bool Visible(VisualElement e) => e.style.display.value == DisplayStyle.Flex;

        [Test]
        public void WithData_TheCardShowsRowsAndNoState()
        {
            conditions.Current = new ConditionsData
            {
                BeachName = "Praia do Sancho",
                FetchedAtUtc = Now.AddMinutes(-5),
                WaveHeightM = 1.3f,
            };
            Build();

            Assert.That(State(), Is.Not.Null, "the state block is built once and hidden, not created on demand");
            Assert.That(Visible(State()), Is.False);
            Assert.That(Rows().Count, Is.EqualTo(5));
            Assert.That(Rows().All(Visible), Is.True);
            Assert.That(Visible(Sparkline()), Is.True);
            Assert.That(Visible(Freshness()), Is.True);
        }

        /// <summary>
        /// Nothing loaded and the service has said it tried: five em dashes over an
        /// empty chart reads as "the sea is blank" rather than "we could not load
        /// it", so the data block is replaced outright.
        /// </summary>
        [Test]
        public void WithAFailedFetchAndNoData_TheStateReplacesTheRows()
        {
            conditions.Current = null;
            conditions.LastFetchFailed = true;
            Build();

            Assert.That(Visible(State()), Is.True);
            Assert.That(Rows().Any(Visible), Is.False);
            Assert.That(Visible(Sparkline()), Is.False);
            Assert.That(Visible(Freshness()), Is.False);

            Assert.That(State().Variant, Is.EqualTo(MvStateViewVariant.Error));
            Assert.That(State().Title, Is.EqualTo(StateViewCopy.ErrorTitle));
            Assert.That(State().Body, Is.EqualTo(StateViewCopy.ConditionsErrorBody));
            Assert.That(State().ActionText, Is.EqualTo(StateViewCopy.RetryAction));
        }

        [Test]
        public void BeforeTheFirstFetchReturns_NoErrorIsClaimed()
        {
            conditions.Current = null;
            conditions.LastFetchFailed = false;
            Build();

            Assert.That(Visible(State()), Is.False);
            Assert.That(Rows().All(Visible), Is.True, "the '—' rows are the loading state, such as it is");
        }

        [Test]
        public void Offline_SwapsTheVariantAndTheWording()
        {
            conditions.Current = null;
            conditions.LastFetchFailed = true;
            connectivity.IsOnline = false;
            Build();

            Assert.That(State().Variant, Is.EqualTo(MvStateViewVariant.Offline));
            Assert.That(State().Title, Is.EqualTo(StateViewCopy.OfflineTitle));
            Assert.That(State().Body, Is.EqualTo(StateViewCopy.ConditionsOfflineBody));
        }

        [Test]
        public void ARecoveredFetch_PutsTheRowsBack()
        {
            conditions.Current = null;
            conditions.LastFetchFailed = true;
            Build();
            Assert.That(Visible(State()), Is.True);

            conditions.Current = new ConditionsData { BeachName = "Praia do Sancho", WaveHeightM = 1.1f };
            conditions.LastFetchFailed = false;
            conditions.Raise();

            Assert.That(Visible(State()), Is.False);
            Assert.That(Rows().All(Visible), Is.True);
        }

        [Test]
        public void TheRetryButtonIsDisabledWhileTheFetchItStartedIsInFlight()
        {
            conditions.Current = null;
            conditions.LastFetchFailed = true;
            Build();
            Assert.That(State().ActionEnabled, Is.True);

            // What the button's handler does.
            conditions.IsFetching = true;
            vm.RetryConditions();

            Assert.That(conditions.RefreshCalls, Is.EqualTo(1));
            Assert.That(State().ActionEnabled, Is.False);
        }

        // ---- Feature cards ---------------------------------------------------

        /// <summary>
        /// Six cards: the 2×2 grid, the Sobre entry and the "Conteúdo educativo" entry.
        /// Both wide ones are deliberate additions the Figma frame does not draw (no
        /// bottom-bar tab exists for either), and the count is what catches a future
        /// edit that re-points one instead of adding it.
        /// </summary>
        [Test]
        public void TheGridAndBothWideEntriesAreAllDrawn()
        {
            Build();

            var cards = screen.Query<VisualElement>(className: "mv-feature-card").ToList();
            var wide = cards.Where(c => c.ClassListContains("mv-feature-card--wide")).ToList();

            Assert.That(cards.Count, Is.EqualTo(6));
            Assert.That(wide.Count, Is.EqualTo(2), "Sobre and Conteúdo educativo");
        }

        /// <summary>
        /// The learn card follows Sobre, carries the ViewModel's copy and its glyph, and
        /// is the LAST thing in the body — so the grid above it keeps setting the reading
        /// order.
        /// </summary>
        [Test]
        public void TheLearnCardIsTheLastOne_AndCarriesTheViewModelsCopy()
        {
            Build();

            var cards = screen.Query<VisualElement>(className: "mv-feature-card").ToList();
            var last = cards[cards.Count - 1];

            Assert.That(last.ClassListContains("mv-feature-card--wide"), Is.True);
            Assert.That(last.Q<Label>(className: "mv-feature-card__title").text,
                Is.EqualTo(vm.LearnFeature.Title));
            Assert.That(last.Q<Label>(className: "mv-feature-card__body").text,
                Is.EqualTo(vm.LearnFeature.Body));
            Assert.That(last.Q<MdIcon>(className: "mv-feature-card__glyph").Icon,
                Is.EqualTo(vm.LearnFeature.Icon));
        }

        /// <summary>The screen builds no user-visible string of its own — every one
        /// of them comes from the formatter through the ViewModel.</summary>
        [Test]
        public void TheStateCarriesNoCopyOfItsOwn()
        {
            var bare = new MvStateView();
            Assert.That(bare.Title, Is.Empty);
            Assert.That(bare.Body, Is.Empty);
            Assert.That(bare.ActionText, Is.Empty);
        }
    }
}
