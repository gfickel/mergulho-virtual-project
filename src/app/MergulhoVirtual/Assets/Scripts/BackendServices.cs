using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class BackendServices : MonoBehaviour
{
    public TMP_Text countText;
    private const string ApiUrl = "http://192.168.1.122:8000/avistamentos?count=true";

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
        using (UnityWebRequest webRequest = UnityWebRequest.Get(ApiUrl))
        {
            // Send the request and wait for a response
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
