using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class JobQueueTests
{
    private string tempRoot;
    private GameObject host;
    private JobQueue queue;

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
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) UnityEngine.Object.DestroyImmediate(host);
        JobQueue.Instance = null;
        JobQueue.TestRootOverride = null;
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
        catch { }
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

    // ---------- Test-only Job types ----------

    public class TestJob : Job
    {
        public override string Type => "TestJob";

        public JobResult NextResult = JobResult.Success;
        public int Calls;
        public bool ThrowOnExecute;
        public bool? RequiresNetworkOverride;

        public override bool RequiresNetwork => RequiresNetworkOverride ?? base.RequiresNetwork;

        [Serializable]
        private struct Data
        {
            public int nextResult;
            public bool throwOnExecute;
            public bool hasNetworkOverride;
            public bool networkOverrideValue;
        }

        public override IEnumerator Execute(Action<JobResult> setResult)
        {
            Calls++;
            if (ThrowOnExecute) throw new Exception("simulated failure");
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
            });
        }

        protected internal override void DeserializeData(string data)
        {
            var d = JsonUtility.FromJson<Data>(data);
            NextResult = (JobResult)d.nextResult;
            ThrowOnExecute = d.throwOnExecute;
            RequiresNetworkOverride = d.hasNetworkOverride ? d.networkOverrideValue : (bool?)null;
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
