using System;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR copy for the Espécie screen's media blocks — the photo/model credits
    /// and the inline video player.
    ///
    /// <para>Same contract as <see cref="SpeciesCardFormatter"/>,
    /// <see cref="BeachContentFormatter"/> and <see cref="ConditionsFormatter"/>:
    /// plain static C#, no UnityEngine, no state, and <b>the screen never builds a
    /// user-visible string itself</b>. It is a sibling of
    /// <see cref="SpeciesCardFormatter"/> rather than more members on it because
    /// that one is documented as the AR card's copy and the Espécie screen reuses
    /// its three spec rows verbatim — the identity block is shared, the media block
    /// is this screen's alone.</para>
    ///
    /// <para><b>Which "missing" convention applies.</b> Everything here returns
    /// <b>null to hide the element</b>; nothing returns
    /// <see cref="ConditionsFormatter.NoValue"/>, because none of these are fixed
    /// slots sitting next to a sibling that would be stranded — a credit line with
    /// no credit, or a clock with no duration, is simply not drawn.</para>
    /// </summary>
    public static class SpeciesMediaFormatter
    {
        // ---- Video player copy ----------------------------------------------

        /// <summary>The idle call to action — what the poster overlay says before the first tap.</summary>
        public const string WatchLabel = "Assistir";

        /// <summary>Buffering. The only state that can last, so it gets its own words.</summary>
        public const string LoadingLabel = "Carregando…";

        public const string PauseLabel = "Pausar";

        /// <summary>Resuming a clip the user paused — deliberately not "Assistir" again.</summary>
        public const string ResumeLabel = "Continuar";

        public const string RetryLabel = "Tentar de novo";

        /// <summary>
        /// The one line a failed stream shows, in place of Unity's technical English.
        /// Worded as a network problem because that is what it nearly always is (the
        /// clips are public HTTPS objects); the real reason goes to the log.
        /// </summary>
        public const string ErrorText = "Não foi possível carregar o vídeo. Verifique sua conexão.";

        /// <summary>
        /// The label on a card's action control for a given playback state. The
        /// state passed in must be <see cref="VideoPlaybackState.Idle"/> for every
        /// card except the one that currently owns the shared player — that mapping
        /// is the ViewModel's job, not this method's.
        /// </summary>
        public static string ActionLabel(VideoPlaybackState state)
        {
            switch (state)
            {
                case VideoPlaybackState.Loading: return LoadingLabel;
                case VideoPlaybackState.Playing: return PauseLabel;
                case VideoPlaybackState.Paused: return ResumeLabel;
                case VideoPlaybackState.Failed: return RetryLabel;
                default: return WatchLabel;
            }
        }

        /// <summary>
        /// Material Symbols name for the same control. Paired with
        /// <see cref="ActionLabel"/> here so the glyph and the word can never
        /// disagree, which is what happens when a screen switches on the state twice.
        /// </summary>
        public static string ActionIcon(VideoPlaybackState state)
        {
            switch (state)
            {
                case VideoPlaybackState.Playing: return "pause";
                case VideoPlaybackState.Failed: return "refresh";
                default: return "play_arrow";
            }
        }

        /// <summary>
        /// "0:42 / 2:15", or <b>null</b> while the duration is unknown — which it is
        /// until the stream finishes preparing, and forever on a failed one. A
        /// "0:00 / 0:00" would look like a clip of no length rather than a clip not
        /// loaded yet.
        /// </summary>
        public static string PlaybackTime(double positionSeconds, double durationSeconds)
        {
            if (!(durationSeconds > 0d) || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
                return null;
            double clamped = positionSeconds;
            if (double.IsNaN(clamped) || clamped < 0d) clamped = 0d;
            if (clamped > durationSeconds) clamped = durationSeconds;
            return Clock(clamped) + " / " + Clock(durationSeconds);
        }

        /// <summary>
        /// A playhead as "m:ss", or "h:mm:ss" past the hour. Invariant culture: the
        /// separators are colons in every locale and the digits must not pick up a
        /// thousands group.
        /// </summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) seconds = 0d;
            int total = (int)Math.Floor(seconds);
            int s = total % 60;
            int m = total / 60 % 60;
            int h = total / 3600;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, s);
        }

        /// <summary>
        /// Fraction of the clip played, 0–1, for the progress track. 0 while the
        /// duration is unknown, so an unprepared clip draws an empty track rather
        /// than a full one.
        /// </summary>
        public static float Progress(double positionSeconds, double durationSeconds)
        {
            if (!(durationSeconds > 0d) || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
                return 0f;
            if (double.IsNaN(positionSeconds) || positionSeconds <= 0d) return 0f;
            if (positionSeconds >= durationSeconds) return 1f;
            return (float)(positionSeconds / durationSeconds);
        }

        // ---- Credits ---------------------------------------------------------

        /// <summary>
        /// A credit line, trimmed, or <b>null</b> when there is nothing to credit.
        ///
        /// <para>Rendered <b>verbatim</b> — no "Foto:" or "Modelo:" prefix is added,
        /// because the authored values are inconsistent about carrying their own
        /// (hammerhead's photo credit starts "Foto: …", tiger_shark's does not, and
        /// the model credits are raw Sketchfab/Meshy licence paragraphs). Adding a
        /// prefix would print "Foto: Foto: …" for some species, and rewriting the
        /// data to suit the screen is content work, not a formatting decision. The
        /// section heading is what says these are credits.</para>
        /// </summary>
        public static string Credit(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        /// <summary>
        /// A species description, trimmed, or null to hide the card. Verbatim
        /// otherwise: these are Wikipedia extracts written for readers, and nothing
        /// here paraphrases them.
        /// </summary>
        public static string Description(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        /// <summary>
        /// A clip's caption, trimmed, or null when it was authored without one — the
        /// card then shows the player alone rather than an empty line of text.
        /// </summary>
        public static string VideoTitle(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
