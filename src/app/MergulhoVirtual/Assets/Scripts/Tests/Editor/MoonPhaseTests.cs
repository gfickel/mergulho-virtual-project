using System;
using NUnit.Framework;

public class MoonPhaseTests
{
    const float OneDayInPhase = 1f / 29.530588853f;
    const float Tolerance = OneDayInPhase * 1.05f;

    [Test]
    public void Phase_ReferenceEpoch_IsApproximatelyNew()
    {
        DateTime newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        float phase = MoonPhase.Phase(newMoon);
        Assert.That(Math.Min(phase, 1f - phase), Is.LessThan(Tolerance));
    }

    [Test]
    public void Phase_HalfSynodicLater_IsApproximatelyFull()
    {
        DateTime newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        DateTime full = newMoon.AddDays(29.530588853 / 2.0);
        float phase = MoonPhase.Phase(full);
        Assert.That(Math.Abs(phase - 0.5f), Is.LessThan(Tolerance));
    }

    [Test]
    public void Phase_KnownNewMoon_2024_01_11()
    {
        // 2024-01-11 11:57 UTC was a new moon (NASA).
        DateTime t = new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc);
        float phase = MoonPhase.Phase(t);
        Assert.That(Math.Min(phase, 1f - phase), Is.LessThan(Tolerance),
            $"Expected ~0 (new), got {phase}");
    }

    [Test]
    public void Phase_KnownFullMoon_2024_01_25()
    {
        // 2024-01-25 17:54 UTC was a full moon (NASA).
        DateTime t = new DateTime(2024, 1, 25, 17, 54, 0, DateTimeKind.Utc);
        float phase = MoonPhase.Phase(t);
        Assert.That(Math.Abs(phase - 0.5f), Is.LessThan(Tolerance),
            $"Expected ~0.5 (full), got {phase}");
    }

    [Test]
    public void Illumination_NewMoon_IsZero()
    {
        DateTime newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        Assert.That(MoonPhase.Illumination(newMoon), Is.LessThan(0.05f));
    }

    [Test]
    public void Illumination_FullMoon_IsOne()
    {
        DateTime full = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)
            .AddDays(29.530588853 / 2.0);
        Assert.That(MoonPhase.Illumination(full), Is.GreaterThan(0.95f));
    }

    [Test]
    public void Name_BoundaryValues()
    {
        Assert.AreEqual(MoonPhaseName.New, MoonPhase.Name(0f));
        Assert.AreEqual(MoonPhaseName.FirstQuarter, MoonPhase.Name(0.25f));
        Assert.AreEqual(MoonPhaseName.Full, MoonPhase.Name(0.5f));
        Assert.AreEqual(MoonPhaseName.LastQuarter, MoonPhase.Name(0.75f));
        Assert.AreEqual(MoonPhaseName.New, MoonPhase.Name(1.0f));
    }

    [Test]
    public void Name_MidQuadrants()
    {
        Assert.AreEqual(MoonPhaseName.WaxingCrescent, MoonPhase.Name(0.125f));
        Assert.AreEqual(MoonPhaseName.WaxingGibbous, MoonPhase.Name(0.375f));
        Assert.AreEqual(MoonPhaseName.WaningGibbous, MoonPhase.Name(0.625f));
        Assert.AreEqual(MoonPhaseName.WaningCrescent, MoonPhase.Name(0.875f));
    }
}
