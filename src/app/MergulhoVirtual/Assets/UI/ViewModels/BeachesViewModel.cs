using System;
using System.Collections.Generic;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// State + presentation logic for the Beaches screen (list ↔ detail,
    /// conditions/tide/moon rows, beach override selector). Plain C# —
    /// no UnityEngine — so it is fully unit-testable with fake services.
    /// Formatting is ported from the legacy uGUI ConditionsCardView to keep
    /// string-for-string parity during the strangler migration.
    /// </summary>
    public sealed class BeachesViewModel : IDisposable
    {
        /// <summary>DHN "Nível Médio" (MSL→LAT offset) for the Noronha station — sparkline baseline.</summary>
        public const float TideBaselineM = 1.28f;

        public const string AutoOptionLabel = "Automático (GPS)";

        static readonly string[] CardinalDirections = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        readonly IConditionsService conditions;
        readonly ITideService tides;
        readonly IBeachOverride beachOverride;
        readonly Func<DateTime> utcNow;
        readonly Func<DateTime, DateTime> toLocalTime;
        bool disposed;

        public IReadOnlyList<BeachInfo> Beaches { get; }

        /// <summary>Dropdown choices: index 0 = automatic (GPS), then one entry per beach.</summary>
        public IReadOnlyList<string> OverrideChoices { get; }

        /// <summary>Currently selected override choice; 0 = automatic (GPS).</summary>
        public int OverrideIndex { get; private set; }

        /// <summary>Null while the list is showing; the open beach while in detail.</summary>
        public BeachInfo SelectedBeach { get; private set; }

        /// <summary>Raised when the list ↔ detail state (or the open beach) changes.</summary>
        public event Action NavigationChanged;

        /// <summary>Raised when any displayed data row may have changed (conditions, tide, freshness).</summary>
        public event Action DataChanged;

        public BeachesViewModel(
            IBeachCatalog catalog,
            IConditionsService conditions,
            ITideService tides,
            IBeachOverride beachOverride,
            Func<DateTime> utcNow = null,
            Func<DateTime, DateTime> toLocalTime = null)
        {
            this.conditions = conditions;
            this.tides = tides;
            this.beachOverride = beachOverride;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.toLocalTime = toLocalTime ?? (d => d.ToLocalTime());

            Beaches = catalog?.Beaches ?? Array.Empty<BeachInfo>();

            var choices = new List<string>(Beaches.Count + 1) { AutoOptionLabel };
            foreach (var beach in Beaches) choices.Add(beach.Name);
            OverrideChoices = choices;

            if (this.conditions != null) this.conditions.Changed += OnConditionsChanged;
            if (this.tides != null) this.tides.Changed += OnTideChanged;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (conditions != null) conditions.Changed -= OnConditionsChanged;
            if (tides != null) tides.Changed -= OnTideChanged;
        }

        void OnConditionsChanged(ConditionsData _) => DataChanged?.Invoke();
        void OnTideChanged(TideData _) => DataChanged?.Invoke();

        /// <summary>Re-renders time-relative strings ("Atualizado: há 5m"); call ~1/min.</summary>
        public void NotifyTimePassed() => DataChanged?.Invoke();

        // ---- Navigation -----------------------------------------------------

        public void ShowDetail(string beachName)
        {
            foreach (var beach in Beaches)
            {
                if (beach.Name != beachName) continue;
                SelectedBeach = beach;
                NavigationChanged?.Invoke();
                return;
            }
        }

        public void ShowList()
        {
            if (SelectedBeach == null) return;
            SelectedBeach = null;
            NavigationChanged?.Invoke();
        }

        // ---- Beach override -------------------------------------------------

        public void SelectOverride(int index)
        {
            if (index < 0 || index >= OverrideChoices.Count || index == OverrideIndex) return;
            OverrideIndex = index;
            if (index == 0) beachOverride?.ClearOverride();
            else beachOverride?.SetOverride(OverrideChoices[index]);
        }

        // ---- Conditions rows ------------------------------------------------

        public TideData CurrentTide => tides?.Current ?? default;

        /// <summary>
        /// "Condições" — suffixed with the conditions' beach when it differs from
        /// the open detail beach (conditions follow the GPS/override beach, not
        /// the browsed one — same behavior as the legacy screen, made explicit).
        /// </summary>
        public string ConditionsTitle
        {
            get
            {
                var snap = conditions?.Current;
                if (snap == null || string.IsNullOrEmpty(snap.BeachName)) return "Condições";
                if (SelectedBeach != null && snap.BeachName == SelectedBeach.Name) return "Condições";
                return "Condições · " + snap.BeachName;
            }
        }

        public string WaveText
        {
            get
            {
                var s = conditions?.Current;
                if (s == null) return "—";
                var parts = new List<string>(3);
                if (s.WaveHeightM.HasValue)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0} m", s.WaveHeightM.Value));
                if (s.WavePeriodS.HasValue)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} s", s.WavePeriodS.Value));
                if (s.WaveDirectionDeg.HasValue)
                    parts.Add(DegToCardinal(s.WaveDirectionDeg.Value));
                return parts.Count == 0 ? "—" : string.Join(" · ", parts);
            }
        }

        public string TideText
        {
            get
            {
                var t = CurrentTide;
                if (!t.Valid) return "—";
                if (t.Rising && t.NextHighAtUtc != DateTime.MinValue)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "subindo, próxima alta {0:HH:mm} ({1:0.0} m)",
                        toLocalTime(t.NextHighAtUtc), t.NextHighM);
                }
                if (!t.Rising && t.NextLowAtUtc != DateTime.MinValue)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "descendo, próxima baixa {0:HH:mm} ({1:0.0} m)",
                        toLocalTime(t.NextLowAtUtc), t.NextLowM);
                }
                return t.Rising ? "subindo" : "descendo";
            }
        }

        public string MoonText
        {
            get
            {
                DateTime now = utcNow();
                var name = MoonPhase.Name(MoonPhase.Phase(now));
                int illumPct = (int)Math.Round(MoonPhase.Illumination(now) * 100f);
                return $"{MoonPhaseLabelPtBr(name)} · {illumPct}% iluminada";
            }
        }

        public string WindText
        {
            get
            {
                var s = conditions?.Current;
                if (s == null) return "—";
                var parts = new List<string>(2);
                if (s.WindSpeedKmh.HasValue)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} km/h", s.WindSpeedKmh.Value));
                if (s.WindDirectionDeg.HasValue)
                    parts.Add(DegToCardinal(s.WindDirectionDeg.Value));
                return parts.Count == 0 ? "—" : string.Join(" ", parts);
            }
        }

        public string WaterText
        {
            get
            {
                var s = conditions?.Current;
                if (s == null || !s.SeaTempC.HasValue) return "—";
                return string.Format(CultureInfo.InvariantCulture, "{0:0} °C", s.SeaTempC.Value);
            }
        }

        public string FreshnessText
        {
            get
            {
                var s = conditions?.Current;
                if (s == null || s.FetchedAtUtc == DateTime.MinValue) return "Atualizado: —";
                TimeSpan age = utcNow() - s.FetchedAtUtc;
                if (age.TotalSeconds < 60) return "Atualizado: agora";
                if (age.TotalMinutes < 60) return $"Atualizado: há {(int)age.TotalMinutes}m";
                if (age.TotalHours < 24) return $"Atualizado: há {(int)age.TotalHours}h";
                return $"Atualizado: há {(int)age.TotalDays}d";
            }
        }

        /// <summary>
        /// Local "HH:mm" label for a tide extremum at the given sparkline sample
        /// index (hours after the tide window start). Shape matches
        /// MdSparkline.ExtremumLabelFormatter.
        /// </summary>
        public string FormatTideExtremumLabel(int sampleIndex, bool isHigh)
        {
            var t = CurrentTide;
            if (!t.Valid) return null;
            return toLocalTime(t.WindowStartUtc.AddHours(sampleIndex)).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        static string DegToCardinal(float deg)
        {
            deg = ((deg % 360f) + 360f) % 360f;
            int idx = (int)Math.Round(deg / 45f) % 8;
            return CardinalDirections[idx];
        }

        static string MoonPhaseLabelPtBr(MoonPhaseName n)
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
                default: return "—";
            }
        }
    }
}
