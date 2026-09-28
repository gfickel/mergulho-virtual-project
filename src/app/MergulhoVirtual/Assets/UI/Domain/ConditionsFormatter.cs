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

        /// <summary>"subindo, próxima alta 14:40 (2.2 m)" / "descendo, próxima baixa …".</summary>
        public static string Tide(TideData t, Func<DateTime, DateTime> toLocalTime)
        {
            if (!t.Valid) return NoValue;
            var toLocal = toLocalTime ?? (d => d.ToLocalTime());
            if (t.Rising && t.NextHighAtUtc != DateTime.MinValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "subindo, próxima alta {0:HH:mm} ({1:0.0} m)",
                    toLocal(t.NextHighAtUtc), t.NextHighM);
            }
            if (!t.Rising && t.NextLowAtUtc != DateTime.MinValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "descendo, próxima baixa {0:HH:mm} ({1:0.0} m)",
                    toLocal(t.NextLowAtUtc), t.NextLowM);
            }
            return t.Rising ? "subindo" : "descendo";
        }

        /// <summary>"Gibosa Crescente · 88% iluminada".</summary>
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
