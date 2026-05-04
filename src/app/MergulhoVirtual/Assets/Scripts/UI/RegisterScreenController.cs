using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the "Reportar Avistamento" form. Picks a photo via <see cref="GalleryPicker"/>,
/// previews it, lets the user pick a beach + optional date/time/notes, and enqueues
/// a <see cref="ReportSightingJob"/> for the JobQueue to upload (with retry/persistence).
///
/// Image bytes are copied into persistentDataPath/sightings/&lt;guid&gt;.&lt;ext&gt; before
/// enqueueing so the upload survives the user deleting the picked photo, app kill, etc.
/// EXIF is preserved end-to-end (raw byte copy, no decode).
/// </summary>
public class RegisterScreenController : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("Multipart POST endpoint for sightings. Must be HTTPS (cleartext is blocked by AndroidManifest).")]
    [SerializeField] private string uploadUrl = "https://example.invalid/sightings";

    [Header("Photo")]
    [SerializeField] private Button pickPhotoButton;
    [SerializeField] private Image photoPreview;
    [SerializeField] private GameObject photoPlaceholder;

    [Header("Beach")]
    [SerializeField] private TMP_Dropdown beachDropdown;
    [SerializeField] private GPSHandler gpsHandler;
    [SerializeField] private string autoOptionLabel = "Detectar pela localização";

    [Header("When")]
    [SerializeField] private TMP_InputField dateInput;
    [SerializeField] private TMP_InputField timeInput;

    [Header("Optional")]
    [SerializeField] private TMP_InputField speciesInput;
    [SerializeField] private TMP_InputField notesInput;

    [Header("Submit")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private ScreenManager screenManager;
    [Tooltip("How long the 'Enviando avistamento…' overlay stays up before bouncing back to the AR screen.")]
    [SerializeField] private float postSubmitDelaySeconds = 2.0f;
    [Tooltip("RoundedRectCard.mat — gives the post-submit overlay card the same rounded corners as other cards. Optional.")]
    [SerializeField] private Material roundedRectMaterial;

    private string pickedImagePath;     // raw path returned by the picker (cache)
    private bool isUploading;
    private Coroutine submitCoroutine;
    private GameObject sendingOverlay;
    private RectTransform sendingCard;
    private TMP_Text sendingLabel;

    void Awake()
    {
        if (pickPhotoButton != null) pickPhotoButton.onClick.AddListener(OnPickPhoto);
        if (submitButton != null) submitButton.onClick.AddListener(OnSubmit);
    }

    void OnDestroy()
    {
        if (pickPhotoButton != null) pickPhotoButton.onClick.RemoveListener(OnPickPhoto);
        if (submitButton != null) submitButton.onClick.RemoveListener(OnSubmit);
    }

    void OnDisable()
    {
        // If the user navigated away mid-overlay (e.g. tapped BottomNav), drop the
        // pending navigation and hide the overlay so it doesn't reappear on re-entry.
        if (submitCoroutine != null)
        {
            StopCoroutine(submitCoroutine);
            submitCoroutine = null;
        }
        if (sendingOverlay != null) sendingOverlay.SetActive(false);
    }

    void OnEnable()
    {
        PopulateBeachDropdown();
        PrefillDateTime();
        RefreshSubmitState();
        SetStatus("");
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    void PopulateBeachDropdown()
    {
        if (beachDropdown == null) return;

        List<string> names = ReverseGeocoding.GetAllPlaceNames();
        var options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData(autoOptionLabel),
        };
        foreach (var n in names) options.Add(new TMP_Dropdown.OptionData(n));

        beachDropdown.ClearOptions();
        beachDropdown.AddOptions(options);

        // Pre-select the GPS-detected beach if we have one and it matches a known name.
        int selected = 0;
        string current = gpsHandler != null ? gpsHandler.CurrentPlaceName : null;
        if (!string.IsNullOrEmpty(current))
        {
            int idx = names.IndexOf(current);
            if (idx >= 0) selected = idx + 1; // +1 for the "auto" sentinel at index 0
        }
        beachDropdown.SetValueWithoutNotify(selected);
    }

    void PrefillDateTime()
    {
        DateTime now = DateTime.Now;
        if (dateInput != null && string.IsNullOrEmpty(dateInput.text))
            dateInput.text = now.ToString("dd/MM/yyyy");
        if (timeInput != null && string.IsNullOrEmpty(timeInput.text))
            timeInput.text = now.ToString("HH:mm");
    }

    void OnPickPhoto()
    {
        if (isUploading) return;
        SetStatus("");
        GalleryPicker.PickImage((path, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                if (error != "cancelled") SetStatus("Falha ao abrir a galeria: " + error, isError: true);
                return;
            }
            pickedImagePath = path;
            ShowPhotoPreview(path);
            RefreshSubmitState();
        });
    }

    void ShowPhotoPreview(string path)
    {
        if (photoPreview == null) return;

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            // Texture2D loads JPEG/PNG natively; the preview is a downscaled in-memory copy,
            // it does NOT replace the on-disk file we'll upload (which keeps full EXIF).
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(bytes))
            {
                Destroy(tex);
                SetStatus("Não foi possível ler a imagem.", isError: true);
                return;
            }
            // Free the previous preview's texture before swapping (re-pick scenario).
            if (photoPreview.sprite != null && photoPreview.sprite.texture != null)
                Destroy(photoPreview.sprite.texture);
            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            photoPreview.sprite = sprite;
            photoPreview.color = Color.white;
            photoPreview.enabled = true;
            photoPreview.preserveAspect = true;
            if (photoPlaceholder != null) photoPlaceholder.SetActive(false);
        }
        catch (Exception e)
        {
            SetStatus("Erro lendo a imagem: " + e.Message, isError: true);
        }
    }

    void OnSubmit()
    {
        if (isUploading) return;

        if (string.IsNullOrEmpty(pickedImagePath) || !File.Exists(pickedImagePath))
        {
            SetStatus("Escolha uma foto antes de enviar.", isError: true);
            return;
        }

        string beach = ResolveBeach();
        string iso = BuildIsoTimestamp();

        string ext = Path.GetExtension(pickedImagePath);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        string sightingId = Guid.NewGuid().ToString("N");
        string destDir = Path.Combine(Application.persistentDataPath, "sightings");
        string destPath = Path.Combine(destDir, sightingId + ext);

        try
        {
            Directory.CreateDirectory(destDir);
            File.Copy(pickedImagePath, destPath, overwrite: true);
        }
        catch (Exception e)
        {
            SetStatus("Não foi possível salvar a imagem: " + e.Message, isError: true);
            return;
        }

        var job = new ReportSightingJob
        {
            Url = uploadUrl,
            ImagePath = destPath,
            MimeType = GuessMime(ext),
            BeachName = beach,
            IsoTimestamp = iso,
            SpeciesGuess = speciesInput != null ? speciesInput.text : null,
            Notes = notesInput != null ? notesInput.text : null,
            IdempotencyKey = sightingId,
        };

        // Enqueue for background upload (handles retry, persistence, no-network).
        // GetOrCreate auto-spawns a JobServices GameObject if one isn't in the scene,
        // so the user is never blocked by missing wiring.
        if (!JobQueue.GetOrCreate().Enqueue(job))
            Debug.LogWarning("[RegisterScreen] Job queue full — sighting dropped.");

        SetStatus("", isError: false);
        ResetFormAfterSubmit();

        if (submitCoroutine != null) StopCoroutine(submitCoroutine);
        submitCoroutine = StartCoroutine(ShowSendingThenNavigate());
    }

    IEnumerator ShowSendingThenNavigate()
    {
        EnsureSendingOverlay();
        sendingOverlay.transform.SetAsLastSibling();
        sendingOverlay.SetActive(true);

        const string baseLabel = "Enviando avistamento";
        float t = 0f;
        while (t < postSubmitDelaySeconds)
        {
            t += Time.unscaledDeltaTime;
            int dots = Mathf.FloorToInt(t * 2.5f) % 4;
            sendingLabel.text = baseLabel + new string('.', dots);
            // 4% breathing pulse keeps the card feeling alive without being noisy.
            float pulse = 1f + 0.04f * Mathf.Sin(t * 5f);
            sendingCard.localScale = new Vector3(pulse, pulse, 1f);
            yield return null;
        }

        sendingCard.localScale = Vector3.one;
        sendingOverlay.SetActive(false);
        submitCoroutine = null;
        if (screenManager != null) screenManager.ShowMain();
    }

    void EnsureSendingOverlay()
    {
        if (sendingOverlay != null) return;

        int uiLayer = LayerMask.NameToLayer("UI");

        // Backdrop — full-stretch under the screen, semi-transparent, blocks taps on the form behind.
        // (BottomNav is a sibling of RegisterScreen under ScreenUI, so it stays tappable — that's
        // intentional: the user can still navigate away mid-wait, and OnDisable cancels the auto-nav.)
        sendingOverlay = new GameObject("SendingOverlay",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        sendingOverlay.layer = uiLayer;
        sendingOverlay.transform.SetParent(transform, worldPositionStays: false);
        var bdRT = sendingOverlay.GetComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        var bdImg = sendingOverlay.GetComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.55f);
        bdImg.raycastTarget = true;

        // Card — centered, dark surface to match other cards, optionally rounded.
        var card = new GameObject("Card",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.layer = uiLayer;
        card.transform.SetParent(sendingOverlay.transform, worldPositionStays: false);
        sendingCard = card.GetComponent<RectTransform>();
        sendingCard.anchorMin = new Vector2(0.5f, 0.5f);
        sendingCard.anchorMax = new Vector2(0.5f, 0.5f);
        sendingCard.pivot     = new Vector2(0.5f, 0.5f);
        sendingCard.anchoredPosition = Vector2.zero;
        sendingCard.sizeDelta = new Vector2(440, 220);
        var cardImg = card.GetComponent<Image>();
        cardImg.color = new Color(0.102f, 0.161f, 0.251f, 1f); // matches CardSurface in the rest of the UI
        if (roundedRectMaterial != null) cardImg.material = roundedRectMaterial;
        // RectMask2D feeds the SDF rounded shader's clip rect; without it corners stay square.
        card.AddComponent<RectMask2D>();

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.spacing = 16f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.MiddleCenter;

        // Wave icon — uses the project's wired-up TMP sprite asset (🌊 maps to a sprite).
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.layer = uiLayer;
        iconGo.transform.SetParent(card.transform, worldPositionStays: false);
        var iconText = iconGo.AddComponent<TextMeshProUGUI>();
        iconText.text = "\U0001F30A";
        iconText.fontSize = 64;
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.raycastTarget = false;
        var iconLE = iconGo.AddComponent<LayoutElement>();
        iconLE.preferredHeight = 80;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.layer = uiLayer;
        labelGo.transform.SetParent(card.transform, worldPositionStays: false);
        sendingLabel = labelGo.AddComponent<TextMeshProUGUI>();
        sendingLabel.text = "Enviando avistamento";
        sendingLabel.fontSize = 22;
        sendingLabel.fontStyle = FontStyles.Bold;
        sendingLabel.color = Color.white;
        sendingLabel.alignment = TextAlignmentOptions.Center;
        sendingLabel.raycastTarget = false;
        var labelLE = labelGo.AddComponent<LayoutElement>();
        labelLE.preferredHeight = 40;

        sendingOverlay.SetActive(false);
    }

    string ResolveBeach()
    {
        if (beachDropdown == null) return null;
        int idx = beachDropdown.value;
        if (idx <= 0)
        {
            return gpsHandler != null ? gpsHandler.CurrentPlaceName : null;
        }
        return beachDropdown.options[idx].text;
    }

    string BuildIsoTimestamp()
    {
        // Parse the user's date+time inputs ("dd/MM/yyyy" + "HH:mm"). Fall back to now.
        if (dateInput != null && timeInput != null
            && !string.IsNullOrEmpty(dateInput.text) && !string.IsNullOrEmpty(timeInput.text)
            && DateTime.TryParseExact(
                dateInput.text + " " + timeInput.text,
                "dd/MM/yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out DateTime dt))
        {
            return dt.ToUniversalTime().ToString("o");
        }
        return DateTime.UtcNow.ToString("o");
    }

    static string GuessMime(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return "application/octet-stream";
        switch (ext.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".png":  return "image/png";
            case ".heic": return "image/heic";
            case ".webp": return "image/webp";
            default:      return "application/octet-stream";
        }
    }

    void ResetFormAfterSubmit()
    {
        pickedImagePath = null;
        if (photoPreview != null)
        {
            if (photoPreview.sprite != null && photoPreview.sprite.texture != null)
                Destroy(photoPreview.sprite.texture);
            photoPreview.sprite = null;
            photoPreview.enabled = false;
        }
        if (photoPlaceholder != null) photoPlaceholder.SetActive(true);
        if (notesInput != null) notesInput.text = "";
        if (speciesInput != null) speciesInput.text = "";
        PrefillDateTime();
        RefreshSubmitState();
    }

    void RefreshSubmitState()
    {
        if (submitButton != null)
            submitButton.interactable = !string.IsNullOrEmpty(pickedImagePath) && !isUploading;
    }

    void SetStatus(string message, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text = message ?? "";
        statusText.color = isError ? new Color(1f, 0.45f, 0.45f) : new Color(0.7f, 0.95f, 0.7f);
    }
}
