using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class BackendServices : MonoBehaviour
{
    public TMP_Text countText;
    private const string ApiUrl = "https://mergulhovirtual.dev/api/v1/avistamentos/count";

    [System.Serializable]
    private class CountResponse
    {
        public int count;
    }

    void Start()
    {
        if (countText == null)
        {
            Debug.LogError("BackendServices: countText is not assigned!");
            return;
        }

        StartCoroutine(GetCount());
    }

    private IEnumerator GetCount()
    {
        // Fetch an App Check token first. Returns null until the Firebase
        // Unity SDK is installed AND FIREBASE_APPCHECK_ENABLED is defined —
        // see AppCheckTokenProvider.cs. Against a BACKEND_DEBUG=1 backend the
        // missing header is ignored; against the prod backend it would 401.
        Task<string> tokenTask = AppCheckTokenProvider.GetTokenAsync();
        while (!tokenTask.IsCompleted) yield return null;
        string token = tokenTask.Status == TaskStatus.RanToCompletion ? tokenTask.Result : null;

        using (UnityWebRequest webRequest = UnityWebRequest.Get(ApiUrl))
        {
            if (!string.IsNullOrEmpty(token))
                webRequest.SetRequestHeader("X-Firebase-AppCheck", token);

            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error fetching count: {webRequest.error}");
                countText.text = "Error: " + webRequest.error;
            }
            else
            {
                try
                {
                    string jsonResponse = webRequest.downloadHandler.text;
                    CountResponse response = JsonUtility.FromJson<CountResponse>(jsonResponse);

                    if (response != null)
                    {
                        countText.text = "Avistamentos: " + response.count.ToString();
                    }
                    else
                    {
                        Debug.LogError("Failed to parse response.");
                        countText.text = "Error: failed to parse response.";
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Exception parsing response: {e.Message}");
                    countText.text = "Error" + e.Message;
                }
            }
        }
    }
}
