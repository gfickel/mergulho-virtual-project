using System;
using System.Collections;

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

    public abstract string Type { get; }

    public virtual bool RequiresNetwork => true;

    public abstract IEnumerator Execute(Action<JobResult> setResult);

    protected internal abstract string SerializeData();
    protected internal abstract void DeserializeData(string data);
}

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
}
