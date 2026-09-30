using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class JobQueueTests
{
    private string tempRoot;
    private GameObject host;
    private JobQueue queue;
    private float savedTokenWaitTimeout;

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "mv-jobqueue-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        JobQueue.TestRootOverride = tempRoot;
        JobQueue.Instance = null;
        host = new GameObject("JobServices-test");
        queue = host.AddComponent<JobQueue>();
        queue.RegisterType<TestJob>();
        queue.IsOnlineOverride = () => true;
        savedTokenWaitTimeout = ReportSightingJob.TokenWaitTimeoutSeconds;
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) UnityEngine.Object.DestroyImmediate(host);
        JobQueue.Instance = null;
        JobQueue.TestRootOverride = null;
        // Both are process-wide statics: leaving either set would leak into every
        // later test in the run.
        AppCheckTokenProvider.TokenOverride = null;
        ReportSightingJob.TokenWaitTimeoutSeconds = savedTokenWaitTimeout;
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
        catch { }
    }

    /// <summary>Finds a loaded job by id, for the reload tests.</summary>
    private Job FindPending(string id)
    {
        foreach (var j in queue.PendingForTests)
            if (j.Id == id) return j;
        return null;
    }

    /// <summary>
    /// Hand-writes a pending job file with full control over the envelope fields —
    /// the only way to set <c>CreatedAtUtc</c> and a far-future
    /// <c>NextAttemptAtUtc</c>, both of which Enqueue overwrites with "now".
    /// </summary>
    private void WriteEnvelope(string id, DateTime createdAtUtc, DateTime nextAttemptAtUtc,
                               bool lastFailureWasNetwork = false, int attemptCount = 1,
                               int backoffStep = 0, int networkFailureStep = 0)
    {
        queue.EnsureInitialized();
        var env = new JobEnvelope
        {
            type = "TestJob",
            id = id,
            attemptCount = attemptCount,
            nextAttemptAtUtcTicks = nextAttemptAtUtc.Ticks,
            createdAtUtcTicks = createdAtUtc.Ticks,
            lastError = "boom",
            data = new TestJob { NextResult = JobResult.TransientFailure }.SerializeData(),
            lastFailureWasNetwork = lastFailureWasNetwork,
            backoffStep = backoffStep,
            networkFailureStep = networkFailureStep,
        };
        File.WriteAllText(Path.Combine(queue.PendingDirForTests, id + ".json"), JsonUtility.ToJson(env));
    }

    private JobQueue RecreateQueue()
    {
        if (host != null) UnityEngine.Object.DestroyImmediate(host);
        JobQueue.Instance = null;
        host = new GameObject("JobServices-test-2");
        queue = host.AddComponent<JobQueue>();
        queue.RegisterType<TestJob>();
        queue.IsOnlineOverride = () => true;
        queue.LoadPendingFromDisk();
        return queue;
    }

    // ---------- Enqueue + capacity ----------

    [Test]
    public void Enqueue_AssignsIdWhenMissing()
    {
        var job = new TestJob();
        Assert.IsTrue(queue.Enqueue(job));
        Assert.IsFalse(string.IsNullOrEmpty(job.Id));
    }

    [Test]
    public void Enqueue_PreservesProvidedId()
    {
        var job = new TestJob { Id = "my-fixed-id" };
        Assert.IsTrue(queue.Enqueue(job));
        Assert.AreEqual("my-fixed-id", job.Id);
    }

    [Test]
    public void Enqueue_PersistsFileToPendingDir()
    {
        var job = new TestJob { Id = "persist-1" };
        queue.Enqueue(job);
        string expected = Path.Combine(queue.PendingDirForTests, "persist-1.json");
        Assert.IsTrue(File.Exists(expected), "expected pending file " + expected);
    }

    [Test]
    public void Enqueue_RejectsWhenAtCapacity()
    {
        queue.maxQueueSize = 2;
        Assert.IsTrue(queue.Enqueue(new TestJob()));
        Assert.IsTrue(queue.Enqueue(new TestJob()));

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*max queue size.*"));
        Assert.IsFalse(queue.Enqueue(new TestJob()));
        Assert.AreEqual(2, queue.PendingCount);
    }

    [Test]
    public void Enqueue_RejectsUnregisteredType()
    {
        var unregistered = new UnregisteredTestJob();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*unregistered job type.*"));
        Assert.IsFalse(queue.Enqueue(unregistered));
        Assert.AreEqual(0, queue.PendingCount);
    }

    [Test]
    public void Enqueue_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null));
    }

    // ---------- Run: success / permanent / transient ----------

    [UnityTest]
    public IEnumerator RunOnce_Success_RemovesJobAndFiresEvent()
    {
        var job = new TestJob { NextResult = JobResult.Success };
        queue.Enqueue(job);
        string path = Path.Combine(queue.PendingDirForTests, job.Id + ".json");

        string completedId = null;
        JobResult? completedResult = null;
        queue.JobCompleted += (id, r) => { completedId = id; completedResult = r; };

        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, job.Calls);
        Assert.AreEqual(0, queue.PendingCount);
        Assert.IsFalse(File.Exists(path), "pending file should be deleted on success");
        Assert.AreEqual(job.Id, completedId);
        Assert.AreEqual(JobResult.Success, completedResult);
        Assert.AreEqual(JobStatus.NotFound, queue.GetStatus(job.Id));
    }

    [UnityTest]
    public IEnumerator RunOnce_PermanentFailure_MovesToFailedDirAndFiresEvent()
    {
        var job = new TestJob { NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        string pendingPath = Path.Combine(queue.PendingDirForTests, job.Id + ".json");
        string failedPath = Path.Combine(queue.FailedDirForTests, job.Id + ".json");

        JobResult? completedResult = null;
        queue.JobCompleted += (id, r) => completedResult = r;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(0, queue.PendingCount);
        Assert.IsFalse(File.Exists(pendingPath));
        Assert.IsTrue(File.Exists(failedPath), "failed file should exist at " + failedPath);
        Assert.AreEqual(JobResult.PermanentFailure, completedResult);
        Assert.AreEqual(JobStatus.Failed, queue.GetStatus(job.Id));
    }

    [UnityTest]
    public IEnumerator RunOnce_TransientFailure_StaysPendingAndAdvancesNextAttempt()
    {
        var job = new TestJob { NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);
        DateTime before = DateTime.UtcNow;

        bool eventFired = false;
        queue.JobCompleted += (id, r) => eventFired = true;

        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, queue.PendingCount, "transient failure should keep job pending");
        Assert.AreEqual(1, job.AttemptCount);
        Assert.IsFalse(eventFired, "JobCompleted should not fire on transient failure");
        Assert.IsTrue(job.NextAttemptAtUtc >= before.AddSeconds(4), "next attempt should be ~5s out, got " + (job.NextAttemptAtUtc - before).TotalSeconds);
        Assert.IsTrue(job.NextAttemptAtUtc <= before.AddSeconds(10));
    }

    [UnityTest]
    public IEnumerator RunOnce_BackoffEscalatesPerSchedule()
    {
        var job = new TestJob { NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);

        // Expected schedule: 5s, 30s, 2min, 10min, 1h, 1h (capped)
        var expectedSeconds = new[] { 5, 30, 120, 600, 3600, 3600 };

        for (int i = 0; i < expectedSeconds.Length; i++)
        {
            // Force the job due now so RunOnce will pick it up
            ((TestJob)queue.PendingForTests[0]).NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);

            DateTime before = DateTime.UtcNow;
            yield return queue.RunOnceForTests();
            DateTime after = DateTime.UtcNow;

            double seconds = (job.NextAttemptAtUtc - before).TotalSeconds;
            int expected = expectedSeconds[i];
            Assert.That(seconds, Is.InRange(expected - 2, expected + 2),
                $"attempt {i + 1}: expected ~{expected}s backoff, got {seconds:F1}s");
            Assert.AreEqual(i + 1, job.AttemptCount);
        }
    }

    [UnityTest]
    public IEnumerator RunOnce_ExceptionInExecuteIsTreatedAsTransient()
    {
        var job = new TestJob { ThrowOnExecute = true };
        queue.Enqueue(job);

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*exception.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, queue.PendingCount);
        Assert.AreEqual(1, job.AttemptCount);
        Assert.IsNotNull(job.LastError);
    }

    // ---------- Scheduling: NextAttemptAtUtc + connectivity ----------

    [UnityTest]
    public IEnumerator NextDueJob_RespectsNextAttemptAtUtc()
    {
        var job = new TestJob { NextResult = JobResult.Success };
        queue.Enqueue(job);
        job.NextAttemptAtUtc = DateTime.UtcNow.AddHours(1);

        yield return queue.RunOnceForTests();

        Assert.AreEqual(0, job.Calls, "future job must not run");
        Assert.AreEqual(1, queue.PendingCount);
    }

    [UnityTest]
    public IEnumerator NextDueJob_SkipsNetworkRequiringJobsWhenOffline()
    {
        var job = new TestJob { NextResult = JobResult.Success, RequiresNetworkOverride = true };
        queue.Enqueue(job);
        queue.IsOnlineOverride = () => false;

        yield return queue.RunOnceForTests();

        Assert.AreEqual(0, job.Calls, "network job must not run while offline");
        Assert.AreEqual(1, queue.PendingCount);
    }

    [UnityTest]
    public IEnumerator NextDueJob_RunsNonNetworkJobWhenOffline()
    {
        var job = new TestJob { NextResult = JobResult.Success, RequiresNetworkOverride = false };
        queue.Enqueue(job);
        queue.IsOnlineOverride = () => false;

        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, job.Calls);
    }

    [UnityTest]
    public IEnumerator NextDueJob_RunsNetworkJobWhenBackOnline()
    {
        var job = new TestJob { NextResult = JobResult.Success };
        queue.Enqueue(job);
        queue.IsOnlineOverride = () => false;
        yield return queue.RunOnceForTests();
        Assert.AreEqual(0, job.Calls);

        queue.IsOnlineOverride = () => true;
        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, job.Calls);
    }

    // ---------- GetStatus ----------

    [Test]
    public void GetStatus_NotFoundForUnknownId()
    {
        Assert.AreEqual(JobStatus.NotFound, queue.GetStatus("nope"));
        Assert.AreEqual(JobStatus.NotFound, queue.GetStatus(null));
        Assert.AreEqual(JobStatus.NotFound, queue.GetStatus(""));
    }

    [Test]
    public void GetStatus_PendingForEnqueuedJob()
    {
        var job = new TestJob { Id = "status-pending" };
        queue.Enqueue(job);
        Assert.AreEqual(JobStatus.Pending, queue.GetStatus("status-pending"));
    }

    // ---------- ListPending / ListFailed ----------
    //
    // These are the unambiguous half of the status API: unlike GetStatus, which
    // answers NotFound both for "already succeeded" and "never enqueued", a record
    // exists only for a job that really is in that state.

    [Test]
    public void ListPending_IsEmptyWhenNothingIsQueued()
    {
        Assert.AreEqual(0, queue.ListPending().Count);
    }

    [Test]
    public void ListPending_ReturnsEveryQueuedJobInQueueOrder()
    {
        queue.Enqueue(new TestJob { Id = "first" });
        queue.Enqueue(new TestJob { Id = "second" });

        var records = queue.ListPending();

        Assert.AreEqual(2, records.Count);
        Assert.AreEqual("first", records[0].Id);
        Assert.AreEqual("second", records[1].Id);
        Assert.AreEqual("TestJob", records[0].Type);
        Assert.AreEqual(0, records[0].AttemptCount);
        Assert.AreEqual(JobRecordState.Queued, records[0].State);
    }

    [Test]
    public void ListPending_FiltersByType()
    {
        queue.RegisterType<OtherTestJob>();
        queue.Enqueue(new TestJob { Id = "a-test-job" });
        queue.Enqueue(new OtherTestJob { Id = "an-other-job" });

        Assert.AreEqual(2, queue.ListPending().Count);

        var filtered = queue.ListPending("TestJob");
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("a-test-job", filtered[0].Id);
    }

    [Test]
    public void ListPending_ReportsWaitingForNetworkWhileOffline()
    {
        queue.Enqueue(new TestJob { Id = "net" });
        queue.IsOnlineOverride = () => false;

        Assert.AreEqual(JobRecordState.WaitingForNetwork, queue.ListPending()[0].State);

        queue.IsOnlineOverride = () => true;
        Assert.AreEqual(JobRecordState.Queued, queue.ListPending()[0].State);
    }

    [Test]
    public void ListPending_ANonNetworkJobIsNeverWaitingForNetwork()
    {
        queue.Enqueue(new TestJob { Id = "local", RequiresNetworkOverride = false });
        queue.IsOnlineOverride = () => false;

        Assert.AreEqual(JobRecordState.Queued, queue.ListPending()[0].State);
    }

    [UnityTest]
    public IEnumerator ListPending_ReportsRetryingAfterATransientFailure()
    {
        var job = new TestJob { Id = "retry-me", NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);

        yield return queue.RunOnceForTests();

        var record = queue.ListPending()[0];
        Assert.AreEqual(JobRecordState.Retrying, record.State);
        Assert.AreEqual(1, record.AttemptCount);
        Assert.IsTrue(record.NextAttemptAtUtc > DateTime.UtcNow, "backoff should still be pending");
    }

    [UnityTest]
    public IEnumerator ListPending_DropsAJobThatSucceeded()
    {
        var job = new TestJob { Id = "done", NextResult = JobResult.Success };
        queue.Enqueue(job);
        Assert.AreEqual(1, queue.ListPending().Count);

        yield return queue.RunOnceForTests();

        Assert.AreEqual(0, queue.ListPending().Count);
        Assert.AreEqual(0, queue.ListFailed().Count, "a success is not a failure");
        // GetStatus cannot tell this apart from an id that never existed; the
        // enumerations do not reproduce that ambiguity — they simply have no row.
        Assert.AreEqual(JobStatus.NotFound, queue.GetStatus("done"));
    }

    [Test]
    public void ListPending_SnapshotIsADetachedCopyOfTheJob()
    {
        var job = new TestJob { Id = "snap", NextResult = JobResult.PermanentFailure, ThrowOnExecute = true };
        queue.Enqueue(job);

        var snapshot = queue.ListPending()[0].Snapshot<TestJob>();

        Assert.IsNotNull(snapshot);
        Assert.AreNotSame(job, snapshot, "handing out the live job would let a caller mutate what is about to run");
        Assert.AreEqual("snap", snapshot.Id);
        Assert.AreEqual(JobResult.PermanentFailure, snapshot.NextResult);
        Assert.IsTrue(snapshot.ThrowOnExecute);

        snapshot.NextResult = JobResult.Success;
        Assert.AreEqual(JobResult.PermanentFailure, ((TestJob)queue.PendingForTests[0]).NextResult);
    }

    [Test]
    public void ListPending_SnapshotOfTheWrongTypeIsNull()
    {
        queue.Enqueue(new TestJob { Id = "typed" });

        var record = queue.ListPending()[0];
        Assert.IsNull(record.Snapshot<UnregisteredTestJob>(),
            "a caller that forgot to filter by Type must get nothing, not a half-populated job");
    }

    [Test]
    public void ListFailed_IsEmptyByDefault()
    {
        queue.Enqueue(new TestJob { Id = "fine" });
        Assert.AreEqual(0, queue.ListFailed().Count);
    }

    [UnityTest]
    public IEnumerator ListFailed_ContainsPermanentlyFailedJobs()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(0, queue.ListPending().Count);

        var failed = queue.ListFailed();
        Assert.AreEqual(1, failed.Count);
        Assert.AreEqual("dead", failed[0].Id);
        Assert.AreEqual(JobRecordState.Failed, failed[0].State);
        Assert.AreEqual(1, failed[0].AttemptCount);
        Assert.IsNotNull(failed[0].Snapshot<TestJob>());

        Assert.AreEqual(0, queue.ListFailed("Unregistered").Count, "type filter applies to failed rows too");
    }

    [UnityTest]
    public IEnumerator ListPending_SurvivesARestart()
    {
        queue.Enqueue(new TestJob { Id = "persisted", NextResult = JobResult.TransientFailure });
        yield return queue.RunOnceForTests();

        RecreateQueue();

        var records = queue.ListPending();
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("persisted", records[0].Id);
        Assert.AreEqual(1, records[0].AttemptCount);
        Assert.AreEqual(JobRecordState.Retrying, records[0].State);
        Assert.AreEqual(DateTimeKind.Utc, records[0].CreatedAtUtc.Kind);
    }

    // ---------- Persistence: survives queue destruction + recreation ----------

    [UnityTest]
    public IEnumerator Persistence_PendingJobsSurviveRestart()
    {
        var a = new TestJob { Id = "survive-a", NextResult = JobResult.Success };
        var b = new TestJob { Id = "survive-b", NextResult = JobResult.TransientFailure };
        queue.Enqueue(a);
        queue.Enqueue(b);

        // Run b once so it has an elevated attempt count + lastError + delayed nextAttempt
        a.NextAttemptAtUtc = DateTime.UtcNow.AddHours(1); // keep a from running
        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, b.AttemptCount);

        // "Restart" the queue
        RecreateQueue();

        Assert.AreEqual(2, queue.PendingCount, "both jobs should survive restart");

        TestJob recoveredA = null, recoveredB = null;
        foreach (var j in queue.PendingForTests)
        {
            var t = (TestJob)j;
            if (t.Id == "survive-a") recoveredA = t;
            if (t.Id == "survive-b") recoveredB = t;
        }
        Assert.IsNotNull(recoveredA);
        Assert.IsNotNull(recoveredB);
        Assert.AreEqual(0, recoveredA.AttemptCount);
        Assert.AreEqual(1, recoveredB.AttemptCount);
        Assert.AreEqual(DateTimeKind.Utc, recoveredB.NextAttemptAtUtc.Kind);
    }

    [UnityTest]
    public IEnumerator Persistence_FailedJobRecognizedAfterRestart()
    {
        var job = new TestJob { Id = "fail-survive", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        RecreateQueue();

        Assert.AreEqual(JobStatus.Failed, queue.GetStatus("fail-survive"));
        Assert.AreEqual(0, queue.PendingCount);
    }

    [UnityTest]
    public IEnumerator Persistence_AtomicWriteLeavesNoTmpFile()
    {
        var job = new TestJob { Id = "atomic-1", NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);
        yield return queue.RunOnceForTests();

        var leftover = Directory.GetFiles(queue.PendingDirForTests, "*.tmp");
        Assert.AreEqual(0, leftover.Length, "no .tmp files should remain after writes");
    }

    // ---------- Singleton ----------

    [Test]
    public void Singleton_InstanceIsSetAfterInit()
    {
        queue.EnsureInitialized();
        Assert.AreSame(queue, JobQueue.Instance);
    }

    [Test]
    public void Singleton_LazyInitDoesNotOverwriteExistingInstance()
    {
        queue.EnsureInitialized();
        Assert.AreSame(queue, JobQueue.Instance);

        var second = new GameObject("JobServices-dup");
        var dup = second.AddComponent<JobQueue>();
        dup.RegisterType<TestJob>();
        dup.IsOnlineOverride = () => true;
        dup.EnsureInitialized();

        Assert.AreSame(queue, JobQueue.Instance, "Instance should not be overwritten by a later EnsureInitialized");
        UnityEngine.Object.DestroyImmediate(second);
    }

    // ---------- Requeue ----------

    [UnityTest]
    public IEnumerator Requeue_MovesAFailedJobBackIntoPending()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, queue.ListFailed().Count);

        Assert.IsTrue(queue.Requeue("dead"));

        Assert.AreEqual(0, queue.ListFailed().Count, "it must not appear in both lists");
        Assert.IsFalse(File.Exists(Path.Combine(queue.FailedDirForTests, "dead.json")));

        var pending = queue.ListPending();
        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual("dead", pending[0].Id);
        Assert.IsTrue(File.Exists(Path.Combine(queue.PendingDirForTests, "dead.json")));
    }

    /// <summary>
    /// The attempt count resets because a requeue is a person asking for a fresh
    /// try, and job retry policies are written in terms of past failures —
    /// ReportSightingJob gives up once it has already refreshed its App Check token
    /// and been rejected again, so carrying the old state forward would send the
    /// retry straight back to failed/. The two backoff cursors and the network flag
    /// reset for the same reason.
    /// </summary>
    [UnityTest]
    public IEnumerator Requeue_ResetsTheAttemptCountAndClearsTheError_ButKeepsTheId()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, queue.ListFailed()[0].AttemptCount);

        Assert.IsTrue(queue.Requeue("dead"));

        var row = queue.ListPending()[0];
        Assert.AreEqual("dead", row.Id, "the id is the idempotency key the backend dedupes on");
        Assert.AreEqual(0, row.AttemptCount);
        Assert.AreEqual(JobRecordState.Queued, row.State);
        Assert.IsTrue(string.IsNullOrEmpty(row.LastError));
    }

    [Test]
    public void Requeue_ReturnsFalseForAnUnknownOrEmptyId()
    {
        Assert.IsFalse(queue.Requeue("no-such-job"));
        Assert.IsFalse(queue.Requeue(""));
        Assert.IsFalse(queue.Requeue(null));
    }

    /// <summary>A job still pending is not requeueable — only failed/ is read.</summary>
    [Test]
    public void Requeue_IgnoresAPendingJob()
    {
        var job = new TestJob { Id = "alive" };
        queue.Enqueue(job);
        Assert.IsFalse(queue.Requeue("alive"));
        Assert.AreEqual(1, queue.ListPending().Count, "and it is left alone");
    }

    [UnityTest]
    public IEnumerator Requeue_SurvivesARestart()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();
        Assert.IsTrue(queue.Requeue("dead"));

        var reloaded = RecreateQueue();
        Assert.AreEqual(1, reloaded.ListPending().Count);
        Assert.AreEqual(0, reloaded.ListFailed().Count);
    }

    // ---------- Backoff: network failures vs server failures ----------
    //
    // The two are on separate schedules and separate cursors. IsOnline() reports
    // "online" for any associated interface, so a captive portal or a dead resolver
    // lets a job run and collect a connection error — which used to walk it up the
    // 5s/30s/2min/10min/1h rungs and park the sighting an hour out over something
    // the user had already fixed.

    [UnityTest]
    public IEnumerator RunOnce_ANetworkTransientFailureUsesTheGentleSchedule()
    {
        var job = new TestJob { NextResult = JobResult.TransientFailure, NetworkFailure = true };
        queue.Enqueue(job);

        // 15s, 30s, 60s, 120s, 120s (capped) — NOT the escalating schedule.
        var expectedSeconds = new[] { 15, 30, 60, 120, 120 };

        for (int i = 0; i < expectedSeconds.Length; i++)
        {
            ((TestJob)queue.PendingForTests[0]).NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);

            DateTime before = DateTime.UtcNow;
            yield return queue.RunOnceForTests();

            double seconds = (job.NextAttemptAtUtc - before).TotalSeconds;
            int expected = expectedSeconds[i];
            Assert.That(seconds, Is.InRange(expected - 2, expected + 2),
                $"network failure {i + 1}: expected ~{expected}s backoff, got {seconds:F1}s");
            Assert.AreEqual(0, job.BackoffStep,
                "a dead uplink is not the server's fault and must not advance the server-side cursor");
        }

        Assert.AreEqual(expectedSeconds.Length, job.NetworkFailureStep);
    }

    [UnityTest]
    public IEnumerator RunOnce_AServerTransientFailureKeepsTheOldScheduleAndResetsTheNetworkCursor()
    {
        var job = new TestJob { NextResult = JobResult.TransientFailure, NetworkFailure = true };
        queue.Enqueue(job);

        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, job.NetworkFailureStep, "one network failure so far");
        Assert.AreEqual(0, job.BackoffStep);

        // Now a server actually answers.
        job.NetworkFailure = false;
        job.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);

        DateTime before = DateTime.UtcNow;
        yield return queue.RunOnceForTests();

        double seconds = (job.NextAttemptAtUtc - before).TotalSeconds;
        Assert.That(seconds, Is.InRange(3, 7),
            $"the first server rung is 5s, not the 30s the network cursor had reached; got {seconds:F1}s");
        Assert.AreEqual(1, job.BackoffStep);
        Assert.AreEqual(0, job.NetworkFailureStep,
            "a server answering makes whatever the network was doing before irrelevant");
    }

    // ---------- Pull-forward: connectivity regained, and relaunch ----------

    [UnityTest]
    public IEnumerator OnConnectivityRegained_PullsANetworkDelayedJobForwardAndPersistsIt()
    {
        var net = new TestJob { Id = "net", NextResult = JobResult.TransientFailure, NetworkFailure = true };
        queue.Enqueue(net);
        yield return queue.RunOnceForTests();

        var srv = new TestJob { Id = "srv", NextResult = JobResult.TransientFailure, NetworkFailure = false };
        queue.Enqueue(srv);
        yield return queue.RunOnceForTests();
        Assert.AreEqual(1, srv.AttemptCount, "the server-failing job must have run too");

        // Park both far out so the pull-forward is visible.
        DateTime far = DateTime.UtcNow.AddHours(1);
        net.NextAttemptAtUtc = far;
        srv.NextAttemptAtUtc = far;

        DateTime before = DateTime.UtcNow;
        Assert.AreEqual(1, queue.OnConnectivityRegained(), "only the network-delayed job is pulled");

        Assert.That(net.NextAttemptAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow), "it is due now");
        Assert.AreEqual(0, net.NetworkFailureStep, "and the gentle cursor starts over on the new network");
        Assert.AreEqual(far.Ticks, srv.NextAttemptAtUtc.Ticks,
            "connectivity returning says nothing about whether the backend stopped erroring");

        // The pull-forward has to survive an app kill, so it must be on disk.
        string json = File.ReadAllText(Path.Combine(queue.PendingDirForTests, "net.json"));
        var env = JsonUtility.FromJson<JobEnvelope>(json);
        Assert.AreEqual(net.NextAttemptAtUtc.Ticks, env.nextAttemptAtUtcTicks);
        Assert.IsTrue(env.nextAttemptAtUtcTicks <= DateTime.UtcNow.Ticks);
        Assert.IsTrue(env.nextAttemptAtUtcTicks >= before.Ticks);
    }

    [Test]
    public void OnConnectivityRegained_LeavesAJobThatIsAlreadyDueAlone()
    {
        var job = new TestJob { Id = "due", NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);
        job.LastFailureWasNetwork = true;
        job.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-5);
        DateTime was = job.NextAttemptAtUtc;

        Assert.AreEqual(0, queue.OnConnectivityRegained());
        Assert.AreEqual(was.Ticks, job.NextAttemptAtUtc.Ticks);
    }

    /// <summary>
    /// Relaunching the app is the user's clearest "send it now" signal, so a
    /// network-caused delay is clamped to a few seconds on load. A server-caused
    /// one is not: restarting the app is no evidence the backend recovered.
    /// </summary>
    [Test]
    public void LoadPendingFromDisk_ClampsANetworkCausedDelayButNotAServerOne()
    {
        DateTime created = DateTime.UtcNow.AddMinutes(-10);
        DateTime far = DateTime.UtcNow.AddHours(1);
        WriteEnvelope("net-delayed", created, far, lastFailureWasNetwork: true, networkFailureStep: 3);
        WriteEnvelope("srv-delayed", created.AddSeconds(1), far, lastFailureWasNetwork: false, backoffStep: 4);

        DateTime before = DateTime.UtcNow;
        RecreateQueue();

        var net = FindPending("net-delayed");
        var srv = FindPending("srv-delayed");
        Assert.IsNotNull(net);
        Assert.IsNotNull(srv);

        double seconds = (net.NextAttemptAtUtc - before).TotalSeconds;
        Assert.That(seconds, Is.InRange(0, 20),
            $"a network-caused delay should be clamped to ~5s from launch, got {seconds:F1}s");
        Assert.AreEqual(0, net.NetworkFailureStep,
            "the escalation was earned on a network that is very likely gone, so the new one must not inherit its rung");
        Assert.AreEqual(far.Ticks, srv.NextAttemptAtUtc.Ticks,
            "a server backoff keeps its rung across a relaunch");
        Assert.AreEqual(4, srv.BackoffStep, "and keeps its cursor too");
    }

    /// <summary>
    /// The clamp also has to be on disk, or a launch that is killed before the first
    /// attempt reverts to the old rung.
    /// </summary>
    [Test]
    public void LoadPendingFromDisk_PersistsTheClampAndTheCursorReset()
    {
        WriteEnvelope("net-delayed", DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddHours(1),
            lastFailureWasNetwork: true, networkFailureStep: 3);

        RecreateQueue();

        var env = JsonUtility.FromJson<JobEnvelope>(
            File.ReadAllText(Path.Combine(queue.PendingDirForTests, "net-delayed.json")));
        Assert.AreEqual(0, env.networkFailureStep);
        Assert.AreEqual(FindPending("net-delayed").NextAttemptAtUtc.Ticks, env.nextAttemptAtUtcTicks);
    }

    [Test]
    public void LoadPendingFromDisk_DoesNotClampANetworkDelayThatIsAlreadyImminent()
    {
        DateTime soon = DateTime.UtcNow.AddSeconds(10);
        WriteEnvelope("soon", DateTime.UtcNow.AddMinutes(-1), soon, lastFailureWasNetwork: true);

        RecreateQueue();

        Assert.AreEqual(soon.Ticks, FindPending("soon").NextAttemptAtUtc.Ticks,
            "there is nothing to gain from rewriting a delay that is about to elapse anyway");
    }

    // ---------- Envelope compatibility ----------

    /// <summary>
    /// The three fields added for the backoff split are absent from every job file
    /// the installed build has already written. JsonUtility leaves a missing key at
    /// its default, and false/0 is exactly the "has never failed" state — which is
    /// the only reason envelope fields may be added but never renamed or removed.
    /// </summary>
    [Test]
    public void LoadPendingFromDisk_AFileFromTheOldBuildLoadsWithTheNewFieldsAtTheirDefaults()
    {
        queue.EnsureInitialized();
        DateTime created = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        DateTime next = new DateTime(2026, 9, 1, 10, 0, 5, DateTimeKind.Utc);
        string legacy =
            "{\"type\":\"TestJob\",\"id\":\"legacy-1\",\"attemptCount\":3," +
            "\"nextAttemptAtUtcTicks\":" + next.Ticks + "," +
            "\"createdAtUtcTicks\":" + created.Ticks + "," +
            "\"lastError\":\"500 boom\"," +
            "\"data\":\"{\\\"nextResult\\\":1}\"}";
        File.WriteAllText(Path.Combine(queue.PendingDirForTests, "legacy-1.json"), legacy);

        RecreateQueue();

        var job = FindPending("legacy-1");
        Assert.IsNotNull(job, "a job queued by the old build must survive the update");
        Assert.AreEqual(3, job.AttemptCount);
        Assert.AreEqual("500 boom", job.LastError);
        Assert.AreEqual(next.Ticks, job.NextAttemptAtUtc.Ticks);
        Assert.AreEqual(created.Ticks, job.CreatedAtUtc.Ticks);
        Assert.IsFalse(job.LastFailureWasNetwork);
        Assert.AreEqual(0, job.BackoffStep);
        Assert.AreEqual(0, job.NetworkFailureStep);
    }

    // ---------- FIFO order after a reload ----------

    /// <summary>
    /// Directory.GetFiles returns filesystem order, which is neither enqueue order
    /// nor stable across platforms — while ListPending's contract, and the pending
    /// feed built on it, promise FIFO. The ids here are chosen so that any
    /// name-based ordering is the exact reverse of creation order.
    /// </summary>
    [Test]
    public void LoadPendingFromDisk_RestoresFifoOrderByCreationRegardlessOfFileOrder()
    {
        DateTime t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        DateTime due = t0.AddSeconds(30);
        WriteEnvelope("zzz-oldest", t0, due);
        WriteEnvelope("mmm-middle", t0.AddMinutes(1), due);
        WriteEnvelope("aaa-newest", t0.AddMinutes(2), due);

        RecreateQueue();

        var ids = new List<string>();
        foreach (var r in queue.ListPending()) ids.Add(r.Id);
        CollectionAssert.AreEqual(new[] { "zzz-oldest", "mmm-middle", "aaa-newest" }, ids);
    }

    [Test]
    public void LoadPendingFromDisk_BreaksATieOnIdSoTheOrderIsStable()
    {
        DateTime t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        DateTime due = t0.AddSeconds(30);
        WriteEnvelope("b-same-tick", t0, due);
        WriteEnvelope("a-same-tick", t0, due);

        RecreateQueue();

        var ids = new List<string>();
        foreach (var r in queue.ListPending()) ids.Add(r.Id);
        CollectionAssert.AreEqual(new[] { "a-same-tick", "b-same-tick" }, ids,
            "two jobs enqueued inside the same tick must still come back in one fixed order");
    }

    [UnityTest]
    public IEnumerator LoadPendingFromDisk_IsRefusedWhileAJobIsExecuting()
    {
        // Reloading mid-run would clear `pending` and orphan the instance the loop
        // is executing: HandleResult's Remove would no-op and the copy on disk
        // (written before the attempt) would be retried from scratch.
        var job = new TestJob { Id = "mid-flight", NextResult = JobResult.TransientFailure };
        queue.Enqueue(job);
        job.ReloadDuringExecute = queue;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*skipping the reload.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, queue.PendingCount);
        Assert.AreSame(job, queue.PendingForTests[0], "the live instance must still be the one in the list");
        Assert.AreEqual(1, job.AttemptCount);
    }

    // ---------- JobCompleted fan-out ----------

    /// <summary>
    /// The subscriber chain reaches the app layer's sightings feed, which rebuilds a
    /// UI Toolkit tree. A plain Invoke ran it on the run loop's own stack, so one
    /// throw skipped every later subscriber AND terminated the retry coroutine for
    /// the rest of the process, with `running` stuck true.
    /// </summary>
    [UnityTest]
    public IEnumerator JobCompleted_AThrowingSubscriberDoesNotDenyTheOthersOrWedgeTheQueue()
    {
        var first = new TestJob { Id = "boom", NextResult = JobResult.Success };
        var second = new TestJob { Id = "after", NextResult = JobResult.Success };
        queue.Enqueue(first);
        queue.Enqueue(second);

        int laterSubscriberCalls = 0;
        queue.JobCompleted += (id, r) => throw new Exception("simulated subscriber failure");
        queue.JobCompleted += (id, r) => laterSubscriberCalls++;

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*JobCompleted subscriber threw.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, laterSubscriberCalls, "one broken subscriber must not deny the others");
        Assert.IsFalse(queue.RunningForTests, "`running` must be released even when the fan-out throws");
        Assert.AreEqual(1, queue.PendingCount, "the first job still completed normally");

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*JobCompleted subscriber threw.*"));
        yield return queue.RunOnceForTests();

        Assert.AreEqual(1, second.Calls, "the next job must still be executed");
        Assert.AreEqual(0, queue.PendingCount);
        Assert.AreEqual(2, laterSubscriberCalls);
    }

    // ---------- Permanent failure: archive before delete ----------

    [UnityTest]
    public IEnumerator PermanentFailure_EndsUpArchivedAndOutOfPending()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        Assert.IsTrue(File.Exists(Path.Combine(queue.FailedDirForTests, "dead.json")));
        Assert.IsFalse(File.Exists(Path.Combine(queue.PendingDirForTests, "dead.json")));
    }

    /// <summary>
    /// The archive is written BEFORE the pending copy is deleted. Nothing can kill
    /// the process between the two inside a test, so the ordering is proved by its
    /// consequence instead: make the archive fail and the pending file must still be
    /// there. Under the old delete-then-write order the sighting would now be gone
    /// from both directories.
    /// </summary>
    [UnityTest]
    public IEnumerator PermanentFailure_WhenTheArchiveCannotBeWrittenThePendingFileIsKept()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        Directory.Delete(queue.FailedDirForTests, recursive: true); // make failed/ unwritable

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*failed to persist job.*"));
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*could not archive job.*"));
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        Assert.IsTrue(File.Exists(Path.Combine(queue.PendingDirForTests, "dead.json")),
            "losing the archive must not also lose the only remaining copy of the sighting");
        Assert.AreEqual(0, queue.PendingCount, "it did permanently fail, so it leaves the run loop");
        Assert.IsFalse(queue.RunningForTests, "and an IO error must not wedge the queue");
    }

    // ---------- ReportSightingJob's 401 rule ----------
    //
    // The whole point of FIX 4: the rule is keyed on whether a token refresh has
    // been tried, not on AttemptCount, which connection errors inflate.

    [Test]
    public void ReportSightingJob_A401BuysOneTokenRefreshEvenWhenAttemptCountIsHigh()
    {
        var job = new ReportSightingJob();
        job.AttemptCount = 9; // several connection errors already burned the counter

        Assert.IsFalse(job.TokenRefreshAttempted);
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(401),
            "the first genuine 401 is an aged App Check token and must retry — the old AttemptCount == 1 rule filed it as permanent here");
        Assert.IsTrue(job.TokenRefreshAttempted, "and the next attempt must ask for a fresh token");

        Assert.AreEqual(JobResult.PermanentFailure, job.ClassifyHttpFailure(401),
            "a freshly minted token being rejected means the install is genuinely deregistered");
    }

    [Test]
    public void ReportSightingJob_TheOtherStatusesClassifyExactlyAsBefore()
    {
        var job = new ReportSightingJob();
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(500));
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(503));
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(408));
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(429));
        Assert.AreEqual(JobResult.TransientFailure, job.ClassifyHttpFailure(0));
        Assert.AreEqual(JobResult.PermanentFailure, job.ClassifyHttpFailure(400));
        Assert.AreEqual(JobResult.PermanentFailure, job.ClassifyHttpFailure(415));
        Assert.IsFalse(job.TokenRefreshAttempted, "only a 401 touches the refresh flag");
    }

    // ---------- Enqueue / Requeue cannot half-commit ----------

    /// <summary>
    /// A write failure at enqueue must come back as <c>false</c> — the channel
    /// <c>SightingReportsAdapter.Submit</c> already handles, by keeping the user on
    /// the form with everything they typed and deleting the photo copy it made — not
    /// as an exception escaping into a UI Toolkit click handler.
    ///
    /// <para>Forced here with an id containing path separators, so Path.Combine
    /// points at a directory that does not exist. <b>Production cannot reach that
    /// particular cause:</b> nothing sets <c>Job.Id</c> (the adapter leaves it null),
    /// so Enqueue assigns <c>Guid.NewGuid().ToString("N")</c> — 32 hex characters
    /// with no separator. The reachable causes are a full disk and a permissions
    /// change, neither of which a test can force cheaply; this drives the same
    /// branch.</para>
    /// </summary>
    [Test]
    public void Enqueue_ReturnsFalseAndRollsBackWhenTheJobCannotBePersisted()
    {
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*failed to persist job.*"));

        Assert.IsFalse(queue.Enqueue(new TestJob { Id = "no/such/dir/job" }));
        Assert.AreEqual(0, queue.PendingCount,
            "an unpersisted job must not linger in memory: it would be attempted this session and then vanish on the next launch, after the caller was told it was taken");
    }

    /// <summary>
    /// Same rollback on the requeue side, and the failed copy must survive: the row
    /// stays in the feed and the person can press retry again. The bad id is planted
    /// in the envelope's own <c>id</c> field, which is what WriteJob names the file
    /// after, while the file on disk keeps a normal name.
    /// </summary>
    [UnityTest]
    public IEnumerator Requeue_ReturnsFalseAndLeavesTheFailedCopyWhenItCannotBePersisted()
    {
        var job = new TestJob { Id = "dead", NextResult = JobResult.PermanentFailure };
        queue.Enqueue(job);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*permanently failed.*"));
        yield return queue.RunOnceForTests();

        string failedPath = Path.Combine(queue.FailedDirForTests, "dead.json");
        var env = JsonUtility.FromJson<JobEnvelope>(File.ReadAllText(failedPath));
        env.id = "no/such/dir/dead";
        File.WriteAllText(failedPath, JsonUtility.ToJson(env));

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*failed to persist job.*"));
        Assert.IsFalse(queue.Requeue("dead"));

        Assert.AreEqual(0, queue.PendingCount, "nothing half-moved into the queue");
        Assert.IsTrue(File.Exists(failedPath), "and the only copy of the sighting is still on disk");
    }

    // ---------- ReportSightingJob: the App Check token wait is bounded ----------

    /// <summary>
    /// An unbounded wait for the token was the same defect as a SendWebRequest with
    /// no timeout, and the last single point of wedge in the queue: concurrency is 1,
    /// so `running` stuck true kills every other queued sighting, and the loop
    /// watchdog cannot see it because a stuck job looks exactly like a slow upload.
    /// </summary>
    [UnityTest]
    public IEnumerator ReportSightingJob_GivesUpOnAHungTokenFetchInsteadOfWedgingTheQueue()
    {
        string imagePath = Path.Combine(tempRoot, "photo.jpg");
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        // Never completed — the shape of Firebase's dependency check hanging.
        var neverCompletes = new TaskCompletionSource<string>();
        AppCheckTokenProvider.TokenOverride = _ => neverCompletes.Task;
        ReportSightingJob.TokenWaitTimeoutSeconds = 0.2f;

        var job = new ReportSightingJob
        {
            Id = "hung-token",
            // Never reached: the job gives up before it builds a request.
            Url = "http://127.0.0.1:1/unreachable",
            ImagePath = imagePath,
            MimeType = "image/jpeg",
            IdempotencyKey = "hung-token",
        };
        Assert.IsTrue(queue.Enqueue(job));

        DateTime before = DateTime.UtcNow;
        yield return queue.RunOnceForTests();

        Assert.IsFalse(queue.RunningForTests, "the queue must be free to run the next job");
        Assert.AreEqual(1, queue.PendingCount,
            "a hung Firebase init is transient — the retry that matters is the one on the next launch");
        Assert.AreEqual(1, job.AttemptCount);
        StringAssert.Contains("timed out", job.LastError);
        Assert.IsTrue(job.LastFailureWasNetwork,
            "filed as a network failure so it gets the gentle schedule and is pulled forward on relaunch");

        double seconds = (job.NextAttemptAtUtc - before).TotalSeconds;
        Assert.That(seconds, Is.InRange(13, 18),
            $"the gentle schedule's first rung is 15s, got {seconds:F1}s");
        Assert.AreEqual(0, job.BackoffStep, "and the server-side cursor is untouched");
    }

    // ---------- Test-only Job types ----------

    public class TestJob : Job
    {
        public override string Type => "TestJob";

        public JobResult NextResult = JobResult.Success;
        public int Calls;
        public bool ThrowOnExecute;
        public bool? RequiresNetworkOverride;

        /// <summary>What this job reports as <see cref="Job.LastFailureWasNetwork"/>.
        /// The real job types derive it from the UnityWebRequest result or a stall
        /// abort, neither of which an EditMode test can produce, so the test double
        /// states it outright.</summary>
        public bool NetworkFailure;

        /// <summary>When set, the job asks the queue to reload from disk from inside
        /// Execute — the one way a test can reach the mid-run reload guard.</summary>
        public JobQueue ReloadDuringExecute;

        public override bool RequiresNetwork => RequiresNetworkOverride ?? base.RequiresNetwork;

        [Serializable]
        private struct Data
        {
            public int nextResult;
            public bool throwOnExecute;
            public bool hasNetworkOverride;
            public bool networkOverrideValue;
            public bool networkFailure;
        }

        public override IEnumerator Execute(Action<JobResult> setResult)
        {
            Calls++;
            if (ThrowOnExecute) throw new Exception("simulated failure");
            if (ReloadDuringExecute != null) ReloadDuringExecute.LoadPendingFromDisk();
            LastFailureWasNetwork = NetworkFailure;
            setResult(NextResult);
            yield break;
        }

        protected internal override string SerializeData()
        {
            return JsonUtility.ToJson(new Data
            {
                nextResult = (int)NextResult,
                throwOnExecute = ThrowOnExecute,
                hasNetworkOverride = RequiresNetworkOverride.HasValue,
                networkOverrideValue = RequiresNetworkOverride ?? false,
                networkFailure = NetworkFailure,
            });
        }

        protected internal override void DeserializeData(string data)
        {
            var d = JsonUtility.FromJson<Data>(data);
            NextResult = (JobResult)d.nextResult;
            ThrowOnExecute = d.throwOnExecute;
            RequiresNetworkOverride = d.hasNetworkOverride ? d.networkOverrideValue : (bool?)null;
            NetworkFailure = d.networkFailure;
        }
    }

    public class OtherTestJob : Job
    {
        public override string Type => "OtherTestJob";
        public override IEnumerator Execute(Action<JobResult> setResult)
        {
            setResult(JobResult.Success);
            yield break;
        }
        protected internal override string SerializeData() => "{}";
        protected internal override void DeserializeData(string data) { }
    }

    public class UnregisteredTestJob : Job
    {
        public override string Type => "Unregistered";
        public override IEnumerator Execute(Action<JobResult> setResult) { yield break; }
        protected internal override string SerializeData() => "{}";
        protected internal override void DeserializeData(string data) { }
    }
}
