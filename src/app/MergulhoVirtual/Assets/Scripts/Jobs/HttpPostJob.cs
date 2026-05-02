using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class HttpPostJob : Job
{
    public string Url;
    public string JsonBody;
    public string IdempotencyKeyHeader;

    public override string Type => "HttpPost";

    [Serializable]
    private struct Data
    {
        public string url;
        public string jsonBody;
        public string idempotencyKeyHeader;
    }

    public override IEnumerator Execute(Action<JobResult> setResult)
    {
        using (var req = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = string.IsNullOrEmpty(JsonBody)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(JsonBody);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(IdempotencyKeyHeader))
                req.SetRequestHeader("Idempotency-Key", IdempotencyKeyHeader);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
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

    protected internal override string SerializeData()
    {
        return JsonUtility.ToJson(new Data
        {
            url = Url,
            jsonBody = JsonBody,
            idempotencyKeyHeader = IdempotencyKeyHeader,
        });
    }

    protected internal override void DeserializeData(string data)
    {
        var d = JsonUtility.FromJson<Data>(data);
        Url = d.url;
        JsonBody = d.jsonBody;
        IdempotencyKeyHeader = d.idempotencyKeyHeader;
    }
}
