using System;

public enum MoonPhaseName
{
    New,
    WaxingCrescent,
    FirstQuarter,
    WaxingGibbous,
    Full,
    WaningGibbous,
    LastQuarter,
    WaningCrescent
}

public static class MoonPhase
{
    const double SynodicMonthDays = 29.530588853;

    static readonly DateTime ReferenceNewMoonUtc =
        new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    /// <summary>
    /// Phase as a fraction of the synodic month: 0 = new, 0.25 = first quarter,
    /// 0.5 = full, 0.75 = last quarter. Accuracy ±1 day.
    /// </summary>
    public static float Phase(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
        double daysSince = (utc - ReferenceNewMoonUtc).TotalDays;
        double phase = (daysSince / SynodicMonthDays) % 1.0;
        if (phase < 0) phase += 1.0;
        return (float)phase;
    }

    /// <summary>
    /// Illuminated fraction in [0, 1] (cosine approximation; 0 at new, 1 at full).
    /// </summary>
    public static float Illumination(DateTime utc)
    {
        double phase = Phase(utc);
        return (float)((1.0 - Math.Cos(phase * 2.0 * Math.PI)) * 0.5);
    }

    public static MoonPhaseName Name(float phase)
    {
        phase -= (float)Math.Floor(phase);
        if (phase < 0.03125f || phase >= 0.96875f) return MoonPhaseName.New;
        if (phase < 0.21875f) return MoonPhaseName.WaxingCrescent;
        if (phase < 0.28125f) return MoonPhaseName.FirstQuarter;
        if (phase < 0.46875f) return MoonPhaseName.WaxingGibbous;
        if (phase < 0.53125f) return MoonPhaseName.Full;
        if (phase < 0.71875f) return MoonPhaseName.WaningGibbous;
        if (phase < 0.78125f) return MoonPhaseName.LastQuarter;
        return MoonPhaseName.WaningCrescent;
    }
}
