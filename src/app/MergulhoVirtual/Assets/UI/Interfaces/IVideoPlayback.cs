using System;
using UnityEngine;

namespace MergulhoVirtual.UI
{
    /// <summary>What the one shared player is doing. Exactly one of these at a time.</summary>
    public enum VideoPlaybackState
    {
        /// <summary>Nothing bound. No stream, no decoder, no texture.</summary>
        Idle,

        /// <summary>A URL is bound and buffering. This is the only state that can
        /// last a long time on a bad connection, so it is the one the screen has to
        /// show something for.</summary>
        Loading,

        Playing,

        Paused,

        /// <summary>The stream failed; <see cref="IVideoPlayback.ErrorMessage"/> says how.</summary>
        Failed,
    }

    /// <summary>
    /// Inline video playback for the Espécie screen's educational clips
    /// (<see cref="SpeciesInfo.Videos"/>) — the UI Toolkit counterpart of the uGUI
    /// <c>VideoPlayerController</c> the About screen's Instagram card still uses.
    ///
    /// <para><b>Why a seam at all.</b> <c>UnityEngine.Video.VideoPlayer</c> is a
    /// <c>MonoBehaviour</c>, so it cannot live in <c>MergulhoVirtual.UI</c>; the
    /// adapter owns the component and the screen only ever sees a state, a couple of
    /// doubles and a <see cref="UnityEngine.Texture"/>. Same shape as
    /// <see cref="ISpeciesModelViewer"/>, and for the same reason.</para>
    ///
    /// <para><b>ONE player, shared by every card on the screen.</b> Two videos
    /// playing at once is never what anyone wants, and a decoder per card would
    /// hold a stream open per card. So <see cref="Url"/> is which card currently
    /// owns the player: a card is "the playing one" when its URL matches, and
    /// <see cref="Play"/> on a different URL implicitly stops the first. The screen
    /// needs no cross-card bookkeeping of its own.</para>
    ///
    /// <para><b>The texture is a decoder output and is NOT stable</b> — it appears
    /// when the stream finishes preparing and goes away on <see cref="Stop"/>, so
    /// re-read it on <see cref="Changed"/> rather than caching it. Same contract as
    /// the model viewer's render target.</para>
    ///
    /// <para><b>A failure here is ordinary.</b> The clips stream from a public GCS
    /// bucket over HTTPS, so no signal means no video; and on a <b>Linux editor
    /// playback always fails</b> (Unity's Linux VideoPlayer cannot decode H.264 —
    /// CLAUDE.md says so twice), which makes this the one part of the screen that
    /// cannot be verified without an Android build. The screen must therefore treat
    /// <see cref="VideoPlaybackState.Failed"/> as a normal state with a normal
    /// inline message, never as something that hides or breaks the card.</para>
    /// </summary>
    public interface IVideoPlayback
    {
        /// <summary>
        /// A player exists. False in the screenshot harness and in any host that did
        /// not build one — the screen then hides the whole video section rather than
        /// offering a play button that cannot play.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>The URL currently bound, or null when <see cref="VideoPlaybackState.Idle"/>.</summary>
        string Url { get; }

        VideoPlaybackState State { get; }

        /// <summary>The decoded frame, or null before the stream is prepared. See the class remarks.</summary>
        Texture Texture { get; }

        /// <summary>Playhead in seconds. 0 when nothing is bound.</summary>
        double PositionSeconds { get; }

        /// <summary>
        /// Clip length in seconds, known only once prepared. 0 means "not known
        /// yet", which is why the screen must not divide by it unguarded.
        /// </summary>
        double DurationSeconds { get; }

        // There is deliberately no ErrorMessage here. Unity's video errors are
        // technical English ("Can't play movie […] Cannot read file"), which is not
        // something to print at a user in a pt-BR app, and the screen's copy for a
        // failure is one fixed line from SpeciesMediaFormatter either way. The raw
        // reason is logged by the adapter, where logcat can see it.

        /// <summary>
        /// State, texture, position or duration changed. Fired on every frame of
        /// playback is NOT the contract — the screen polls the position on its own
        /// schedule; this is for the transitions.
        /// </summary>
        event Action Changed;

        /// <summary>
        /// Binds and plays a URL. The same URL while paused resumes in place; a
        /// different one replaces what is playing. A null or empty URL is ignored.
        /// </summary>
        void Play(string url);

        /// <summary>Pauses, keeping the playhead and the texture. No-op unless playing.</summary>
        void Pause();

        /// <summary>
        /// Unbinds: stops the stream, releases the texture and returns to
        /// <see cref="VideoPlaybackState.Idle"/>. Idempotent, and what the screen
        /// must call on exit — a paused player still holds an open stream.
        /// </summary>
        void Stop();

        /// <summary>
        /// Moves the playhead, in seconds, clamped to the clip. Ignored before the
        /// stream is prepared (there is nothing to seek within yet).
        /// </summary>
        void Seek(double seconds);
    }
}
