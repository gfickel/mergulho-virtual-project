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

        event Action<ConditionsData> Changed;
    }
}
