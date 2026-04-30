using System;

[Serializable]
public class ConditionsSnapshot
{
    public string beachName;

    // DateTime.UtcNow.Ticks at fetch — JsonUtility-friendly.
    // 0 means "no fetch ever" (e.g. an empty placeholder).
    public long fetchedAtTicksUtc;

    public bool hasWaveHeight;
    public float waveHeightM;

    public bool hasWavePeriod;
    public float wavePeriodS;

    public bool hasWaveDirection;
    public float waveDirectionDeg;

    public bool hasSeaTemp;
    public float seaTempC;

    public bool hasWindSpeed;
    public float windSpeedKmh;

    public bool hasWindDirection;
    public float windDirectionDeg;

    public DateTime FetchedAtUtc =>
        fetchedAtTicksUtc > 0
            ? new DateTime(fetchedAtTicksUtc, DateTimeKind.Utc)
            : DateTime.MinValue;

    public bool IsStale =>
        fetchedAtTicksUtc <= 0 ||
        (DateTime.UtcNow - FetchedAtUtc).TotalHours > 4.0;

    public float? WaveHeightM     => hasWaveHeight    ? waveHeightM     : (float?)null;
    public float? WavePeriodS     => hasWavePeriod    ? wavePeriodS     : (float?)null;
    public float? WaveDirectionDeg => hasWaveDirection ? waveDirectionDeg : (float?)null;
    public float? SeaTempC        => hasSeaTemp       ? seaTempC        : (float?)null;
    public float? WindSpeedKmh    => hasWindSpeed     ? windSpeedKmh    : (float?)null;
    public float? WindDirectionDeg => hasWindDirection ? windDirectionDeg : (float?)null;
}
