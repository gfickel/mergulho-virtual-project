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
    /// ReportScreen is a VisualElement over a plain-C# ViewModel, so everything
    /// it decides for itself is reachable in EditMode: which sections exist at
    /// all, how a feed row is coloured, and — the part with real consequences —
    /// what each of the three submit outcomes does to the screen.
    ///
    /// <para>What is NOT tested here is input: EditMode has no panel, so a
    /// <c>Clickable</c> cannot be driven with a synthetic pointer event. Taps are
    /// therefore expressed as ViewModel calls (which is exactly what the handlers
    /// do), and the submit button goes through
    /// <see cref="ReportScreen.SubmitForTests"/>.</para>
    /// </summary>
    public class ReportScreenTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 28, 12, 32, 0, DateTimeKind.Utc);

        // ---- Fakes (mirrors of ReportViewModelTests' — same reason the shot
        // fixtures are mirrored: each set is free to serve its own purpose) -----

        sealed class FakeSpecies : ISpeciesCatalog
        {
            public List<SpeciesInfo> Items = new List<SpeciesInfo>
            {
                new SpeciesInfo { Key = "lemon_shark", DisplayName = "Tubarão-limão" },
                new SpeciesInfo { Key = "tiger_shark", DisplayName = "Tubarão-tigre" },
                new SpeciesInfo { Key = "nurse_shark", DisplayName = "Tubarão-lixa" },
            };

            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
            }
        }

        sealed class FakeReports : ISightingReports
        {
            public bool Accept = true;
            public List<SightingRecord> Pending = new List<SightingRecord>();
            public List<SightingRecord> Failed = new List<SightingRecord>();

#pragma warning disable 67 // The screen never subscribes; the ViewModel does.
            public event Action Changed;
#pragma warning restore 67

            public bool Submit(SightingDraft draft) => Accept;
            public IReadOnlyList<SightingRecord> ListPending() => Pending;
            public IReadOnlyList<SightingRecord> ListFailed() => Failed;
        }

        sealed class FakePicker : IPhotoPicker
        {
            public PhotoPickResult Next = PhotoPickResult.Picked("/tmp/mv-test-photo.jpg", 1024);
            public void PickPhoto(Action<PhotoPickResult> onResult) => onResult(Next);
        }

        sealed class FakeActiveBeach : IActiveBeach
        {
            public string Key = "Sueste Beach";
            public string ActiveBeachKey => Key;
#pragma warning disable 67
            public event Action<string> ActiveBeachChanged;
#pragma warning restore 67
        }

        sealed class FakeBeaches : IBeachCatalog
        {
            public List<BeachInfo> Items = new List<BeachInfo>
            {
                new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
            };
            public IReadOnlyList<BeachInfo> Beaches => Items;
        }

        FakeSpecies species;
        FakeReports reports;
        FakePicker picker;
        ReportViewModel vm;
        ReportScreen screen;
        List<string> routes;

        [SetUp]
        public void SetUp()
        {
            species = new FakeSpecies();
            reports = new FakeReports();
            picker = new FakePicker();
            routes = new List<string>();
        }

        [TearDown]
        public void TearDown() => vm?.Dispose();

        /// <summary>Builds the ViewModel + screen and enters it, which is the only
        /// state a user ever sees.</summary>
        void Build()
        {
            vm = new ReportViewModel(species, reports, picker, new FakeActiveBeach(), new FakeBeaches(),
                utcNow: () => Now, toLocalTime: d => d);
            screen = new ReportScreen(vm);
            screen.NavigationRequested += route => routes.Add(route);
            screen.OnEnter();
        }

        // ---- Queries --------------------------------------------------------

        List<MdChip> Chips() => screen.Query<MdChip>().ToList();
        List<MdCheckbox> Checkboxes() => screen.Query<MdCheckbox>().ToList();
        List<MvOptionCard> ProfileCards() => screen.Query<MvOptionCard>().ToList();
        List<VisualElement> PendingCards() =>
            screen.Query<VisualElement>(className: "mv-report__pending-card").ToList();
        Label Status() => screen.Q<Label>(className: "mv-report__status");
        Label PhotoError() => screen.Q<Label>(className: "mv-report__error");
        VisualElement PendingSection() => screen.Q<VisualElement>(className: "mv-report__pending-section");

        static bool Visible(VisualElement element) => element.style.display.value == DisplayStyle.Flex;

        // ---- Header ---------------------------------------------------------

        [Test]
        public void BackButton_IsHiddenOnTheTabRoot()
        {
            Build();
            var bar = screen.Q<MdTopAppBar>();

            Assert.That(screen.ShowBackButton, Is.False, "Avistamentos is a tab root — nothing to go back to");
            Assert.That(bar.NavigationIcon, Is.Empty);
            Assert.That(bar.Title, Is.EqualTo(ReportViewModel.ScreenTitle));
        }

        [Test]
        public void BackButton_AppearsWhenTheScreenIsPushedRatherThanTabbedTo()
        {
            Build();
            int backs = 0;
            screen.BackRequested += () => backs++;

            screen.ShowBackButton = true;

            var nav = screen.Q<MdIconButton>(className: MdTopAppBar.NavClassName);
            Assert.That(screen.Q<MdTopAppBar>().NavigationIcon, Is.EqualTo("arrow_back"));
            Assert.That(nav.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(backs, Is.Zero, "nothing was tapped");
        }

        // ---- Sections and their empty states --------------------------------

        [Test]
        public void SpeciesSection_IsHidden_WhenTheCatalogIsEmpty()
        {
            species.Items.Clear();
            Build();

            Assert.That(Visible(screen.Q("species-section")), Is.False);
            // The fixed size chips are unaffected — only the catalog-driven
            // section can vanish.
            Assert.That(Chips(), Has.Count.EqualTo(vm.SizeOptions.Count));
        }

        [Test]
        public void SpeciesSection_IsShown_WhenTheCatalogHasEntries()
        {
            Build();

            Assert.That(Visible(screen.Q("species-section")), Is.True);
            Assert.That(Chips(), Has.Count.EqualTo(species.Items.Count + vm.SizeOptions.Count));
            Assert.That(Chips()[0].Text, Is.EqualTo("Tubarão-limão"));
        }

        [Test]
        public void Chips_Checkboxes_AndProfileCards_PaintTheViewModelSelection()
        {
            Build();

            vm.SelectSpecies(1);
            vm.SelectSize(2);
            vm.ToggleBehaviour(0);
            vm.SelectProfile(1);

            var chips = Chips();
            Assert.That(chips[1].Selected, Is.True, "species 1");
            Assert.That(chips[0].Selected, Is.False);
            Assert.That(chips[species.Items.Count + 2].Selected, Is.True, "size 2");
            Assert.That(Checkboxes()[0].Checked, Is.True);
            Assert.That(Checkboxes()[1].Checked, Is.False);
            Assert.That(ProfileCards()[1].Selected, Is.True);
            Assert.That(ProfileCards()[0].Selected, Is.False, "the profile group is single-choice");
        }

        // ---- Pending feed ---------------------------------------------------

        [Test]
        public void PendingSection_IsHidden_WhenThereIsNothingInFlight()
        {
            Build();

            Assert.That(Visible(PendingSection()), Is.False);
            Assert.That(PendingCards(), Is.Empty);
        }

        [Test]
        public void PendingFeed_ShowsAFailedReportDistinctlyFromAQueuedOne()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "a",
                SpeciesLabel = "Tubarão-limão",
                BeachKey = "Sueste Beach",
                WhenUtc = Now.AddHours(-3),
                State = SightingState.Queued,
            });
            reports.Failed.Add(new SightingRecord
            {
                Id = "b",
                SpeciesLabel = "Tubarão-tigre",
                WhenUtc = Now.AddHours(-1),
                State = SightingState.Failed,
            });
            Build();

            var cards = PendingCards();
            Assert.That(Visible(PendingSection()), Is.True);
            Assert.That(cards, Has.Count.EqualTo(2));

            // Newest first (the ViewModel's order): the failed one leads.
            var failed = cards[0];
            var queued = cards[1];
            Assert.That(failed.Q<MvTag>().Variant, Is.EqualTo(MvTagVariant.Error));
            Assert.That(failed.Q<MvTag>().Text, Is.EqualTo(ReportFormatter.FailedLabel));
            Assert.That(failed.ClassListContains("mv-report__pending-card--failed"), Is.True,
                "a report that will never be delivered must not look like one that is merely queued");

            Assert.That(queued.Q<MvTag>().Variant, Is.EqualTo(MvTagVariant.Neutral));
            Assert.That(queued.Q<MvTag>().Text, Is.EqualTo(ReportFormatter.QueuedLabel));
            Assert.That(queued.ClassListContains("mv-report__pending-card--failed"), Is.False);
        }

        [Test]
        public void PendingRow_UsesTheFormatterStrings_AndNeverBuildsItsOwn()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "a",
                SpeciesLabel = null,             // a valid submission with no species
                BeachKey = "Sueste Beach",
                WhenUtc = Now,
                State = SightingState.WaitingForNetwork,
            });
            Build();

            var card = PendingCards()[0];
            var labels = card.Query<Label>().ToList();
            Assert.That(labels[0].text, Is.EqualTo(ReportFormatter.UnnamedSpeciesTitle));
            Assert.That(card.Q<MvTag>().Text, Is.EqualTo(ReportFormatter.OfflineLabel));
            Assert.That(card.Q<MvTag>().Variant, Is.EqualTo(MvTagVariant.Warning),
                "blocked on connectivity is not a neutral state and not a failure either");
            Assert.That(labels.Last().text, Does.Contain("Baía do Sueste"));
        }

        // ---- Photo ----------------------------------------------------------

        [Test]
        public void PickedPhoto_FillsTheGrid_EvenWhenTheFileCannotBeDecoded()
        {
            Build();
            vm.PickPhoto();   // the fake path does not exist, so no texture loads

            var media = screen.Q<MvMediaPicker>();
            Assert.That(vm.HasPhoto, Is.True);
            Assert.That(media.ItemCount, Is.EqualTo(1),
                "the report HAS a photo — the tile is the only way to remove it");
            Assert.That(media.AtCapacity, Is.True, "one photo this slice");
        }

        [Test]
        public void PhotoPickFailure_IsShownUnderThePicker()
        {
            Build();
            Assert.That(Visible(PhotoError()), Is.False);

            picker.Next = PhotoPickResult.Failed("sem permissão");
            vm.PickPhoto();

            Assert.That(Visible(PhotoError()), Is.True);
            Assert.That(PhotoError().text, Does.StartWith(ReportViewModel.PhotoPickFailedText));
        }

        [Test]
        public void CancellingThePicker_SaysNothing()
        {
            Build();
            picker.Next = PhotoPickResult.Cancelled();
            vm.PickPhoto();

            Assert.That(Visible(PhotoError()), Is.False);
            Assert.That(vm.HasPhoto, Is.False);
        }

        // ---- The three submit outcomes --------------------------------------

        [Test]
        public void SubmitWithoutAPhoto_KeepsTheUserHere_AndSaysWhy()
        {
            Build();
            screen.SubmitForTests();

            Assert.That(routes, Is.Empty, "nothing was queued, so there is nothing to leave for");
            Assert.That(Visible(Status()), Is.True);
            Assert.That(Status().text, Is.EqualTo(ReportViewModel.MissingPhotoText));
            Assert.That(Status().ClassListContains("mv-report__status--error"), Is.True);
        }

        [Test]
        public void SubmitRefusedByTheQueue_StaysPut_InAnErrorTone_WithTheFormIntact()
        {
            reports.Accept = false;
            Build();
            vm.PickPhoto();
            vm.SelectSpecies(0);

            screen.SubmitForTests();

            Assert.That(routes, Is.Empty,
                "navigating away would drop a report nobody is going to deliver");
            Assert.That(Status().text, Is.EqualTo(ReportViewModel.SubmitFailedText));
            Assert.That(Status().ClassListContains("mv-report__status--error"), Is.True);
            Assert.That(vm.HasPhoto, Is.True, "the form must survive a refusal");
            Assert.That(Chips()[0].Selected, Is.True);
        }

        [Test]
        public void SubmitAccepted_ShowsTheNoticeInANeutralTone_ResetsTheForm_AndLeaves()
        {
            Build();
            vm.PickPhoto();
            vm.SelectSpecies(0);
            vm.SelectSize(1);
            vm.ToggleBehaviour(2);
            vm.SelectProfile(0);
            vm.ReporterName = "Ana";

            screen.SubmitForTests();

            // Detached from any panel the exit is immediate; on screen the status
            // line is shown for SubmitExitDelayMs first.
            Assert.That(routes, Is.EqualTo(new[] { AppRoutes.Home }));
            Assert.That(Status().text, Is.EqualTo(ReportViewModel.SubmittingText));
            Assert.That(Status().ClassListContains("mv-report__status--error"), Is.False);

            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(Chips().Any(chip => chip.Selected), Is.False);
            Assert.That(Checkboxes().Any(box => box.Checked), Is.False);
            // The identity is the user's, not the report's — a second sighting in
            // the same session must not mean re-typing it.
            Assert.That(vm.ReporterName, Is.EqualTo("Ana"));
            Assert.That(ProfileCards()[0].Selected, Is.True);
        }

        /// <summary>
        /// Regression: MdCheckbox raises ValueChanged on a programmatic set too, so
        /// a handler that toggled blindly would re-check every box the moment the
        /// form reset painted them back to false — and the next report would ship
        /// behaviours the user never picked.
        /// </summary>
        [Test]
        public void ResettingTheForm_DoesNotReSelectTheBehavioursViaTheCheckboxes()
        {
            Build();
            vm.PickPhoto();
            vm.ToggleBehaviour(0);
            vm.ToggleBehaviour(1);
            Assert.That(vm.SelectedBehaviourCount, Is.EqualTo(2));

            screen.SubmitForTests();

            Assert.That(vm.SelectedBehaviourCount, Is.Zero);
            Assert.That(Checkboxes().Any(box => box.Checked), Is.False);
        }

        [Test]
        public void ReEntering_DropsThePreviousVisitsStatusLine()
        {
            Build();
            screen.SubmitForTests();          // leaves the missing-photo message up
            Assert.That(Visible(Status()), Is.True);

            screen.OnExit();
            screen.OnEnter();

            Assert.That(Visible(Status()), Is.False);
            Assert.That(vm.HasStatus, Is.False);
        }

        // ---- Context line ---------------------------------------------------

        [Test]
        public void BeachLine_NamesWhereTheReportWillBeFiled()
        {
            Build();
            var label = screen.Q<Label>(className: "mv-report__beach-label");
            Assert.That(label.text, Is.EqualTo("Baía do Sueste"));
        }

        [Test]
        public void BeachLine_IsHonestWhenGpsResolvedNothing()
        {
            vm = new ReportViewModel(species, reports, picker, null, null,
                utcNow: () => Now, toLocalTime: d => d);
            screen = new ReportScreen(vm);
            screen.OnEnter();

            var label = screen.Q<Label>(className: "mv-report__beach-label");
            Assert.That(label.text, Is.EqualTo(ReportViewModel.NoBeachText));
        }
    }
}
