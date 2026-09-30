using System;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    public class ReportFormatterTests
    {
        // Local times — the ViewModel converts before calling, so these are naive
        // by design and the assertions are timezone-proof.
        static readonly DateTime Now = new DateTime(2026, 9, 28, 14, 5, 0);

        // ---- Status pill ----------------------------------------------------

        [Test]
        public void StateLabel_HasItsOwnWordsForEveryState()
        {
            Assert.That(ReportFormatter.StateLabel(SightingState.Queued), Is.EqualTo("PENDENTE"),
                "uppercase because the Figma frame renders the pill that way; UI Toolkit has no text-transform");
            Assert.That(ReportFormatter.StateLabel(SightingState.Retrying), Is.EqualTo("Tentando de novo"));
            Assert.That(ReportFormatter.StateLabel(SightingState.WaitingForNetwork), Is.EqualTo("Sem conexão"));
            Assert.That(ReportFormatter.StateLabel(SightingState.Failed), Is.EqualTo("Falhou"));
        }

        [Test]
        public void StateLabel_EveryStateIsDistinct()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (SightingState state in Enum.GetValues(typeof(SightingState)))
                Assert.That(seen.Add(ReportFormatter.StateLabel(state)), Is.True, state + " reuses another state's label");
        }

        // ---- Row caption ----------------------------------------------------

        [Test]
        public void DayTime_Today_IsTheFigmaShape()
        {
            var when = new DateTime(2026, 9, 28, 9, 32, 0);
            Assert.That(ReportFormatter.DayTime(when, Now), Is.EqualTo("Hoje, 9:32"));
        }

        [Test]
        public void DayTime_Yesterday()
        {
            var when = new DateTime(2026, 9, 27, 18, 4, 0);
            Assert.That(ReportFormatter.DayTime(when, Now), Is.EqualTo("Ontem, 18:04"));
        }

        [Test]
        public void DayTime_Older_FallsBackToDayMonth()
        {
            var when = new DateTime(2026, 9, 3, 7, 12, 0);
            Assert.That(ReportFormatter.DayTime(when, Now), Is.EqualTo("03/09, 7:12"));
        }

        [Test]
        public void DayTime_ComparesCalendarDays_NotElapsedHours()
        {
            // 23:55 yesterday is 14 hours ago but still "Ontem".
            var when = new DateTime(2026, 9, 27, 23, 55, 0);
            Assert.That(ReportFormatter.DayTime(when, Now), Does.StartWith("Ontem"));
        }

        [Test]
        public void RowCaption_JoinsWithTheMiddleDot()
        {
            Assert.That(ReportFormatter.RowCaption("Hoje, 9:32", "Baía do Sueste"),
                Is.EqualTo("Hoje, 9:32 · Baía do Sueste"));
        }

        [Test]
        public void RowCaption_DropsTheBeachHalfWhenThereIsNone()
        {
            Assert.That(ReportFormatter.RowCaption("Hoje, 9:32", null), Is.EqualTo("Hoje, 9:32"));
            Assert.That(ReportFormatter.RowCaption("Hoje, 9:32", "  "), Is.EqualTo("Hoje, 9:32"));
        }

        // ---- Photo size -----------------------------------------------------

        [Test]
        public void Megabytes_RendersWholeAndFractionalSizes()
        {
            Assert.That(ReportFormatter.Megabytes(20L * 1024 * 1024), Is.EqualTo("20 MB"));
            Assert.That(ReportFormatter.Megabytes(24L * 1024 * 1024 + 512 * 1024), Is.EqualTo("24.5 MB"));
        }

        [Test]
        public void PhotoTooLarge_NamesBothNumbers()
        {
            string text = ReportFormatter.PhotoTooLarge(24L * 1024 * 1024, 20L * 1024 * 1024);
            Assert.That(text, Is.EqualTo("A foto tem 24 MB. O limite é 20 MB."));
        }
    }
}
