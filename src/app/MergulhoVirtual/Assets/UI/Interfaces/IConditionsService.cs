using System;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Sea/weather conditions for the active beach, with per-field availability.
    /// Mirror of Assembly-CSharp's ConditionsSnapshot, kept engine-free so
    /// ViewModels can be tested as plain C#.
    /// </summary>
    public sealed class ConditionsData
    {
        public string BeachName;

        /// <summary>UTC fetch time; DateTime.MinValue = never fetched.</summary>
        public DateTime FetchedAtUtc;

        public float? WaveHeightM;
        public float? WavePeriodS;
        public float? WaveDirectionDeg;
        public float? SeaTempC;
        public float? WindSpeedKmh;
        public float? WindDirectionDeg;
    }

    /// <summary>
    /// Conditions for whichever beach is currently active (GPS-resolved,
    /// fallback, or override) — same semantics as ConditionsService.
    /// </summary>
    public interface IConditionsService
    {
        /// <summary>Latest snapshot, or null when nothing has been fetched or cached yet.</summary>
        ConditionsData Current { get; }

        /// <summary>
        /// True when the most recent fetch produced nothing usable, cleared by the
        /// next one that does.
        ///
        /// <para><b>It is what separates "failed" from "loading".</b> A null
        /// <see cref="Current"/> on its own is ambiguous — it is also the state
        /// before the first fetch returns — and telling a user something went wrong
        /// while it is still on its way would be a lie. A screen shows an error only
        /// for null <see cref="Current"/> AND this flag.</para>
        /// </summary>
        bool LastFetchFailed { get; }

        /// <summary>True while a fetch is in flight, so a retry affordance can show
        /// that the tap did something.</summary>
        bool IsFetching { get; }

        /// <summary>
        /// Fetch now instead of waiting out the background interval. This is a real
        /// request, not a repaint — it is what a "Tentar novamente" button calls.
        /// A no-op while one is already in flight.
        /// </summary>
        void Refresh();

        event Action<ConditionsData> Changed;
    }
}
