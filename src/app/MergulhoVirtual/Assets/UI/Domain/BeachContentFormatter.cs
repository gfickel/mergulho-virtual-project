using System;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR formatting for the Praias screens' editorial rows — the risk pill,
    /// the "Melhor Época"/"Maré Ideal" stat values, the lifeguard/advisory alert
    /// bar, the species behaviour line, the tip numbering and the sighting count.
    ///
    /// <para>Same contract as <see cref="ConditionsFormatter"/>, which is the
    /// precedent: plain static C#, no UnityEngine, no state, every clock/local-time
    /// conversion injected by the caller. <b>Screens must not build user-visible
    /// strings themselves</b> — if a new string is needed it is added here and
    /// pinned by a test.</para>
    ///
    /// <para>Two return conventions, deliberately different:
    /// <b>null</b> means "there is nothing to say, hide this element" (the pill,
    /// the alert bar, the behaviour line), while <b>"—"</b>
    /// (<see cref="ConditionsFormatter.NoValue"/>) is what a stat slot that always
    /// occupies a column shows when its value is unfilled. Neither is ever an
    /// invented value — an unfilled risk level draws no pill rather than a
    /// reassuring one.</para>
    /// </summary>
    public static class BeachContentFormatter
    {
        /// <summary>Shown wherever a value has no data yet — shared with the conditions rows.</summary>
        public const string NoValue = ConditionsFormatter.NoValue;

        public const string RiskPrefix = "Risco: ";
        public const string LifeguardPrefix = "Salva-vidas: ";
        public const string BehaviourPrefix = "Comportamento nessa praia: ";

        /// <summary>pt-BR thousands grouping ("1.200 avistamentos"). Decimals stay
        /// invariant ("2.2 m") to match ConditionsFormatter's existing output.</summary>
        static readonly NumberFormatInfo PtBrGrouping = BuildPtBrGrouping();

        static NumberFormatInfo BuildPtBrGrouping()
        {
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberGroupSeparator = ".";
            nfi.NumberGroupSizes = new[] { 3 };
            return nfi;
        }

        // ---- Risk -----------------------------------------------------------

        /// <summary>"Baixo" / "Médio" / "Alto"; null when unfilled.</summary>
        public static string RiskLabel(BeachRiskLevel level)
        {
            switch (level)
            {
                case BeachRiskLevel.Low: return "Baixo";
                case BeachRiskLevel.Medium: return "Médio";
                case BeachRiskLevel.High: return "Alto";
                default: return null;
            }
        }

        /// <summary>"Risco: Baixo"; null when unfilled — the screen draws no pill.</summary>
        public static string RiskPill(BeachRiskLevel level)
        {
            string label = RiskLabel(level);
            return label == null ? null : RiskPrefix + label;
        }

        // ---- Tide -----------------------------------------------------------

        /// <summary>"Baixa" / "Alta" / "Qualquer"; null when unfilled.</summary>
        public static string IdealTide(BeachIdealTide tide)
        {
            switch (tide)
            {
                case BeachIdealTide.Low: return "Baixa";
                case BeachIdealTide.High: return "Alta";
                case BeachIdealTide.Any: return "Qualquer";
                default: return null;
            }
        }

        /// <summary>
        /// The live half of "Maré Ideal": "próx. baixa 14:40" for an ideal low tide,
        /// "próx. alta 14:40" for a high one. Null when the ideal tide is unfilled or
        /// "qualquer", or when the tide table has no such upcoming event.
        ///
        /// <para>Kept separate from <see cref="IdealTide"/> on purpose. The Figma
        /// frame reads "Baixa (até 14h)", but "até" asserts a window the DHN table
        /// does not give us — it publishes the instant of each extremum, not how long
        /// the useful window lasts. Rather than invent that, the ViewModel exposes the
        /// tide word and this caption separately and the screen decides how to place
        /// them.</para>
        /// </summary>
        public static string IdealTideNextEvent(
            BeachIdealTide tide, TideData t, Func<DateTime, DateTime> toLocalTime)
        {
            if (!t.Valid) return null;
            var toLocal = toLocalTime ?? (d => d.ToLocalTime());

            DateTime atUtc;
            string word;
            if (tide == BeachIdealTide.Low) { atUtc = t.NextLowAtUtc; word = "baixa"; }
            else if (tide == BeachIdealTide.High) { atUtc = t.NextHighAtUtc; word = "alta"; }
            else return null;

            if (atUtc == DateTime.MinValue) return null;
            return string.Format(CultureInfo.InvariantCulture, "próx. {0} {1:HH:mm}", word, toLocal(atUtc));
        }

        /// <summary>
        /// "1.4 m · subindo" / "1.4 m · descendo" for the "Maré agora" stat; "—" when invalid.
        /// <para>
        /// THE SPACE AFTER THE SEPARATOR IS U+00A0 — do not "clean" the <c>\u00A0</c>
        /// escape back to a plain space. The stat card is half the row, so its content
        /// box is 139dp at 390 and 124dp at 360; at the card's 16/700 the two strings
        /// measure 116.9dp and 131.1dp, so "descendo" still wraps on a 360dp phone.
        /// With a normal space the wrap landed AFTER the separator — "1.4 m ·" alone on
        /// line one, which reads as a stray character rather than a continuation.
        /// Binding the separator to the word it introduces moves the only remaining
        /// break to the space before it, so the wrap reads "1.4 m" / "· descendo".
        /// Advance width is unchanged (U+00A0 is the same width as U+0020 in Inter),
        /// so nothing that already fitted on one line moves. See ConditionsFormatter.Tide
        /// for the same idiom and the evidence that UI Toolkit honours U+00A0.
        /// </para>
        /// </summary>
        public static string TideNow(TideData t)
        {
            if (!t.Valid) return NoValue;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} m ·\u00A0{1}",
                t.CurrentHeightM, t.Rising ? "subindo" : "descendo");
        }

        // ---- Alerts and body copy -------------------------------------------

        /// <summary>"Salva-vidas: Das 08h às 17h"; null when unfilled.</summary>
        public static string Lifeguard(string hours) =>
            string.IsNullOrWhiteSpace(hours) ? null : LifeguardPrefix + hours.Trim();

        /// <summary>"Comportamento nessa praia: …"; null when unfilled.</summary>
        public static string SpeciesBehaviour(string behaviour) =>
            string.IsNullOrWhiteSpace(behaviour) ? null : BehaviourPrefix + behaviour.Trim();

        /// <summary>"1", "2", "3" — the numbered bullet of a "Dicas de convivência" row.</summary>
        public static string TipNumber(int index) =>
            (index + 1).ToString(CultureInfo.InvariantCulture);

        // ---- Sightings ------------------------------------------------------

        /// <summary>
        /// "120 avistamentos registrados" / "1 avistamento registrado" /
        /// "Nenhum avistamento registrado". Null when the count is unknown — which
        /// is the state today: the backend counter is global, not per beach
        /// (DESIGN_IMPLEMENTATION.md §5.1), so the row stays hidden rather than
        /// showing the island-wide total as if it were this beach's.
        /// </summary>
        public static string SightingCount(int? count)
        {
            if (!count.HasValue || count.Value < 0) return null;
            if (count.Value == 0) return "Nenhum avistamento registrado";
            if (count.Value == 1) return "1 avistamento registrado";
            return count.Value.ToString("#,0", PtBrGrouping) + " avistamentos registrados";
        }

        // ---- Helpers --------------------------------------------------------

        /// <summary>The value, or "—" when it is unfilled. For stat slots that always
        /// occupy a column; sections that can disappear should test <c>Has*</c> instead.</summary>
        public static string OrNoValue(string value) =>
            string.IsNullOrWhiteSpace(value) ? NoValue : value.Trim();
    }
}
