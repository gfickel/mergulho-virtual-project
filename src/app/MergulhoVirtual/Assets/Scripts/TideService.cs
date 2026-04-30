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
                "Run tools/generate_tides.py to produce it. " +
                "Tide data will be unavailable until then.");
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
        if (!force && idx == lastEmittedHourIdx) return;

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

        // Scan ahead for the next local high and next local low.
        snap.nextHighAt = DateTime.MinValue;
        snap.nextLowAt = DateTime.MinValue;
        bool foundHigh = false, foundLow = false;
        int scanLimit = Math.Min(heights.Length - 1, idx + 36); // tides are < 13h apart; 36h is plenty
        for (int j = idx + 1; j < scanLimit; j++)
        {
            float prev = heights[j - 1];
            float curr = heights[j];
            float next = heights[j + 1];
            if (!foundHigh && curr >= prev && curr >= next)
            {
                snap.nextHighAt = startUtc.AddHours(j);
                snap.nextHighM = curr;
                foundHigh = true;
            }
            if (!foundLow && curr <= prev && curr <= next)
            {
                snap.nextLowAt = startUtc.AddHours(j);
                snap.nextLowM = curr;
                foundLow = true;
            }
            if (foundHigh && foundLow) break;
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
    }
}
