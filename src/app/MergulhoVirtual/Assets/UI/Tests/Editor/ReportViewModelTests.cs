using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    public class ReportViewModelTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 28, 12, 32, 0, DateTimeKind.Utc);

        // ---- Fakes ----------------------------------------------------------

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
            public readonly List<SightingDraft> Submitted = new List<SightingDraft>();
            public List<SightingRecord> Pending = new List<SightingRecord>();
            public List<SightingRecord> Failed = new List<SightingRecord>();

            public event Action Changed;
            public void Raise() => Changed?.Invoke();
            public bool HasSubscribers => Changed != null;

            public bool Submit(SightingDraft draft)
            {
                Submitted.Add(draft);
                return Accept;
            }

            public IReadOnlyList<SightingRecord> ListPending() => Pending;
            public IReadOnlyList<SightingRecord> ListFailed() => Failed;
            /// <summary>Moves the record from Failed to Pending, like the real
            /// adapter does, so a test can assert the row actually changed state.</summary>
            public readonly List<string> Retried = new List<string>();
            public bool Retry(string id)
            {
                Retried.Add(id);
                var record = Failed.Find(r => r != null && r.Id == id);
                if (record == null) return false;
                Failed.Remove(record);
                record.State = SightingState.Queued;
                record.AttemptCount = 0;
                Pending.Add(record);
                return true;
            }
        }

        sealed class FakePicker : IPhotoPicker
        {
            public int Calls;
            Action<PhotoPickResult> pendingCallback;

            /// <summary>Result delivered synchronously; null = hold the callback
            /// so a test can deliver it later with <see cref="Deliver"/>.</summary>
            public PhotoPickResult Next;

            public void PickPhoto(Action<PhotoPickResult> onResult)
            {
                Calls++;
                if (Next != null) { var r = Next; Next = null; onResult(r); return; }
                pendingCallback = onResult;
            }

            public void Deliver(PhotoPickResult result)
            {
                var cb = pendingCallback;
                pendingCallback = null;
                cb?.Invoke(result);
            }
        }

        sealed class FakeActiveBeach : IActiveBeach
        {
            public string Key;
            public string ActiveBeachKey => Key;
            public event Action<string> ActiveBeachChanged;
            public void Raise() => ActiveBeachChanged?.Invoke(Key);
        }

        sealed class FakeBeaches : IBeachCatalog
        {
            public List<BeachInfo> Items = new List<BeachInfo>
            {
                new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
                new BeachInfo { Name = "Praia do Sancho" },
            };
            public IReadOnlyList<BeachInfo> Beaches => Items;
        }

        FakeSpecies species;
        FakeReports reports;
        FakePicker picker;
        FakeActiveBeach activeBeach;
        FakeBeaches beaches;

        [SetUp]
        public void SetUp()
        {
            species = new FakeSpecies();
            reports = new FakeReports();
            picker = new FakePicker();
            activeBeach = new FakeActiveBeach { Key = "Sueste Beach" };
            beaches = new FakeBeaches();
        }

        ReportViewModel NewVm() =>
            // Identity local-time conversion keeps assertions timezone-independent.
            new ReportViewModel(species, reports, picker, activeBeach, beaches,
                utcNow: () => Now, toLocalTime: d => d);

        void AttachPhoto(ReportViewModel vm, string path = "/tmp/photo.jpg", long size = 1024)
        {
            picker.Next = PhotoPickResult.Picked(path, size);
            vm.PickPhoto();
        }

        // ---- Option sets ----------------------------------------------------

        [Test]
        public void SpeciesOptions_ComeFromTheCatalogInOrder()
        {
            var vm = NewVm();
            Assert.That(vm.SpeciesOptions.Count, Is.EqualTo(3));
            Assert.That(vm.SpeciesOptions[0].Key, Is.EqualTo("lemon_shark"));
            Assert.That(vm.SpeciesOptions[0].Label, Is.EqualTo("Tubarão-limão"));
            Assert.That(vm.SpeciesOptions[2].Label, Is.EqualTo("Tubarão-lixa"));
            Assert.That(vm.HasSpeciesOptions, Is.True);
        }

        [Test]
        public void SpeciesOptions_DropEntriesWithNoLabel()
        {
            species.Items.Add(new SpeciesInfo { Key = "ghost", DisplayName = "  " });
            var vm = NewVm();
            Assert.That(vm.SpeciesOptions.Count, Is.EqualTo(3), "a blank chip is worse than no chip");
        }

        [Test]
        public void SpeciesOptions_EmptyCatalogIsARealState()
        {
            species.Items.Clear();
            var vm = NewVm();
            Assert.That(vm.SpeciesOptions, Is.Empty);
            Assert.That(vm.HasSpeciesOptions, Is.False);
        }

        [Test]
        public void SizeOptions_AreTheThreeFigmaBuckets()
        {
            var vm = NewVm();
            Assert.That(vm.SizeOptions.Count, Is.EqualTo(3));
            Assert.That(vm.SizeOptions[0].Label, Is.EqualTo("Menor que 1m"));
            Assert.That(vm.SizeOptions[1].Label, Is.EqualTo("1m - 2m"));
            Assert.That(vm.SizeOptions[2].Label, Is.EqualTo("Maior que 3m"));
        }

        [Test]
        public void BehaviourOptions_AreTheFourFigmaRows()
        {
            var vm = NewVm();
            Assert.That(vm.BehaviourOptions.Count, Is.EqualTo(4));
            Assert.That(vm.BehaviourOptions[0].Label, Is.EqualTo("Calmo e inofensivo"));
            Assert.That(vm.BehaviourOptions[1].Label, Is.EqualTo("Nadando perto da praia"));
            Assert.That(vm.BehaviourOptions[2].Label, Is.EqualTo("Em processo de alimentação"));
            Assert.That(vm.BehaviourOptions[3].Label, Is.EqualTo("Comportamento arredio/agressivo"));
        }

        [Test]
        public void ProfileOptions_AreTheTwoFigmaCards()
        {
            var vm = NewVm();
            Assert.That(vm.ProfileOptions.Count, Is.EqualTo(2));
            Assert.That(vm.ProfileOptions[0].Key, Is.EqualTo(ReportViewModel.ProfileKeyTourist));
            Assert.That(vm.ProfileOptions[0].Label, Is.EqualTo("Turista / Visitante"));
            Assert.That(vm.ProfileOptions[1].Key, Is.EqualTo(ReportViewModel.ProfileKeyGuide));
            Assert.That(vm.ProfileOptions[1].Label, Is.EqualTo("Condutor / Guia"));
        }

        [Test]
        public void EveryOptionKey_IsDistinctWithinItsSet()
        {
            var vm = NewVm();
            AssertDistinctKeys(vm.SizeOptions);
            AssertDistinctKeys(vm.BehaviourOptions);
            AssertDistinctKeys(vm.ProfileOptions);
        }

        static void AssertDistinctKeys(IReadOnlyList<ReportOption> options)
        {
            var seen = new HashSet<string>();
            foreach (var option in options)
            {
                Assert.That(option.Key, Is.Not.Null.And.Not.Empty);
                Assert.That(seen.Add(option.Key), Is.True, "duplicate key " + option.Key);
            }
        }

        // ---- Single-select behaviour ---------------------------------------

        [Test]
        public void SelectSpecies_SelectsAndExposesKeyAndLabel()
        {
            var vm = NewVm();
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(-1));

            vm.SelectSpecies(1);

            Assert.That(vm.HasSpecies, Is.True);
            Assert.That(vm.SelectedSpeciesKey, Is.EqualTo("tiger_shark"));
            Assert.That(vm.SelectedSpeciesLabel, Is.EqualTo("Tubarão-tigre"));
        }

        [Test]
        public void SelectSpecies_ReTappingTheSelectedChipClearsIt()
        {
            var vm = NewVm();
            vm.SelectSpecies(1);
            vm.SelectSpecies(1);
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.SelectedSpeciesKey, Is.Null);
        }

        [Test]
        public void SelectSpecies_OutOfRangeIsANoOp()
        {
            var vm = NewVm();
            vm.SelectSpecies(0);
            vm.SelectSpecies(99);
            vm.SelectSpecies(-1);
            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(0));
        }

        [Test]
        public void SelectSpecies_RaisesChanged()
        {
            var vm = NewVm();
            int changes = 0;
            vm.Changed += () => changes++;
            vm.SelectSpecies(0);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void SelectSize_SelectsAndReTapClears()
        {
            var vm = NewVm();
            vm.SelectSize(2);
            Assert.That(vm.SelectedSizeKey, Is.EqualTo(ReportViewModel.SizeKeyOver3m));
            vm.SelectSize(2);
            Assert.That(vm.HasSize, Is.False);
            Assert.That(vm.SelectedSizeKey, Is.Null);
        }

        [Test]
        public void SelectProfile_SelectsAndReTapClears()
        {
            var vm = NewVm();
            vm.SelectProfile(1);
            Assert.That(vm.SelectedProfileKey, Is.EqualTo(ReportViewModel.ProfileKeyGuide));
            vm.SelectProfile(1);
            Assert.That(vm.HasProfile, Is.False);
        }

        // ---- Multi-select behaviour ----------------------------------------

        [Test]
        public void ToggleBehaviour_ChecksAndUnchecksIndependently()
        {
            var vm = NewVm();
            vm.ToggleBehaviour(0);
            vm.ToggleBehaviour(2);

            Assert.That(vm.IsBehaviourSelected(0), Is.True);
            Assert.That(vm.IsBehaviourSelected(1), Is.False);
            Assert.That(vm.IsBehaviourSelected(2), Is.True);
            Assert.That(vm.SelectedBehaviourCount, Is.EqualTo(2));

            vm.ToggleBehaviour(0);
            Assert.That(vm.IsBehaviourSelected(0), Is.False);
            Assert.That(vm.SelectedBehaviourCount, Is.EqualTo(1));
        }

        [Test]
        public void SelectedBehaviourKeys_AreInOptionOrderNotTapOrder()
        {
            var vm = NewVm();
            vm.ToggleBehaviour(3);
            vm.ToggleBehaviour(1);
            vm.ToggleBehaviour(0);

            Assert.That(vm.SelectedBehaviourKeys, Is.EqualTo(new[]
            {
                ReportViewModel.BehaviourKeyCalm,
                ReportViewModel.BehaviourKeyNearShore,
                ReportViewModel.BehaviourKeyAggressive,
            }));
        }

        [Test]
        public void ToggleBehaviour_OutOfRangeIsANoOp()
        {
            var vm = NewVm();
            vm.ToggleBehaviour(-1);
            vm.ToggleBehaviour(4);
            Assert.That(vm.SelectedBehaviourCount, Is.EqualTo(0));
        }

        // ---- Photo ----------------------------------------------------------

        [Test]
        public void PickPhoto_Picked_AttachesItAndClearsErrors()
        {
            var vm = NewVm();
            AttachPhoto(vm, "/tmp/a.jpg", 2048);

            Assert.That(vm.HasPhoto, Is.True);
            Assert.That(vm.PhotoPath, Is.EqualTo("/tmp/a.jpg"));
            Assert.That(vm.PhotoCount, Is.EqualTo(1));
            Assert.That(vm.HasPhotoError, Is.False);
            Assert.That(vm.IsPickingPhoto, Is.False);
        }

        [Test]
        public void PickPhoto_CancelledSaysNothing()
        {
            var vm = NewVm();
            picker.Next = PhotoPickResult.Cancelled();
            vm.PickPhoto();

            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(vm.HasPhotoError, Is.False, "cancelling is not an error");
            Assert.That(vm.IsPickingPhoto, Is.False);
        }

        [Test]
        public void PickPhoto_FailureIsReported()
        {
            var vm = NewVm();
            picker.Next = PhotoPickResult.Failed("no gallery");
            vm.PickPhoto();

            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(vm.PhotoErrorText, Does.StartWith(ReportViewModel.PhotoPickFailedText));
            Assert.That(vm.PhotoErrorText, Does.Contain("no gallery"));
        }

        [Test]
        public void PickPhoto_OverTheSizeLimitIsRejectedWithBothNumbers()
        {
            var vm = NewVm();
            picker.Next = PhotoPickResult.Picked("/tmp/huge.jpg", ReportViewModel.MaxPhotoBytes + 1);
            vm.PickPhoto();

            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(vm.PhotoErrorText, Does.Contain("20 MB"));
        }

        [Test]
        public void PickPhoto_AtTheLimitExactlyIsAccepted()
        {
            var vm = NewVm();
            AttachPhoto(vm, "/tmp/exact.jpg", ReportViewModel.MaxPhotoBytes);
            Assert.That(vm.HasPhoto, Is.True);
        }

        [Test]
        public void PickPhoto_IsRefusedOnceTheCapIsReached()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            Assert.That(vm.CanAddPhoto, Is.False, "one photo this slice");

            vm.PickPhoto();
            Assert.That(picker.Calls, Is.EqualTo(1), "the picker must not reopen");
        }

        [Test]
        public void PickPhoto_IsPickingWhileTheGalleryIsOpen()
        {
            var vm = NewVm();
            vm.PickPhoto();                       // FakePicker holds the callback
            Assert.That(vm.IsPickingPhoto, Is.True);
            Assert.That(vm.CanAddPhoto, Is.False, "no second picker while one is open");

            picker.Deliver(PhotoPickResult.Picked("/tmp/late.jpg", 10));
            Assert.That(vm.IsPickingPhoto, Is.False);
            Assert.That(vm.PhotoPath, Is.EqualTo("/tmp/late.jpg"));
        }

        [Test]
        public void PickPhoto_ResultArrivingAfterTheFormWasResetIsIgnored()
        {
            var vm = NewVm();
            vm.PickPhoto();                       // the fake holds the callback
            vm.ResetSightingFields();             // the screen resets while the gallery is open

            picker.Deliver(PhotoPickResult.Picked("/tmp/stale.jpg", 10));

            Assert.That(vm.HasPhoto, Is.False, "a pick the user has since abandoned must not attach itself");
            Assert.That(vm.IsPickingPhoto, Is.False);
        }

        [Test]
        public void PickPhoto_AfterAResetTheNextPickStillWorks()
        {
            var vm = NewVm();
            vm.PickPhoto();
            vm.ResetSightingFields();
            picker.Deliver(PhotoPickResult.Picked("/tmp/stale.jpg", 10));

            AttachPhoto(vm, "/tmp/fresh.jpg");

            Assert.That(vm.PhotoPath, Is.EqualTo("/tmp/fresh.jpg"));
        }

        [Test]
        public void RemovePhoto_ClearsEverythingAboutIt()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            vm.RemovePhoto();

            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(vm.PhotoPath, Is.Null);
            Assert.That(vm.PhotoCount, Is.EqualTo(0));
            Assert.That(vm.CanAddPhoto, Is.True);
        }

        // ---- Validation ------------------------------------------------------

        [Test]
        public void CanSubmit_RequiresOnlyAPhoto()
        {
            var vm = NewVm();
            Assert.That(vm.CanSubmit, Is.False);
            Assert.That(vm.ValidationText, Is.EqualTo(ReportViewModel.MissingPhotoText));

            AttachPhoto(vm);

            Assert.That(vm.CanSubmit, Is.True, "species, size, behaviour, identity and beach are all optional");
            Assert.That(vm.ValidationText, Is.Null);
        }

        [Test]
        public void CanSubmit_DoesNotRequireABeach()
        {
            activeBeach.Key = null;
            var vm = NewVm();
            AttachPhoto(vm);

            Assert.That(vm.HasBeach, Is.False);
            Assert.That(vm.BeachText, Is.EqualTo(ReportViewModel.NoBeachText));
            Assert.That(vm.CanSubmit, Is.True);
        }

        [Test]
        public void Submit_WithoutAPhoto_KeepsTheUserOnTheScreen()
        {
            var vm = NewVm();
            bool submitted = false;
            vm.Submitted += () => submitted = true;

            Assert.That(vm.Submit(), Is.False);
            Assert.That(submitted, Is.False, "an incomplete form must not navigate away");
            Assert.That(vm.StatusText, Is.EqualTo(ReportViewModel.MissingPhotoText));
            Assert.That(reports.Submitted, Is.Empty);
        }

        // ---- Submit ----------------------------------------------------------

        [Test]
        public void Submit_BuildsADraftFromEveryField()
        {
            var vm = NewVm();
            AttachPhoto(vm, "/tmp/sighting.jpg");
            vm.SelectSpecies(0);
            vm.SelectSize(1);
            vm.ToggleBehaviour(0);
            vm.ToggleBehaviour(2);
            vm.ReporterName = "  Maria  ";
            vm.ReporterEmail = " maria@example.com ";
            vm.SelectProfile(1);

            Assert.That(vm.Submit(), Is.True);

            var draft = reports.Submitted[0];
            Assert.That(draft.PhotoPath, Is.EqualTo("/tmp/sighting.jpg"));
            Assert.That(draft.BeachKey, Is.EqualTo("Sueste Beach"), "the places.json key, not the label");
            Assert.That(draft.WhenUtc, Is.EqualTo(Now), "the injected clock, not DateTime.UtcNow");
            Assert.That(draft.SpeciesKey, Is.EqualTo("lemon_shark"));
            Assert.That(draft.SpeciesLabel, Is.EqualTo("Tubarão-limão"));
            Assert.That(draft.SizeBucket, Is.EqualTo(ReportViewModel.SizeKey1to2m));
            Assert.That(draft.BehaviourKeys, Is.EqualTo(new[]
            {
                ReportViewModel.BehaviourKeyCalm,
                ReportViewModel.BehaviourKeyFeeding,
            }));
            Assert.That(draft.ReporterName, Is.EqualTo("Maria"), "trimmed");
            Assert.That(draft.ReporterEmail, Is.EqualTo("maria@example.com"));
            Assert.That(draft.ProfileKey, Is.EqualTo(ReportViewModel.ProfileKeyGuide));
        }

        [Test]
        public void Submit_UnansweredFieldsAreNullNotEmptyStrings()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            vm.ReporterName = "   ";

            vm.Submit();

            var draft = reports.Submitted[0];
            Assert.That(draft.SpeciesKey, Is.Null);
            Assert.That(draft.SpeciesLabel, Is.Null);
            Assert.That(draft.SizeBucket, Is.Null);
            Assert.That(draft.BehaviourKeys, Is.Empty);
            Assert.That(draft.ReporterName, Is.Null, "whitespace is not a name");
            Assert.That(draft.ReporterEmail, Is.Null);
            Assert.That(draft.ProfileKey, Is.Null);
        }

        [Test]
        public void Submit_WithNoBeachPassesNull()
        {
            activeBeach.Key = null;
            var vm = NewVm();
            AttachPhoto(vm);
            vm.Submit();
            Assert.That(reports.Submitted[0].BeachKey, Is.Null);
        }

        [Test]
        public void Submit_Accepted_ResetsTheSightingFieldsAndNavigatesAway()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            vm.SelectSpecies(0);
            vm.SelectSize(0);
            vm.ToggleBehaviour(1);

            bool submitted = false;
            vm.Submitted += () => submitted = true;

            Assert.That(vm.Submit(), Is.True);

            Assert.That(submitted, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo(ReportViewModel.SubmittingText));
            Assert.That(vm.LastSubmitAccepted, Is.True);
            Assert.That(vm.HasPhoto, Is.False);
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.HasSize, Is.False);
            Assert.That(vm.SelectedBehaviourCount, Is.EqualTo(0));
        }

        [Test]
        public void Submit_Accepted_KeepsTheIdentityForTheNextReport()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            vm.ReporterName = "Maria";
            vm.ReporterEmail = "maria@example.com";
            vm.SelectProfile(0);

            vm.Submit();

            Assert.That(vm.ReporterName, Is.EqualTo("Maria"));
            Assert.That(vm.ReporterEmail, Is.EqualTo("maria@example.com"));
            Assert.That(vm.SelectedProfileKey, Is.EqualTo(ReportViewModel.ProfileKeyTourist));
        }

        [Test]
        public void ClearIdentity_DropsNameEmailAndProfile()
        {
            var vm = NewVm();
            vm.ReporterName = "Maria";
            vm.ReporterEmail = "maria@example.com";
            vm.SelectProfile(0);

            vm.ClearIdentity();

            Assert.That(vm.ReporterName, Is.Null);
            Assert.That(vm.ReporterEmail, Is.Null);
            Assert.That(vm.HasProfile, Is.False);
        }

        [Test]
        public void Submit_Refused_StillNavigatesAwayButKeepsTheForm()
        {
            reports.Accept = false;
            var vm = NewVm();
            AttachPhoto(vm, "/tmp/kept.jpg");
            vm.SelectSpecies(0);

            bool submitted = false;
            vm.Submitted += () => submitted = true;

            Assert.That(vm.Submit(), Is.False);

            Assert.That(submitted, Is.True, "the user is never blocked on infrastructure");
            Assert.That(vm.LastSubmitAccepted, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo(ReportViewModel.SubmitFailedText));
            Assert.That(vm.PhotoPath, Is.EqualTo("/tmp/kept.jpg"), "nothing the user entered is lost");
            Assert.That(vm.SelectedSpeciesKey, Is.EqualTo("lemon_shark"));
        }

        [Test]
        public void Submit_WithNoService_DoesNotThrow()
        {
            var vm = new ReportViewModel(species, null, picker, activeBeach, beaches,
                utcNow: () => Now, toLocalTime: d => d);
            AttachPhoto(vm);

            Assert.That(vm.Submit(), Is.False);
            Assert.That(vm.LastSubmitAccepted, Is.False);
            Assert.That(vm.PendingRows, Is.Empty);
        }

        // ---- Pending feed ----------------------------------------------------

        [Test]
        public void PendingRows_EmptyWhenNothingIsQueued()
        {
            var vm = NewVm();
            Assert.That(vm.PendingRows, Is.Empty);
            Assert.That(vm.HasPending, Is.False);
            Assert.That(vm.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void PendingRows_RenderTheFigmaCard()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "abc",
                SpeciesLabel = "Tubarão-limão",
                BeachKey = "Sueste Beach",
                WhenUtc = new DateTime(2026, 9, 28, 9, 32, 0, DateTimeKind.Utc),
                PhotoPath = "/tmp/a.jpg",
                State = SightingState.Queued,
            });

            var vm = NewVm();
            Assert.That(vm.HasPending, Is.True);
            var row = vm.PendingRows[0];
            Assert.That(row.Id, Is.EqualTo("abc"));
            Assert.That(row.TitleText, Is.EqualTo("Tubarão-limão"));
            Assert.That(row.StatusText, Is.EqualTo("PENDENTE"));
            Assert.That(row.CaptionText, Is.EqualTo("Hoje, 9:32 · Baía do Sueste"),
                "the beach's pt-BR label, not the places.json key");
            Assert.That(row.PhotoPath, Is.EqualTo("/tmp/a.jpg"));
        }

        [Test]
        public void PendingRows_WithoutASpeciesUseTheNeutralTitle()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "x", WhenUtc = Now, State = SightingState.Queued,
            });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].TitleText, Is.EqualTo(ReportFormatter.UnnamedSpeciesTitle));
        }

        [Test]
        public void PendingRows_WithoutABeachDropTheBeachHalfOfTheCaption()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "x", WhenUtc = new DateTime(2026, 9, 28, 9, 32, 0, DateTimeKind.Utc),
                State = SightingState.Queued,
            });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].CaptionText, Is.EqualTo("Hoje, 9:32"));
        }

        [Test]
        public void PendingRows_UnknownBeachKeyFallsBackToTheKey()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "x", BeachKey = "Atlantis", WhenUtc = Now, State = SightingState.Queued,
            });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].CaptionText, Does.EndWith("Atlantis"));
        }

        [Test]
        public void PendingRows_BeachWithNoDisplayNameFallsBackToItsKey()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "x", BeachKey = "Praia do Sancho", WhenUtc = Now, State = SightingState.Queued,
            });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].CaptionText, Does.EndWith("Praia do Sancho"));
        }

        [Test]
        public void PendingRows_IncludeFailedReportsNewestFirst()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "old", SpeciesLabel = "Antigo",
                WhenUtc = Now.AddHours(-3), State = SightingState.Retrying,
            });
            reports.Failed.Add(new SightingRecord
            {
                Id = "dead", SpeciesLabel = "Rejeitado",
                WhenUtc = Now.AddMinutes(-1), State = SightingState.Failed,
            });

            var vm = NewVm();

            Assert.That(vm.PendingCount, Is.EqualTo(2));
            Assert.That(vm.PendingRows[0].Id, Is.EqualTo("dead"), "newest first");
            Assert.That(vm.PendingRows[0].StatusText, Is.EqualTo("Falhou"));
            Assert.That(vm.PendingRows[0].State, Is.EqualTo(SightingState.Failed));
            Assert.That(vm.PendingRows[1].Id, Is.EqualTo("old"));
            Assert.That(vm.PendingRows[1].StatusText, Is.EqualTo("Tentando de novo"));
        }

        [Test]
        public void PendingRows_OfflineStateHasItsOwnPill()
        {
            reports.Pending.Add(new SightingRecord
            {
                Id = "x", WhenUtc = Now, State = SightingState.WaitingForNetwork,
            });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].StatusText, Is.EqualTo("Sem conexão"));
        }

        [Test]
        public void PendingFeed_FollowsTheServicesChangedEvent()
        {
            var vm = NewVm();
            Assert.That(vm.PendingRows, Is.Empty);

            reports.Pending.Add(new SightingRecord
            {
                Id = "x", SpeciesLabel = "Novo", WhenUtc = Now, State = SightingState.Queued,
            });

            int changes = 0;
            vm.Changed += () => changes++;
            reports.Raise();

            Assert.That(vm.PendingCount, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void Refresh_ClearsTheStatusAndRereadsTheFeed()
        {
            var vm = NewVm();
            AttachPhoto(vm);
            vm.Submit();
            Assert.That(vm.HasStatus, Is.True);

            reports.Pending.Add(new SightingRecord
            {
                Id = "x", WhenUtc = Now, State = SightingState.Queued,
            });
            vm.Refresh();

            Assert.That(vm.HasStatus, Is.False, "the previous submit's notice must not outlive the visit");
            Assert.That(vm.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_UnsubscribesFromTheService()
        {
            var vm = NewVm();
            Assert.That(reports.HasSubscribers, Is.True);
            vm.Dispose();
            Assert.That(reports.HasSubscribers, Is.False);
            Assert.DoesNotThrow(() => vm.Dispose(), "Dispose is idempotent");
        }

        // ---- Retrying a failed report ---------------------------------------

        /// <summary>
        /// Only a failed row offers a retry. Queued / retrying / offline rows are
        /// already going to be tried again by the queue, so a button there would
        /// promise what is happening anyway.
        /// </summary>
        [Test]
        public void OnlyFailedRows_OfferARetry()
        {
            foreach (var state in new[]
                     {
                         SightingState.Queued, SightingState.Retrying, SightingState.WaitingForNetwork,
                     })
            {
                reports.Pending.Clear();
                reports.Failed.Clear();
                reports.Pending.Add(new SightingRecord { Id = "p", WhenUtc = Now, State = state });
                var open = NewVm();
                Assert.That(open.PendingRows[0].CanRetry, Is.False, state.ToString());
                open.Dispose();
            }

            reports.Pending.Clear();
            reports.Failed.Clear();
            reports.Failed.Add(new SightingRecord { Id = "f", WhenUtc = Now, State = SightingState.Failed });
            var vm = NewVm();
            Assert.That(vm.PendingRows[0].CanRetry, Is.True);
            Assert.That(vm.PendingRows[0].RetryText, Is.EqualTo(ReportFormatter.RetryLabel));
        }

        [Test]
        public void RetryPending_HandsTheRowIdToTheService_AndRebuildsTheFeed()
        {
            reports.Failed.Add(new SightingRecord
            {
                Id = "f1",
                SpeciesLabel = "Tubarão-limão",
                WhenUtc = Now,
                State = SightingState.Failed,
                AttemptCount = 3,
            });
            var vm = NewVm();
            int changes = 0;
            vm.Changed += () => changes++;

            Assert.That(vm.RetryPending("f1"), Is.True);

            Assert.That(reports.Retried, Is.EqualTo(new[] { "f1" }));
            Assert.That(changes, Is.GreaterThanOrEqualTo(1), "the row must repaint in the same frame as the tap");
            Assert.That(vm.PendingRows.Count, Is.EqualTo(1));
            Assert.That(vm.PendingRows[0].State, Is.EqualTo(SightingState.Queued));
            Assert.That(vm.PendingRows[0].CanRetry, Is.False, "it is queued now, not failed");
            Assert.That(vm.PendingRows[0].StatusText, Is.EqualTo(ReportFormatter.QueuedLabel));
        }

        [Test]
        public void RetryPending_WithAnUnknownId_ReportsFalseAndLeavesTheFeedAlone()
        {
            reports.Failed.Add(new SightingRecord { Id = "f1", WhenUtc = Now, State = SightingState.Failed });
            var vm = NewVm();

            Assert.That(vm.RetryPending("nope"), Is.False);
            Assert.That(vm.PendingRows.Count, Is.EqualTo(1));
            Assert.That(vm.PendingRows[0].State, Is.EqualTo(SightingState.Failed));
        }

        [Test]
        public void RetryPending_WithNoService_IsASafeNoOp()
        {
            var vm = new ReportViewModel(species, null, picker, new FakeActiveBeach(), new FakeBeaches(),
                utcNow: () => Now, toLocalTime: d => d);
            Assert.That(vm.RetryPending("anything"), Is.False);
            vm.Dispose();
        }
    }
}
