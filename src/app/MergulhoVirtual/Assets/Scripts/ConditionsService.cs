using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ConditionsService : MonoBehaviour
{
    [SerializeField] GPSHandler gps;
    [Tooltip("Beach to fetch conditions for when GPS hasn't resolved to a known polygon. " +
             "Leave blank to use the first entry from places.json.")]
    [SerializeField] string fallbackBeachName = "Praia do Sancho";
    [Tooltip("Seconds between background refreshes while the same beach is active.")]
    [SerializeField] float refreshIntervalSeconds = 1800f;

    public ConditionsSnapshot CurrentConditions { get; private set; }
    public event Action<ConditionsSnapshot> ConditionsChanged;

    /// <summary>
    /// True when the most recent attempt to reach Open-Meteo produced nothing
    /// usable. Cleared by the next attempt that does.
    ///
    /// <para>Before this existed a total failure was invisible to every caller:
    /// nothing was assigned and <see cref="ConditionsChanged"/> was not raised, so
    /// the UI could not tell "still loading" from "we tried and could not".</para>
    /// </summary>
    public bool LastFetchFailed { get; private set; }

    /// <summary>True while a fetch is in flight — lets a retry affordance show
    /// that the tap did something.</summary>
    public bool IsFetching { get; private set; }

    /// <summary>
    /// Raised whenever <see cref="IsFetching"/> or <see cref="LastFetchFailed"/>
    /// changes. Deliberately separate from <see cref="ConditionsChanged"/>, which
    /// carries a snapshot and must keep meaning "there is new data".
    /// </summary>
    public event Action FetchStateChanged;

    Coroutine refreshLoop;
    string activeBeach;

    void OnEnable()
    {
        if (gps == null)
        {
            Debug.LogError("ConditionsService: gps is not assigned in the Inspector.");
            return;
        }
        gps.PlaceChanged += OnPlaceChanged;
        OnPlaceChanged(gps.CurrentPlaceName);
    }

    void OnDisable()
    {
        if (gps != null) gps.PlaceChanged -= OnPlaceChanged;
        StopRefresh();
    }

    void OnPlaceChanged(string beachName)
    {
        string targetBeach = string.IsNullOrEmpty(beachName) ? ResolveFallbackBeach() : beachName;
        if (targetBeach == activeBeach && refreshLoop != null) return;

        activeBeach = targetBeach;
        StopRefresh();

        if (string.IsNullOrEmpty(targetBeach))
        {
            CurrentConditions = null;
            ConditionsChanged?.Invoke(null);
            return;
        }

        var cached = LoadCache(targetBeach);
        if (cached != null)
        {
            CurrentConditions = cached;
            ConditionsChanged?.Invoke(cached);
        }

        refreshLoop = StartCoroutine(RefreshLoop(targetBeach));
    }

    /// <summary>
    /// Re-fetch the active beach's conditions now, instead of waiting out the rest
    /// of <c>refreshIntervalSeconds</c>. This is what a "Tentar novamente" button
    /// calls, so it must really restart the request — it stops the idle loop and
    /// starts a new one, whose first action is a fetch.
    ///
    /// <para>No-op while one is already in flight (a double tap must not fire two
    /// pairs of requests), and when the component is disabled.</para>
    /// </summary>
    public void Refresh()
    {
        if (!isActiveAndEnabled || IsFetching) return;

        if (string.IsNullOrEmpty(activeBeach))
        {
            // Nothing resolved yet (no GPS fix, or the first resolve failed) —
            // go through the normal path so the fallback beach is picked again.
            OnPlaceChanged(gps != null ? gps.CurrentPlaceName : null);
            return;
        }

        StopRefresh();
        refreshLoop = StartCoroutine(RefreshLoop(activeBeach));
    }

    void SetFetchState(bool fetching, bool failed)
    {
        if (IsFetching == fetching && LastFetchFailed == failed) return;
        IsFetching = fetching;
        LastFetchFailed = failed;
        FetchStateChanged?.Invoke();
    }

    string ResolveFallbackBeach()
    {
        if (!string.IsNullOrEmpty(fallbackBeachName)) return fallbackBeachName;
        var names = ReverseGeocoding.GetAllPlaceNames();
        return names != null && names.Count > 0 ? names[0] : null;
    }

    void StopRefresh()
    {
        if (refreshLoop != null)
        {
            StopCoroutine(refreshLoop);
            refreshLoop = null;
        }
    }

    IEnumerator RefreshLoop(string beachName)
    {
        while (activeBeach == beachName)
        {
            yield return Refresh(beachName);
            float elapsed = 0f;
            while (elapsed < refreshIntervalSeconds && activeBeach == beachName)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }
    }

    IEnumerator Refresh(string beachName)
    {
        Vector2? centroid = ReverseGeocoding.GetCentroid(beachName);
        if (!centroid.HasValue)
        {
            Debug.LogWarning($"ConditionsService: no centroid for beach '{beachName}'.");
            // A data problem, not a network one — but from the UI's side it is
            // still "we have no conditions and nothing is on its way".
            SetFetchState(false, true);
            yield break;
        }

        SetFetchState(true, LastFetchFailed);

        // ReverseGeocoding maps lon→x, lat→y (see GetCentroid).
        float lon = centroid.Value.x;
        float lat = centroid.Value.y;

        string lonStr = lon.ToString("F4", CultureInfo.InvariantCulture);
        string latStr = lat.ToString("F4", CultureInfo.InvariantCulture);

        string marineUrl =
            $"https://marine-api.open-meteo.com/v1/marine?latitude={latStr}&longitude={lonStr}" +
            "&hourly=wave_height,wave_period,wave_direction,sea_surface_temperature" +
            "&forecast_days=1&timezone=auto";

        string forecastUrl =
            $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}" +
            "&hourly=wind_speed_10m,wind_direction_10m" +
            "&forecast_days=1&timezone=auto";

        UnityWebRequest marineReq = UnityWebRequest.Get(marineUrl);
        UnityWebRequest forecastReq = UnityWebRequest.Get(forecastUrl);

        marineReq.SendWebRequest();
        forecastReq.SendWebRequest();

        while (!marineReq.isDone || !forecastReq.isDone) yield return null;

        if (activeBeach != beachName)
        {
            marineReq.Dispose();
            forecastReq.Dispose();
            // Superseded by a beach change, not a failure — leave the flag alone.
            SetFetchState(false, LastFetchFailed);
            yield break;
        }

        var snapshot = new ConditionsSnapshot
        {
            beachName = beachName,
            fetchedAtTicksUtc = DateTime.UtcNow.Ticks
        };
        bool anySuccess = false;

        if (marineReq.result == UnityWebRequest.Result.Success)
        {
            ApplyMarine(snapshot, marineReq.downloadHandler.text);
            anySuccess = true;
        }
        else
        {
            Debug.LogWarning($"ConditionsService: marine API failed — {marineReq.error}");
        }

        if (forecastReq.result == UnityWebRequest.Result.Success)
        {
            ApplyForecast(snapshot, forecastReq.downloadHandler.text);
            anySuccess = true;
        }
        else
        {
            Debug.LogWarning($"ConditionsService: forecast API failed — {forecastReq.error}");
        }

        marineReq.Dispose();
        forecastReq.Dispose();

        if (!anySuccess)
        {
            SetFetchState(false, true);
            yield break;
        }

        // Preserve fields from the prior snapshot (same beach) that this fetch didn't fill —
        // partial-success fetches shouldn't lose previously-known values.
        if (CurrentConditions != null && CurrentConditions.beachName == beachName)
        {
            MergeFromPrior(snapshot, CurrentConditions);
        }

        CurrentConditions = snapshot;
        SaveCache(beachName, snapshot);
        SetFetchState(false, false);
        ConditionsChanged?.Invoke(snapshot);
    }

    void ApplyMarine(ConditionsSnapshot snap, string json)
    {
        var parsed = JsonUtility.FromJson<MarineResponse>(json);
        if (parsed?.hourly == null) return;
        int idx = CurrentHourIndex(parsed.hourly.time);
        if (idx < 0) return;

        if (Pick(parsed.hourly.wave_height, idx, out float wh))
        { snap.hasWaveHeight = true; snap.waveHeightM = wh; }
        if (Pick(parsed.hourly.wave_period, idx, out float wp))
        { snap.hasWavePeriod = true; snap.wavePeriodS = wp; }
        if (Pick(parsed.hourly.wave_direction, idx, out float wd))
        { snap.hasWaveDirection = true; snap.waveDirectionDeg = wd; }
        if (Pick(parsed.hourly.sea_surface_temperature, idx, out float st))
        { snap.hasSeaTemp = true; snap.seaTempC = st; }
    }

    void ApplyForecast(ConditionsSnapshot snap, string json)
    {
        var parsed = JsonUtility.FromJson<ForecastResponse>(json);
        if (parsed?.hourly == null) return;
        int idx = CurrentHourIndex(parsed.hourly.time);
        if (idx < 0) return;

        if (Pick(parsed.hourly.wind_speed_10m, idx, out float ws))
        { snap.hasWindSpeed = true; snap.windSpeedKmh = ws; }
        if (Pick(parsed.hourly.wind_direction_10m, idx, out float wd))
        { snap.hasWindDirection = true; snap.windDirectionDeg = wd; }
    }

    static int CurrentHourIndex(string[] times)
    {
        if (times == null || times.Length == 0) return -1;
        DateTime now = DateTime.Now;
        int bestIdx = -1;
        TimeSpan bestDelta = TimeSpan.MaxValue;
        for (int i = 0; i < times.Length; i++)
        {
            if (!DateTime.TryParse(times[i], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime t)) continue;
            TimeSpan delta = (now - t).Duration();
            if (delta < bestDelta) { bestDelta = delta; bestIdx = i; }
        }
        return bestIdx;
    }

    static bool Pick(float[] arr, int idx, out float value)
    {
        if (arr == null || idx < 0 || idx >= arr.Length || float.IsNaN(arr[idx]))
        {
            value = 0f;
            return false;
        }
        value = arr[idx];
        return true;
    }

    static void MergeFromPrior(ConditionsSnapshot fresh, ConditionsSnapshot prior)
    {
        if (!fresh.hasWaveHeight && prior.hasWaveHeight)
        { fresh.hasWaveHeight = true; fresh.waveHeightM = prior.waveHeightM; }
        if (!fresh.hasWavePeriod && prior.hasWavePeriod)
        { fresh.hasWavePeriod = true; fresh.wavePeriodS = prior.wavePeriodS; }
        if (!fresh.hasWaveDirection && prior.hasWaveDirection)
        { fresh.hasWaveDirection = true; fresh.waveDirectionDeg = prior.waveDirectionDeg; }
        if (!fresh.hasSeaTemp && prior.hasSeaTemp)
        { fresh.hasSeaTemp = true; fresh.seaTempC = prior.seaTempC; }
        if (!fresh.hasWindSpeed && prior.hasWindSpeed)
        { fresh.hasWindSpeed = true; fresh.windSpeedKmh = prior.windSpeedKmh; }
        if (!fresh.hasWindDirection && prior.hasWindDirection)
        { fresh.hasWindDirection = true; fresh.windDirectionDeg = prior.windDirectionDeg; }
    }

    // ---- Cache ---------------------------------------------------------------

    static string CachePath(string beachName)
    {
        return Path.Combine(Application.persistentDataPath,
            "conditions_" + SanitizeBeachKey(beachName) + ".json");
    }

    static string SanitizeBeachKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unknown";
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue; // strip accents
            char lower = char.ToLowerInvariant(c);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9')) sb.Append(lower);
            else if (lower == ' ' || lower == '-' || lower == '_') sb.Append('_');
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    static ConditionsSnapshot LoadCache(string beachName)
    {
        try
        {
            string path = CachePath(beachName);
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var snap = JsonUtility.FromJson<ConditionsSnapshot>(json);
            return snap != null && snap.beachName == beachName ? snap : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ConditionsService: failed to load cache for '{beachName}': {e.Message}");
            return null;
        }
    }

    static void SaveCache(string beachName, ConditionsSnapshot snap)
    {
        try
        {
            File.WriteAllText(CachePath(beachName), JsonUtility.ToJson(snap));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ConditionsService: failed to save cache for '{beachName}': {e.Message}");
        }
    }

    // ---- Open-Meteo response shapes -----------------------------------------

    [Serializable]
    class MarineResponse { public MarineHourly hourly; }

    [Serializable]
    class MarineHourly
    {
        public string[] time;
        public float[] wave_height;
        public float[] wave_period;
        public float[] wave_direction;
        public float[] sea_surface_temperature;
    }

    [Serializable]
    class ForecastResponse { public ForecastHourly hourly; }

    [Serializable]
    class ForecastHourly
    {
        public string[] time;
        public float[] wind_speed_10m;
        public float[] wind_direction_10m;
    }
}
