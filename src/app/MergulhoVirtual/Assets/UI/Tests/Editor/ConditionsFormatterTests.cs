using System;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// Pins the pt-BR conditions strings. These exact strings shipped in the legacy
    /// uGUI ConditionsCardView, then in BeachesViewModel, and are now shared by the
    /// Beaches and Início cards — so this fixture is the guard that the extraction
    /// (and any future refactor) cannot silently reword them.
    /// </summary>
    public class ConditionsFormatterTests
    {
        // Fixed "now" — also the reference new moon, so Moon() is deterministic.
        static readonly DateTime Now = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

        // Identity conversion keeps assertions timezone-independent.
        static readonly Func<DateTime, DateTime> Local = d => d;

        [Test]
        public void NoValue_IsTheEmDash()
        {
            Assert.That(ConditionsFormatter.NoValue, Is.EqualTo("—"));
        }

        [Test]
        public void Wave_JoinsAvailableParts()
        {
            var s = new ConditionsData { WaveHeightM = 1.23f, WavePeriodS = 8.4f, WaveDirectionDeg = 135f };
            Assert.That(ConditionsFormatter.Wave(s), Is.EqualTo("1.2 m · 8 s · SE"));
        }

        [Test]
        public void Wave_PartialAndMissing()
        {
            Assert.That(ConditionsFormatter.Wave(null), Is.EqualTo("—"));
            Assert.That(ConditionsFormatter.Wave(new ConditionsData()), Is.EqualTo("—"));
            Assert.That(ConditionsFormatter.Wave(new ConditionsData { WaveHeightM = 2.4f }),
                Is.EqualTo("2.4 m"), "single part carries no separator");
        }

        [Test]
        public void Tide_RisingUsesNextHigh()
        {
            var t = new TideData
            {
                Valid = true,
                Rising = true,
                NextHighAtUtc = new DateTime(2000, 1, 6, 14, 40, 0, DateTimeKind.Utc),
                NextHighM = 2.24f,
            };
            Assert.That(ConditionsFormatter.Tide(t, Local), Is.EqualTo("subindo, próxima alta 14:40 (2.2\u00A0m)"));
        }

        [Test]
        public void Tide_FallingUsesNextLow()
        {
            var t = new TideData
            {
                Valid = true,
                Rising = false,
                NextLowAtUtc = new DateTime(2000, 1, 6, 12, 5, 0, DateTimeKind.Utc),
                NextLowM = 0.42f,
            };
            Assert.That(ConditionsFormatter.Tide(t, Local), Is.EqualTo("descendo, próxima baixa 12:05 (0.4\u00A0m)"));
        }

        [Test]
        public void Tide_InvalidOrNoEvents()
        {
            Assert.That(ConditionsFormatter.Tide(default, Local), Is.EqualTo("—"));
            Assert.That(ConditionsFormatter.Tide(new TideData { Valid = true, Rising = true }, Local),
                Is.EqualTo("subindo"));
            Assert.That(ConditionsFormatter.Tide(new TideData { Valid = true, Rising = false }, Local),
                Is.EqualTo("descendo"));
        }

        [Test]
        public void Wind_And_Water()
        {
            var s = new ConditionsData { WindSpeedKmh = 23.6f, WindDirectionDeg = 90f, SeaTempC = 26.7f };
            Assert.That(ConditionsFormatter.Wind(s), Is.EqualTo("24 km/h E"));
            Assert.That(ConditionsFormatter.Water(s), Is.EqualTo("27 °C"));

            Assert.That(ConditionsFormatter.Wind(null), Is.EqualTo("—"));
            Assert.That(ConditionsFormatter.Water(new ConditionsData()), Is.EqualTo("—"));
        }

        [Test]
        public void Moon_AtReferenceNewMoon()
        {
            Assert.That(ConditionsFormatter.Moon(Now), Is.EqualTo("Nova · 0% iluminada"));
        }

        [Test]
        public void Moon_NamesAreThePtBrSet()
        {
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.New), Is.EqualTo("Nova"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.WaxingCrescent), Is.EqualTo("Crescente"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.FirstQuarter), Is.EqualTo("Quarto Crescente"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.WaxingGibbous), Is.EqualTo("Gibosa Crescente"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.Full), Is.EqualTo("Cheia"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.WaningGibbous), Is.EqualTo("Gibosa Minguante"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.LastQuarter), Is.EqualTo("Quarto Minguante"));
            Assert.That(ConditionsFormatter.MoonPhaseLabelPtBr(MoonPhaseName.WaningCrescent), Is.EqualTo("Minguante"));
        }

        [Test]
        public void Freshness_Ages()
        {
            Assert.That(ConditionsFormatter.Freshness(null, Now), Is.EqualTo("Atualizado: —"));
            Assert.That(ConditionsFormatter.Freshness(new ConditionsData(), Now), Is.EqualTo("Atualizado: —"));
            Assert.That(ConditionsFormatter.Freshness(new ConditionsData { FetchedAtUtc = Now.AddSeconds(-30) }, Now),
                Is.EqualTo("Atualizado: agora"));
            Assert.That(ConditionsFormatter.Freshness(new ConditionsData { FetchedAtUtc = Now.AddMinutes(-5) }, Now),
                Is.EqualTo("Atualizado: há 5m"));
            Assert.That(ConditionsFormatter.Freshness(new ConditionsData { FetchedAtUtc = Now.AddHours(-3) }, Now),
                Is.EqualTo("Atualizado: há 3h"));
            Assert.That(ConditionsFormatter.Freshness(new ConditionsData { FetchedAtUtc = Now.AddDays(-2) }, Now),
                Is.EqualTo("Atualizado: há 2d"));
        }

        [Test]
        public void DegToCardinal_CoversTheCompass()
        {
            Assert.That(ConditionsFormatter.DegToCardinal(0f), Is.EqualTo("N"));
            Assert.That(ConditionsFormatter.DegToCardinal(45f), Is.EqualTo("NE"));
            Assert.That(ConditionsFormatter.DegToCardinal(90f), Is.EqualTo("E"));
            Assert.That(ConditionsFormatter.DegToCardinal(180f), Is.EqualTo("S"));
            Assert.That(ConditionsFormatter.DegToCardinal(270f), Is.EqualTo("W"));
            Assert.That(ConditionsFormatter.DegToCardinal(315f), Is.EqualTo("NW"));
            Assert.That(ConditionsFormatter.DegToCardinal(360f), Is.EqualTo("N"), "wraps");
            Assert.That(ConditionsFormatter.DegToCardinal(-90f), Is.EqualTo("W"), "negatives wrap too");
        }

        [Test]
        public void TideExtremumLabel_HoursAfterWindowStart()
        {
            var t = new TideData
            {
                Valid = true,
                WindowStartUtc = new DateTime(2000, 1, 6, 10, 0, 0, DateTimeKind.Utc),
            };
            Assert.That(ConditionsFormatter.TideExtremumLabel(t, 3, true, Local), Is.EqualTo("\u25B2 13:00"));
            Assert.That(ConditionsFormatter.TideExtremumLabel(t, 0, false, Local), Is.EqualTo("\u25BC 10:00"));
            Assert.That(ConditionsFormatter.TideExtremumLabel(default, 3, true, Local), Is.Null,
                "invalid tide — no label");
        }

        /// <summary>
        /// The marker is the only thing separating a high from a low in the V2 frame
        /// (both sit on one baseline under the curve), so it is pinned explicitly.
        /// </summary>
        [Test]
        public void TideExtremumLabel_MarksHighsAndLowsDifferently()
        {
            var t = new TideData
            {
                Valid = true,
                WindowStartUtc = new DateTime(2000, 1, 6, 10, 0, 0, DateTimeKind.Utc),
            };
            string high = ConditionsFormatter.TideExtremumLabel(t, 5, true, Local);
            string low = ConditionsFormatter.TideExtremumLabel(t, 5, false, Local);

            Assert.That(high, Does.StartWith(ConditionsFormatter.HighTideMarker));
            Assert.That(low, Does.StartWith(ConditionsFormatter.LowTideMarker));
            Assert.That(high, Is.Not.EqualTo(low), "same hour, different marker");
            Assert.That(high.Substring(2), Is.EqualTo(low.Substring(2)), "only the marker differs");
            Assert.That(ConditionsFormatter.HighTideMarker, Is.EqualTo("\u25B2"));
            Assert.That(ConditionsFormatter.LowTideMarker, Is.EqualTo("\u25BC"));
        }

        [Test]
        public void TideBaseline_IsTheDhnNivelMedio()
        {
            Assert.That(ConditionsFormatter.TideBaselineM, Is.EqualTo(1.28f));
            Assert.That(BeachesViewModel.TideBaselineM, Is.EqualTo(ConditionsFormatter.TideBaselineM));
            Assert.That(HomeViewModel.TideBaselineM, Is.EqualTo(ConditionsFormatter.TideBaselineM));
        }
    }
}
