using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    [Tooltip("Multipart POST endpoint for sightings. Cleartext is blocked by AndroidManifest — works in editor against the dev LAN backend, but device builds need this swapped to HTTPS.")]
    [SerializeField] private string uploadUrl = "http://192.168.1.122:8000/avistamentos";

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

    [Header("Validation visuals")]
    [Tooltip("Background tint applied to a field's targetGraphic when validation fails. The original color is captured on first error and restored when the user fixes the field.")]
    [SerializeField] private Color invalidFieldColor = new Color(0.42f, 0.18f, 0.22f, 1f);

    private string pickedImagePath;     // raw path returned by the picker (cache)
    private Coroutine submitCoroutine;
    private readonly Dictionary<Image, Color> originalFieldColors = new Dictionary<Image, Color>();
    private GameObject sendingOverlay;
    private RectTransform sendingCard;
    private TMP_Text sendingLabel;

    void Awake()
    {
        if (pickPhotoButton != null) pickPhotoButton.onClick.AddListener(OnPickPhoto);
        if (submitButton != null) submitButton.onClick.AddListener(OnSubmit);
        if (beachDropdown != null) beachDropdown.onValueChanged.AddListener(OnBeachEdited);
        if (dateInput != null)     dateInput.onValueChanged.AddListener(OnDateEdited);
        if (timeInput != null)     timeInput.onValueChanged.AddListener(OnTimeEdited);

        // Let TMP drive the status text's height so multi-line validation messages
        // aren't clipped by the LayoutElement.preferredHeight set by the builder (60).
        if (statusText != null)
        {
            var le = statusText.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = -1f;
        }
    }

    void OnDestroy()
    {
        if (pickPhotoButton != null) pickPhotoButton.onClick.RemoveListener(OnPickPhoto);
        if (submitButton != null) submitButton.onClick.RemoveListener(OnSubmit);
        if (beachDropdown != null) beachDropdown.onValueChanged.RemoveListener(OnBeachEdited);
        if (dateInput != null)     dateInput.onValueChanged.RemoveListener(OnDateEdited);
        if (timeInput != null)     timeInput.onValueChanged.RemoveListener(OnTimeEdited);
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
        ClearAllFieldErrors();
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
        if (submitCoroutine != null) return;
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
            SetFieldError(GetFieldBackground(pickPhotoButton), false);
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
        // Already submitting (overlay up, screen about to swap) — swallow re-taps.
        if (submitCoroutine != null) return;

        // Validate every field, collect a per-field message, and tint the offending
        // input so the user sees both *what's* missing (highlight) and *why* (status).
        var errors = new List<string>();

        bool photoOk = !string.IsNullOrEmpty(pickedImagePath) && File.Exists(pickedImagePath);
        SetFieldError(GetFieldBackground(pickPhotoButton), !photoOk);
        if (!photoOk) errors.Add("Adicione uma foto.");

        string beach = ResolveBeach();
        bool beachOk = !string.IsNullOrEmpty(beach);
        bool autoNoGps = beachDropdown != null && beachDropdown.value == 0
            && (gpsHandler == null || string.IsNullOrEmpty(gpsHandler.CurrentPlaceName));
        SetFieldError(GetFieldBackground(beachDropdown), !beachOk);
        if (!beachOk)
            errors.Add(autoNoGps
                ? "Selecione uma praia (GPS ainda não detectou)."
                : "Selecione uma praia.");

        bool dateOk = TryParseDateInput(out _);
        SetFieldError(GetFieldBackground(dateInput), !dateOk);
        if (!dateOk) errors.Add("Insira uma data válida (DD/MM/AAAA).");

        bool timeOk = TryParseTimeInput(out _);
        SetFieldError(GetFieldBackground(timeInput), !timeOk);
        if (!timeOk) errors.Add("Insira um horário válido (HH:MM).");

        if (errors.Count > 0)
        {
            string msg = errors.Count == 1
                ? errors[0]
                : "Verifique os campos destacados:\n• " + string.Join("\n• ", errors);
            SetStatus(msg, isError: true);
            return;
        }

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
        // Submit-time path: validators already ensured both inputs parse, so this
        // can only fall back to "now" if a caller bypasses validation.
        if (TryParseDateInput(out DateTime d) && TryParseTimeInput(out DateTime t))
        {
            var dt = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, 0, DateTimeKind.Local);
            return dt.ToUniversalTime().ToString("o");
        }
        return DateTime.UtcNow.ToString("o");
    }

    bool TryParseDateInput(out DateTime date)
    {
        date = default;
        if (dateInput == null || string.IsNullOrEmpty(dateInput.text)) return false;
        return DateTime.TryParseExact(
            dateInput.text, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    bool TryParseTimeInput(out DateTime time)
    {
        time = default;
        if (timeInput == null || string.IsNullOrEmpty(timeInput.text)) return false;
        return DateTime.TryParseExact(
            timeInput.text, "HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
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
        // Always tappable except while a previous submit's overlay is still up —
        // the user can only learn what's missing by tapping submit and reading the
        // per-field errors. A disabled button would just swallow that question.
        if (submitButton != null)
            submitButton.interactable = submitCoroutine == null;
    }

    // ------------------------------------------------------------------------
    // Validation visuals — tint each invalid field's targetGraphic, restore on
    // edit. Uses Selectable.targetGraphic (pickPhotoButton, beachDropdown,
    // dateInput, timeInput all expose it) so no extra Inspector wiring is needed
    // on already-built scenes.
    // ------------------------------------------------------------------------
    static Image GetFieldBackground(Selectable s) => s != null ? s.targetGraphic as Image : null;

    void SetFieldError(Image img, bool hasError)
    {
        if (img == null) return;
        if (!originalFieldColors.ContainsKey(img))
            originalFieldColors[img] = img.color;
        img.color = hasError ? invalidFieldColor : originalFieldColors[img];
    }

    void ClearAllFieldErrors()
    {
        SetFieldError(GetFieldBackground(pickPhotoButton), false);
        SetFieldError(GetFieldBackground(beachDropdown), false);
        SetFieldError(GetFieldBackground(dateInput), false);
        SetFieldError(GetFieldBackground(timeInput), false);
    }

    // Field-edit handlers — clear that field's red highlight as soon as the user
    // touches it, and clear the summary message so the form feels responsive.
    // The other fields' highlights persist until they're individually fixed.
    void OnBeachEdited(int _) { SetFieldError(GetFieldBackground(beachDropdown), false); SetStatus(""); }
    void OnDateEdited(string _) { SetFieldError(GetFieldBackground(dateInput), false); SetStatus(""); }
    void OnTimeEdited(string _) { SetFieldError(GetFieldBackground(timeInput), false); SetStatus(""); }

    void SetStatus(string message, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text = message ?? "";
        statusText.color = isError ? new Color(1f, 0.45f, 0.45f) : new Color(0.7f, 0.95f, 0.7f);
    }
}
