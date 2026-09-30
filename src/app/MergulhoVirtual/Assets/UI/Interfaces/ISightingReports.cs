using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Where one of the user's own sighting reports sits on its way to the
    /// backend. Mirrors <c>JobRecordState</c> in Assembly-CSharp without
    /// referencing it — the UI layer may not see the job queue.
    ///
    /// <para>There is no <c>Sent</c> member, and that is not an oversight: the
    /// queue keeps no success records (CLAUDE.md, "Background job system"), so a
    /// delivered sighting simply stops appearing. Every value here is a state a
    /// report is genuinely in.</para>
    /// </summary>
    public enum SightingState
    {
        /// <summary>Queued, not attempted yet.</summary>
        Queued,

        /// <summary>Attempted and failed transiently; waiting on backoff.</summary>
        Retrying,

        /// <summary>Waiting for the device to come back online.</summary>
        WaitingForNetwork,

        /// <summary>Permanently rejected. It will not be retried.</summary>
        Failed,
    }

    /// <summary>
    /// Everything the Reportar form collected, handed to the submission service
    /// as one value. Plain data: the service owns the photo file, the idempotency
    /// key and the queueing — the ViewModel never sees any of them.
    ///
    /// <para><b>Keys, not labels.</b> <see cref="SpeciesKey"/>,
    /// <see cref="SizeBucket"/>, <see cref="BehaviourKeys"/> and
    /// <see cref="ProfileKey"/> are machine values that travel to the backend;
    /// <see cref="SpeciesLabel"/> is the pt-BR name the backend already stores as
    /// <c>nome_popular</c>, carried separately so today's contract keeps
    /// working.</para>
    /// </summary>
    public sealed class SightingDraft
    {
        /// <summary>Path the photo picker returned. Required — the service copies
        /// it somewhere it owns before queueing.</summary>
        public string PhotoPath;

        /// <summary>places.json key of the beach, or null when GPS resolved none.
        /// Null is a supported value: the backend stores an empty <c>local</c>.</summary>
        public string BeachKey;

        /// <summary>When the sighting happened. The V2 form has no date field, so
        /// this is the submit time.</summary>
        public DateTime WhenUtc;

        public string SpeciesKey;

        /// <summary>pt-BR species label — what the backend stores today.</summary>
        public string SpeciesLabel;

        public string SizeBucket;

        /// <summary>Selected behaviour keys, in option order. Never null.</summary>
        public IReadOnlyList<string> BehaviourKeys = Array.Empty<string>();

        public string ReporterName;

        /// <summary>
        /// Optional e-mail. <b>Personal data.</b> It is sent with the report and
        /// nothing else in the app stores it — no prefill, no preferences entry.
        /// A retention decision and a privacy notice are owed before the backend
        /// persists it.
        /// </summary>
        public string ReporterEmail;

        /// <summary><c>turista</c> / <c>condutor</c>, or null when unanswered.</summary>
        public string ProfileKey;

        /// <summary>Free-text notes. The V2 form has no notes field; kept so the
        /// AR screen (Slice 4) or a later form revision can fill it.</summary>
        public string Notes;
    }

    /// <summary>
    /// One row of "Seus avistamentos pendentes" — a report the app has taken
    /// responsibility for but has not delivered.
    /// </summary>
    public sealed class SightingRecord
    {
        /// <summary>Stable id (the idempotency key). Distinguishes rows; never shown.</summary>
        public string Id;

        public string SpeciesKey;

        /// <summary>pt-BR species label as submitted, or null when none was chosen.</summary>
        public string SpeciesLabel;

        /// <summary>places.json key of the beach, or null.</summary>
        public string BeachKey;

        /// <summary>The sighting's own timestamp (falls back to the enqueue time).</summary>
        public DateTime WhenUtc;

        /// <summary>Local path of the queued photo, for a thumbnail. May be null.</summary>
        public string PhotoPath;

        public SightingState State;

        /// <summary>Delivery attempts so far — 0 for a report that has not been tried.</summary>
        public int AttemptCount;
    }

    /// <summary>
    /// The app's own sighting reports: submit one, and read back the ones still
    /// in flight. Implemented by <c>UiServiceAdapters.SightingReportsAdapter</c>
    /// over the Assembly-CSharp <c>JobQueue</c>, which the UI assembly cannot
    /// reference.
    ///
    /// <para><b>Fire-and-forget by contract.</b> <see cref="Submit"/> returns as
    /// soon as the report is durable on disk; it never waits on the network and
    /// never reports a backend result. A <c>false</c> return means the report was
    /// not taken at all (the photo could not be copied, the queue is full) — the
    /// caller says so and moves on rather than blocking the user on
    /// infrastructure.</para>
    /// </summary>
    public interface ISightingReports
    {
        /// <summary>
        /// Takes ownership of the draft and queues it. True when the report is
        /// safely persisted and will be uploaded (now, or whenever the device is
        /// next online, across app restarts).
        /// </summary>
        bool Submit(SightingDraft draft);

        /// <summary>
        /// Reports still waiting to be delivered — queued, backing off or blocked
        /// on connectivity. Order is the queue's (oldest first); callers sort for
        /// display. Never null.
        /// </summary>
        IReadOnlyList<SightingRecord> ListPending();

        /// <summary>
        /// Reports the backend rejected outright. Kept as their own list rather
        /// than dropped, so the feed can show a submission that will never go
        /// through instead of losing it silently. Never null.
        /// </summary>
        IReadOnlyList<SightingRecord> ListFailed();

        /// <summary>
        /// Puts a permanently-failed report back in the queue, by the
        /// <see cref="SightingRecord.Id"/> <see cref="ListFailed"/> reported. True
        /// when it was taken back; false when there is no failed report with that
        /// id (it already went through, or another retry beat this one).
        ///
        /// <para><b>This is the only way out of <see cref="SightingState.Failed"/>.</b>
        /// A permanent failure is otherwise terminal: the report sits on disk
        /// forever with nothing the user can do about it. Retrying keeps the
        /// report's own id, which is the idempotency key, so a retry of something
        /// the backend actually stored collapses server-side instead of
        /// duplicating.</para>
        /// </summary>
        bool Retry(string id);

        /// <summary>Raised when a report finished (delivered or failed), i.e. when
        /// the two lists may have changed.</summary>
        event Action Changed;
    }
}
