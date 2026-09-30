using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public enum JobResult
{
    Success,
    TransientFailure,
    PermanentFailure,
}

public abstract class Job
{
    public string Id { get; internal set; }
    public int AttemptCount { get; internal set; }
    public DateTime NextAttemptAtUtc { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public string LastError { get; internal set; }

    /// <summary>
    /// Whether the last failure was the network failing rather than the server
    /// answering. Set by the job itself (a <see cref="UnityWebRequest.Result.ConnectionError"/>
    /// or a <see cref="Send"/> watchdog abort) immediately before it returns
    /// <see cref="JobResult.TransientFailure"/>, and cleared for every other
    /// transient — a 5xx, a 408/429, or an exception thrown out of Execute.
    ///
    /// <para><b>Why the queue needs to know.</b> The two failures deserve very
    /// different patience. A server that is 500ing has earned an escalating
    /// backoff, all the way to the hour-long rung. A dead uplink has not: it is
    /// not the server's fault, it usually clears in seconds, and burning the
    /// escalating schedule on it parks the job an hour out over something the user
    /// fixed by walking back into Wi-Fi. <see cref="JobQueue"/> therefore runs a
    /// separate, gentler schedule for these, and is allowed to pull a job of this
    /// kind forward the moment connectivity returns or the app is relaunched.
    /// Neither is safe to do for a server-side failure.</para>
    /// </summary>
    public bool LastFailureWasNetwork { get; internal set; }

    /// <summary>
    /// How many server-side transient failures this job has taken, i.e. the index
    /// into the queue's escalating backoff schedule. Kept separate from
    /// <see cref="AttemptCount"/> because that one also counts network failures,
    /// which must not push a job up the escalating rungs.
    /// </summary>
    public int BackoffStep { get; internal set; }

    /// <summary>
    /// How many network failures this job has taken since its last server-side
    /// failure, i.e. the index into the queue's gentler network schedule. Reset
    /// whenever a server actually answers, and whenever the queue pulls the job
    /// forward on connectivity returning.
    /// </summary>
    public int NetworkFailureStep { get; internal set; }

    public abstract string Type { get; }

    public virtual bool RequiresNetwork => true;

    public abstract IEnumerator Execute(Action<JobResult> setResult);

    protected internal abstract string SerializeData();
    protected internal abstract void DeserializeData(string data);

    /// <summary>
    /// What <see cref="Send"/> decided about a request, read by the caller before
    /// it looks at <see cref="UnityWebRequest.result"/> — an aborted request reports
    /// itself as an ordinary connection error, which would otherwise be
    /// indistinguishable from the radio genuinely dropping.
    /// </summary>
    protected sealed class RequestOutcome
    {
        /// <summary>True when the watchdog gave up and called <c>Abort()</c>.</summary>
        public bool Aborted;

        /// <summary>Human-readable reason, suitable for <see cref="Job.LastError"/>.</summary>
        public string Reason;
    }

    /// <summary>
    /// Sends <paramref name="req"/> and aborts it if it stops making progress, or
    /// exceeds a hard cap. Every job type must go through this instead of
    /// <c>yield return req.SendWebRequest()</c>.
    ///
    /// <para><b>The failure this prevents.</b> A bare <c>SendWebRequest()</c> with
    /// no timeout never finishes on a half-open TCP socket — routine on mobile data
    /// when the radio hands off or the carrier NAT drops the flow: the peer never
    /// sends a FIN, so the request neither succeeds nor errors. The queue runs one
    /// job at a time, so that single request leaves <c>running = true</c> forever
    /// and silently stops every other queued job for the life of the process. No
    /// log line, no failed/ file, nothing on screen: the sighting simply never
    /// goes.</para>
    ///
    /// <para><b>Why a stall watchdog and not a flat deadline.</b> A multi-megabyte
    /// original photo over a slow link legitimately takes minutes, and killing it
    /// while it is making progress would turn a slow upload into an upload that
    /// never completes. So the clock that matters is time <em>without</em> bytes
    /// moving, measured on the byte counters rather than the coarse float
    /// <c>uploadProgress</c> (which quantises and can sit still across a genuinely
    /// progressing chunk). <paramref name="hardCapSeconds"/> is the backstop for the
    /// pathological case where bytes trickle forever.</para>
    /// </summary>
    /// <param name="outcome">Filled in by this method; must be non-null.</param>
    /// <param name="stallSeconds">Seconds with no byte moving at all before the
    /// request is abandoned. 45s is well past any plausible server think-time for
    /// these endpoints and well short of a user's patience.</param>
    /// <param name="hardCapSeconds">Total wall-clock ceiling. 300s suits a small
    /// JSON POST; a photo upload passes a larger value.</param>
    protected static IEnumerator Send(UnityWebRequest req, RequestOutcome outcome,
                                      float stallSeconds = 45f, float hardCapSeconds = 300f)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (outcome == null) throw new ArgumentNullException(nameof(outcome));

        outcome.Aborted = false;
        outcome.Reason = null;

        // A second line of defence, not the primary one: UnityWebRequest's own
        // timeout is enforced by the native transport, so it still fires if this
        // coroutine is starved (a long GC pause, a stalled frame, a scene that
        // stops pumping). It is set to the hard cap rather than the stall window
        // because it cannot tell "slow but progressing" from "wedged".
        req.timeout = (int)hardCapSeconds;

        float startedAt = Time.realtimeSinceStartup;
        float lastProgressAt = startedAt;
        ulong lastBytes = 0;

        req.SendWebRequest();

        while (!req.isDone)
        {
            yield return null;

            // realtimeSinceStartup, never Time.time: Time.time is scaled by
            // Time.timeScale, so a paused or slowed game would stretch or freeze
            // the watchdog exactly when a wedged request most needs killing.
            float now = Time.realtimeSinceStartup;
            ulong bytes = req.uploadedBytes + req.downloadedBytes;
            if (bytes > lastBytes)
            {
                lastBytes = bytes;
                lastProgressAt = now;
            }

            if (now - lastProgressAt >= stallSeconds)
            {
                outcome.Aborted = true;
                outcome.Reason = $"stalled {now - lastProgressAt:F0}s with no bytes transferred ({bytes} byte(s) moved)";
            }
            else if (now - startedAt >= hardCapSeconds)
            {
                outcome.Aborted = true;
                outcome.Reason = $"exceeded the {hardCapSeconds:F0}s hard cap ({bytes} byte(s) moved)";
            }

            if (outcome.Aborted)
            {
                req.Abort();
                break;
            }
        }
    }
}

/// <summary>
/// The persisted wrapper around one job: the queue's own bookkeeping plus the
/// job's type-specific payload as a nested JSON string (JsonUtility cannot
/// deserialize polymorphically, hence the discriminator + nested string).
///
/// <para><b>Fields are added here, never renamed or removed.</b> JsonUtility
/// leaves a key it does not find at the field's default, so a job file written by
/// an older build still loads into a newer envelope — every field added since the
/// first release (<c>lastFailureWasNetwork</c>, <c>backoffStep</c>,
/// <c>networkFailureStep</c>) reads back as false/0 from such a file, which is
/// exactly the "never failed yet" state. Rename one and every job already sitting
/// on a user's disk silently loses that value on reload instead.</para>
/// </summary>
[Serializable]
internal struct JobEnvelope
{
    public string type;
    public string id;
    public int attemptCount;
    public long nextAttemptAtUtcTicks;
    public long createdAtUtcTicks;
    public string lastError;
    public string data;

    // ---- Added for the network-vs-server backoff split -----------------------
    public bool lastFailureWasNetwork;
    public int backoffStep;
    public int networkFailureStep;
}
