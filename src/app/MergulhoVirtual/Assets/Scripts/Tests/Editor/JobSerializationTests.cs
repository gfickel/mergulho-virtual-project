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
