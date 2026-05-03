using System;
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

    private string pickedImagePath;     // raw path returned by the picker (cache)
    private bool isUploading;

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

        SetStatus("Enviando avistamento…", isError: false);
        ResetFormAfterSubmit();

        if (screenManager != null) screenManager.ShowMain();
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
