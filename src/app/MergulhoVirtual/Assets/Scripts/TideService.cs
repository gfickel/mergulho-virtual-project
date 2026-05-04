using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public struct TideSnapshot
{
    public float currentHeightM;
    public bool rising;            // true if next hourly sample > current
    public DateTime nextHighAt;
    public float nextHighM;
    public DateTime nextLowAt;
    public float nextLowM;
    public float[] next24hHeights; // exactly 24 floats — sparkline window
    public DateTime windowStart;   // anchor for next24hHeights[0]
    public bool valid;
}

public class TideService : MonoBehaviour
{
    [SerializeField] string resourceName = "tides_noronha";
    [SerializeField] float pollIntervalSeconds = 60f;

    public TideSnapshot CurrentTide { get; private set; }
    public event Action<TideSnapshot> TideChanged;

    TidesData data;
    DateTime startUtc;
    int lastEmittedHourIdx = -1;
    float pollTimer;

    void Start()
    {
        if (!Load()) return;
        EmitForCurrentHour(force: true);
    }

    void Update()
    {
        if (data == null) return;
        pollTimer += Time.unscaledDeltaTime;
        if (pollTimer < pollIntervalSeconds) return;
        pollTimer = 0f;
        EmitForCurrentHour(force: false);
    }

    bool Load()
    {
        var asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null)
        {
            Debug.LogWarning(
                $"TideService: Resources/{resourceName}.json not found. " +
                "Run tools/parse_dhn_tide_table.py then tools/build_app_tides.py " +
                "to produce it. Tide data will be unavailable until then.");
            return false;
        }

        try
        {
            data = JsonUtility.FromJson<TidesData>(asset.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"TideService: failed to parse {resourceName}.json — {e.Message}");
            return false;
        }

        if (data == null || data.heights_m == null || data.heights_m.Length == 0)
        {
            Debug.LogError($"TideService: {resourceName}.json is empty or malformed.");
            data = null;
            return false;
        }

        if (!DateTime.TryParseExact(data.start_date, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out startUtc))
        {
            Debug.LogError($"TideService: invalid start_date '{data.start_date}'.");
            data = null;
            return false;
        }
        startUtc = DateTime.SpecifyKind(startUtc.Date, DateTimeKind.Utc);
        return true;
    }

    void EmitForCurrentHour(bool force)
    {
        int idx = HourIndex(DateTime.UtcNow);
        if (idx < 0) return;

        // Re-emit when the cached next-high/low has passed, otherwise users see
        // a "próxima baixa 12:40" label five minutes after that's already happened.
        DateTime now = DateTime.UtcNow;
        bool eventsStale = CurrentTide.valid && (
            (CurrentTide.nextHighAt != DateTime.MinValue && CurrentTide.nextHighAt <= now) ||
            (CurrentTide.nextLowAt  != DateTime.MinValue && CurrentTide.nextLowAt  <= now));

        if (!force && idx == lastEmittedHourIdx && !eventsStale) return;

        lastEmittedHourIdx = idx;
        CurrentTide = Compute(idx);
        TideChanged?.Invoke(CurrentTide);
    }

    int HourIndex(DateTime utcNow)
    {
        if (data == null) return -1;
        double hours = (utcNow - startUtc).TotalHours;
        int idx = (int)Math.Floor(hours);
        if (idx < 0 || idx >= data.heights_m.Length) return -1;
        return idx;
    }

    TideSnapshot Compute(int idx)
    {
        var heights = data.heights_m;
        var snap = new TideSnapshot
        {
            valid = true,
            currentHeightM = heights[idx],
            windowStart = startUtc.AddHours(idx),
            next24hHeights = new float[24]
        };

        for (int i = 0; i < 24; i++)
        {
            int j = idx + i;
            snap.next24hHeights[i] = j < heights.Length ? heights[j] : heights[heights.Length - 1];
        }

        snap.rising = idx + 1 < heights.Length && heights[idx + 1] > heights[idx];

        // Find next high/low from the original DHN extrema list (preserves the
        // published HH:MM peak times exactly — the hourly heights_m above can
        // only resolve them to HH:00).
        DateTime nowUtc = DateTime.UtcNow;
        snap.nextHighAt = DateTime.MinValue;
        snap.nextLowAt = DateTime.MinValue;
        if (data.events != null && data.events.Length > 0)
        {
            bool foundHigh = false, foundLow = false;
            foreach (var ev in data.events)
            {
                if (foundHigh && foundLow) break;
                DateTime evUtc;
                if (!DateTime.TryParseExact(ev.utc, "yyyy-MM-ddTHH:mm:ssZ",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out evUtc))
                    continue;
                if (evUtc <= nowUtc) continue;
                if (!foundHigh && ev.k == "H")
                {
                    snap.nextHighAt = DateTime.SpecifyKind(evUtc, DateTimeKind.Utc);
                    snap.nextHighM = ev.h;
                    foundHigh = true;
                }
                else if (!foundLow && ev.k == "L")
                {
                    snap.nextLowAt = DateTime.SpecifyKind(evUtc, DateTimeKind.Utc);
                    snap.nextLowM = ev.h;
                    foundLow = true;
                }
            }
        }
        return snap;
    }

    [ContextMenu("Advance 1h (editor test)")]
    void AdvanceOneHourForTesting()
    {
        if (data == null) return;
        lastEmittedHourIdx = (lastEmittedHourIdx + 1) % data.heights_m.Length;
        CurrentTide = Compute(lastEmittedHourIdx);
        TideChanged?.Invoke(CurrentTide);
    }

    [Serializable]
    class TidesData
    {
        public string station;
        public double lat;
        public double lon;
        public string generated_at;
        public string valid_until;
        public int samples_per_day;
        public string start_date;
        public float[] heights_m;
        public TideEvent[] events;
    }

    [Serializable]
    class TideEvent
    {
        public string utc;  // ISO 8601, e.g. "2026-05-04T14:40:00Z"
        public float h;     // height in metres (LAT)
        public string k;    // "H" or "L"
    }
}
