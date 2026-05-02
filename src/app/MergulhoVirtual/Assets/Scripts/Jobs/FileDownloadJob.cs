using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public class FileDownloadJob : Job
{
    public string Url;
    public string DestPath;
    public string ExpectedSha256;

    public override string Type => "FileDownload";

    [Serializable]
    private struct Data
    {
        public string url;
        public string destPath;
        public string expectedSha256;
    }

    public override IEnumerator Execute(Action<JobResult> setResult)
    {
        string partial = DestPath + ".partial";

        try
        {
            string dir = Path.GetDirectoryName(DestPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(partial))
                File.Delete(partial);
        }
        catch (Exception e)
        {
            LastError = "prep: " + e.Message;
            setResult(JobResult.TransientFailure);
            yield break;
        }

        using (var req = UnityWebRequest.Get(Url))
        {
            req.downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true };

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = $"{(int)req.responseCode} {req.error}";
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    setResult(JobResult.TransientFailure);
                else if (req.responseCode >= 500 || req.responseCode == 408 || req.responseCode == 429 || req.responseCode == 0)
                    setResult(JobResult.TransientFailure);
                else
                    setResult(JobResult.PermanentFailure);
                yield break;
            }
        }

        if (!string.IsNullOrEmpty(ExpectedSha256))
        {
            string actual;
            try { actual = ComputeSha256(partial); }
            catch (Exception e)
            {
                LastError = "hash: " + e.Message;
                setResult(JobResult.TransientFailure);
                yield break;
            }
            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                LastError = $"sha256 mismatch (got {actual})";
                try { File.Delete(partial); } catch { }
                setResult(JobResult.TransientFailure);
                yield break;
            }
        }

        try
        {
            if (File.Exists(DestPath)) File.Delete(DestPath);
            File.Move(partial, DestPath);
        }
        catch (Exception e)
        {
            LastError = "finalize: " + e.Message;
            setResult(JobResult.TransientFailure);
            yield break;
        }

        setResult(JobResult.Success);
    }

    private static string ComputeSha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            byte[] hash = sha.ComputeHash(stream);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    protected internal override string SerializeData()
    {
        return JsonUtility.ToJson(new Data
        {
            url = Url,
            destPath = DestPath,
            expectedSha256 = ExpectedSha256,
        });
    }

    protected internal override void DeserializeData(string data)
    {
        var d = JsonUtility.FromJson<Data>(data);
        Url = d.url;
        DestPath = d.destPath;
        ExpectedSha256 = d.expectedSha256;
    }
}
