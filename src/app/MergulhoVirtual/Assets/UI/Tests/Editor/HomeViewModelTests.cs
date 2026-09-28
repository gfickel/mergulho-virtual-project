using System;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    public class HomeViewModelTests
    {
        // Fixed "now" (also the reference new moon, so MoonText is deterministic).
        static readonly DateTime Now = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

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

        sealed class FakeOnboarding : IOnboardingState
        {
            public bool WelcomeDismissed { get; set; }
            public int DismissCount;
            public void DismissWelcome() { WelcomeDismissed = true; DismissCount++; }
        }

        FakeConditions conditions;
        FakeTides tides;
        FakeOnboarding onboarding;

        HomeViewModel NewVm(IOnboardingState state = null)
        {
            // Identity local-time conversion keeps assertions timezone-independent.
            return new HomeViewModel(conditions, tides, state ?? onboarding,
                utcNow: () => Now, toLocalTime: d => d);
        }

        [SetUp]
        public void SetUp()
        {
            conditions = new FakeConditions();
            tides = new FakeTides();
            onboarding = new FakeOnboarding();
        }

        // ---- Feature grid ---------------------------------------------------

        [Test]
        public void GridFeatures_AreTheFourFigmaTiles_InOrder()
        {
            var vm = NewVm();
            Assert.That(vm.GridFeatures.Count, Is.EqualTo(4));

            Assert.That(vm.GridFeatures[0].Route, Is.EqualTo(AppRoutes.Praias));
            Assert.That(vm.GridFeatures[0].Title, Is.EqualTo("Praias"));
            Assert.That(vm.GridFeatures[0].Body,
                Is.EqualTo("Explore as praias, condições e informações de segurança."));

            Assert.That(vm.GridFeatures[1].Route, Is.EqualTo(AppRoutes.Mergulho));
            Assert.That(vm.GridFeatures[1].Title, Is.EqualTo("Mergulho Virtual"));
            Assert.That(vm.GridFeatures[1].Body,
                Is.EqualTo("Veja de perto diversas espécies de tubarões."));

            Assert.That(vm.GridFeatures[2].Route, Is.EqualTo(AppRoutes.Avistamentos));
            Assert.That(vm.GridFeatures[2].Title, Is.EqualTo("Avistamentos"));
            Assert.That(vm.GridFeatures[2].Body,
                Is.EqualTo("Registre e navegue por avistamentos de vida marinha."));

            Assert.That(vm.GridFeatures[3].Route, Is.EqualTo(AppRoutes.Sos));
            Assert.That(vm.GridFeatures[3].Title, Is.EqualTo("SOS"));
            Assert.That(vm.GridFeatures[3].Body,
                Is.EqualTo("Primeiros socorros, hospitais próximos e números de emergência."));
        }

        [Test]
        public void EveryFeature_HasARouteKeyAndAnIcon()
        {
            var vm = NewVm();
            foreach (var f in vm.GridFeatures)
            {
                Assert.That(f.Route, Is.Not.Null.And.Not.Empty);
                Assert.That(f.Icon, Is.Not.Null.And.Not.Empty, f.Title + " has no glyph");
                Assert.That(f.Body, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void WideFeature_IsSobre_TheD2Addition()
        {
            // Sobre is NOT in the Figma frame — it lost its bottom-bar tab (Decision
            // D2) and lands here instead, full-width below the 2×2 grid.
            var vm = NewVm();
            Assert.That(vm.WideFeature, Is.Not.Null);
            Assert.That(vm.WideFeature.Route, Is.EqualTo(AppRoutes.Sobre));
            Assert.That(vm.GridFeatures, Has.No.Member(vm.WideFeature));
        }

        // ---- Welcome card (Tela 6) ------------------------------------------

        [Test]
        public void Welcome_ShowsOnFirstRun_AndHidesOnceDismissed()
        {
            var vm = NewVm();
            Assert.That(vm.ShowWelcome, Is.True, "never dismissed = first run");

            int raised = 0;
            vm.WelcomeChanged += () => raised++;

            vm.DismissWelcome();
            Assert.That(vm.ShowWelcome, Is.False);
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(onboarding.DismissCount, Is.EqualTo(1), "dismissal must be persisted");

            vm.DismissWelcome();
            Assert.That(raised, Is.EqualTo(1), "second dismiss is a no-op");
            Assert.That(onboarding.DismissCount, Is.EqualTo(1));
        }

        [Test]
        public void Welcome_StaysHiddenOnLaterRuns()
        {
            onboarding.WelcomeDismissed = true;
            Assert.That(NewVm().ShowWelcome, Is.False);
        }

        [Test]
        public void Welcome_WithoutPersistence_ShowsAndDismissesInMemory()
        {
            var vm = new HomeViewModel(conditions, tides, onboarding: null,
                utcNow: () => Now, toLocalTime: d => d);
            Assert.That(vm.ShowWelcome, Is.True, "no adapter wired = treat as first run");
            vm.DismissWelcome();
            Assert.That(vm.ShowWelcome, Is.False, "dismissal must still work without persistence");
        }

        // ---- Conditions rows (shared formatting) ----------------------------

        [Test]
        public void Rows_MatchTheSharedFormatter()
        {
            conditions.Current = new ConditionsData
            {
                WaveHeightM = 2.1f,
                WavePeriodS = 6f,
                WaveDirectionDeg = 135f,
                WindSpeedKmh = 40f,
                WindDirectionDeg = 135f,
                SeaTempC = 27f,
                FetchedAtUtc = Now.AddSeconds(-10),
            };
            tides.Current = new TideData
            {
                Valid = true,
                Rising = false,
                NextLowAtUtc = new DateTime(2000, 1, 6, 20, 19, 0, DateTimeKind.Utc),
                NextLowM = 0.6f,
            };

            var vm = NewVm();
            Assert.That(vm.WaveText, Is.EqualTo("2.1 m · 6 s · SE"));
            Assert.That(vm.TideText, Is.EqualTo("descendo, próxima baixa 20:19 (0.6 m)"));
            Assert.That(vm.MoonText, Is.EqualTo("Nova · 0% iluminada"));
            Assert.That(vm.WindText, Is.EqualTo("40 km/h SE"));
            Assert.That(vm.WaterText, Is.EqualTo("27 °C"));
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: agora"));
        }

        [Test]
        public void Rows_FallBackToTheEmDashWithNoServices()
        {
            var vm = new HomeViewModel(null, null, onboarding, () => Now, d => d);
            Assert.That(vm.WaveText, Is.EqualTo("—"));
            Assert.That(vm.TideText, Is.EqualTo("—"));
            Assert.That(vm.WindText, Is.EqualTo("—"));
            Assert.That(vm.WaterText, Is.EqualTo("—"));
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: —"));
            Assert.That(vm.CurrentTide.Valid, Is.False);
            Assert.That(vm.FormatTideExtremumLabel(3, true), Is.Null);
            Assert.DoesNotThrow(() => vm.Dispose());
        }

        [Test]
        public void HomeAndBeaches_RenderIdenticalRowStrings()
        {
            // The whole point of extracting ConditionsFormatter: the two cards must
            // never drift apart.
            conditions.Current = new ConditionsData
            {
                WaveHeightM = 1.23f, WavePeriodS = 8.4f, WaveDirectionDeg = 135f,
                WindSpeedKmh = 23.6f, WindDirectionDeg = 90f, SeaTempC = 26.7f,
                FetchedAtUtc = Now.AddMinutes(-5),
            };
            tides.Current = new TideData
            {
                Valid = true, Rising = true,
                NextHighAtUtc = new DateTime(2000, 1, 6, 14, 40, 0, DateTimeKind.Utc),
                NextHighM = 2.24f,
                WindowStartUtc = new DateTime(2000, 1, 6, 10, 0, 0, DateTimeKind.Utc),
            };

            var home = NewVm();
            var beaches = new BeachesViewModel(null, conditions, tides, null,
                utcNow: () => Now, toLocalTime: d => d);

            Assert.That(home.WaveText, Is.EqualTo(beaches.WaveText));
            Assert.That(home.TideText, Is.EqualTo(beaches.TideText));
            Assert.That(home.MoonText, Is.EqualTo(beaches.MoonText));
            Assert.That(home.WindText, Is.EqualTo(beaches.WindText));
            Assert.That(home.WaterText, Is.EqualTo(beaches.WaterText));
            Assert.That(home.FreshnessText, Is.EqualTo(beaches.FreshnessText));
            Assert.That(home.FormatTideExtremumLabel(3, true),
                Is.EqualTo(beaches.FormatTideExtremumLabel(3, true)));
            beaches.Dispose();
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
            Assert.That(vm.FormatTideExtremumLabel(3, true), Is.EqualTo("\u25B2 13:00"));
            Assert.That(vm.FormatTideExtremumLabel(0, false), Is.EqualTo("\u25BC 10:00"));
        }

        // ---- Events ---------------------------------------------------------

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

            Assert.DoesNotThrow(() => vm.Dispose(), "Dispose is idempotent");
        }

        [Test]
        public void StaticCopy_MatchesTheFigmaStrings()
        {
            Assert.That(HomeViewModel.ConditionsCardTitle, Is.EqualTo("Hoje"));
            Assert.That(HomeViewModel.TideTableLinkText, Is.EqualTo("Baixar a tábua de maré do mês"));
            Assert.That(HomeViewModel.WelcomeTitle, Is.EqualTo("Bem-vindo ao Mergulho Virtual"));
            Assert.That(HomeViewModel.WelcomeBody, Does.StartWith("Explore as praias, acompanhe avistamentos marinhos"));
            Assert.That(HomeViewModel.WelcomeBody, Does.EndWith("conectada com Fernando de Noronha."));
        }
    }
}
