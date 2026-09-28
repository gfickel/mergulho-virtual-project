using System;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Tide state derived from the DHN table. Mirror of Assembly-CSharp's
    /// TideSnapshot, kept engine-free for ViewModel tests.
    /// </summary>
    public struct TideData
    {
        public bool Valid;
        public float CurrentHeightM;

        /// <summary>True when the next hourly sample is higher than the current one.</summary>
        public bool Rising;

        /// <summary>Next DHN high-tide event (UTC); DateTime.MinValue = unknown.</summary>
        public DateTime NextHighAtUtc;
        public float NextHighM;

        /// <summary>Next DHN low-tide event (UTC); DateTime.MinValue = unknown.</summary>
        public DateTime NextLowAtUtc;
        public float NextLowM;

        /// <summary>Exactly 24 hourly heights starting at WindowStartUtc (sparkline window).</summary>
        public float[] Next24hHeights;
        public DateTime WindowStartUtc;
    }

    public interface ITideService
    {
        /// <summary>Latest tide state; Valid == false when tide data is unavailable.</summary>
        TideData Current { get; }

        event Action<TideData> Changed;
    }
}
