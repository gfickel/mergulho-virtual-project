using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// DTO for GET /api/v1/latest-post. Field names deliberately match the JSON
/// keys (snake_case) so JsonUtility maps them directly. The backend guarantees
/// absent fields are empty strings, never null/missing.
/// </summary>
[Serializable]
public class InstagramPostData
{
    public string media_type;   // "IMAGE" | "VIDEO" | "CAROUSEL_ALBUM"
    public string image_url;    // our backend's cached-image endpoint
    public string video_url;    // Instagram CDN mp4 (VIDEO only, else "")
    public string caption;
    public string permalink;    // https://www.instagram.com/p/...
    public string timestamp;    // ISO-8601
}

/// <summary>
/// Shows the latest Instagram post (image / video thumbnail + caption) fetched
/// from our own backend — the app never talks to Instagram directly and holds
/// no credentials. Lives on the "InstagramSection" root built by
/// Tools > Mergulho Virtual > Create Instagram Widget (all fields auto-wired).
///
/// Flow on enable:
///   1. Show the offline cache instantly if present
///      (persistentDataPath/instagram/{latest.json, media.jpg}).
///   2. Fetch fresh JSON (UnityWebRequest + X-Firebase-AppCheck header, same
///      pattern as BackendServices). If it differs from what's shown, download
///      the image bytes, update the UI, and persist both to the cache.
///   3. Any failure with nothing to show → hide the whole section
///      (never broken UI). 404 = backend has no post cached yet.
///
/// The image is always fetched from {baseUrl}/api/v1/latest-post/image (the
/// contract's fixed path) rather than the image_url string, so repointing
/// baseUrl at a LAN dev backend in the Inspector repoints both requests.
///
/// VIDEO posts: thumbnail + play overlay, no autoplay. Playback is delegated
/// to the existing VideoPlayerController (APIOnly streaming into a second
/// RawImage layered over the thumbnail), configured by the builder to start
/// muted with an unmute control. Tap anywhere on the card except the
/// play/controls area opens the post via Application.OpenURL(permalink).
/// </summary>
public class InstagramPostWidget : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("Prod HTTPS backend. Repoint at a LAN dev backend (e.g. http://192.168.68.108:8000) to test against BACKEND_DEBUG=1.")]
    [SerializeField] private string baseUrl = "https://mergulhovirtual.dev";

    [Header("UI (wired by InstagramWidgetBuilder — no manual dragging needed)")]
    [SerializeField] private RawImage mediaImage;          // thumbnail / photo surface
    [SerializeField] private AspectRatioFitter mediaAspect;
    [SerializeField] private TMP_Text captionText;
    [SerializeField] private GameObject playOverlay;       // shown only for VIDEO
    [SerializeField] private GameObject loadingIndicator;  // initial "Carregando…"
    [SerializeField] private VideoPlayerController videoController; // on MediaFrame

    // Don't re-hit the backend more often than this while navigating screens.
    private const float RefreshIntervalSeconds = 300f;

    private InstagramPostData current;
    private string currentJson;       // exact JSON text backing `current` (change detection)
    private Texture2D mediaTexture;
    private bool displayed;           // something (cache or fresh) is on screen
    private float lastSuccessfulFetch = float.NegativeInfinity;

    private string CacheDir  => Path.Combine(Application.persistentDataPath, "instagram");
    private string JsonFile  => Path.Combine(CacheDir, "latest.json");
    private string ImageFile => Path.Combine(CacheDir, "media.jpg");

    void OnEnable()
    {
        if (!displayed)
            TryShowCached();
        else
            ReapplyMediaState(); // video card was stopped on disable — reset to idle poster

        if (Time.realtimeSinceStartup - lastSuccessfulFetch < RefreshIntervalSeconds)
        {
            if (loadingIndicator != null && displayed) loadingIndicator.SetActive(false);
            return;
        }
        StartCoroutine(RefreshRoutine());
    }

    void OnDisable() => StopAllCoroutines();

    void OnDestroy()
    {
        if (mediaTexture != null) Destroy(mediaTexture);
    }

    /// <summary>Wired to the card Button by the builder. Opens the post.</summary>
    public void OpenPermalink()
    {
        if (current != null && !string.IsNullOrEmpty(current.permalink))
            Application.OpenURL(current.permalink);
    }

    // ------------------------------------------------------------------ fetch

    private IEnumerator RefreshRoutine()
    {
        // App Check token — same await-in-coroutine pattern as BackendServices.
        // Null until Firebase is installed + FIREBASE_APPCHECK_ENABLED defined;
        // a BACKEND_DEBUG=1 backend ignores the missing header.
        Task<string> tokenTask = AppCheckTokenProvider.GetTokenAsync();
        while (!tokenTask.IsCompleted) yield return null;
        string token = tokenTask.Status == TaskStatus.RanToCompletion ? tokenTask.Result : null;

        string json;
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/api/v1/latest-post"))
        {
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("X-Firebase-AppCheck", token);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 404 = backend has nothing cached yet; others = network/auth.
                Debug.Log($"[InstagramPostWidget] latest-post fetch failed " +
                          $"(HTTP {req.responseCode}): {req.error}");
                OnFetchFailed();
                yield break;
            }
            json = req.downloadHandler.text;
        }

        InstagramPostData data = null;
        try { data = JsonUtility.FromJson<InstagramPostData>(json); }
        catch (Exception e)
        {
            Debug.LogWarning($"[InstagramPostWidget] bad JSON: {e.Message}");
        }
        if (data == null || string.IsNullOrEmpty(data.media_type))
        {
            OnFetchFailed();
            yield break;
        }

        // Unchanged post (same JSON, incl. timestamp) → nothing to redownload.
        if (json == currentJson && displayed && mediaTexture != null)
        {
            lastSuccessfulFetch = Time.realtimeSinceStartup;
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            yield break;
        }

        // Image bytes (photo, carousel cover, or video thumbnail) — fixed path
        // on our backend, App Check header required.
        byte[] imageBytes;
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/api/v1/latest-post/image"))
        {
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("X-Firebase-AppCheck", token);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[InstagramPostWidget] image fetch failed " +
                          $"(HTTP {req.responseCode}): {req.error}");
                OnFetchFailed();
                yield break;
            }
            imageBytes = req.downloadHandler.data;
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (imageBytes == null || imageBytes.Length == 0 || !tex.LoadImage(imageBytes))
        {
            Destroy(tex);
            OnFetchFailed();
            yield break;
        }

        lastSuccessfulFetch = Time.realtimeSinceStartup;
        ApplyPost(data, json, tex);
        PersistCache(json, imageBytes);
    }

    private void OnFetchFailed()
    {
        if (!displayed)
        {
            // No cache, no network — hide the whole section, never broken UI.
            gameObject.SetActive(false);
            return;
        }
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
    }

    // --------------------------------------------------------------------- ui

    private void ApplyPost(InstagramPostData data, string json, Texture2D tex)
    {
        if (mediaTexture != null && mediaTexture != tex) Destroy(mediaTexture);
        mediaTexture = tex;
        current = data;
        currentJson = json;

        if (mediaImage != null)
        {
            mediaImage.texture = tex;
            mediaImage.color = Color.white;
        }
        if (mediaAspect != null && tex.height > 0)
            mediaAspect.aspectRatio = (float)tex.width / tex.height;

        if (captionText != null)
        {
            captionText.text = data.caption ?? string.Empty;
            captionText.gameObject.SetActive(!string.IsNullOrEmpty(data.caption));
        }

        ReapplyMediaState();

        if (loadingIndicator != null) loadingIndicator.SetActive(false);
        displayed = true;
    }

    /// <summary>
    /// Puts the video layer in the right idle state for the current post.
    /// Bind() resets VideoPlayerController to its poster state (the poster
    /// color is transparent here, so the thumbnail underneath shows through);
    /// the play overlay is then forced to match media_type — visible for
    /// VIDEO, hidden otherwise.
    /// </summary>
    private void ReapplyMediaState()
    {
        bool isVideo = current != null
                       && current.media_type == "VIDEO"
                       && !string.IsNullOrEmpty(current.video_url);
        if (videoController != null)
            videoController.Bind(isVideo ? current.video_url : string.Empty, null);
        if (playOverlay != null)
            playOverlay.SetActive(isVideo);
    }

    // ------------------------------------------------------------------ cache

    private void TryShowCached()
    {
        try
        {
            if (!File.Exists(JsonFile) || !File.Exists(ImageFile)) return;
            string json = File.ReadAllText(JsonFile);
            var data = JsonUtility.FromJson<InstagramPostData>(json);
            if (data == null || string.IsNullOrEmpty(data.media_type)) return;
            byte[] bytes = File.ReadAllBytes(ImageFile);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { Destroy(tex); return; }
            ApplyPost(data, json, tex);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InstagramPostWidget] cache load failed: {e.Message}");
        }
    }

    private void PersistCache(string json, byte[] imageBytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            WriteAtomic(JsonFile, Encoding.UTF8.GetBytes(json));
            WriteAtomic(ImageFile, imageBytes);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InstagramPostWidget] cache write failed: {e.Message}");
        }
    }

    // tmp + move, same crash-safety convention as JobQueue's persistence.
    private static void WriteAtomic(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
