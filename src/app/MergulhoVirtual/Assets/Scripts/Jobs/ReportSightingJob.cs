using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public string Url;
    public string ImagePath;
    public string MimeType;        // e.g. "image/jpeg" — server uses this when storing
    public string BeachName;
    public string IsoTimestamp;    // RFC3339; user-provided or derived from EXIF/now
    public string SpeciesGuess;    // optional
    public string Notes;           // optional
    public string IdempotencyKey;

    public override string Type => "ReportSighting";

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

        using (var req = UnityWebRequest.Post(Url, form))
        {
            if (!string.IsNullOrEmpty(IdempotencyKey))
                req.SetRequestHeader("Idempotency-Key", IdempotencyKey);

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
            if (code >= 500 || code == 408 || code == 429 || code == 0)
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
    }
}
