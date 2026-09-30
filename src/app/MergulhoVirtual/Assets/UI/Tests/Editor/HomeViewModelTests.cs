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
            public bool LastFetchFailed { get; set; }
            public bool IsFetching { get; set; }
            public int RefreshCalls;
            public void Refresh() => RefreshCalls++;
            public event Action<ConditionsData> Changed;
            public void Raise() => Changed?.Invoke(Current);
        }

        sealed class FakeConnectivity : IConnectivity
        {
            public bool IsOnline { get; set; } = true;
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
        FakeConnectivity connectivity;

        HomeViewModel NewVm(IOnboardingState state = null)
        {
            // Identity local-time conversion keeps assertions timezone-independent.
            return new HomeViewModel(conditions, tides, state ?? onboarding, connectivity,
                utcNow: () => Now, toLocalTime: d => d);
        }

        [SetUp]
        public void SetUp()
        {
            connectivity = new FakeConnectivity();
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

        /// <summary>
        /// "Conteúdo educativo" — also NOT in the Figma frame, for the same reason Sobre
        /// is not: the feature gets no bottom-bar tab (V2 has four destinations and that
        /// is fixed), so it needs a discoverable entry point.
        ///
        /// <para>It is a THIRD property rather than a fifth grid cell or a re-pointed
        /// <c>WideFeature</c>, and the last two assertions are what pin that: a fifth
        /// cell would orphan a half-row, and re-pointing WideFeature would silently
        /// delete the Sobre entry along with the Instagram widget it reaches.</para>
        /// </summary>
        [Test]
        public void LearnFeature_IsTheEducationalContentEntry_AndIsAdditive()
        {
            var vm = NewVm();

            Assert.That(vm.LearnFeature, Is.Not.Null);
            Assert.That(vm.LearnFeature.Route, Is.EqualTo(AppRoutes.Conteudos));
            Assert.That(vm.LearnFeature.Title, Is.EqualTo(ArticleFormatter.EntryPointLabel));
            Assert.That(vm.LearnFeature.Body,
                Is.EqualTo("Artigos sobre a vida marinha, as praias e o projeto."));
            Assert.That(vm.LearnFeature.Icon, Is.EqualTo("menu_book"));

            Assert.That(vm.GridFeatures.Count, Is.EqualTo(4), "the 2x2 grid is untouched");
            Assert.That(vm.GridFeatures, Has.No.Member(vm.LearnFeature));
            Assert.That(vm.WideFeature.Route, Is.EqualTo(AppRoutes.Sobre), "Sobre is untouched");
        }

        /// <summary>The card and the screen it opens must not spell the feature's name
        /// twice — one constant, two sites.</summary>
        [Test]
        public void LearnFeature_TitleIsTheFormattersOwnEntryPointLabel()
        {
            Assert.That(NewVm().LearnFeature.Title, Is.EqualTo(ArticleFormatter.EntryPointLabel));
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
            Assert.That(vm.TideText, Is.EqualTo("descendo, próxima baixa 20:19 (0.6\u00A0m)"));
            Assert.That(vm.MoonText, Is.EqualTo("Nova · 0% iluminada"));
            Assert.That(vm.WindText, Is.EqualTo("40 km/h SE"));
            Assert.That(vm.WaterText, Is.EqualTo("27 °C"));
            Assert.That(vm.FreshnessText, Is.EqualTo("Atualizado: agora"));
        }

        [Test]
        public void Rows_FallBackToTheEmDashWithNoServices()
        {
            var vm = new HomeViewModel(null, null, onboarding, utcNow: () => Now, toLocalTime: d => d);
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


        // ---- Conditions failure state (§8.7) --------------------------------

        [Test]
        public void ConditionsUnavailable_NeedsBothNoDataAndAReportedFailure()
        {
            var vm = NewVm();

            // Before the first fetch returns: nothing to show, but nothing has gone
            // wrong yet — claiming an error here would be a lie.
            conditions.Current = null;
            conditions.LastFetchFailed = false;
            Assert.That(vm.ConditionsUnavailable, Is.False);

            conditions.LastFetchFailed = true;
            Assert.That(vm.ConditionsUnavailable, Is.True);
        }

        [Test]
        public void CachedDataWithAFailedRefresh_IsNotTheErrorState()
        {
            var vm = NewVm();
            conditions.Current = new ConditionsData { BeachName = "Praia do Sancho", WaveHeightM = 1.2f };
            conditions.LastFetchFailed = true;

            Assert.That(vm.ConditionsUnavailable, Is.False,
                "the rows are real data; the freshness line is the honest signal for their age");
        }

        [Test]
        public void NoConditionsServiceAtAll_IsNotAnError()
        {
            var vm = new HomeViewModel(null, tides, onboarding, connectivity,
                utcNow: () => Now, toLocalTime: d => d);
            Assert.That(vm.ConditionsUnavailable, Is.False);
        }

        [Test]
        public void OfflineWording_IsUsedOnlyWhenTheDeviceReportsNoLink()
        {
            var vm = NewVm();

            connectivity.IsOnline = true;
            Assert.That(vm.IsOffline, Is.False);
            Assert.That(vm.ConditionsStateTitle, Is.EqualTo(StateViewCopy.ErrorTitle));
            Assert.That(vm.ConditionsStateBody, Is.EqualTo(StateViewCopy.ConditionsErrorBody));

            connectivity.IsOnline = false;
            Assert.That(vm.IsOffline, Is.True);
            Assert.That(vm.ConditionsStateTitle, Is.EqualTo(StateViewCopy.OfflineTitle));
            Assert.That(vm.ConditionsStateBody, Is.EqualTo(StateViewCopy.ConditionsOfflineBody));

            Assert.That(vm.ConditionsStateAction, Is.EqualTo(StateViewCopy.RetryAction));
        }

        [Test]
        public void WithNoConnectivityWired_TheGenericFailureIsUsed()
        {
            var vm = new HomeViewModel(conditions, tides, onboarding, connectivity: null,
                utcNow: () => Now, toLocalTime: d => d);
            Assert.That(vm.IsOffline, Is.False, "the claim that is always safe");
            Assert.That(vm.ConditionsStateTitle, Is.EqualTo(StateViewCopy.ErrorTitle));
        }

        [Test]
        public void RetryConditions_CallsTheServiceAndRepaints()
        {
            var vm = NewVm();
            int repaints = 0;
            vm.DataChanged += () => repaints++;

            vm.RetryConditions();

            Assert.That(conditions.RefreshCalls, Is.EqualTo(1), "a retry must make a real request");
            Assert.That(repaints, Is.EqualTo(1));
        }

        [Test]
        public void RetryIsDisabledWhileAFetchIsInFlight()
        {
            var vm = NewVm();
            conditions.IsFetching = true;
            Assert.That(vm.ConditionsRetryEnabled, Is.False);

            conditions.IsFetching = false;
            Assert.That(vm.ConditionsRetryEnabled, Is.True);
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
