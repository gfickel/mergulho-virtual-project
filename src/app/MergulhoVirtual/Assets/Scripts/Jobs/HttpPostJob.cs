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

            // Never a bare SendWebRequest(): with no timeout, a half-open socket
            // holds the queue's single execution slot forever. See Job.Send.
            var outcome = new RequestOutcome();
            yield return Send(req, outcome);

            if (outcome.Aborted)
            {
                LastError = "watchdog: " + outcome.Reason;
                LastFailureWasNetwork = true;
                setResult(JobResult.TransientFailure);
                yield break;
            }

            if (req.result == UnityWebRequest.Result.Success)
            {
                LastFailureWasNetwork = false;
                setResult(JobResult.Success);
                yield break;
            }

            LastError = $"{(int)req.responseCode} {req.error}";

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                // The radio, not the server: the queue runs a gentler schedule for
                // these and is allowed to pull the job forward when signal returns.
                LastFailureWasNetwork = true;
                setResult(JobResult.TransientFailure);
                yield break;
            }

            // A server answered, whatever it said, so the escalating schedule applies.
            LastFailureWasNetwork = false;
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
