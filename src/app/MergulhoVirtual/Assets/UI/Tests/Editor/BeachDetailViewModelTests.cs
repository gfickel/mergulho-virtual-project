using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The Praia detalhe ViewModel. The first fixture below is the case that
    /// matters most today: <b>every editorial field is blank</b> for 17 of 17
    /// beaches (docs/beaches-content-todo.md), so the empty path is the shipping
    /// path, not an edge case.
    /// </summary>
    public class BeachDetailViewModelTests
    {
        static readonly DateTime Now = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

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
            public event Action<TideData> Changed;
            public void Raise() => Changed?.Invoke(Current);
        }

        FakeCatalog catalog;
        FakeContent content;
        FakeSpecies species;
        FakeTides tides;

        BeachDetailViewModel NewVm() =>
            new BeachDetailViewModel(catalog, content, species, tides,
                utcNow: () => Now, toLocalTime: d => d);

        [SetUp]
        public void SetUp()
        {
            catalog = new FakeCatalog
            {
                Beaches = new List<BeachInfo>
                {
                    new BeachInfo
                    {
                        Name = "Praia do Sancho", DisplayName = "Praia do Sancho",
                        ImageName = "praia_do_sancho", Description = "desc", PhotoCredit = "Foto: alguém",
                    },
                    // The key/label split: an English key with a pt-BR label.
                    new BeachInfo { Name = "Sueste Beach", DisplayName = "Baía do Sueste" },
                    // No content entry at all, on purpose.
                    new BeachInfo { Name = "Praia do Meio", DisplayName = "Praia do Meio" },
                },
            };

            content = new FakeContent();
            // The shipping state: an entry exists, everything in it is blank.
            content.Entries["Praia do Sancho"] = BeachContent.EmptyFor("Praia do Sancho");
            // A partially filled beach — the most any beach has today.
            content.Entries["Sueste Beach"] = new BeachContent
            {
                BeachName = "Sueste Beach",
                IdealTide = BeachIdealTide.Low,
                EnvironmentTags = new[] { "Ambiente recifal", "Manguezal" },
                Advisories = new[] { "O snorkeling é permitido apenas nas raias delimitadas." },
                Species = new[]
                {
                    new BeachSpeciesContent { SpeciesKey = "lemon_shark", Tag = "Área de berçário", Behaviour = "Usam a rasa como berçário." },
                    new BeachSpeciesContent { SpeciesKey = "nurse_shark" },
                    new BeachSpeciesContent { SpeciesKey = "raia_manta" },   // not in the catalog
                },
            };

            species = new FakeSpecies();
            species.Add(new SpeciesInfo { Key = "lemon_shark", DisplayName = "Tubarão-limão", Binomial = "Negaprion brevirostris", ImageName = "lemon_shark" });
            species.Add(new SpeciesInfo { Key = "nurse_shark", DisplayName = "Tubarão-lixa", Binomial = "Ginglymostoma cirratum" });
            // Mirrors reef_shark in the real catalog: named, but no binomial yet.
            species.Add(new SpeciesInfo { Key = "reef_shark", DisplayName = "Tubarão-bico-fino" });

            tides = new FakeTides();
        }

        // ---- Navigation -----------------------------------------------------

        [Test]
        public void NoBeachOpen_IsSafeToBind()
        {
            var vm = NewVm();
            Assert.That(vm.HasBeach, Is.False);
            Assert.That(vm.Beach, Is.Null);
            Assert.That(vm.Content, Is.Not.Null, "Content is never null");
            Assert.That(vm.Content.IsEmpty, Is.True);
            Assert.That(vm.Title, Is.Empty);
            Assert.That(vm.SelectedBeachIndex, Is.EqualTo(-1));
            Assert.That(vm.SpeciesChips, Is.Empty);
            Assert.That(vm.SelectedSpecies, Is.Null);
        }

        [Test]
        public void ShowBeach_OpensByKeyAndRaisesChanged()
        {
            var vm = NewVm();
            int raised = 0;
            vm.Changed += () => raised++;

            vm.ShowBeach("Sueste Beach");
            Assert.That(vm.HasBeach, Is.True);
            Assert.That(vm.BeachKey, Is.EqualTo("Sueste Beach"));
            Assert.That(vm.Title, Is.EqualTo("Baía do Sueste"));
            Assert.That(vm.SelectedBeachIndex, Is.EqualTo(1));
            Assert.That(raised, Is.EqualTo(1));

            vm.ShowBeach("Praia inexistente");
            Assert.That(vm.BeachKey, Is.EqualTo("Sueste Beach"), "unknown key must be a no-op");
            Assert.That(raised, Is.EqualTo(1));

            vm.ShowBeach("Baía do Sueste");
            Assert.That(raised, Is.EqualTo(1), "the label is not a key — it must not resolve");
        }

        [Test]
        public void SelectBeachByIndex_SwitchesBeach_AndIgnoresOutOfRange()
        {
            var vm = NewVm();
            vm.SelectBeachByIndex(1);
            Assert.That(vm.Title, Is.EqualTo("Baía do Sueste"));

            vm.SelectBeachByIndex(99);
            Assert.That(vm.Title, Is.EqualTo("Baía do Sueste"));
            vm.SelectBeachByIndex(-1);
            Assert.That(vm.Title, Is.EqualTo("Baía do Sueste"));

            vm.SelectBeachByIndex(0);
            Assert.That(vm.Title, Is.EqualTo("Praia do Sancho"));
            Assert.That(vm.SpeciesChips, Is.Empty, "species must reset with the beach");
        }

        [Test]
        public void Close_ClearsStateAndIsIdempotent()
        {
            var vm = NewVm();
            int raised = 0;
            vm.Changed += () => raised++;

            vm.Close();
            Assert.That(raised, Is.EqualTo(0), "nothing open — no event");

            vm.ShowBeach("Sueste Beach");
            vm.Close();
            Assert.That(vm.HasBeach, Is.False);
            Assert.That(vm.SelectedBeachIndex, Is.EqualTo(-1));
            Assert.That(vm.SpeciesChips, Is.Empty);
            Assert.That(vm.Content.IsEmpty, Is.True);
            Assert.That(raised, Is.EqualTo(2));
        }

        // ---- The empty path (today's shipping state) ------------------------

        [Test]
        public void EmptyContent_HidesEverySectionAndDashesTheStats()
        {
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");

            Assert.That(vm.Content.IsEmpty, Is.True);

            // Sections disappear...
            Assert.That(vm.HasRisk, Is.False);
            Assert.That(vm.RiskPillText, Is.Null, "no pill at all — never a default 'Baixo'");
            Assert.That(vm.HasEnvironmentTags, Is.False);
            Assert.That(vm.EnvironmentTags, Is.Empty);
            Assert.That(vm.HasAlerts, Is.False);
            Assert.That(vm.AlertLines, Is.Empty);
            Assert.That(vm.LifeguardText, Is.Null);
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.SelectedSpecies, Is.Null);
            Assert.That(vm.HasTips, Is.False);
            Assert.That(vm.HasStats, Is.False);
            Assert.That(vm.HasSightingCount, Is.False);
            Assert.That(vm.SightingCountText, Is.Null);
            Assert.That(vm.HasGallery, Is.False);

            // ...while the stat columns that always occupy space show "—".
            Assert.That(vm.BestSeasonText, Is.EqualTo("—"));
            Assert.That(vm.IdealTideText, Is.EqualTo("—"));
            Assert.That(vm.SightingPeakText, Is.EqualTo("—"));
            Assert.That(vm.IdealTideNextEventText, Is.Null);

            // The beach's own places.json fields still render.
            Assert.That(vm.HasDescription, Is.True);
            Assert.That(vm.ImageName, Is.EqualTo("praia_do_sancho"));
            Assert.That(vm.HasPhotoCredit, Is.True);
        }

        [Test]
        public void BeachMissingFromContentFile_RendersLikeAnEmptyEntry()
        {
            var vm = NewVm();
            vm.ShowBeach("Praia do Meio");   // no entry in FakeContent at all

            Assert.That(vm.Content, Is.Not.Null);
            Assert.That(vm.Content.BeachName, Is.EqualTo("Praia do Meio"), "empty content is still attributed");
            Assert.That(vm.Content.IsEmpty, Is.True);
            Assert.That(vm.HasStats, Is.False);
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.BestSeasonText, Is.EqualTo("—"));

            // The distinction is available for tooling, not for rendering.
            Assert.That(content.TryGetContent("Praia do Meio", out _), Is.False);
            Assert.That(content.TryGetContent("Praia do Sancho", out _), Is.True);
        }

        // ---- The partially filled path --------------------------------------

        [Test]
        public void PartiallyFilledBeach_RendersOnlyWhatIsFilled()
        {
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");

            Assert.That(vm.HasEnvironmentTags, Is.True);
            Assert.That(vm.EnvironmentTags, Is.EqualTo(new[] { "Ambiente recifal", "Manguezal" }));

            Assert.That(vm.HasIdealTide, Is.True);
            Assert.That(vm.IdealTideText, Is.EqualTo("Baixa"));
            Assert.That(vm.HasStats, Is.True, "one filled stat is enough to keep the card");

            Assert.That(vm.HasBestSeason, Is.False);
            Assert.That(vm.BestSeasonText, Is.EqualTo("—"));
            Assert.That(vm.HasRisk, Is.False);
            Assert.That(vm.HasTips, Is.False);

            // Lifeguard hours are blank, so the alert bar is advisories only.
            Assert.That(vm.HasLifeguard, Is.False);
            Assert.That(vm.AlertLines,
                Is.EqualTo(new[] { "O snorkeling é permitido apenas nas raias delimitadas." }));
        }

        [Test]
        public void AlertLines_PutLifeguardFirst()
        {
            content.Entries["Praia do Sancho"] = new BeachContent
            {
                BeachName = "Praia do Sancho",
                LifeguardHours = "Das 08h às 17h",
                Advisories = new[] { "Acesso por escada vertical.", "   ", null },
            };
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");

            Assert.That(vm.LifeguardText, Is.EqualTo("Salva-vidas: Das 08h às 17h"));
            Assert.That(vm.AlertLines, Is.EqualTo(new[]
            {
                "Salva-vidas: Das 08h às 17h",
                "Acesso por escada vertical.",
            }), "blank advisories are dropped, not rendered as empty rows");
        }

        [Test]
        public void RiskPill_OnlyWhenFilled()
        {
            content.Entries["Praia do Sancho"] = new BeachContent
            {
                BeachName = "Praia do Sancho",
                RiskLevel = BeachRiskLevel.Low,
            };
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");
            Assert.That(vm.HasRisk, Is.True);
            Assert.That(vm.RiskLevel, Is.EqualTo(BeachRiskLevel.Low));
            Assert.That(vm.RiskPillText, Is.EqualTo("Risco: Baixo"));
        }

        [Test]
        public void Tips_AreNumberedByTheApp()
        {
            content.Entries["Praia do Sancho"] = new BeachContent
            {
                BeachName = "Praia do Sancho",
                Tips = new[] { "Mantenha distância.", "Não alimente.", "Silêncio na água." },
            };
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");

            Assert.That(vm.HasTips, Is.True);
            Assert.That(vm.Tips.Count, Is.EqualTo(3));
            Assert.That(vm.TipNumberText(0), Is.EqualTo("1"));
            Assert.That(vm.TipNumberText(2), Is.EqualTo("3"));
            Assert.That(vm.Tips[0], Is.EqualTo("Mantenha distância."), "the text carries no number");
        }

        // ---- Species --------------------------------------------------------

        [Test]
        public void SpeciesChips_UseCatalogLabels_FirstSelected_UnknownKeysDropped()
        {
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");

            Assert.That(vm.HasSpecies, Is.True);
            Assert.That(new[] { vm.SpeciesChips[0].Label, vm.SpeciesChips[1].Label },
                Is.EqualTo(new[] { "Tubarão-limão", "Tubarão-lixa" }));
            Assert.That(vm.SpeciesChips.Count, Is.EqualTo(2), "the unknown key must not become a blank chip");
            Assert.That(vm.UnknownSpeciesKeys, Is.EqualTo(new[] { "raia_manta" }));
            Assert.That(vm.SpeciesChips[0].Key, Is.EqualTo("lemon_shark"), "the key travels with the chip");

            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(0), "author order decides the first chip");
            Assert.That(vm.SelectedSpecies.DisplayName, Is.EqualTo("Tubarão-limão"));
            Assert.That(vm.SelectedSpecies.Binomial, Is.EqualTo("Negaprion brevirostris"));
            Assert.That(vm.SelectedSpecies.HasTag, Is.True);
            Assert.That(vm.SelectedSpecies.TagText, Is.EqualTo("Área de berçário"));
            Assert.That(vm.SelectedSpeciesBehaviourText,
                Is.EqualTo("Comportamento nessa praia: Usam a rasa como berçário."));
        }

        [Test]
        public void SelectSpecies_SwitchesTheCard_AndIgnoresOutOfRange()
        {
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");
            int raised = 0;
            vm.Changed += () => raised++;

            vm.SelectSpecies(1);
            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(1));
            Assert.That(vm.SelectedSpecies.DisplayName, Is.EqualTo("Tubarão-lixa"));
            Assert.That(raised, Is.EqualTo(1));

            // nurse_shark has no per-beach tag/behaviour — both must read as absent.
            Assert.That(vm.SelectedSpecies.HasTag, Is.False);
            Assert.That(vm.SelectedSpecies.TagText, Is.Null);
            Assert.That(vm.HasSelectedSpeciesBehaviour, Is.False);
            Assert.That(vm.SelectedSpeciesBehaviourText, Is.Null);

            vm.SelectSpecies(1);
            Assert.That(raised, Is.EqualTo(1), "re-selecting the same chip is a no-op");
            vm.SelectSpecies(7);
            vm.SelectSpecies(-1);
            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(1));
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void SpeciesWithoutBinomial_RendersTheNameAlone()
        {
            content.Entries["Praia do Sancho"] = new BeachContent
            {
                BeachName = "Praia do Sancho",
                Species = new[] { new BeachSpeciesContent { SpeciesKey = "reef_shark" } },
            };
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");

            Assert.That(vm.SelectedSpecies.DisplayName, Is.EqualTo("Tubarão-bico-fino"));
            Assert.That(vm.SelectedSpecies.HasBinomial, Is.False);
            Assert.That(vm.SelectedSpecies.Binomial, Is.Null, "no binomial is better than a guessed one");
        }

        [Test]
        public void EveryListedSpeciesUnknown_HidesTheSection()
        {
            content.Entries["Praia do Sancho"] = new BeachContent
            {
                BeachName = "Praia do Sancho",
                Species = new[] { new BeachSpeciesContent { SpeciesKey = "tartaruga_verde" } },
            };
            var vm = NewVm();
            vm.ShowBeach("Praia do Sancho");

            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.SelectedSpeciesIndex, Is.EqualTo(-1));
            Assert.That(vm.SelectedSpecies, Is.Null);
            Assert.That(vm.UnknownSpeciesKeys, Is.EqualTo(new[] { "tartaruga_verde" }));
        }

        // ---- Tide-derived values --------------------------------------------

        [Test]
        public void IdealTideNextEvent_CompletesFromTheTideTable()
        {
            tides.Current = new TideData
            {
                Valid = true,
                Rising = false,
                NextLowAtUtc = new DateTime(2000, 1, 6, 14, 40, 0, DateTimeKind.Utc),
                NextLowM = 0.42f,
                NextHighAtUtc = new DateTime(2000, 1, 6, 20, 55, 0, DateTimeKind.Utc),
                NextHighM = 2.2f,
                CurrentHeightM = 1.37f,
            };
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");   // idealTide = baixa

            Assert.That(vm.IdealTideText, Is.EqualTo("Baixa"));
            Assert.That(vm.IdealTideNextEventText, Is.EqualTo("próx. baixa 14:40"));
            Assert.That(vm.HasIdealTideNextEvent, Is.True);
            Assert.That(vm.TideNowText, Is.EqualTo("1.4 m ·\u00A0descendo"));
        }

        [Test]
        public void IdealTideNextEvent_IsSilentWithoutTideData()
        {
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");
            Assert.That(vm.IdealTideText, Is.EqualTo("Baixa"), "the authored value still shows");
            Assert.That(vm.IdealTideNextEventText, Is.Null);
            Assert.That(vm.TideNowText, Is.EqualTo("—"));
        }

        [Test]
        public void SightingCount_HiddenUntilSomebodySuppliesOne()
        {
            var vm = NewVm();
            vm.ShowBeach("Sueste Beach");
            int raised = 0;
            vm.Changed += () => raised++;

            Assert.That(vm.HasSightingCount, Is.False, "the backend counter is island-wide, not per beach");

            vm.SightingCount = 120;
            Assert.That(vm.SightingCountText, Is.EqualTo("120 avistamentos registrados"));
            Assert.That(vm.HasSightingCount, Is.True);
            Assert.That(raised, Is.EqualTo(1));

            vm.SightingCount = 120;
            Assert.That(raised, Is.EqualTo(1), "same value — no event");

            // It must not survive a beach change.
            vm.SelectBeachByIndex(0);
            Assert.That(vm.HasSightingCount, Is.False);
        }

        [Test]
        public void Changed_FollowsTideEvents_UntilDisposed()
        {
            var vm = NewVm();
            int raised = 0;
            vm.Changed += () => raised++;

            tides.Raise();
            Assert.That(raised, Is.EqualTo(1));

            vm.NotifyTimePassed();
            Assert.That(raised, Is.EqualTo(2));

            vm.Dispose();
            tides.Raise();
            Assert.That(raised, Is.EqualTo(2), "disposed VM must be unsubscribed");
        }

        [Test]
        public void NullServices_DegradeInsteadOfThrowing()
        {
            var vm = new BeachDetailViewModel(catalog, null, null, null,
                utcNow: () => Now, toLocalTime: d => d);
            vm.ShowBeach("Sueste Beach");

            Assert.That(vm.HasBeach, Is.True);
            Assert.That(vm.Content.IsEmpty, Is.True);
            Assert.That(vm.HasSpecies, Is.False);
            Assert.That(vm.TideNowText, Is.EqualTo("—"));
            Assert.DoesNotThrow(() => vm.Dispose());
        }
    }
}
