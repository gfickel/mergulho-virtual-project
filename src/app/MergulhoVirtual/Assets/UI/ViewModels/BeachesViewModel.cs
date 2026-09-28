using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// State + presentation logic for the Beaches screen (list ↔ detail,
    /// conditions/tide/moon rows, beach override selector). Plain C# —
    /// no UnityEngine — so it is fully unit-testable with fake services.
    /// Row formatting lives in <see cref="ConditionsFormatter"/> — shared with
    /// HomeViewModel, and string-for-string identical to the legacy uGUI
    /// ConditionsCardView it was ported from.
    /// </summary>
    public sealed class BeachesViewModel : IDisposable
    {
        /// <summary>DHN "Nível Médio" (MSL→LAT offset) for the Noronha station — sparkline baseline.</summary>
        public const float TideBaselineM = ConditionsFormatter.TideBaselineM;

        public const string AutoOptionLabel = "Automático (GPS)";

        readonly IConditionsService conditions;
        readonly ITideService tides;
        readonly IBeachOverride beachOverride;
        readonly Func<DateTime> utcNow;
        readonly Func<DateTime, DateTime> toLocalTime;
        readonly List<string> overrideKeys;
        bool disposed;

        public IReadOnlyList<BeachInfo> Beaches { get; }

        /// <summary>
        /// Dropdown <b>labels</b>: index 0 = automatic (GPS), then one pt-BR beach
        /// name per beach (<see cref="BeachInfo.DisplayName"/>). These are for the
        /// user's eyes only — the override is applied with the matching
        /// <see cref="BeachInfo.Name"/> key, which is what the spawner, the content
        /// file and the backend agree on. Never pass a label to a lookup.
        /// </summary>
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
            // Parallel to `choices`: the key each label applies (null for "automatic").
            overrideKeys = new List<string>(Beaches.Count + 1) { null };
            foreach (var beach in Beaches)
            {
                choices.Add(beach.DisplayName);
                overrideKeys.Add(beach.Name);
            }
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
            else beachOverride?.SetOverride(overrideKeys[index]);
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
                return "Condições · " + DisplayNameForKey(snap.BeachName);
            }
        }

        /// <summary>
        /// pt-BR label for a beach key. The conditions snapshot carries the GPS
        /// resolver's key ("Sueste Beach"), which must never reach a label; falls
        /// back to the key itself for a beach that is not in the catalog.
        /// </summary>
        string DisplayNameForKey(string beachKey)
        {
            foreach (var beach in Beaches)
            {
                if (beach.Name == beachKey) return beach.DisplayName;
            }
            return beachKey;
        }

        public string WaveText => ConditionsFormatter.Wave(conditions?.Current);

        public string TideText => ConditionsFormatter.Tide(CurrentTide, toLocalTime);

        public string MoonText => ConditionsFormatter.Moon(utcNow());

        public string WindText => ConditionsFormatter.Wind(conditions?.Current);

        public string WaterText => ConditionsFormatter.Water(conditions?.Current);

        public string FreshnessText => ConditionsFormatter.Freshness(conditions?.Current, utcNow());

        /// <summary>
        /// Local "▲ HH:mm" (high) / "▼ HH:mm" (low) label for a tide extremum at the
        /// given sparkline sample index (hours after the tide window start). Shape
        /// matches MdSparkline.ExtremumLabelFormatter.
        /// </summary>
        public string FormatTideExtremumLabel(int sampleIndex, bool isHigh) =>
            ConditionsFormatter.TideExtremumLabel(CurrentTide, sampleIndex, isHigh, toLocalTime);
    }
}
