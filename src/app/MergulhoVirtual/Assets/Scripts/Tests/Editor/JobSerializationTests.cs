using System;
using NUnit.Framework;
using UnityEngine;

public class JobSerializationTests
{
    [Test]
    public void HttpPostJob_RoundtripPreservesAllFields()
    {
        var original = new HttpPostJob
        {
            Url = "https://example.com/api/register",
            JsonBody = "{\"name\":\"Mergulhador\",\"age\":42}",
            IdempotencyKeyHeader = "key-abc-123",
        };

        string serialized = original.SerializeData();
        var roundtripped = new HttpPostJob();
        roundtripped.DeserializeData(serialized);

        Assert.AreEqual(original.Url, roundtripped.Url);
        Assert.AreEqual(original.JsonBody, roundtripped.JsonBody);
        Assert.AreEqual(original.IdempotencyKeyHeader, roundtripped.IdempotencyKeyHeader);
    }

    [Test]
    public void HttpPostJob_RoundtripWithMinimalFields()
    {
        var original = new HttpPostJob { Url = "https://example.com/x" };
        var roundtripped = new HttpPostJob();
        roundtripped.DeserializeData(original.SerializeData());

        Assert.AreEqual("https://example.com/x", roundtripped.Url);
        Assert.IsTrue(string.IsNullOrEmpty(roundtripped.JsonBody));
        Assert.IsTrue(string.IsNullOrEmpty(roundtripped.IdempotencyKeyHeader));
    }

    [Test]
    public void HttpPostJob_TypeIsStable()
    {
        Assert.AreEqual("HttpPost", new HttpPostJob().Type);
    }

    [Test]
    public void FileDownloadJob_RoundtripPreservesAllFields()
    {
        var original = new FileDownloadJob
        {
            Url = "https://cdn.example.com/video.mp4",
            DestPath = "/tmp/video.mp4",
            ExpectedSha256 = "a3b1c2d4",
        };

        var roundtripped = new FileDownloadJob();
        roundtripped.DeserializeData(original.SerializeData());

        Assert.AreEqual(original.Url, roundtripped.Url);
        Assert.AreEqual(original.DestPath, roundtripped.DestPath);
        Assert.AreEqual(original.ExpectedSha256, roundtripped.ExpectedSha256);
    }

    [Test]
    public void FileDownloadJob_TypeIsStable()
    {
        Assert.AreEqual("FileDownload", new FileDownloadJob().Type);
    }

    // ---------- ReportSightingJob ----------

    [Test]
    public void ReportSightingJob_TypeIsStable()
    {
        // The discriminator is written into every persisted envelope — changing it
        // orphans every job already on a user's disk.
        Assert.AreEqual("ReportSighting", new ReportSightingJob().Type);
        Assert.AreEqual(ReportSightingJob.JobType, new ReportSightingJob().Type);
    }

    [Test]
    public void ReportSightingJob_RoundtripPreservesAllFields()
    {
        var original = new ReportSightingJob
        {
            Url = "https://mergulhovirtual.dev/api/v1/avistamentos",
            ImagePath = "/data/sightings/abc123.jpg",
            MimeType = "image/jpeg",
            BeachName = "Sueste Beach",
            IsoTimestamp = "2026-09-28T12:32:00.0000000Z",
            SpeciesGuess = "Tubarão-limão",
            Notes = "perto do costão",
            IdempotencyKey = "abc123",
            SpeciesKey = "lemon_shark",
            SizeBucket = "1_2m",
            Behaviours = new[] { "calmo", "alimentacao" },
            ReporterName = "Maria",
            ReporterEmail = "maria@example.com",
            ReporterProfile = "condutor",
        };

        var roundtripped = new ReportSightingJob();
        roundtripped.DeserializeData(original.SerializeData());

        Assert.AreEqual(original.Url, roundtripped.Url);
        Assert.AreEqual(original.ImagePath, roundtripped.ImagePath);
        Assert.AreEqual(original.MimeType, roundtripped.MimeType);
        Assert.AreEqual(original.BeachName, roundtripped.BeachName);
        Assert.AreEqual(original.IsoTimestamp, roundtripped.IsoTimestamp);
        Assert.AreEqual(original.SpeciesGuess, roundtripped.SpeciesGuess);
        Assert.AreEqual(original.Notes, roundtripped.Notes);
        Assert.AreEqual(original.IdempotencyKey, roundtripped.IdempotencyKey);
        Assert.AreEqual(original.SpeciesKey, roundtripped.SpeciesKey);
        Assert.AreEqual(original.SizeBucket, roundtripped.SizeBucket);
        CollectionAssert.AreEqual(original.Behaviours, roundtripped.Behaviours);
        Assert.AreEqual(original.ReporterName, roundtripped.ReporterName);
        Assert.AreEqual(original.ReporterEmail, roundtripped.ReporterEmail);
        Assert.AreEqual(original.ReporterProfile, roundtripped.ReporterProfile);
    }

    [Test]
    public void ReportSightingJob_BehavioursNeverComeBackNull()
    {
        var roundtripped = new ReportSightingJob();
        roundtripped.DeserializeData(new ReportSightingJob().SerializeData());

        Assert.IsNotNull(roundtripped.Behaviours, "Execute iterates this without a null check per element");
        Assert.AreEqual(0, roundtripped.Behaviours.Length);
    }

    [Test]
    public void ReportSightingJob_PayloadFromTheShippedBuildStillLoads()
    {
        // Byte-for-byte what the installed build writes: no Slice 3 keys at all.
        // JsonUtility leaves a missing field at its default, which is the ONLY
        // reason new fields may be added but never renamed or removed — a job
        // queued by that build must survive the update intact.
        const string legacy =
            "{\"url\":\"https://mergulhovirtual.dev/api/v1/avistamentos\"," +
            "\"imagePath\":\"/data/sightings/abc123.jpg\"," +
            "\"mimeType\":\"image/jpeg\"," +
            "\"beachName\":\"Sueste Beach\"," +
            "\"isoTimestamp\":\"2026-06-05T12:00:00.0000000Z\"," +
            "\"speciesGuess\":\"Tubarão-limão\"," +
            "\"notes\":\"\"," +
            "\"idempotencyKey\":\"abc123\"}";

        var job = new ReportSightingJob();
        job.DeserializeData(legacy);

        Assert.AreEqual("/data/sightings/abc123.jpg", job.ImagePath);
        Assert.AreEqual("Sueste Beach", job.BeachName);
        Assert.AreEqual("Tubarão-limão", job.SpeciesGuess);
        Assert.AreEqual("abc123", job.IdempotencyKey, "the idempotency key must survive — it is the server-side dedupe");
        Assert.IsTrue(string.IsNullOrEmpty(job.SpeciesKey));
        Assert.IsTrue(string.IsNullOrEmpty(job.SizeBucket));
        Assert.IsTrue(string.IsNullOrEmpty(job.ReporterEmail));
        Assert.IsNotNull(job.Behaviours);
        Assert.AreEqual(0, job.Behaviours.Length);
    }

    [Test]
    public void Envelope_RoundtripPreservesBaseFields()
    {
        var env = new JobEnvelope
        {
            type = "HttpPost",
            id = "abc123",
            attemptCount = 7,
            nextAttemptAtUtcTicks = 638000000000000000L,
            createdAtUtcTicks = 637000000000000000L,
            lastError = "500 Internal Server Error",
            data = "{\"url\":\"x\"}",
        };

        string json = JsonUtility.ToJson(env);
        var back = JsonUtility.FromJson<JobEnvelope>(json);

        Assert.AreEqual(env.type, back.type);
        Assert.AreEqual(env.id, back.id);
        Assert.AreEqual(env.attemptCount, back.attemptCount);
        Assert.AreEqual(env.nextAttemptAtUtcTicks, back.nextAttemptAtUtcTicks);
        Assert.AreEqual(env.createdAtUtcTicks, back.createdAtUtcTicks);
        Assert.AreEqual(env.lastError, back.lastError);
        Assert.AreEqual(env.data, back.data);
    }

    [Test]
    public void Envelope_DateTimeRoundtripIsLossless()
    {
        var original = new DateTime(2026, 5, 2, 14, 30, 15, DateTimeKind.Utc);
        long ticks = original.Ticks;
        var recovered = new DateTime(ticks, DateTimeKind.Utc);
        Assert.AreEqual(original, recovered);
        Assert.AreEqual(DateTimeKind.Utc, recovered.Kind);
    }
}
