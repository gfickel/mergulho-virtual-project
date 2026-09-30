using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Uploads a single shark sighting (photo + metadata) as a multipart/form-data POST.
///
/// The image file is read raw (no decode/re-encode), so EXIF is preserved end-to-end.
/// The caller is responsible for placing the image at <see cref="ImagePath"/> in a
/// stable location (e.g. persistentDataPath/sightings/&lt;guid&gt;.jpg) — this job
/// only owns the file from enqueue onward and deletes it on success.
/// </summary>
public class ReportSightingJob : Job
{
    /// <summary>The <see cref="Job.Type"/> discriminator, so callers can filter
    /// <see cref="JobQueue.ListPending"/> without repeating the string.</summary>
    public const string JobType = "ReportSighting";

    public string Url;
    public string ImagePath;
    public string MimeType;        // e.g. "image/jpeg" — server uses this when storing
    public string BeachName;
    public string IsoTimestamp;    // RFC3339; user-provided or derived from EXIF/now
    public string SpeciesGuess;    // optional — pt-BR label; the server stores it as nome_popular
    public string Notes;           // optional
    public string IdempotencyKey;

    // ---- Slice 3 fields (DESIGN_IMPLEMENTATION.md §5.2 / §8.5) --------------
    // Added, never renamed: JsonUtility leaves a missing field at its default, so
    // a job queued by an older build still reloads intact — and because every part
    // below is only appended when non-empty, such a job also produces a multipart
    // body byte-identical to what the shipped build sends.

    /// <summary>Species catalog key (the AnimalDef asset name, "lemon_shark").
    /// Sent alongside <see cref="SpeciesGuess"/>, which keeps carrying the label
    /// the backend already stores — the key is what a future backend can join on.</summary>
    public string SpeciesKey;

    /// <summary>Size bucket key — one of <c>lt_1m</c> / <c>1_2m</c> / <c>gt_3m</c>.</summary>
    public string SizeBucket;

    /// <summary>Observed-behaviour keys; sent as one repeated <c>behaviours</c>
    /// part each. Never null after a round-trip (JsonUtility materializes an
    /// empty array).</summary>
    public string[] Behaviours;

    /// <summary>Optional reporter name.</summary>
    public string ReporterName;

    /// <summary>
    /// Optional reporter e-mail. <b>Personal data</b> the moment anything stores
    /// it: it is carried here and sent as a form part, and nothing in the app
    /// persists it anywhere else (no PlayerPrefs, no prefill). A retention /
    /// privacy-notice decision is owed before the backend writes it to Firestore.
    /// </summary>
    public string ReporterEmail;

    /// <summary>Reporter profile key — <c>turista</c> or <c>condutor</c>.</summary>
    public string ReporterProfile;

    /// <summary>
    /// Whether this job has already asked <see cref="AppCheckTokenProvider"/> for a
    /// FRESH token (bypassing the SDK's hourly cache) because the backend rejected
    /// the cached one. Persisted with the rest of the payload, so the decision
    /// survives the app being killed mid-queue.
    ///
    /// <para><b>Why this, and not the attempt count.</b> The 401 rule used to read
    /// <see cref="Job.AttemptCount"/>: transient on attempt 1, permanent after. But
    /// that counter also ticks for every connection error, and a queued sighting on
    /// a bad network collects those freely — so the FIRST genuine 401, which is
    /// almost always an App Check token that simply aged past its hour while the
    /// job waited, arrived with the counter already at 3 or 4 and was filed as
    /// permanent. The sighting went to <c>failed/</c> for exactly the reason the
    /// retry was written to handle. Keyed on this flag instead, the count of network
    /// failures is irrelevant: the first 401 always buys one refresh.</para>
    /// </summary>
    public bool TokenRefreshAttempted;

    /// <summary>
    /// How long one attempt waits for an App Check token before giving up on that
    /// attempt. Minting a token is a sub-second operation, so 30s is far past any
    /// legitimate latency; the realistic failure is Firebase's dependency check
    /// hanging on device, which does not return at all.
    ///
    /// <para>Mutable rather than const so a test can shrink it — the same seam
    /// pattern as <c>JobQueue.IsOnlineOverride</c> and
    /// <c>AppCheckTokenProvider.TokenOverride</c>. Nothing in the app writes it.</para>
    /// </summary>
    internal static float TokenWaitTimeoutSeconds = 30f;

    public override string Type => JobType;

    [Serializable]
    private struct Data
    {
        public string url;
        public string imagePath;
        public string mimeType;
        public string beachName;
        public string isoTimestamp;
        public string speciesGuess;
        public string notes;
        public string idempotencyKey;
        public string speciesKey;
        public string sizeBucket;
        public string[] behaviours;
        public string reporterName;
        public string reporterEmail;
        public string reporterProfile;
        public bool tokenRefreshAttempted;
    }

    public override IEnumerator Execute(Action<JobResult> setResult)
    {
        if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
        {
            LastError = "image missing: " + ImagePath;
            LastFailureWasNetwork = false;
            setResult(JobResult.PermanentFailure);
            yield break;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(ImagePath); }
        catch (Exception e)
        {
            LastError = "read: " + e.Message;
            LastFailureWasNetwork = false; // local disk, not the radio
            setResult(JobResult.TransientFailure);
            yield break;
        }

        string fileName = Path.GetFileName(ImagePath);
        string mime = string.IsNullOrEmpty(MimeType) ? "application/octet-stream" : MimeType;

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("photo", bytes, fileName, mime),
        };
        AddIfPresent(form, "beach", BeachName);
        AddIfPresent(form, "timestamp", IsoTimestamp);
        AddIfPresent(form, "species_guess", SpeciesGuess);
        AddIfPresent(form, "notes", Notes);

        // Slice 3 parts. The deployed endpoint declares none of these and FastAPI
        // ignores undeclared multipart parts (verified against the exact signature
        // of api/endpoints/avistamentos_api.create_avistamento: extra parts, and
        // repeated parts for a name it does not declare, still return 201), so
        // sending them today is safe and they start being persisted the moment the
        // backend adds the matching Form() parameters. Empty values are skipped,
        // so a submission that fills none of them is byte-identical to today's.
        AddIfPresent(form, "species_key", SpeciesKey);
        AddIfPresent(form, "size_bucket", SizeBucket);
        if (Behaviours != null)
        {
            foreach (var behaviour in Behaviours) AddIfPresent(form, "behaviours", behaviour);
        }
        AddIfPresent(form, "reporter_name", ReporterName);
        AddIfPresent(form, "reporter_email", ReporterEmail);
        AddIfPresent(form, "reporter_profile", ReporterProfile);

        // Force a fresh token only once the backend has actually rejected one: the
        // cached token is what it rejected, and the SDK keeps serving that until it
        // expires. Keyed on the refusal, not on the attempt count — see
        // TokenRefreshAttempted.
        Task<string> tokenTask = AppCheckTokenProvider.GetTokenAsync(forceRefresh: TokenRefreshAttempted);

        // Bounded, because an unbounded wait here is the same defect as a bare
        // SendWebRequest() with the same blast radius: the queue runs one job at a
        // time, so a token fetch that never completes leaves `running` true forever
        // and silently kills every other queued sighting. JobQueue's loop watchdog
        // deliberately cannot catch it either — from the outside a stuck job is
        // indistinguishable from a slow upload, which is the whole reason the
        // watchdog only fires while nothing is running.
        float tokenDeadline = Time.realtimeSinceStartup + TokenWaitTimeoutSeconds;
        while (!tokenTask.IsCompleted)
        {
            // realtimeSinceStartup, never Time.time: Time.time is scaled by
            // Time.timeScale, so a paused app would freeze the deadline.
            if (Time.realtimeSinceStartup >= tokenDeadline)
            {
                LastError = $"app check token timed out after {TokenWaitTimeoutSeconds:F0}s";
                // Transient, not permanent: a hung Firebase init is no reason to
                // throw the sighting away. It IS permanent for this process, though —
                // AppCheckTokenProvider caches its init Task forever, faults included,
                // and never clears it, so a token that fails to arrive once will never
                // arrive again before the app restarts. The retry that matters is the
                // one on the NEXT process, which is exactly what a queue that survives
                // app kill is for. Filed as a NETWORK failure so it takes the gentle
                // schedule and is pulled forward on relaunch, rather than escalating
                // to the hour-long rung over something no amount of waiting fixes.
                LastFailureWasNetwork = true;
                setResult(JobResult.TransientFailure);
                yield break;
            }
            yield return null;
        }

        // A FAULTED task is left as it was: the token simply comes back null, no
        // header is sent, and the server gets to decide (a BACKEND_DEBUG backend
        // accepts it; prod answers 401, which ClassifyHttpFailure then handles).
        string appCheckToken = tokenTask.Status == TaskStatus.RanToCompletion ? tokenTask.Result : null;

        using (var req = UnityWebRequest.Post(Url, form))
        {
            if (!string.IsNullOrEmpty(IdempotencyKey))
                req.SetRequestHeader("Idempotency-Key", IdempotencyKey);
            if (!string.IsNullOrEmpty(appCheckToken))
                req.SetRequestHeader("X-Firebase-AppCheck", appCheckToken);

            // Never a bare SendWebRequest(): with no timeout, a half-open socket —
            // routine on mobile data — holds the queue's single execution slot
            // forever and silently stops every other queued sighting. The watchdog
            // aborts only a request that has stopped moving bytes, so a multi-megabyte
            // original crawling up a weak uplink is left alone; the hard cap is
            // stretched to ten minutes for the same reason. See Job.Send.
            var outcome = new RequestOutcome();
            yield return Send(req, outcome, stallSeconds: 45f, hardCapSeconds: 600f);

            if (outcome.Aborted)
            {
                LastError = "watchdog: " + outcome.Reason;
                LastFailureWasNetwork = true;
                setResult(JobResult.TransientFailure);
                yield break;
            }

            if (req.result == UnityWebRequest.Result.Success)
            {
                try { File.Delete(ImagePath); } catch { /* best effort */ }
                LastFailureWasNetwork = false;
                setResult(JobResult.Success);
                yield break;
            }

            LastError = $"{(int)req.responseCode} {req.error}";

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                // The radio, not the server: the queue runs a gentler schedule for
                // these and pulls the job forward when signal returns.
                LastFailureWasNetwork = true;
                setResult(JobResult.TransientFailure);
                yield break;
            }

            // A server answered, whatever it said, so the escalating schedule applies.
            LastFailureWasNetwork = false;
            setResult(ClassifyHttpFailure(req.responseCode));
        }
    }

    /// <summary>
    /// Decides what a non-success HTTP status means for this job, and records the
    /// one piece of state that decision depends on.
    ///
    /// <para>A 401 is almost always the App Check token having aged past its hour
    /// while the job sat in the queue, so the first one buys a retry with
    /// <c>forceRefresh: true</c> and sets <see cref="TokenRefreshAttempted"/> — which
    /// the ordinary transient path then persists along with the rest of the payload.
    /// A 401 once that has happened means a <em>freshly minted</em> token was
    /// rejected, i.e. the install is genuinely deregistered (unsigned build,
    /// tampered APK, app removed from the Firebase project) and no amount of retrying
    /// will help → permanent.</para>
    ///
    /// <para>Extracted from <see cref="Execute"/> so the rule is testable without a
    /// live server or a Firebase SDK: everything upstream of it is real HTTP, and
    /// this is the only part with a decision in it.</para>
    /// </summary>
    internal JobResult ClassifyHttpFailure(long responseCode)
    {
        if (responseCode == 401)
        {
            if (TokenRefreshAttempted) return JobResult.PermanentFailure;
            TokenRefreshAttempted = true;
            return JobResult.TransientFailure;
        }

        if (responseCode >= 500 || responseCode == 408 || responseCode == 429 || responseCode == 0)
            return JobResult.TransientFailure;

        return JobResult.PermanentFailure;
    }

    static void AddIfPresent(List<IMultipartFormSection> form, string name, string value)
    {
        if (!string.IsNullOrEmpty(value)) form.Add(new MultipartFormDataSection(name, value));
    }

    protected internal override string SerializeData()
    {
        return JsonUtility.ToJson(new Data
        {
            url = Url,
            imagePath = ImagePath,
            mimeType = MimeType,
            beachName = BeachName,
            isoTimestamp = IsoTimestamp,
            speciesGuess = SpeciesGuess,
            notes = Notes,
            idempotencyKey = IdempotencyKey,
            speciesKey = SpeciesKey,
            sizeBucket = SizeBucket,
            behaviours = Behaviours,
            reporterName = ReporterName,
            reporterEmail = ReporterEmail,
            reporterProfile = ReporterProfile,
            tokenRefreshAttempted = TokenRefreshAttempted,
        });
    }

    protected internal override void DeserializeData(string data)
    {
        var d = JsonUtility.FromJson<Data>(data);
        Url = d.url;
        ImagePath = d.imagePath;
        MimeType = d.mimeType;
        BeachName = d.beachName;
        IsoTimestamp = d.isoTimestamp;
        SpeciesGuess = d.speciesGuess;
        Notes = d.notes;
        IdempotencyKey = d.idempotencyKey;
        SpeciesKey = d.speciesKey;
        SizeBucket = d.sizeBucket;
        Behaviours = d.behaviours ?? Array.Empty<string>();
        ReporterName = d.reporterName;
        ReporterEmail = d.reporterEmail;
        ReporterProfile = d.reporterProfile;
        TokenRefreshAttempted = d.tokenRefreshAttempted;
    }
}
