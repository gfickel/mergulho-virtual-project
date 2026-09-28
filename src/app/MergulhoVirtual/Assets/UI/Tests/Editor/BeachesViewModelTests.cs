using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    public class BeachesViewModelTests
    {
        // Fixed "now" (also the reference new moon, so MoonText is deterministic).
        static readonly DateTime Now = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

        sealed class FakeCatalog : IBeachCatalog
        {
            public IReadOnlyList<BeachInfo> Beaches { get; set; } = new List<BeachInfo>();
        }

        sealed class FakeConditions : IConditionsService
        {
            public ConditionsData Current { get; set; }
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
            public int SetCount;
            public int ClearCount;
            public void SetOverride(string beachName) { LastSet = beachName; SetCount++; }
            public void ClearOverride() => ClearCount++;
        }

        FakeCatalog catalog;
        FakeConditions conditions;
        FakeTides tides;
        FakeOverride beachOverride;

        BeachesViewModel NewVm()
        {
            // Identity local-time conversion keeps assertions timezone-independent.
            return new BeachesViewModel(catalog, conditions, tides, beachOverride,
                utcNow: () => Now, toLocalTime: d => d);
        }

        [SetUp]
        public void SetUp()
        {
            catalog = new FakeCatalog
            {
                Beaches = new List<BeachInfo>
                {
                    new BeachInfo { Name = "Praia do Sancho", ImageName = "sancho", Description = "desc" },
                    new BeachInfo { Name = "Baía dos Porcos", ImageName = "porcos", Description = "desc2" },
                },
            };
            conditions = new FakeConditions();
            tides = new FakeTides();
            beachOverride = new FakeOverride();
        }

        [Test]
        public void OverrideChoices_StartWithAutoThenBeaches()
        {
            var vm = NewVm();
            Assert.That(vm.OverrideChoices, Is.EqualTo(new[]
            {
                BeachesViewModel.AutoOptionLabel, "Praia do Sancho", "Baía dos Porcos",
            }));
            Assert.That(vm.OverrideIndex, Is.EqualTo(0));
        }

        [Test]
        public void ShowDetail_SelectsBeachAndRaisesNavigation()
        {
            var vm = NewVm();
            int raised = 0;
            vm.NavigationChanged += () => raised++;

            vm.ShowDetail("Baía dos Porcos");
            Assert.That(vm.SelectedBeach.Name, Is.EqualTo("Baía dos Porcos"));
            Assert.That(raised, Is.EqualTo(1));

            vm.ShowDetail("Praia inexistente");
            Assert.That(vm.SelectedBeach.Name, Is.EqualTo("Baía dos Porcos"), "unknown beach must be a no-op");
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void ShowList_ClearsSelection_AndIsIdempotent()
        {
            var vm = NewVm();
            int raised = 0;
            vm.NavigationChanged += () => raised++;

            vm.ShowList();
            Assert.That(raised, Is.EqualTo(0), "already at list — no event");

            vm.ShowDetail("Praia do Sancho");
            vm.ShowList();
            Assert.That(vm.SelectedBeach, Is.Null);
            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void SelectOverride_MapsIndexToSetAndClear()
        {
            var vm = NewVm();

            vm.SelectOverride(0);
            Assert.That(beachOverride.ClearCount, Is.EqualTo(0), "index 0 while already 0 is a no-op");

            vm.SelectOverride(2);
            Assert.That(beachOverride.LastSet, Is.EqualTo("Baía dos Porcos"));
            Assert.That(vm.OverrideIndex, Is.EqualTo(2));

            vm.SelectOverride(0);
            Assert.That(beachOverride.ClearCount, Is.EqualTo(1));
            Assert.That(vm.OverrideIndex, Is.EqualTo(0));

            vm.SelectOverride(99);
            Assert.That(vm.OverrideIndex, Is.EqualTo(0), "out of range must be ignored");
        }

        [Test]
        public void WaveText_JoinsAvailableParts()
        {
            conditions.Current = new ConditionsData
            {
                WaveHeightM = 1.23f,
                WavePeriodS = 8.4f,
                WaveDirectionDeg = 135f,
            };
            Assert.That(NewVm().WaveText, Is.EqualTo("1.2 m · 8 s · SE"));
        }

        [Test]
        public void WaveText_NoData()
        {
            Assert.That(NewVm().WaveText, Is.EqualTo("—"), "null snapshot");
            conditions.Current = new ConditionsData();
            Assert.That(NewVm().WaveText, Is.EqualTo("—"), "snapshot with no wave fields");
        }

        [Test]
        public void TideText_RisingWithNextHigh()
        {
            tides.Current = new TideData
            {
                Valid = true,
                Rising = true,
                NextHighAtUtc = new DateTime(2000, 1, 6, 14, 40, 0, DateTimeKind.Utc),
                NextHighM = 2.24f,
            };
            Assert.That(NewVm().TideText, Is.EqualTo("subindo, próxima alta 14:40 (2.2 m)"));
        }

        [Test]
        public void TideText_FallingWithNextLow()
        {
            tides.Current = new TideData
            {
                Valid = true,
                Rising = false,
                NextLowAtUtc = new DateTime(2000, 1, 6, 12, 5, 0, DateTimeKind.Utc),
                NextLowM = 0.42f,
            };
            Assert.That(NewVm().TideText, Is.EqualTo("descendo, próxima baixa 12:05 (0.4 m)"));
        }

        [Test]
        public void TideText_InvalidOrNoEvents()
        {
            Assert.That(NewVm().TideText, Is.EqualTo("—"));
            tides.Current = new TideData { Valid = true, Rising = true };
            Assert.That(NewVm().TideText, Is.EqualTo("subindo"));
        }

        [Test]
        public void WindAndWaterText_Format()
        {
            conditions.Current = new ConditionsData
            {
                WindSpeedKmh = 23.6f,
                WindDirectionDeg = 90f,
                SeaTempC = 26.7f,
            };
            var vm = NewVm();
            Assert.That(vm.WindText, Is.EqualTo("24 km/h E"));
            Assert.That(vm.WaterText, Is.EqualTo("27 °C"));
        }

        [Test]
        public void FreshnessText_Ages()
        {
            var vm = NewVm();
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: —"));

            conditions.Current = new ConditionsData { FetchedAtUtc = Now.AddSeconds(-30) };
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: agora"));

            conditions.Current = new ConditionsData { FetchedAtUtc = Now.AddMinutes(-5) };
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: há 5m"));

            conditions.Current = new ConditionsData { FetchedAtUtc = Now.AddHours(-3) };
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: há 3h"));

            conditions.Current = new ConditionsData { FetchedAtUtc = Now.AddDays(-2) };
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: há 2d"));
        }

        [Test]
        public void MoonText_AtReferenceNewMoon()
        {
            Assert.That(NewVm().MoonText, Is.EqualTo("Nova · 0% iluminada"));
        }

        [Test]
        public void ConditionsTitle_ShowsSourceBeachWhenDifferent()
        {
            var vm = NewVm();
            Assert.That(vm.ConditionsTitle, Is.EqualTo("Condições"), "no snapshot");

            conditions.Current = new ConditionsData { BeachName = "Praia do Sancho" };
            vm.ShowDetail("Praia do Sancho");
            Assert.That(vm.ConditionsTitle, Is.EqualTo("Condições"), "same beach — no suffix");

            vm.ShowDetail("Baía dos Porcos");
            Assert.That(vm.ConditionsTitle, Is.EqualTo("Condições · Praia do Sancho"));
        }

        [Test]
        public void FormatTideExtremumLabel_HoursAfterWindowStart()
        {
            tides.Current = new TideData
            {
                Valid = true,
                WindowStartUtc = new DateTime(2000, 1, 6, 10, 0, 0, DateTimeKind.Utc),
            };
            var vm = NewVm();
            Assert.That(vm.FormatTideExtremumLabel(3, true), Is.EqualTo("13:00"));
            Assert.That(vm.FormatTideExtremumLabel(0, false), Is.EqualTo("10:00"));

            tides.Current = default;
            Assert.That(vm.FormatTideExtremumLabel(3, true), Is.Null, "invalid tide — no label");
        }

        [Test]
        public void DataChanged_FollowsServiceEvents_UntilDisposed()
        {
            var vm = NewVm();
            int raised = 0;
            vm.DataChanged += () => raised++;

            conditions.Raise();
            tides.Raise();
            Assert.That(raised, Is.EqualTo(2));

            vm.NotifyTimePassed();
            Assert.That(raised, Is.EqualTo(3));

            vm.Dispose();
            conditions.Raise();
            tides.Raise();
            Assert.That(raised, Is.EqualTo(3), "disposed VM must be unsubscribed");
        }
    }
}
