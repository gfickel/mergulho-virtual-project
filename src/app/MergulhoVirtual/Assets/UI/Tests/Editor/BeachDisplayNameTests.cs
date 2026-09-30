using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The key-vs-label split. places.json keys are machine-owned and partly
    /// English ("Sueste Beach"); <c>displayName</c> is the pt-BR label. These tests
    /// pin the two rules that keep them apart: <b>labels are what the user sees</b>
    /// and <b>keys are what lookups and services get</b>. A regression here shows up
    /// as "Sueste Beach" on screen, or as a beach override that silently matches
    /// nothing.
    /// </summary>
    public class BeachDisplayNameTests
    {
        static readonly DateTime Now = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

        sealed class FakeCatalog : IBeachCatalog
        {
            public IReadOnlyList<BeachInfo> Beaches { get; set; } = new List<BeachInfo>();
        }

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
            public event Action<TideData> Changed;
            public void Raise() => Changed?.Invoke(Current);
        }

        sealed class FakeOverride : IBeachOverride
        {
            public string LastSet;
            public void SetOverride(string beachName) => LastSet = beachName;
            public void ClearOverride() => LastSet = null;
        }

        sealed class EmptyContent : IBeachContent
        {
            public BeachContent ForBeach(string beachName) => BeachContent.EmptyFor(beachName);
            public bool TryGetContent(string beachName, out BeachContent content)
            {
                content = BeachContent.EmptyFor(beachName);
                return false;
            }
        }

        sealed class EmptySpecies : ISpeciesCatalog
        {
            public IReadOnlyList<SpeciesInfo> Species => Array.Empty<SpeciesInfo>();
            public SpeciesInfo Find(string key) => null;
        }

        FakeCatalog catalog;
        FakeConditions conditions;
        FakeTides tides;
        FakeOverride beachOverride;

        [SetUp]
        public void SetUp()
        {
            catalog = new FakeCatalog
            {
                Beaches = new List<BeachInfo>
                {
                    new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
                    new BeachInfo { Name = "Boldró Beach", DisplayName = "Praia do Boldró" },
                    // Older places.json entries carry no displayName at all.
                    new BeachInfo { Name = "Praia do Meio" },
                },
            };
            conditions = new FakeConditions();
            tides = new FakeTides();
            beachOverride = new FakeOverride();
        }

        [Test]
        public void BeachInfo_DisplayNameFallsBackToTheKey_AndIsNeverEmpty()
        {
            Assert.That(catalog.Beaches[0].DisplayName, Is.EqualTo("Baía do Sueste"));
            Assert.That(catalog.Beaches[2].DisplayName, Is.EqualTo("Praia do Meio"),
                "no displayName in the file → the key is the least-bad label");
            Assert.That(new BeachInfo().DisplayName, Is.Null, "no key and no label is still null, not \"\"");
        }

        [Test]
        public void OverrideDropdown_ShowsLabels_ButAppliesKeys()
        {
            var vm = new BeachesViewModel(catalog, conditions, tides, beachOverride,
                utcNow: () => Now, toLocalTime: d => d);

            Assert.That(vm.OverrideChoices, Is.EqualTo(new[]
            {
                BeachesViewModel.AutoOptionLabel, "Baía do Sueste", "Praia do Boldró", "Praia do Meio",
            }), "the dropdown must never show the English keys");

            vm.SelectOverride(1);
            Assert.That(beachOverride.LastSet, Is.EqualTo("Sueste Beach"),
                "the key is what the GPS handler, the spawner and the backend agree on");

            vm.SelectOverride(2);
            Assert.That(beachOverride.LastSet, Is.EqualTo("Boldró Beach"));
        }

        [Test]
        public void ConditionsTitle_TranslatesTheSourceBeachKey()
        {
            var vm = new BeachesViewModel(catalog, conditions, tides, beachOverride,
                utcNow: () => Now, toLocalTime: d => d);

            // The snapshot carries the GPS resolver's key, not a label.
            conditions.Current = new ConditionsData { BeachName = "Sueste Beach" };
            vm.ShowDetail("Boldró Beach");
            Assert.That(vm.ConditionsTitle, Is.EqualTo("Condições · Baía do Sueste"));

            conditions.Current = new ConditionsData { BeachName = "Praia desconhecida" };
            Assert.That(vm.ConditionsTitle, Is.EqualTo("Condições · Praia desconhecida"),
                "a beach outside the catalog falls back to whatever the service said");
        }

        [Test]
        public void DetailViewModel_OpensOnKeys_AndRendersLabels()
        {
            var vm = new BeachDetailViewModel(catalog, new EmptyContent(), new EmptySpecies(), tides,
                utcNow: () => Now, toLocalTime: d => d);

            Assert.That(vm.BeachChoices, Is.EqualTo(new[]
            {
                "Baía do Sueste", "Praia do Boldró", "Praia do Meio",
            }));

            vm.ShowBeach("Sueste Beach");
            Assert.That(vm.Title, Is.EqualTo("Baía do Sueste"), "the title is the label");
            Assert.That(vm.BeachKey, Is.EqualTo("Sueste Beach"), "the key stays available for lookups");
        }
    }
}
