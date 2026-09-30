using System;
using System.Collections.Generic;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR formatting for the shared conditions rows (Onda / Maré / Lua / Vento /
    /// Água), the "Atualizado:" freshness line and the tide sparkline extremum
    /// labels.
    ///
    /// Extracted from <see cref="BeachesViewModel"/> when the Início (Home) screen
    /// landed: both screens render the same card, and two copies of these strings
    /// would drift. Plain static C# — no UnityEngine, no state — so every ViewModel
    /// passes in its own injected clock / local-time converter and stays testable
    /// and timezone-proof.
    ///
    /// The output is byte-identical to the legacy uGUI ConditionsCardView and is
    /// pinned by ConditionsFormatterTests (and, from the other side, by the
    /// untouched BeachesViewModelTests). Change the strings there or not at all.
    /// </summary>
    public static class ConditionsFormatter
    {
        /// <summary>DHN "Nível Médio" (MSL→LAT offset) for the Noronha station — sparkline baseline.</summary>
        public const float TideBaselineM = 1.28f;

        /// <summary>Shown wherever a value has no data yet (em dash, U+2014).</summary>
        public const string NoValue = "—";

        static readonly string[] CardinalDirections = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>"1.2 m · 8 s · SE" — omits whichever parts the snapshot lacks.</summary>
        public static string Wave(ConditionsData s)
        {
            if (s == null) return NoValue;
            var parts = new List<string>(3);
            if (s.WaveHeightM.HasValue)
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0} m", s.WaveHeightM.Value));
            if (s.WavePeriodS.HasValue)
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} s", s.WavePeriodS.Value));
            if (s.WaveDirectionDeg.HasValue)
                parts.Add(DegToCardinal(s.WaveDirectionDeg.Value));
            return parts.Count == 0 ? NoValue : string.Join(" · ", parts);
        }

        /// <summary>
        /// "subindo, próxima alta 14:40 (2.2 m)" / "descendo, próxima baixa …".
        /// <para>
        /// THE SPACE INSIDE THE PARENTHESIS IS U+00A0, NOT U+0020 — do not "clean"
        /// the <c>\u00A0</c> escape back to a plain space. This is the longest of the
        /// five conditions rows and it is the one that wraps: the label column is a
        /// fixed 68dp, leaving the value 242dp at 390 and 212dp at 360, while the two
        /// strings measure 229.6dp and 257.0dp at body-large (14/400). So "descendo…"
        /// already wrapped at 390 and "subindo…" started wrapping at 360 — and both
        /// broke at the space inside "(2.2 m)", stranding "m)" alone on line two,
        /// which reads as a truncation rather than a wrap.
        /// </para>
        /// <para>
        /// A no-break space removes that break opportunity without removing any other,
        /// so the line now breaks before "(" and the measurement travels as one unit.
        /// It changes no advance width (U+00A0 and U+0020 are the same width in all
        /// four Inter faces), so the 390 single-line case is byte-identical. UI
        /// Toolkit honours it: TextCore's word-wrap save-state test in
        /// TextGeneratorParsing excludes <c>k_NoBreakSpace</c> (0x00A0) from valid
        /// break points, and U+00A0 is in the cmap of all four shipped Inter TTFs.
        /// </para>
        /// </summary>
        public static string Tide(TideData t, Func<DateTime, DateTime> toLocalTime)
        {
            if (!t.Valid) return NoValue;
            var toLocal = toLocalTime ?? (d => d.ToLocalTime());
            if (t.Rising && t.NextHighAtUtc != DateTime.MinValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "subindo, próxima alta {0:HH:mm} ({1:0.0}\u00A0m)",
                    toLocal(t.NextHighAtUtc), t.NextHighM);
            }
            if (!t.Rising && t.NextLowAtUtc != DateTime.MinValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "descendo, próxima baixa {0:HH:mm} ({1:0.0}\u00A0m)",
                    toLocal(t.NextLowAtUtc), t.NextLowM);
            }
            return t.Rising ? "subindo" : "descendo";
        }

        /// <summary>
        /// "Gibosa Crescente · 88% iluminada".
        /// <para>
        /// NO no-break space here, unlike <see cref="Tide"/> — MEASURED, not assumed.
        /// The four long phase names do overflow the 212dp value column on a 360dp
        /// phone ("Gibosa Minguante · 100% iluminada" is 236.1dp at body-large), so
        /// this row wraps there. But the wrap is harmless: the break lands between
        /// "100%" and "iluminada", because the run up to the percentage is only
        /// 168.9dp and word wrapping takes the LAST break that fits, not the first.
        /// The separator therefore stays mid-line and the percentage stays whole at
        /// every phase name and every 1-to-3-digit percentage. Binding the separator
        /// the way TideNow does would be a no-op at best and, if the column ever got
        /// narrower, would force "· 100% iluminada" onto line two with a leading dot —
        /// strictly worse than what it does now. Leave it alone.
        /// </para>
        /// </summary>
        public static string Moon(DateTime utcNow)
        {
            var name = MoonPhase.Name(MoonPhase.Phase(utcNow));
            int illumPct = (int)Math.Round(MoonPhase.Illumination(utcNow) * 100f);
            return $"{MoonPhaseLabelPtBr(name)} · {illumPct}% iluminada";
        }

        /// <summary>"24 km/h E".</summary>
        public static string Wind(ConditionsData s)
        {
            if (s == null) return NoValue;
            var parts = new List<string>(2);
            if (s.WindSpeedKmh.HasValue)
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} km/h", s.WindSpeedKmh.Value));
            if (s.WindDirectionDeg.HasValue)
                parts.Add(DegToCardinal(s.WindDirectionDeg.Value));
            return parts.Count == 0 ? NoValue : string.Join(" ", parts);
        }

        /// <summary>"27 °C".</summary>
        public static string Water(ConditionsData s)
        {
            if (s == null || !s.SeaTempC.HasValue) return NoValue;
            return string.Format(CultureInfo.InvariantCulture, "{0:0} °C", s.SeaTempC.Value);
        }

        /// <summary>"Atualizado: agora" / "… há 5m" / "… há 3h" / "… há 2d".</summary>
        public static string Freshness(ConditionsData s, DateTime utcNow)
        {
            if (s == null || s.FetchedAtUtc == DateTime.MinValue) return "Atualizado: " + NoValue;
            TimeSpan age = utcNow - s.FetchedAtUtc;
            if (age.TotalSeconds < 60) return "Atualizado: agora";
            if (age.TotalMinutes < 60) return $"Atualizado: há {(int)age.TotalMinutes}m";
            if (age.TotalHours < 24) return $"Atualizado: há {(int)age.TotalHours}h";
            return $"Atualizado: há {(int)age.TotalDays}d";
        }

        /// <summary>High-tide marker prefixing an extremum label (V2 draws a filled
        /// up-triangle). U+25B2, a BMP codepoint Inter covers.</summary>
        public const string HighTideMarker = "\u25B2";

        /// <summary>Low-tide marker (U+25BC, filled down-triangle).</summary>
        public const string LowTideMarker = "\u25BC";

        /// <summary>
        /// Local "▲ HH:mm" (high) / "▼ HH:mm" (low) label for a tide extremum at the
        /// given sparkline sample index (hours after the tide window start). The
        /// marker is what tells the two apart in the V2 frame, where every extremum
        /// label sits on the same baseline under the curve. Shape matches
        /// MdSparkline.ExtremumLabelFormatter; null when there is no tide data.
        /// </summary>
        public static string TideExtremumLabel(
            TideData t, int sampleIndex, bool isHigh, Func<DateTime, DateTime> toLocalTime)
        {
            if (!t.Valid) return null;
            var toLocal = toLocalTime ?? (d => d.ToLocalTime());
            string marker = isHigh ? HighTideMarker : LowTideMarker;
            return marker + " " +
                   toLocal(t.WindowStartUtc.AddHours(sampleIndex)).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Degrees (meteorological) to one of the 8 cardinal points.</summary>
        public static string DegToCardinal(float deg)
        {
            deg = ((deg % 360f) + 360f) % 360f;
            int idx = (int)Math.Round(deg / 45f) % 8;
            return CardinalDirections[idx];
        }

        public static string MoonPhaseLabelPtBr(MoonPhaseName n)
        {
            switch (n)
            {
                case MoonPhaseName.New:            return "Nova";
                case MoonPhaseName.WaxingCrescent: return "Crescente";
                case MoonPhaseName.FirstQuarter:   return "Quarto Crescente";
                case MoonPhaseName.WaxingGibbous:  return "Gibosa Crescente";
                case MoonPhaseName.Full:           return "Cheia";
                case MoonPhaseName.WaningGibbous:  return "Gibosa Minguante";
                case MoonPhaseName.LastQuarter:    return "Quarto Minguante";
                case MoonPhaseName.WaningCrescent: return "Minguante";
                default: return NoValue;
            }
        }
    }
}
