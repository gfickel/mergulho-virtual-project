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

    private static readonly TimeSpan[] BackoffSchedule = new[]
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
    };

    private readonly Dictionary<string, Func<Job>> typeFactories = new Dictionary<string, Func<Job>>();
    private readonly List<Job> pending = new List<Job>();
    private string pendingDir;
    private string failedDir;
    private bool running;
    private bool initialized;

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
        StartCoroutine(RunLoop());
    }

    internal void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        if (Instance == null) Instance = this;

        if (!typeFactories.ContainsKey("HttpPost")) RegisterType<HttpPostJob>();
        if (!typeFactories.ContainsKey("FileDownload")) RegisterType<FileDownloadJob>();
        if (!typeFactories.ContainsKey("ReportSighting")) RegisterType<ReportSightingJob>();

        string root = TestRootOverride ?? Application.persistentDataPath;
        pendingDir = Path.Combine(root, "jobs");
        failedDir = Path.Combine(pendingDir, "failed");
        Directory.CreateDirectory(pendingDir);
        Directory.CreateDirectory(failedDir);
    }

    internal void LoadPendingFromDisk()
    {
        EnsureInitialized();
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

        pending.Add(job);
        WriteJob(pendingDir, job);
        return true;
    }

    public JobStatus GetStatus(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return JobStatus.NotFound;
        EnsureInitialized();
        if (pending.Exists(j => j.Id == jobId)) return JobStatus.Pending;
        if (File.Exists(Path.Combine(failedDir, jobId + ".json"))) return JobStatus.Failed;
        return JobStatus.NotFound;
    }

    public int PendingCount => pending.Count;

    private IEnumerator RunLoop()
    {
        var wait = new WaitForSeconds(tickIntervalSeconds);
        while (true)
        {
            if (!running)
            {
                Job due = NextDueJob();
                if (due != null)
                    yield return RunJob(due);
            }
            yield return wait;
        }
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

    private IEnumerator RunJob(Job job)
    {
        running = true;
        job.AttemptCount++;
        JobResult? result = null;

        IEnumerator inner;
        try { inner = job.Execute(r => result = r); }
        catch (Exception e)
        {
            Debug.LogError($"JobQueue: exception starting job {job.Id} ({job.Type}): {e}");
            job.LastError = "start: " + e.Message;
            HandleResult(job, JobResult.TransientFailure);
            running = false;
            yield break;
        }

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
                result = JobResult.TransientFailure;
                break;
            }
            yield return current;
        }

        HandleResult(job, result ?? JobResult.TransientFailure);
        running = false;
    }

    private void HandleResult(Job job, JobResult result)
    {
        switch (result)
        {
            case JobResult.Success:
                pending.Remove(job);
                DeleteJobFile(pendingDir, job.Id);
                Debug.Log($"JobQueue: job {job.Id} ({job.Type}) succeeded after {job.AttemptCount} attempt(s).");
                JobCompleted?.Invoke(job.Id, JobResult.Success);
                break;

            case JobResult.PermanentFailure:
                pending.Remove(job);
                DeleteJobFile(pendingDir, job.Id);
                WriteJob(failedDir, job);
                Debug.LogWarning($"JobQueue: job {job.Id} ({job.Type}) permanently failed: {job.LastError}");
                JobCompleted?.Invoke(job.Id, JobResult.PermanentFailure);
                break;

            case JobResult.TransientFailure:
            default:
                int idx = Math.Min(job.AttemptCount - 1, BackoffSchedule.Length - 1);
                if (idx < 0) idx = 0;
                job.NextAttemptAtUtc = DateTime.UtcNow + BackoffSchedule[idx];
                WriteJob(pendingDir, job);
                Debug.Log($"JobQueue: job {job.Id} ({job.Type}) transient failure (attempt {job.AttemptCount}), retrying at {job.NextAttemptAtUtc:O}. Error: {job.LastError}");
                break;
        }
    }

    private void LoadPending()
    {
        pending.Clear();
        foreach (var path in Directory.GetFiles(pendingDir, "*.json"))
        {
            try
            {
                Job job = ReadJob(path);
                if (job != null) pending.Add(job);
            }
            catch (Exception e)
            {
                Debug.LogError($"JobQueue: failed to load {path}: {e.Message}");
            }
        }
        if (pending.Count > 0)
            Debug.Log($"JobQueue: loaded {pending.Count} pending job(s) from disk.");
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
        };
        string path = Path.Combine(dir, job.Id + ".json");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(env));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    private void DeleteJobFile(string dir, string jobId)
    {
        string path = Path.Combine(dir, jobId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
