using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Drives one inline video card. Streams a remote MP4 (progressive HTTP) from
/// a URL into a RawImage via VideoPlayer's APIOnly render mode — no
/// RenderTexture asset, the surface auto-sizes to the clip's aspect (so
/// vertical "reels" and landscape clips both frame correctly).
///
/// Lazy by design: nothing is fetched until the user taps. The whole card is a
/// tap target (play/pause), plus a control bar with a play/pause button, a
/// draggable seek slider, and elapsed/total time. VideoPlayer ships no UI
/// chrome — these controls are built here against its API (time/length/Play/
/// Pause, seek by assigning videoPlayer.time).
///
/// Playback stops and releases the texture on disable, so leaving the detail
/// screen kills the stream. Reusable beyond Animals — anything with a URL can
/// Bind(). The card template + wiring is built by AnimalsScreenBuilder.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoPlayerController : MonoBehaviour
{
    [Header("Surface")]
    [SerializeField] private RawImage surface;
    [SerializeField] private AspectRatioFitter aspectFitter;
    [SerializeField] private GameObject playOverlay;     // big center ▶, idle-only
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private Color posterColor = new Color(0.027f, 0.082f, 0.149f, 1f); // #071526

    [Header("Control bar")]
    [SerializeField] private GameObject controlsBar;     // shown once prepared
    [SerializeField] private TMP_Text playPauseLabel;    // "Pausar" / "Tocar"
    [SerializeField] private Slider seekSlider;
    [SerializeField] private PointerHeldFlag seekHeld;   // true while the user drags the slider
    [SerializeField] private TMP_Text timeLabel;         // "0:12 / 1:23"

    private VideoPlayer videoPlayer;
    private bool prepared;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.renderMode = VideoRenderMode.APIOnly;
        videoPlayer.playOnAwake = false;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.skipOnDrop = true;
        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.errorReceived += OnError;
        if (seekSlider != null) seekSlider.onValueChanged.AddListener(OnSeekChanged);
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.errorReceived -= OnError;
        }
        if (seekSlider != null) seekSlider.onValueChanged.RemoveListener(OnSeekChanged);
    }

    void OnDisable() => Stop();

    void Update()
    {
        if (!prepared || videoPlayer == null) return;

        // Keep the play/pause label honest.
        if (playPauseLabel != null)
            playPauseLabel.text = videoPlayer.isPlaying ? "Pausar" : "Tocar";

        double length = videoPlayer.length;
        if (length <= 0) return;

        // While the user drags, OnSeekChanged owns the slider/time — don't fight it.
        if (seekHeld != null && seekHeld.Held) return;

        if (seekSlider != null)
            seekSlider.SetValueWithoutNotify((float)(videoPlayer.time / length));
        if (timeLabel != null)
            timeLabel.text = $"{FormatTime(videoPlayer.time)} / {FormatTime(length)}";
    }

    /// <summary>Point this card at a video. Resets to the idle (poster) state.</summary>
    public void Bind(string url, string title)
    {
        Stop();
        videoPlayer.url = url;
        if (titleText != null)
        {
            titleText.text = title;
            titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
        }
        if (errorText != null) errorText.gameObject.SetActive(false);
        ShowIdle();
    }

    /// <summary>Wired to the card tap AND the play/pause button.</summary>
    public void OnCardClicked()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            if (playPauseLabel != null) playPauseLabel.text = "Tocar";
            return;
        }
        if (prepared)
        {
            videoPlayer.Play();
            if (playPauseLabel != null) playPauseLabel.text = "Pausar";
            return;
        }
        BeginPrepare();
    }

    void OnSeekChanged(float v)
    {
        if (!prepared || videoPlayer == null) return;
        double length = videoPlayer.length;
        if (length <= 0) return;
        videoPlayer.time = v * length;
        if (timeLabel != null)
            timeLabel.text = $"{FormatTime(v * length)} / {FormatTime(length)}";
    }

    void BeginPrepare()
    {
        if (string.IsNullOrEmpty(videoPlayer.url)) return;
        SetOverlay(false);
        if (loadingIndicator != null) loadingIndicator.SetActive(true);
        if (errorText != null) errorText.gameObject.SetActive(false);
        videoPlayer.Prepare();
    }

    void OnPrepared(VideoPlayer vp)
    {
        prepared = true;
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
        if (surface != null)
        {
            surface.texture = vp.texture;
            surface.color = Color.white;
        }
        if (aspectFitter != null && vp.width > 0 && vp.height > 0)
            aspectFitter.aspectRatio = (float)vp.width / vp.height;
        SetOverlay(false);
        if (controlsBar != null) controlsBar.SetActive(true);
        vp.Play();
    }

    void OnError(VideoPlayer vp, string message)
    {
        Debug.LogWarning($"[VideoPlayerController] {message}");
        prepared = false;
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
        ShowIdle();
        if (errorText != null)
        {
            errorText.text = "Não foi possível carregar o vídeo.";
            errorText.gameObject.SetActive(true);
        }
    }

    void Stop()
    {
        prepared = false;
        if (videoPlayer != null && (videoPlayer.isPlaying || videoPlayer.isPrepared))
            videoPlayer.Stop();
        if (surface != null) surface.texture = null;
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
    }

    void ShowIdle()
    {
        if (surface != null)
        {
            surface.texture = null;
            surface.color = posterColor; // RawImage with no texture draws a solid quad in `color`
        }
        if (controlsBar != null) controlsBar.SetActive(false);
        if (seekSlider != null) seekSlider.SetValueWithoutNotify(0f);
        if (timeLabel != null) timeLabel.text = "0:00 / 0:00";
        SetOverlay(true);
    }

    void SetOverlay(bool show)
    {
        if (playOverlay != null) playOverlay.SetActive(show);
    }

    static string FormatTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        int total = (int)seconds;
        return $"{total / 60}:{total % 60:00}";
    }
}
