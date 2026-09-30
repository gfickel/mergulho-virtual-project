using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum JobStatus
{
    NotFound,
    Pending,
    Failed,
}

public class JobQueue : MonoBehaviour
{
    public static JobQueue Instance { get; internal set; }

    /// <summary>
    /// Returns the singleton, lazily creating a JobServices GameObject if one
    /// isn't already in the scene. Safe to call from any script that needs to
    /// Enqueue without depending on someone having wired the queue in the Editor.
    /// Enqueue() itself calls EnsureInitialized internally, so it works the
    /// same frame; the RunLoop coroutine starts on the next frame via Start
    /// and will pick up the just-enqueued job from disk on its first tick.
    /// </summary>
    public static JobQueue GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("JobServices");
        Instance = go.AddComponent<JobQueue>(); // Awake → Instance = this
        return Instance;
    }

    [Tooltip("Max number of pending jobs the queue will hold. Enqueue returns false when full.")]
    public int maxQueueSize = 100;

    [Tooltip("How often (seconds) the loop wakes to look for due jobs.")]
    public float tickIntervalSeconds = 2f;

    public event Action<string, JobResult> JobCompleted;

    internal static string TestRootOverride;
    internal Func<bool> IsOnlineOverride;

    /// <summary>
    /// The escalating schedule for failures the <b>server</b> caused (5xx, 408,
    /// 429, or an exception out of Execute). Long rungs are correct here: a backend
    /// that is erroring is not fixed by being hit harder, and the last rung being
    /// an hour is what keeps a broken deploy from draining the battery.
    /// </summary>
    private static readonly TimeSpan[] BackoffSchedule = new[]
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
    };

    /// <summary>
    /// The gentler schedule for failures the <b>network</b> caused (a connection
    /// error, or the stall watchdog aborting a wedged request).
    ///
    /// <para>A dead uplink is not the server's fault and must not cost an hour:
    /// <see cref="IsOnline"/> reports "online" for any associated Wi-Fi or cell
    /// interface, so a captive portal, a dropped DNS resolver or an unreachable
    /// backend all look online, the job runs, and it collects a connection error.
    /// Escalating that to the hour-long rung parks the sighting over a condition
    /// the user typically fixes in seconds. Capping at two minutes instead is an
    /// acceptable battery floor: one aborted request every 120s on a broken
    /// network costs nothing measurable, and the queue also pulls these jobs
    /// forward on the two signals that mean "try now" — connectivity returning
    /// (<see cref="OnConnectivityRegained"/>) and the app being relaunched.</para>
    /// </summary>
    private static readonly TimeSpan[] NetworkBackoffSchedule = new[]
    {
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
    };

    /// <summary>How many missed ticks the <see cref="Update"/> watchdog tolerates
    /// before it concludes the loop is dead and restarts it. Five is generous
    /// enough that an ordinary frame-rate hiccup or a domain reload cannot trip it,
    /// and short enough (10s at the default 2s tick) that a real death is repaired
    /// long before the user notices.</summary>
    private const float LoopWatchdogTickMultiplier = 5f;

    /// <summary>On launch, a network-caused delay further out than this is clamped.
    /// 30s is past the point where the delay can be explained by the job simply
    /// having been written moments ago, so anything beyond it is a real backoff
    /// rung.</summary>
    private const double LaunchPullForwardThresholdSeconds = 30;

    /// <summary>What a clamped launch delay becomes. Not zero: the first frames
    /// after launch are the busiest in the app (AR session, camera permission, GPS
    /// fix, the ONNX worker's GPU allocations) and a multipart upload starting into
    /// that is a worse experience than one starting five seconds later.</summary>
    private const double LaunchPullForwardDelaySeconds = 5;

    private readonly Dictionary<string, Func<Job>> typeFactories = new Dictionary<string, Func<Job>>();
    private readonly List<Job> pending = new List<Job>();
    private string pendingDir;
    private string failedDir;
    private bool running;
    private bool initialized;

    /// <summary>Generation token for <see cref="RunLoop"/>. Restarting the loop
    /// bumps it, so a previous loop that turns out to still be alive exits at its
    /// next resume instead of running alongside the new one.</summary>
    private int loopGeneration;

    /// <summary>Set once <see cref="Start"/> has launched the loop, so the
    /// <see cref="Update"/> watchdog stays inert in EditMode tests and before
    /// the loop has ever run.</summary>
    private bool loopStarted;

    /// <summary><see cref="Time.realtimeSinceStartup"/> at the last loop tick —
    /// the liveness signal the watchdog reads.</summary>
    private float lastTickRealtime;

    /// <summary>Previous <see cref="IsOnline"/> reading, so the tick can spot the
    /// false→true edge. Null until the first poll, which therefore never counts as
    /// a transition.</summary>
    private bool? wasOnline;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return;
        }
        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        EnsureInitialized();
        LoadPendingFromDisk();
        lastTickRealtime = Time.realtimeSinceStartup;
        loopStarted = true;
        StartCoroutine(RunLoop());
    }

    /// <summary>
    /// Liveness watchdog for <see cref="RunLoop"/>.
    ///
    /// <para><b>The failure this repairs.</b> <c>StartCoroutine(RunLoop())</c> is
    /// called from exactly one place and nothing else ever restarts it, so a single
    /// unhandled exception escaping a tick ends every retry for the rest of the
    /// process — with <c>running</c> left stuck true, which is indistinguishable
    /// from "an upload is in progress". The guards inside the loop are meant to make
    /// that impossible; this is the admission that "meant to" is not "proved to".</para>
    ///
    /// <para>Only checked while <c>!running</c>: a legitimately long-running job
    /// (a multi-minute photo upload on a slow link) holds the loop between ticks by
    /// design and must never be mistaken for a dead one.</para>
    /// </summary>
    void Update()
    {
        if (!loopStarted) return;
        if (running) return;

        float now = Time.realtimeSinceStartup;
        float silence = now - lastTickRealtime;
        if (silence <= tickIntervalSeconds * LoopWatchdogTickMultiplier) return;

        // Bump the stamp before restarting, so a loop that is somehow alive but
        // wedged does not make this fire again on every single frame.
        lastTickRealtime = now;
        Debug.LogError($"JobQueue: the retry loop has not ticked for {silence:F1}s (expected every {tickIntervalSeconds}s) — it died mid-tick and every queued job was stalled. Restarting it.");
        StartCoroutine(RunLoop());
    }

    /// <summary>
    /// Re-stamps the loop's liveness clock when the app comes back.
    ///
    /// <para>Time spent backgrounded is not evidence that anything died.
    /// <see cref="Time.realtimeSinceStartup"/> is wall clock and keeps advancing
    /// while the Android player is paused, and <see cref="Update"/> runs before
    /// suspended coroutines resume on the first frame back — so without this, every
    /// single return from the background would log a false "the retry loop died"
    /// error and pointlessly restart a perfectly healthy loop.</para>
    /// </summary>
    void OnApplicationPause(bool paused)
    {
        if (!paused) lastTickRealtime = Time.realtimeSinceStartup;
    }

    /// <summary>Same reasoning as <see cref="OnApplicationPause"/>, for the editor
    /// and for players that pause on focus loss rather than on pause.</summary>
    void OnApplicationFocus(bool focused)
    {
        if (focused) lastTickRealtime = Time.realtimeSinceStartup;
    }

    internal void EnsureInitialized()
    {
        if (initialized) return;

        if (Instance == null) Instance = this;

        if (!typeFactories.ContainsKey("HttpPost")) RegisterType<HttpPostJob>();
        if (!typeFactories.ContainsKey("FileDownload")) RegisterType<FileDownloadJob>();
        if (!typeFactories.ContainsKey("ReportSighting")) RegisterType<ReportSightingJob>();

        // Create the directories BEFORE latching `initialized`. Latching first
        // cached a broken init as a successful one: every later Directory.GetFiles
        // then threw DirectoryNotFoundException out of whatever called it —
        // ListPending from a UI rebuild, ListFailed from the feed — and nothing
        // could ever retry the creation.
        string root = TestRootOverride ?? Application.persistentDataPath;
        string pendingPath = Path.Combine(root, "jobs");
        string failedPath = Path.Combine(pendingPath, "failed");
        try
        {
            Directory.CreateDirectory(pendingPath);
            Directory.CreateDirectory(failedPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: could not create the job storage directories under '{root}': {e.Message}. The queue stays uninitialized; the next call will try again.");
            return;
        }

        pendingDir = pendingPath;
        failedDir = failedPath;
        initialized = true;
    }

    internal void LoadPendingFromDisk()
    {
        EnsureInitialized();
        if (!initialized) return;
        LoadPending();
    }

    public void RegisterType<T>() where T : Job, new()
    {
        var instance = new T();
        typeFactories[instance.Type] = () => new T();
    }

    public bool Enqueue(Job job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        EnsureInitialized();
        if (!initialized)
        {
            Debug.LogError($"JobQueue: storage is not initialized, rejecting job of type '{job.Type}'.");
            return false;
        }
        if (pending.Count >= maxQueueSize)
        {
            Debug.LogWarning($"JobQueue: max queue size ({maxQueueSize}) reached, rejecting job of type '{job.Type}'.");
            return false;
        }
        if (!typeFactories.ContainsKey(job.Type))
        {
            Debug.LogError($"JobQueue: unregistered job type '{job.Type}'. Call RegisterType<{job.GetType().Name}>() before enqueuing.");
            return false;
        }

        if (string.IsNullOrEmpty(job.Id)) job.Id = Guid.NewGuid().ToString("N");
        job.CreatedAtUtc = DateTime.UtcNow;
        job.NextAttemptAtUtc = DateTime.UtcNow;
        job.AttemptCount = 0;
        // A brand-new job has no failure history: both backoff cursors start at the
        // first rung and the network flag is clear, or a recycled instance would
        // inherit the previous job's schedule position.
        job.LastFailureWasNetwork = false;
        job.BackoffStep = 0;
        job.NetworkFailureStep = 0;

        pending.Add(job);
        if (!TryWriteJob(pendingDir, job))
        {
            // Roll back rather than throw. An unpersisted job in memory is the worst
            // of the three outcomes: it would be attempted this session and then
            // vanish on the next launch, while the caller had already been told the
            // sighting was taken. `false` is the channel SightingReportsAdapter.Submit
            // already handles — the user stays on the form with everything they typed
            // and the copied photo is cleaned up — whereas an exception would escape
            // into a UI Toolkit click handler.
            pending.Remove(job);
            return false;
        }
        return true;
    }

    public JobStatus GetStatus(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return JobStatus.NotFound;
        EnsureInitialized();
        if (!initialized) return JobStatus.NotFound;
        if (pending.Exists(j => j.Id == jobId)) return JobStatus.Pending;
        if (File.Exists(Path.Combine(failedDir, jobId + ".json"))) return JobStatus.Failed;
        return JobStatus.NotFound;
    }

    public int PendingCount => pending.Count;

    /// <summary>
    /// Read-only view of everything still queued, in the order the loop will run
    /// it (FIFO). Each <see cref="JobRecord"/> carries the envelope fields plus a
    /// detachable copy of the job's payload, so a feed can render "which sighting,
    /// when, what state" without touching the live job — see
    /// <see cref="JobRecord.Snapshot{T}"/>.
    ///
    /// <para><paramref name="type"/> filters by <see cref="Job.Type"/> (pass
    /// <c>ReportSightingJob.JobType</c> for the sightings feed); null returns
    /// every type.</para>
    ///
    /// <para><b>Absence is not success.</b> A job that is not in this list has
    /// either succeeded, permanently failed (see <see cref="ListFailed"/>) or was
    /// never enqueued. That is deliberately NOT answered here: unlike
    /// <see cref="GetStatus"/>, which collapses "succeeded" and "never existed"
    /// into one <see cref="JobStatus.NotFound"/>, this API only ever reports
    /// states a job is actually in.</para>
    /// </summary>
    public IReadOnlyList<JobRecord> ListPending(string type = null)
    {
        EnsureInitialized();
        var records = new List<JobRecord>(pending.Count);
        if (!initialized) return records;
        bool online = IsOnline();
        foreach (var job in pending)
        {
            if (type != null && job.Type != type) continue;
            records.Add(new JobRecord(job, PendingStateOf(job, online)));
        }
        return records;
    }

    /// <summary>
    /// Read-only view of the permanently failed jobs kept in <c>jobs/failed/</c>.
    /// Same row shape as <see cref="ListPending"/>, always
    /// <see cref="JobRecordState.Failed"/> — so a feed can show a submission that
    /// will never go through instead of silently dropping it.
    ///
    /// <para>Reads the directory on every call (the queue holds no in-memory copy
    /// of failed jobs). Files whose type is not registered are skipped with the
    /// same error <see cref="LoadPendingFromDisk"/> logs.</para>
    /// </summary>
    public IReadOnlyList<JobRecord> ListFailed(string type = null)
    {
        EnsureInitialized();
        var records = new List<JobRecord>();
        if (!initialized) return records;
        foreach (var path in Directory.GetFiles(failedDir, "*.json"))
        {
            try
            {
                Job job = ReadJob(path);
                if (job == null) continue;
                if (type != null && job.Type != type) continue;
                records.Add(new JobRecord(job, JobRecordState.Failed));
            }
            catch (Exception e)
            {
                Debug.LogError($"JobQueue: failed to read {path}: {e.Message}");
            }
        }
        return records;
    }

    /// <summary>
    /// Moves a permanently-failed job back into the pending queue so it is
    /// attempted again. Returns false when no failed job with that id is on disk,
    /// its type is not registered, or the queue is full.
    ///
    /// <para><b>Why the attempt count resets.</b> A requeue is a person deciding to
    /// try again, and the job's own retry policy is written in terms of attempts —
    /// <see cref="ReportSightingJob"/> gives up once it has already refreshed its
    /// App Check token and been rejected again, so carrying the old state forward
    /// would send the retry straight back to <c>failed/</c>. The two backoff
    /// cursors and the network flag reset for the same reason: the person is asking
    /// for a fresh try, not for the rung the job had crawled to.
    /// <c>CreatedAtUtc</c> and the job id are preserved: the id is the idempotency
    /// key the backend dedupes on, and the feed reads the original timestamp out of
    /// the payload.</para>
    ///
    /// <para><b>Write before delete, deliberately.</b> A crash between the two
    /// leaves the job in BOTH directories, i.e. one extra row and one extra upload
    /// that the idempotency key collapses server-side. The other order would lose
    /// the sighting outright.</para>
    /// </summary>
    public bool Requeue(string jobId)
    {
        EnsureInitialized();
        if (!initialized) return false;
        if (string.IsNullOrEmpty(jobId)) return false;

        string path = Path.Combine(failedDir, jobId + ".json");
        if (!File.Exists(path)) return false;

        if (pending.Count >= maxQueueSize)
        {
            Debug.LogWarning($"JobQueue: max queue size ({maxQueueSize}) reached, cannot requeue job {jobId}.");
            return false;
        }

        Job job;
        try
        {
            job = ReadJob(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: failed to read {path}: {e.Message}");
            return false;
        }
        if (job == null) return false;

        job.AttemptCount = 0;
        job.NextAttemptAtUtc = DateTime.UtcNow;
        job.LastError = null;
        job.LastFailureWasNetwork = false;
        job.BackoffStep = 0;
        job.NetworkFailureStep = 0;

        pending.Add(job);
        if (!TryWriteJob(pendingDir, job))
        {
            // Roll back and leave the copy in failed/ exactly where it is: the row
            // stays in the feed and the person can press retry again. Half-moving it
            // into a queue that has no record of it on disk would lose the sighting
            // at the next launch.
            pending.Remove(job);
            return false;
        }
        TryDeleteJobFile(failedDir, jobId);
        Debug.Log($"JobQueue: job {jobId} ({job.Type}) requeued from failed/.");
        return true;
    }

    /// <summary>
    /// Pulls every job that is only waiting because the <b>network</b> failed back
    /// to "due now", and returns how many were moved.
    ///
    /// <para><b>The failure this fixes.</b> <see cref="Job.NextAttemptAtUtc"/> is an
    /// absolute instant, persisted, and nothing ever shortened it. So a job that
    /// collected a connection error on a network that merely <em>looked</em> online
    /// (<see cref="IsOnline"/> cannot see a captive portal or a dead resolver) sat
    /// out its whole rung even after the user walked back into real coverage — the
    /// single clearest signal that retrying now will work.</para>
    ///
    /// <para>Only jobs whose last failure was a network failure are touched. A
    /// server-side backoff is left exactly where it is: connectivity returning says
    /// nothing at all about whether the backend has stopped erroring.
    /// <see cref="Job.NetworkFailureStep"/> is reset too, so the next failure on the
    /// new network starts from the first rung rather than resuming the old one's.</para>
    /// </summary>
    internal int OnConnectivityRegained()
    {
        EnsureInitialized();
        if (!initialized) return 0;

        DateTime now = DateTime.UtcNow;
        int pulled = 0;
        foreach (var job in pending)
        {
            if (!job.RequiresNetwork) continue;
            if (!job.LastFailureWasNetwork) continue;
            if (job.NextAttemptAtUtc <= now) continue;

            job.NextAttemptAtUtc = now;
            job.NetworkFailureStep = 0;
            TryWriteJob(pendingDir, job);
            pulled++;
        }
        if (pulled > 0)
            Debug.Log($"JobQueue: connectivity returned — pulled {pulled} network-delayed job(s) forward to now.");
        return pulled;
    }

    private static JobRecordState PendingStateOf(Job job, bool online)
    {
        // Offline wins over backoff: it is the actionable thing to tell the user,
        // and NextDueJob skips these jobs regardless of how due they are.
        if (job.RequiresNetwork && !online) return JobRecordState.WaitingForNetwork;
        return job.AttemptCount > 0 ? JobRecordState.Retrying : JobRecordState.Queued;
    }

    private IEnumerator RunLoop()
    {
        int generation = ++loopGeneration;

        while (generation == loopGeneration)
        {
            // Stamped first and unconditionally: this is the liveness signal the
            // Update watchdog reads, and it must mean "the loop reached the top of
            // a tick", not "the loop got all the way through one".
            lastTickRealtime = Time.realtimeSinceStartup;

            if (!running)
            {
                // Polled before picking a job so a connection that just came back
                // makes its jobs due on THIS tick rather than the next one.
                try { PollConnectivity(); }
                catch (Exception e)
                {
                    Debug.LogError($"JobQueue: exception polling connectivity: {e}");
                }

                Job due = null;
                try { due = NextDueJob(); }
                catch (Exception e)
                {
                    // Nothing here is expected to throw, which is exactly why it is
                    // guarded: an exception escaping a tick used to end the loop —
                    // and with it every retry — for the rest of the process.
                    Debug.LogError($"JobQueue: exception selecting the next due job: {e}");
                }

                if (due != null)
                    yield return RunJob(due);
            }

            // A fresh WaitForSecondsRealtime every iteration: realtime because
            // WaitForSeconds is scaled by Time.timeScale (a paused app would stop
            // retrying), and fresh because the instruction latches its deadline in
            // its constructor — re-yielding one that has already elapsed returns
            // immediately and spins the loop every frame.
            yield return new WaitForSecondsRealtime(tickIntervalSeconds);
        }
    }

    /// <summary>
    /// Watches for the offline→online edge and hands it to
    /// <see cref="OnConnectivityRegained"/>. The first poll only records the
    /// current value, so launching while online is not mistaken for a transition.
    /// </summary>
    private void PollConnectivity()
    {
        bool online = IsOnline();
        bool regained = wasOnline == false && online;
        wasOnline = online;
        if (regained) OnConnectivityRegained();
    }

    private Job NextDueJob()
    {
        DateTime now = DateTime.UtcNow;
        bool online = IsOnline();
        for (int i = 0; i < pending.Count; i++)
        {
            var j = pending[i];
            if (j.NextAttemptAtUtc > now) continue;
            if (j.RequiresNetwork && !online) continue;
            return j;
        }
        return null;
    }

    private bool IsOnline()
    {
        if (IsOnlineOverride != null) return IsOnlineOverride();
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    internal IEnumerator RunOnceForTests()
    {
        EnsureInitialized();
        if (running) yield break;
        Job due = NextDueJob();
        if (due != null) yield return RunJob(due);
    }

    internal IReadOnlyList<Job> PendingForTests => pending;
    internal string PendingDirForTests => pendingDir;
    internal string FailedDirForTests => failedDir;
    internal bool RunningForTests => running;

    private IEnumerator RunJob(Job job)
    {
        running = true;
        job.AttemptCount++;
        JobResult? result = null;

        IEnumerator inner = null;
        bool startFailed = false;
        try { inner = job.Execute(r => result = r); }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: exception starting job {job.Id} ({job.Type}): {e}");
            job.LastError = "start: " + e.Message;
            job.LastFailureWasNetwork = false; // our own bug, not the radio's
            result = JobResult.TransientFailure;
            startFailed = true;
        }

        if (!startFailed)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError($"JobQueue: exception in job {job.Id} ({job.Type}): {e}");
                    job.LastError = "execute: " + e.Message;
                    job.LastFailureWasNetwork = false; // our own bug, not the radio's
                    result = JobResult.TransientFailure;
                    break;
                }
                yield return current;
            }
        }

        // The tail is a separate non-yielding method purely so it can be wrapped:
        // C# forbids `yield return` inside a try-with-catch, so the only way to
        // guard HandleResult (unprotected file IO, plus a synchronous JobCompleted
        // that fans out into a full UI Toolkit tree rebuild) is to move it out of
        // the iterator body. It used to sit here bare, so any throw propagated out
        // through `yield return RunJob(due)`, Unity terminated the loop coroutine,
        // and `running` stayed true forever — no retries again, ever.
        FinishJob(job, result ?? JobResult.TransientFailure);
    }

    /// <summary>
    /// Applies a finished job's result and releases the queue. Cannot throw:
    /// <c>running = false</c> is in a finally, so even a bug in
    /// <see cref="HandleResult"/> leaves the queue able to run the next job.
    /// </summary>
    private void FinishJob(Job job, JobResult result)
    {
        try
        {
            HandleResult(job, result);
        }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: exception handling the result of job {job.Id} ({job.Type}): {e}");
        }
        finally
        {
            running = false;
            lastTickRealtime = Time.realtimeSinceStartup;
        }
    }

    private void HandleResult(Job job, JobResult result)
    {
        switch (result)
        {
            case JobResult.Success:
                pending.Remove(job);
                TryDeleteJobFile(pendingDir, job.Id);
                Debug.Log($"JobQueue: job {job.Id} ({job.Type}) succeeded after {job.AttemptCount} attempt(s).");
                RaiseJobCompleted(job.Id, JobResult.Success);
                break;

            case JobResult.PermanentFailure:
                // Write the failed copy BEFORE removing the pending one, mirroring
                // Requeue: a kill between the two then leaves the job in both
                // directories — one extra row the idempotency key collapses
                // server-side — where the old order (delete, then write) lost the
                // sighting outright.
                bool archived = TryWriteJob(failedDir, job);
                pending.Remove(job);
                if (archived)
                {
                    TryDeleteJobFile(pendingDir, job.Id);
                }
                else
                {
                    Debug.LogError($"JobQueue: could not archive job {job.Id} to failed/; leaving jobs/{job.Id}.json in place so the next launch can still see it.");
                }
                Debug.LogWarning($"JobQueue: job {job.Id} ({job.Type}) permanently failed: {job.LastError}");
                RaiseJobCompleted(job.Id, JobResult.PermanentFailure);
                break;

            case JobResult.TransientFailure:
            default:
                TimeSpan delay;
                if (job.LastFailureWasNetwork)
                {
                    int idx = Math.Min(job.NetworkFailureStep, NetworkBackoffSchedule.Length - 1);
                    delay = NetworkBackoffSchedule[idx];
                    job.NetworkFailureStep++;
                    // BackoffStep is deliberately untouched: a dead uplink must not
                    // walk the job up the server-side rungs, which is exactly how a
                    // few dropped connections used to leave a sighting parked an
                    // hour out over a problem the user had already fixed.
                }
                else
                {
                    int idx = Math.Min(job.BackoffStep, BackoffSchedule.Length - 1);
                    delay = BackoffSchedule[idx];
                    job.BackoffStep++;
                    // A server answered, so whatever the network was doing before is
                    // history and the gentle cursor starts over next time.
                    job.NetworkFailureStep = 0;
                }
                job.NextAttemptAtUtc = DateTime.UtcNow + delay;
                TryWriteJob(pendingDir, job);
                Debug.Log($"JobQueue: job {job.Id} ({job.Type}) transient failure (attempt {job.AttemptCount}, {(job.LastFailureWasNetwork ? "network" : "server")}), retrying at {job.NextAttemptAtUtc:O}. Error: {job.LastError}");
                break;
        }
    }

    /// <summary>
    /// Fans <see cref="JobCompleted"/> out one subscriber at a time, each in its own
    /// try/catch.
    ///
    /// <para><b>Why not just <c>JobCompleted?.Invoke(...)</c>.</b> A plain invoke
    /// runs the chain on one stack: the first subscriber that throws skips every
    /// later one AND propagates out of the run loop, which terminated the retry
    /// coroutine for the rest of the process. The subscriber chain here reaches the
    /// app layer's sightings feed, which rebuilds a UI Toolkit tree — far too much
    /// code to promise it never throws.</para>
    /// </summary>
    private void RaiseJobCompleted(string jobId, JobResult result)
    {
        var handler = JobCompleted;
        if (handler == null) return;

        foreach (Delegate d in handler.GetInvocationList())
        {
            try
            {
                ((Action<string, JobResult>)d)(jobId, result);
            }
            catch (Exception e)
            {
                Debug.LogError($"JobQueue: a JobCompleted subscriber threw for job {jobId}: {e}");
            }
        }
    }

    /// <summary>Persists a job, degrading an IO error to a log. Returns whether it
    /// was written — callers that are about to delete the other copy need to know.</summary>
    private bool TryWriteJob(string dir, Job job)
    {
        try
        {
            WriteJob(dir, job);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: failed to persist job {job.Id} ({job.Type}) to {dir}: {e.Message}");
            return false;
        }
    }

    /// <summary>Deletes a job file, degrading an IO error to a log. A file that
    /// cannot be deleted is a stale row on the next launch; a throw here used to be
    /// the end of all retries.</summary>
    private void TryDeleteJobFile(string dir, string jobId)
    {
        try
        {
            DeleteJobFile(dir, jobId);
        }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: failed to delete the job file for {jobId} in {dir}: {e.Message}");
        }
    }

    private void LoadPending()
    {
        if (running)
        {
            // Reloading would clear `pending` and drop the instance the run loop is
            // currently executing: the job would finish against a list it is no
            // longer in, HandleResult's Remove would no-op, and the reloaded copy
            // (written before the attempt) would be retried from scratch.
            Debug.LogWarning("JobQueue: a job is executing; skipping the reload from disk so the running job is not orphaned.");
            return;
        }

        pending.Clear();

        var loaded = new List<Job>();
        foreach (var path in Directory.GetFiles(pendingDir, "*.json"))
        {
            try
            {
                Job job = ReadJob(path);
                if (job != null) loaded.Add(job);
            }
            catch (Exception e)
            {
                Debug.LogError($"JobQueue: failed to load {path}: {e.Message}");
            }
        }

        // Directory.GetFiles hands back filesystem order, which is neither
        // enqueue order nor stable across platforms — yet ListPending's contract,
        // and the feed built on it, promise FIFO. Sort by creation, with the id as
        // a stable tiebreak for jobs enqueued inside the same tick.
        loaded.Sort((a, b) =>
        {
            int byTime = a.CreatedAtUtc.CompareTo(b.CreatedAtUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
        });
        pending.AddRange(loaded);

        if (pending.Count > 0)
            Debug.Log($"JobQueue: loaded {pending.Count} pending job(s) from disk.");

        PullNetworkDelayedJobsForwardOnLaunch();
    }

    /// <summary>
    /// Clamps network-caused backoff for jobs just read off disk.
    ///
    /// <para>Relaunching the app is the user's clearest "send it now" signal, and a
    /// job parked on a network rung has no reason to keep waiting: the delay was
    /// charged for an uplink that may not even be the current one. The clamp is to
    /// a few seconds rather than to zero so the first attempt lands after the
    /// launch rush (AR session, camera permission, GPS fix, the ONNX worker) rather
    /// than inside it.</para>
    ///
    /// <para><see cref="Job.NetworkFailureStep"/> resets for the same reason
    /// <see cref="OnConnectivityRegained"/> resets it: the escalation was earned on
    /// a network that is very likely gone — a different Wi-Fi, a different day — so
    /// the app has no evidence at all about the current one and must not pre-punish
    /// it with a rung it did nothing to deserve.</para>
    ///
    /// <para>A server-caused delay is left alone. A backend that is 500ing has
    /// earned its rung, and restarting the app is not evidence it has recovered —
    /// clamping that too would just turn every relaunch into another hit on a
    /// broken server.</para>
    /// </summary>
    private void PullNetworkDelayedJobsForwardOnLaunch()
    {
        DateTime now = DateTime.UtcNow;
        DateTime threshold = now.AddSeconds(LaunchPullForwardThresholdSeconds);
        int pulled = 0;

        foreach (var job in pending)
        {
            if (!job.LastFailureWasNetwork) continue;
            if (job.NextAttemptAtUtc <= threshold) continue;

            job.NextAttemptAtUtc = now.AddSeconds(LaunchPullForwardDelaySeconds);
            job.NetworkFailureStep = 0;
            TryWriteJob(pendingDir, job);
            pulled++;
        }

        if (pulled > 0)
            Debug.Log($"JobQueue: pulled {pulled} network-delayed job(s) forward to ~{LaunchPullForwardDelaySeconds:F0}s from now — a relaunch is an explicit retry signal.");
    }

    private Job ReadJob(string path)
    {
        string json = File.ReadAllText(path);
        var env = JsonUtility.FromJson<JobEnvelope>(json);
        if (!typeFactories.TryGetValue(env.type, out var factory))
        {
            Debug.LogError($"JobQueue: unknown job type '{env.type}' in {path}, skipping.");
            return null;
        }
        var job = factory();
        job.Id = env.id;
        job.AttemptCount = env.attemptCount;
        job.NextAttemptAtUtc = new DateTime(env.nextAttemptAtUtcTicks, DateTimeKind.Utc);
        job.CreatedAtUtc = new DateTime(env.createdAtUtcTicks, DateTimeKind.Utc);
        job.LastError = env.lastError;
        // Absent in a file written by a build that predates the backoff split;
        // JsonUtility leaves them at false/0, which is the "never failed" state.
        job.LastFailureWasNetwork = env.lastFailureWasNetwork;
        job.BackoffStep = env.backoffStep;
        job.NetworkFailureStep = env.networkFailureStep;
        job.DeserializeData(env.data);
        return job;
    }

    private void WriteJob(string dir, Job job)
    {
        var env = new JobEnvelope
        {
            type = job.Type,
            id = job.Id,
            attemptCount = job.AttemptCount,
            nextAttemptAtUtcTicks = job.NextAttemptAtUtc.Ticks,
            createdAtUtcTicks = job.CreatedAtUtc.Ticks,
            lastError = job.LastError,
            data = job.SerializeData(),
            lastFailureWasNetwork = job.LastFailureWasNetwork,
            backoffStep = job.BackoffStep,
            networkFailureStep = job.NetworkFailureStep,
        };
        string path = Path.Combine(dir, job.Id + ".json");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(env));

        // Overwrite in ONE filesystem operation. The old code did Delete-then-Move,
        // which despite the comment it carried is not atomic at all: a kill in the
        // window between them left neither file, i.e. the sighting gone.
        //
        // File.Move's overwrite overload would be the obvious fix, but it does not
        // exist here: the netstandard 2.1 surface Unity 6000.3 compiles and runs
        // against exposes only Move(source, dest) — no three-argument form, in
        // either the reference assembly or the runtime BCL. File.Replace with a null
        // backup is the primitive that does exist, and on both runtimes we ship it
        // is precisely a single rename(2) of source over dest (il2cpp
        // os/Posix/File.cpp ReplaceFile, mono w32file-unix.c ReplaceFile: with no
        // backup name, the only syscall is the rename). Replace requires the
        // destination to exist, which is not the first write — and a Move onto a
        // free name is already atomic, so that case needs nothing.
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    private void DeleteJobFile(string dir, string jobId)
    {
        string path = Path.Combine(dir, jobId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
