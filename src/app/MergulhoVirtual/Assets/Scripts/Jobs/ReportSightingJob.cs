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
    }

    public override IEnumerator Execute(Action<JobResult> setResult)
    {
        if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
        {
            LastError = "image missing: " + ImagePath;
            setResult(JobResult.PermanentFailure);
            yield break;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(ImagePath); }
        catch (Exception e)
        {
            LastError = "read: " + e.Message;
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

        // Force a fresh token on retry — the cached one is what the backend
        // just rejected, and the SDK keeps it in cache until expiry otherwise.
        Task<string> tokenTask = AppCheckTokenProvider.GetTokenAsync(forceRefresh: AttemptCount > 1);
        while (!tokenTask.IsCompleted) yield return null;
        string appCheckToken = tokenTask.Status == TaskStatus.RanToCompletion ? tokenTask.Result : null;

        using (var req = UnityWebRequest.Post(Url, form))
        {
            if (!string.IsNullOrEmpty(IdempotencyKey))
                req.SetRequestHeader("Idempotency-Key", IdempotencyKey);
            if (!string.IsNullOrEmpty(appCheckToken))
                req.SetRequestHeader("X-Firebase-AppCheck", appCheckToken);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try { File.Delete(ImagePath); } catch { /* best effort */ }
                setResult(JobResult.Success);
                yield break;
            }

            LastError = $"{(int)req.responseCode} {req.error}";

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                setResult(JobResult.TransientFailure);
                yield break;
            }

            long code = req.responseCode;
            // 401 on the first attempt is likely an expired App Check token —
            // retry once with forceRefresh=true. If the second attempt 401s
            // too, the app is genuinely deregistered → permanent failure.
            if (code == 401 && AttemptCount == 1)
                setResult(JobResult.TransientFailure);
            else if (code >= 500 || code == 408 || code == 429 || code == 0)
                setResult(JobResult.TransientFailure);
            else
                setResult(JobResult.PermanentFailure);
        }
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
    }
}
