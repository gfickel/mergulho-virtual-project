using System;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The pt-BR strings of the Praias screens, pinned the way
    /// <see cref="ConditionsFormatterTests"/> pins the conditions rows: change them
    /// here or not at all. The two return conventions are the point — <c>null</c>
    /// for "hide this element", <c>"—"</c> for "this column has no value yet" —
    /// and neither is ever a made-up value.
    /// </summary>
    public class BeachContentFormatterTests
    {
        static TideData Tide(bool rising = false) => new TideData
        {
            Valid = true,
            Rising = rising,
            CurrentHeightM = 1.37f,
            NextLowAtUtc = new DateTime(2000, 1, 6, 14, 40, 0, DateTimeKind.Utc),
            NextLowM = 0.42f,
            NextHighAtUtc = new DateTime(2000, 1, 6, 20, 55, 0, DateTimeKind.Utc),
            NextHighM = 2.24f,
        };

        static DateTime Identity(DateTime d) => d;

        [Test]
        public void RiskPill()
        {
            Assert.That(BeachContentFormatter.RiskPill(BeachRiskLevel.Low), Is.EqualTo("Risco: Baixo"));
            Assert.That(BeachContentFormatter.RiskPill(BeachRiskLevel.Medium), Is.EqualTo("Risco: Médio"));
            Assert.That(BeachContentFormatter.RiskPill(BeachRiskLevel.High), Is.EqualTo("Risco: Alto"));
            Assert.That(BeachContentFormatter.RiskPill(BeachRiskLevel.Unknown), Is.Null,
                "unfilled draws no pill — it must never read as 'Baixo'");
        }

        [Test]
        public void IdealTideWord()
        {
            Assert.That(BeachContentFormatter.IdealTide(BeachIdealTide.Low), Is.EqualTo("Baixa"));
            Assert.That(BeachContentFormatter.IdealTide(BeachIdealTide.High), Is.EqualTo("Alta"));
            Assert.That(BeachContentFormatter.IdealTide(BeachIdealTide.Any), Is.EqualTo("Qualquer"));
            Assert.That(BeachContentFormatter.IdealTide(BeachIdealTide.Unknown), Is.Null);
        }

        [Test]
        public void IdealTideNextEvent()
        {
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.Low, Tide(), Identity),
                Is.EqualTo("próx. baixa 14:40"));
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.High, Tide(), Identity),
                Is.EqualTo("próx. alta 20:55"));
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.Any, Tide(), Identity),
                Is.Null, "'qualquer maré' has no event to point at");
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.Unknown, Tide(), Identity), Is.Null);
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.Low, default, Identity), Is.Null);

            var noEvents = new TideData { Valid = true };
            Assert.That(BeachContentFormatter.IdealTideNextEvent(BeachIdealTide.Low, noEvents, Identity), Is.Null);
        }

        [Test]
        public void TideNow()
        {
            Assert.That(BeachContentFormatter.TideNow(Tide(rising: true)), Is.EqualTo("1.4 m · subindo"));
            Assert.That(BeachContentFormatter.TideNow(Tide()), Is.EqualTo("1.4 m · descendo"));
            Assert.That(BeachContentFormatter.TideNow(default), Is.EqualTo("—"));
        }

        [Test]
        public void LifeguardAndBehaviourPrefixes()
        {
            Assert.That(BeachContentFormatter.Lifeguard("Das 08h às 17h"),
                Is.EqualTo("Salva-vidas: Das 08h às 17h"));
            Assert.That(BeachContentFormatter.Lifeguard(""), Is.Null);
            Assert.That(BeachContentFormatter.Lifeguard("   "), Is.Null);
            Assert.That(BeachContentFormatter.Lifeguard(null), Is.Null);

            Assert.That(BeachContentFormatter.SpeciesBehaviour("Usam a rasa como berçário."),
                Is.EqualTo("Comportamento nessa praia: Usam a rasa como berçário."));
            Assert.That(BeachContentFormatter.SpeciesBehaviour(null), Is.Null);
        }

        [Test]
        public void SightingCount()
        {
            Assert.That(BeachContentFormatter.SightingCount(null), Is.Null, "unknown ≠ zero");
            Assert.That(BeachContentFormatter.SightingCount(0), Is.EqualTo("Nenhum avistamento registrado"));
            Assert.That(BeachContentFormatter.SightingCount(1), Is.EqualTo("1 avistamento registrado"));
            Assert.That(BeachContentFormatter.SightingCount(120), Is.EqualTo("120 avistamentos registrados"));
            Assert.That(BeachContentFormatter.SightingCount(1200), Is.EqualTo("1.200 avistamentos registrados"),
                "pt-BR groups thousands with a dot");
            Assert.That(BeachContentFormatter.SightingCount(-3), Is.Null);
        }

        [Test]
        public void TipNumberAndNoValue()
        {
            Assert.That(BeachContentFormatter.TipNumber(0), Is.EqualTo("1"));
            Assert.That(BeachContentFormatter.TipNumber(2), Is.EqualTo("3"));

            Assert.That(BeachContentFormatter.OrNoValue("Set - Fev"), Is.EqualTo("Set - Fev"));
            Assert.That(BeachContentFormatter.OrNoValue("  Set - Fev  "), Is.EqualTo("Set - Fev"));
            Assert.That(BeachContentFormatter.OrNoValue(""), Is.EqualTo("—"));
            Assert.That(BeachContentFormatter.OrNoValue(null), Is.EqualTo("—"));
            Assert.That(BeachContentFormatter.NoValue, Is.EqualTo(ConditionsFormatter.NoValue),
                "one em dash convention across both formatters");
        }

        // ---- Token parsing (the beaches_content.json vocabulary) -------------

        [Test]
        public void RiskTokens()
        {
            Assert.That(BeachContentTokens.TryParseRisk("baixo", out var low), Is.True);
            Assert.That(low, Is.EqualTo(BeachRiskLevel.Low));

            Assert.That(BeachContentTokens.TryParseRisk(" MÉDIO ", out var medium), Is.True,
                "hand-edited files carry accents, capitals and stray spaces");
            Assert.That(medium, Is.EqualTo(BeachRiskLevel.Medium));

            Assert.That(BeachContentTokens.TryParseRisk("alto", out var high), Is.True);
            Assert.That(high, Is.EqualTo(BeachRiskLevel.High));

            Assert.That(BeachContentTokens.TryParseRisk("", out var blank), Is.True,
                "blank is the documented 'unfilled' value, not an error");
            Assert.That(blank, Is.EqualTo(BeachRiskLevel.Unknown));

            Assert.That(BeachContentTokens.TryParseRisk("moderado", out var bogus), Is.False,
                "an unrecognized token is reported so the loader can warn");
            Assert.That(bogus, Is.EqualTo(BeachRiskLevel.Unknown), "…and never guessed into a level");
        }

        [Test]
        public void IdealTideTokens()
        {
            Assert.That(BeachContentTokens.TryParseIdealTide("baixa", out var low), Is.True);
            Assert.That(low, Is.EqualTo(BeachIdealTide.Low));
            Assert.That(BeachContentTokens.TryParseIdealTide("Alta", out var high), Is.True);
            Assert.That(high, Is.EqualTo(BeachIdealTide.High));
            Assert.That(BeachContentTokens.TryParseIdealTide("qualquer", out var any), Is.True);
            Assert.That(any, Is.EqualTo(BeachIdealTide.Any));
            Assert.That(BeachContentTokens.TryParseIdealTide(null, out var blank), Is.True);
            Assert.That(blank, Is.EqualTo(BeachIdealTide.Unknown));
            Assert.That(BeachContentTokens.TryParseIdealTide("meia", out var bogus), Is.False);
            Assert.That(bogus, Is.EqualTo(BeachIdealTide.Unknown));
        }

        // ---- The optional-field contract on the mirror types ----------------

        [Test]
        public void BeachContent_ModelsAbsenceExplicitly()
        {
            var empty = BeachContent.EmptyFor("Praia do Meio");
            Assert.That(empty.BeachName, Is.EqualTo("Praia do Meio"));
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.HasRiskLevel, Is.False);
            Assert.That(empty.HasIdealTide, Is.False);
            Assert.That(empty.EnvironmentTags, Is.Not.Null, "collections are empty, never null");
            Assert.That(empty.Advisories, Is.Empty);
            Assert.That(empty.Species, Is.Empty);
            Assert.That(empty.Tips, Is.Empty);

            Assert.That(BeachContent.EmptyFor("a"), Is.Not.SameAs(BeachContent.EmptyFor("a")),
                "no shared mutable singleton");

            var blankStrings = new BeachContent { BestSeason = "   ", SightingPeak = "" };
            Assert.That(blankStrings.HasBestSeason, Is.False, "whitespace is absence, not a value");
            Assert.That(blankStrings.HasSightingPeak, Is.False);
            Assert.That(blankStrings.IsEmpty, Is.True);

            var oneField = new BeachContent { IdealTide = BeachIdealTide.Low };
            Assert.That(oneField.IsEmpty, Is.False);
        }
    }
}
