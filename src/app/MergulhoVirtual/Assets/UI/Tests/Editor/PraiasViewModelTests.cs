using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The Praias slice's ViewModel — the glue between "which beach is the user
    /// at" and the one <see cref="BeachDetailViewModel"/> both Praias screens
    /// read. Three things are worth pinning here and are all easy to break:
    /// that a null or unknown active beach closes the detail instead of leaving
    /// a stale one open, that the selector maps an index to a <b>key</b> and
    /// never to a label, and that the landing's card never renders a beach the
    /// detail is not actually showing.
    /// </summary>
    public class PraiasViewModelTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 15, 17, 30, 0, DateTimeKind.Utc);

        sealed class FakeCatalog : IBeachCatalog
        {
            public IReadOnlyList<BeachInfo> Beaches { get; set; } = new List<BeachInfo>();
        }

        sealed class FakeContent : IBeachContent
        {
            public readonly Dictionary<string, BeachContent> Entries =
                new Dictionary<string, BeachContent>(StringComparer.Ordinal);

            public BeachContent ForBeach(string beachName) =>
                Entries.TryGetValue(beachName ?? "", out var c) ? c : BeachContent.EmptyFor(beachName);

            public bool TryGetContent(string beachName, out BeachContent content)
            {
                if (Entries.TryGetValue(beachName ?? "", out content)) return true;
                content = BeachContent.EmptyFor(beachName);
                return false;
            }
        }

        sealed class FakeSpecies : ISpeciesCatalog
        {
            readonly List<SpeciesInfo> all = new List<SpeciesInfo>();
            public IReadOnlyList<SpeciesInfo> Species => all;
            public void Add(SpeciesInfo info) => all.Add(info);
            public SpeciesInfo Find(string key)
            {
                foreach (var info in all) if (info.Key == key) return info;
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
#pragma warning disable 67
            public event Action<ConditionsData> Changed;
#pragma warning restore 67
        }

        sealed class FakeOverride : IBeachOverride
        {
            public string LastSet;
            public int SetCalls;
            public int ClearCalls;

            public void SetOverride(string beachName) { LastSet = beachName; SetCalls++; }
            public void ClearOverride() { LastSet = null; ClearCalls++; }
        }

        sealed class FakeActiveBeach : IActiveBeach
        {
            public string ActiveBeachKey { get; private set; }
            public event Action<string> ActiveBeachChanged;

            public FakeActiveBeach(string initial = null) => ActiveBeachKey = initial;

            public void Set(string key)
            {
                ActiveBeachKey = key;
                ActiveBeachChanged?.Invoke(key);
            }
        }

        FakeCatalog catalog;
        FakeContent content;
        FakeSpecies species;
        FakeTides tides;
        FakeConditions conditions;
        FakeOverride beachOverride;
        FakeActiveBeach activeBeach;

        BeachDetailViewModel detail;
        BeachesViewModel beaches;

        PraiasViewModel NewVm(string initialActiveBeach = null)
        {
            activeBeach = new FakeActiveBeach(initialActiveBeach);
            detail = new BeachDetailViewModel(catalog, content, species, tides,
                utcNow: () => Now, toLocalTime: d => d);
            beaches = new BeachesViewModel(catalog, conditions, tides, beachOverride,
                utcNow: () => Now, toLocalTime: d => d);
            return new PraiasViewModel(detail, beaches, activeBeach);
        }

        [SetUp]
        public void SetUp()
        {
            catalog = new FakeCatalog
            {
                Beaches = new List<BeachInfo>
                {
                    new BeachInfo { Name = "Praia do Sancho", DisplayName = "Praia do Sancho", ImageName = "praia_do_sancho" },
                    // The key/label split: an English key with a pt-BR label.
                    new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
                    new BeachInfo { Name = "Praia do Meio", DisplayName = "Praia do Meio" },
                },
            };
            content = new FakeContent();
            content.Entries["Sueste Beach"] = new BeachContent
            {
                BeachName = "Sueste Beach",
                EnvironmentTags = new[] { "Mar de fora" },
            };
            species = new FakeSpecies();
            tides = new FakeTides();
            conditions = new FakeConditions();
            beachOverride = new FakeOverride();
        }

        [TearDown]
        public void TearDown()
        {
            detail?.Dispose();
            beaches?.Dispose();
        }

        // ---- Binding to the active beach ------------------------------------

        [Test]
        public void Construction_OpensTheActiveBeach()
        {
            var vm = NewVm("Sueste Beach");

            Assert.That(vm.HasActiveBeach, Is.True);
            Assert.That(vm.ActiveBeachKey, Is.EqualTo("Sueste Beach"));
            Assert.That(vm.Detail.HasBeach, Is.True);
            Assert.That(vm.Detail.BeachKey, Is.EqualTo("Sueste Beach"));
        }

        [Test]
        public void NoActiveBeach_LeavesTheDetailClosed()
        {
            var vm = NewVm();

            Assert.That(vm.HasActiveBeach, Is.False);
            Assert.That(vm.ActiveBeachKey, Is.Null);
            Assert.That(vm.Detail.HasBeach, Is.False);
        }

        [Test]
        public void ActiveBeachChanged_RepointsTheDetailAndRaisesChanged()
        {
            var vm = NewVm("Sueste Beach");
            int raised = 0;
            vm.Changed += () => raised++;

            activeBeach.Set("Praia do Meio");

            Assert.That(vm.Detail.BeachKey, Is.EqualTo("Praia do Meio"));
            Assert.That(raised, Is.GreaterThan(0));
        }

        [Test]
        public void ActiveBeachCleared_ClosesTheDetail()
        {
            var vm = NewVm("Sueste Beach");

            activeBeach.Set(null);

            Assert.That(vm.HasActiveBeach, Is.False);
            Assert.That(vm.Detail.HasBeach, Is.False);
        }

        /// <summary>
        /// A key the catalog does not know (places.json re-keyed under a stale
        /// override, say) must CLOSE the detail, not silently leave the previous
        /// beach open — BeachDetailViewModel.ShowBeach is a no-op for an unknown
        /// key, so this is the one place that difference is caught.
        /// </summary>
        [Test]
        public void UnknownActiveBeach_ClosesTheDetailRatherThanKeepingTheOldOne()
        {
            var vm = NewVm("Sueste Beach");

            activeBeach.Set("Praia Inexistente");

            Assert.That(vm.Detail.HasBeach, Is.False);
            Assert.That(vm.HasActiveBeach, Is.False);
            Assert.That(vm.LocationTitleText, Is.EqualTo(PraiasViewModel.ChooseBeachLabel));
        }

        [Test]
        public void SyncToActiveBeach_IsIdempotentAndAlwaysRepaints()
        {
            var vm = NewVm("Sueste Beach");
            int raised = 0;
            vm.Changed += () => raised++;

            vm.SyncToActiveBeach();
            vm.SyncToActiveBeach();

            Assert.That(vm.Detail.BeachKey, Is.EqualTo("Sueste Beach"));
            Assert.That(raised, Is.GreaterThanOrEqualTo(2), "every sync repaints, even when nothing moved");
        }

        [Test]
        public void DetailChanges_ForwardToChanged()
        {
            var vm = NewVm("Sueste Beach");
            int raised = 0;
            vm.Changed += () => raised++;

            vm.Detail.NotifyTimePassed();

            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_StopsForwarding()
        {
            var vm = NewVm("Sueste Beach");
            vm.Dispose();
            int raised = 0;
            vm.Changed += () => raised++;

            vm.Detail.NotifyTimePassed();
            activeBeach.Set("Praia do Meio");

            Assert.That(raised, Is.Zero);
        }

        // ---- Location card --------------------------------------------------

        [Test]
        public void LocationCard_ShowsTheDisplayNameNeverTheKey()
        {
            var vm = NewVm("Sueste Beach");

            Assert.That(vm.LocationTitleText, Is.EqualTo("Baía do Sueste"));
            Assert.That(vm.LocationTitleText, Is.Not.EqualTo("Sueste Beach"));
            Assert.That(vm.HasYouAreAtLabel, Is.True);
            Assert.That(vm.YouAreAtText, Is.EqualTo(BeachDetailViewModel.YouAreAtLabel));
            Assert.That(vm.RegionText, Is.EqualTo(BeachDetailViewModel.RegionLabel));
        }

        /// <summary>
        /// With no beach the card becomes an action, so the "Você está em"
        /// caption must go — captioning "Escolha uma praia" with it would read as
        /// a claim about where the user is.
        /// </summary>
        [Test]
        public void LocationCard_WithoutABeach_IsTheSelectorPrompt()
        {
            var vm = NewVm();

            Assert.That(vm.LocationTitleText, Is.EqualTo(PraiasViewModel.ChooseBeachLabel));
            Assert.That(vm.HasYouAreAtLabel, Is.False);
        }

        // ---- Selector -------------------------------------------------------

        [Test]
        public void SelectorChoices_AreAutomaticPlusEveryBeachAsLabels()
        {
            var vm = NewVm("Sueste Beach");

            Assert.That(vm.SelectorChoices.Count, Is.EqualTo(catalog.Beaches.Count + 1));
            Assert.That(vm.SelectorChoices[0], Is.EqualTo(BeachesViewModel.AutoOptionLabel));
            Assert.That(vm.SelectorChoices[1], Is.EqualTo("Praia do Sancho"));
            Assert.That(vm.SelectorChoices[2], Is.EqualTo("Baía do Sueste"));
            Assert.That(vm.SelectorChoices[3], Is.EqualTo("Praia do Meio"));
        }

        [Test]
        public void SelectBeach_PinsTheKeyNotTheLabel()
        {
            var vm = NewVm();

            vm.SelectBeach(2);

            Assert.That(beachOverride.SetCalls, Is.EqualTo(1));
            Assert.That(beachOverride.LastSet, Is.EqualTo("Sueste Beach"));
            Assert.That(vm.SelectorIndex, Is.EqualTo(2));
        }

        [Test]
        public void SelectBeach_Zero_HandsControlBackToGps()
        {
            var vm = NewVm();
            vm.SelectBeach(2);

            vm.SelectBeach(0);

            Assert.That(beachOverride.ClearCalls, Is.EqualTo(1));
            Assert.That(vm.SelectorIndex, Is.Zero);
        }

        [Test]
        public void SelectBeach_OutOfRange_DoesNothing()
        {
            var vm = NewVm();

            vm.SelectBeach(-1);
            vm.SelectBeach(99);

            Assert.That(beachOverride.SetCalls, Is.Zero);
            Assert.That(beachOverride.ClearCalls, Is.Zero);
            Assert.That(vm.SelectorIndex, Is.Zero);
        }

        /// <summary>
        /// The selector reports the override MODE: while GPS is driving, the
        /// automatic row is the chosen one even though a beach is open. Getting
        /// this backwards would make the menu highlight a row that tapping it
        /// would not change.
        /// </summary>
        [Test]
        public void SelectorIndex_StaysAutomaticWhileGpsDrives()
        {
            var vm = NewVm("Sueste Beach");

            Assert.That(vm.HasActiveBeach, Is.True);
            Assert.That(vm.SelectorIndex, Is.Zero);
        }

        // ---- The shipping case ----------------------------------------------

        /// <summary>
        /// What 14 of the 17 real beaches look like: a name, a region, and not a
        /// single editorial field. Every section flag must be false so the screen
        /// draws nothing rather than empty headings — and the two landing stats
        /// must still return a string, because a stat column is a fixed slot.
        /// </summary>
        [Test]
        public void ContentEmptyBeach_HidesEverySectionButStillHasStatSlots()
        {
            var vm = NewVm("Praia do Meio");
            var d = vm.Detail;

            Assert.That(vm.HasActiveBeach, Is.True);
            Assert.That(vm.LocationTitleText, Is.EqualTo("Praia do Meio"));

            Assert.That(d.HasRisk, Is.False);
            Assert.That(d.HasEnvironmentTags, Is.False);
            Assert.That(d.HasStats, Is.False);
            Assert.That(d.HasAlerts, Is.False);
            Assert.That(d.HasSpecies, Is.False);
            Assert.That(d.HasTips, Is.False);
            Assert.That(d.HasGallery, Is.False);
            Assert.That(d.HasSightingCount, Is.False);

            Assert.That(d.SightingPeakText, Is.EqualTo(BeachContentFormatter.NoValue));
            Assert.That(d.TideNowText, Is.EqualTo(BeachContentFormatter.NoValue),
                "no tide data in this fixture — the slot still has to say something");
        }
    }
}
