using System;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR formatting for the Reportar screen — the pending feed's status pill
    /// and its "Hoje, 9:32 · Baía do Sueste" caption, plus the photo-size error.
    ///
    /// <para>Same contract as <see cref="ConditionsFormatter"/> and
    /// <see cref="BeachContentFormatter"/>: plain static C#, no UnityEngine, no
    /// state, every clock value passed in by the caller. <b>Screens must not build
    /// user-visible strings themselves</b> — a new string is added here and pinned
    /// by a test.</para>
    /// </summary>
    public static class ReportFormatter
    {
        /// <summary>Separator between the time and the beach in a feed row —
        /// the same middle dot the conditions rows use.</summary>
        public const string Separator = " · ";

        /// <summary>Title of a feed row whose report carries no species. Not a
        /// placeholder standing in for data: the species chips are optional, so
        /// "an avistamento with no species named" is a real, valid submission.</summary>
        public const string UnnamedSpeciesTitle = "Avistamento";

        // ---- Status pill ----------------------------------------------------

        public const string QueuedLabel = "Pendente";
        public const string RetryingLabel = "Tentando de novo";
        public const string OfflineLabel = "Sem conexão";
        public const string FailedLabel = "Falhou";

        /// <summary>
        /// The pill over a feed row. Every state has its own words — a report
        /// blocked on connectivity reads differently from one the backend
        /// rejected, because the user can do something about the first one.
        /// </summary>
        public static string StateLabel(SightingState state)
        {
            switch (state)
            {
                case SightingState.Retrying: return RetryingLabel;
                case SightingState.WaitingForNetwork: return OfflineLabel;
                case SightingState.Failed: return FailedLabel;
                default: return QueuedLabel;
            }
        }

        // ---- Row caption ----------------------------------------------------

        public const string TodayPrefix = "Hoje";
        public const string YesterdayPrefix = "Ontem";

        /// <summary>
        /// "Hoje, 9:32" / "Ontem, 18:04" / "27/09, 7:12" — both arguments are
        /// already in local time (the ViewModel owns the conversion, so tests stay
        /// timezone-proof). Hours are not zero-padded, matching the V2 frame.
        /// </summary>
        public static string DayTime(DateTime localWhen, DateTime localNow)
        {
            string time = localWhen.ToString("H:mm", CultureInfo.InvariantCulture);
            int days = (localNow.Date - localWhen.Date).Days;
            if (days == 0) return TodayPrefix + ", " + time;
            if (days == 1) return YesterdayPrefix + ", " + time;
            return localWhen.ToString("dd/MM", CultureInfo.InvariantCulture) + ", " + time;
        }

        /// <summary>
        /// "Hoje, 9:32 · Baía do Sueste", or just the time when no beach was
        /// resolved — which is a real state (off every polygon, no GPS fix), so
        /// the row drops the half rather than showing an empty tail.
        /// </summary>
        public static string RowCaption(string dayTime, string beachLabel)
        {
            if (string.IsNullOrWhiteSpace(beachLabel)) return dayTime;
            if (string.IsNullOrWhiteSpace(dayTime)) return beachLabel;
            return dayTime + Separator + beachLabel;
        }

        // ---- Photo size -----------------------------------------------------

        /// <summary>"24 MB" — whole megabytes, the unit the design's limit is written in.</summary>
        public static string Megabytes(long bytes)
        {
            double mb = bytes / (1024d * 1024d);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} MB", mb);
        }

        /// <summary>"A foto tem 24 MB. O limite é 20 MB." — says both numbers, so
        /// the user knows how much smaller it has to be.</summary>
        public static string PhotoTooLarge(long sizeBytes, long limitBytes) =>
            $"A foto tem {Megabytes(sizeBytes)}. O limite é {Megabytes(limitBytes)}.";
    }
}
