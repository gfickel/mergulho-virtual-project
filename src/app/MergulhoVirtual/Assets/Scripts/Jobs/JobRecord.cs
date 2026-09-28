using System;

/// <summary>
/// Where a job sits in the queue's storage, as a UI can usefully describe it.
///
/// <para>Every value here is a state a job is actually <b>in</b>. There is no
/// "unknown" member on purpose: a <see cref="JobRecord"/> only ever exists for a
/// job the queue has on disk, so a record's state is never a guess.</para>
/// </summary>
public enum JobRecordState
{
    /// <summary>Enqueued, never attempted — waiting its turn.</summary>
    Queued,

    /// <summary>Attempted at least once, failed transiently, waiting on backoff.</summary>
    Retrying,

    /// <summary>Needs the network (<see cref="Job.RequiresNetwork"/>) and the device is offline.</summary>
    WaitingForNetwork,

    /// <summary>Permanently failed; the envelope was moved to <c>jobs/failed/</c>.</summary>
    Failed,
}

/// <summary>
/// A read-only snapshot of one job in the queue's storage: the envelope fields
/// (<see cref="Id"/>, attempts, timestamps, last error) plus the job's own
/// serialized payload, which <see cref="Snapshot{T}"/> hands back as a detached
/// instance.
///
/// <para><b>Why a record rather than the live <see cref="Job"/>.</b> Handing out
/// the queue's own instance would let a caller mutate a job the run loop is about
/// to execute — change its Url, clear its ImagePath — with no way for the queue
/// to notice. A record is a copy taken at read time: <see cref="Snapshot{T}"/>
/// deserializes a fresh instance from the payload, so writing to it changes
/// nothing that will be uploaded.</para>
///
/// <para><b>This is the unambiguous half of the status API.</b>
/// <see cref="JobQueue.GetStatus"/> answers <c>NotFound</c> both for a job that
/// already succeeded (no success records are kept) and for an id that was never
/// enqueued — a caller cannot tell those apart. The enumerations
/// (<see cref="JobQueue.ListPending"/>, <see cref="JobQueue.ListFailed"/>) do not
/// reproduce that: a record exists only for a job that really is in that state,
/// and a job in neither list is simply not in the queue's storage any more.
/// Callers that additionally need durable "did this ever succeed?" semantics must
/// still persist their own flag (see CLAUDE.md, "No success records").</para>
/// </summary>
public sealed class JobRecord
{
    public string Id { get; }
    public string Type { get; }
    public int AttemptCount { get; }
    public DateTime CreatedAtUtc { get; }

    /// <summary>When the queue will next try this job. Meaningless for <see cref="JobRecordState.Failed"/>.</summary>
    public DateTime NextAttemptAtUtc { get; }

    /// <summary>The last failure's message, or null if it has never failed.</summary>
    public string LastError { get; }

    public JobRecordState State { get; }

    readonly string payload;

    internal JobRecord(Job job, JobRecordState state)
    {
        Id = job.Id;
        Type = job.Type;
        AttemptCount = job.AttemptCount;
        CreatedAtUtc = job.CreatedAtUtc;
        NextAttemptAtUtc = job.NextAttemptAtUtc;
        LastError = job.LastError;
        State = state;
        payload = job.SerializeData();
    }

    /// <summary>
    /// A detached copy of the job, for callers that know its concrete type —
    /// the only way to read type-specific fields (a sighting's species, beach and
    /// photo path) off a record.
    ///
    /// <para>Returns null when <typeparamref name="T"/> is not this record's type,
    /// so a caller filtering by <see cref="Type"/> and one that forgot to both end
    /// up with "no data" rather than a half-populated job.</para>
    /// </summary>
    public T Snapshot<T>() where T : Job, new()
    {
        var job = new T();
        if (job.Type != Type) return null;
        job.DeserializeData(payload);
        job.Id = Id;
        job.AttemptCount = AttemptCount;
        job.CreatedAtUtc = CreatedAtUtc;
        job.NextAttemptAtUtc = NextAttemptAtUtc;
        job.LastError = LastError;
        return job;
    }
}
