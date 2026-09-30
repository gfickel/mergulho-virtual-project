using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Characterization tests for the extracted AR stabilization cores.
///
/// The contract: <see cref="StillnessCore"/> / <see cref="MotionGateCore"/> /
/// <see cref="DriftFusionCore"/> must be bit-for-bit identical to the
/// pre-extraction MonoBehaviour algorithms. "Identical" is pinned two ways:
///
///  1. against the verbatim transcriptions in ArCoreReference.cs, replayed
///     frame-by-frame over long deterministic input sequences that cross every
///     branch (this file). ArLegacyEquivalenceTests.cs proved those
///     transcriptions equal the SHIPPED components by reflection-driving the
///     real MonoBehaviours before the refactor; it was deleted afterwards
///     because the private state it poked moved into the cores.
///  2. against hard-coded goldens below, so a future edit cannot "fix" the
///     reference and the core in the same stroke and stay green.
///
/// If one of these fails, the core changed behaviour. That may be intended —
/// but it is a shipped-app change, not a refactor, and it needs re-tuning on a
/// device plus a new golden.
/// </summary>
public class ArCoreCharacterizationTests
{
    const float Dt30 = 1f / 30f;

    // =====================================================================
    // StillnessCore
    // =====================================================================

    [Test]
    public void StillnessCore_MatchesReference_FrameByFrame()
    {
        var samples = ArRefSequences.Imu();
        var reference = new RefStillness();
        var core = new StillnessCore { Config = StillnessCore.Settings.Defaults };

        int stillFrames = 0, loudFrames = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            reference.Update(s.dt, s.gyro, s.accel, s.hasAccel);
            core.Step(s.dt, s.gyro, s.accel, s.hasAccel);

            Assert.AreEqual(reference.SmoothedGyro, core.SmoothedGyro, 0f, $"SmoothedGyro @{i}");
            Assert.AreEqual(reference.SmoothedAccel, core.SmoothedAccel, 0f, $"SmoothedAccel @{i}");
            Assert.AreEqual(reference.IsStill, core.IsStill, $"IsStill @{i}");
            Assert.AreEqual(reference.quietTimer, core.QuietTimer, 0f, $"quietTimer @{i}");

            if (core.IsStill) stillFrames++; else loudFrames++;
        }

        // The sequence must actually exercise both states, or the test proves nothing.
        Assert.That(stillFrames, Is.GreaterThan(100), "sequence never reaches stillness");
        Assert.That(loudFrames, Is.GreaterThan(100), "sequence never leaves stillness");

        Console.WriteLine($"AR-GOLDEN stillness still={stillFrames} loud={loudFrames} " +
                          $"gyro={core.SmoothedGyro:R} accel={core.SmoothedAccel:R} timer={core.QuietTimer:R}");
    }

    [Test]
    public void StillnessCore_Goldens()
    {
        var samples = ArRefSequences.Imu();
        var core = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        int stillFrames = 0;
        foreach (var s in samples)
            if (core.Step(s.dt, s.gyro, s.accel, s.hasAccel)) stillFrames++;

        // Captured from the reference transcription, which ArLegacyEquivalenceTests
        // showed equals the shipped StillnessDetector.Update() (2026-09-28).
        Assert.AreEqual(276, stillFrames, "still-frame count over the canonical IMU sequence");
        Assert.AreEqual(0.129110381f, core.SmoothedGyro, 1e-6f);
        // SmoothedAccel keeps tracking the raw signal even over the final 120
        // gyro-only frames — hasAccelSensor gates the DECISION, not the EMA.
        Assert.AreEqual(0.0335483253f, core.SmoothedAccel, 1e-6f);
    }

    [Test]
    public void StillnessCore_FastExit_BreaksStillnessInOneFrame()
    {
        var core = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        for (int i = 0; i < 40; i++) core.Step(Dt30, 0.01f, 0.002f, true);
        Assert.IsTrue(core.IsStill, "should have settled");

        // Instantaneous reading above threshold * exitMultiplier.
        core.Step(Dt30, 0.12f * 1.6f + 0.001f, 0.002f, true);
        Assert.IsFalse(core.IsStill, "fast exit must fire on a single loud frame");
    }

    [Test]
    public void StillnessCore_SlowEntry_TakesEnterStillTime()
    {
        var cfg = StillnessCore.Settings.Defaults;
        var core = new StillnessCore { Config = cfg };
        core.Step(Dt30, 5f, 1f, true);           // start loud
        Assert.IsFalse(core.IsStill);

        int frames = 0;
        while (!core.IsStill && frames < 500) { core.Step(Dt30, 0f, 0f, true); frames++; }

        // The timer only starts once the SMOOTHED values fall under threshold,
        // so it takes longer than enterStillTime alone — that lag is the
        // behaviour being pinned, not a bug.
        Assert.IsTrue(core.IsStill, "never settled");
        Assert.That(frames * Dt30, Is.GreaterThanOrEqualTo(cfg.enterStillTime));
        Assert.AreEqual(20, frames, "frames to settle from a loud start");
    }

    [Test]
    public void StillnessCore_HysteresisBand_HoldsPreviousState()
    {
        var core = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        // Between gyroStillThreshold (0.12) and threshold*exitMultiplier (0.192):
        // not loud, and the smoothed value settles above the quiet threshold.
        for (int i = 0; i < 60; i++) core.Step(Dt30, 0.15f, 0.001f, true);
        Assert.IsFalse(core.IsStill, "band must not grant stillness");

        var settled = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        for (int i = 0; i < 40; i++) settled.Step(Dt30, 0.0f, 0f, true);
        Assert.IsTrue(settled.IsStill);
        for (int i = 0; i < 60; i++) settled.Step(Dt30, 0.15f, 0f, true);
        Assert.IsTrue(settled.IsStill, "band must not revoke stillness either");
    }

    [Test]
    public void StillnessCore_NoAccelSensor_UsesGyroOnly()
    {
        var core = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        // A huge accel reading must be ignored entirely when the sensor is absent.
        for (int i = 0; i < 40; i++) core.Step(Dt30, 0.01f, 99f, hasAccelSensor: false);
        Assert.IsTrue(core.IsStill);
    }

    [Test]
    public void StillnessCore_EmaIsFrameRateDependent_KnownDefect()
    {
        // Same 1 s of identical motion at 30 Hz and 60 Hz must NOT smooth the
        // same, because the EMA weight is per frame rather than per second.
        // This pins the defect so the offline harness can measure the fix later.
        var at30 = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        for (int i = 0; i < 30; i++) at30.Step(1f / 30f, 1f, 0f, true);

        var at60 = new StillnessCore { Config = StillnessCore.Settings.Defaults };
        for (int i = 0; i < 60; i++) at60.Step(1f / 60f, 1f, 0f, true);

        Assert.That(at60.SmoothedGyro, Is.GreaterThan(at30.SmoothedGyro),
            "60 Hz converges further per second — the dt-dependence being documented");
    }

    // =====================================================================
    // MotionGateCore
    // =====================================================================

    [Test]
    public void MotionGateCore_MatchesReference_FrameByFrame()
    {
        var samples = ArRefSequences.Gate();
        var reference = new RefGate();
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };

        Vector3 refOrigin = Vector3.zero, coreOrigin = Vector3.zero, session = Vector3.zero;
        for (int i = 0; i < samples.Length; i++)
        {
            session += samples[i].camDelta;
            reference.LateUpdate(refOrigin + session, samples[i].isStill, ref refOrigin);
            coreOrigin += core.Step(coreOrigin + session, samples[i].isStill);

            Assert.AreEqual(refOrigin.x, coreOrigin.x, 0f, $"origin.x @{i}");
            Assert.AreEqual(refOrigin.y, coreOrigin.y, 0f, $"origin.y @{i}");
            Assert.AreEqual(refOrigin.z, coreOrigin.z, 0f, $"origin.z @{i}");
            Assert.AreEqual(reference.TotalSuppressed, core.TotalSuppressed, $"suppressed @{i}");
        }

        Console.WriteLine($"AR-GOLDEN gate origin=({coreOrigin.x:R},{coreOrigin.y:R},{coreOrigin.z:R})");
    }

    [Test]
    public void MotionGateCore_Golden_MatchesShippedComponentCapture()
    {
        // Captured from the SHIPPED SpuriousMotionGate.LateUpdate() before the
        // extraction (ArLegacyEquivalenceTests, 2026-09-28): 600 frames of
        // ArRefSequences.Gate() drove the real component inside a real XROrigin
        // rig and left the origin exactly here. No dt in this algorithm, so the
        // number is machine-independent.
        var samples = ArRefSequences.Gate();
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        Vector3 origin = Vector3.zero, session = Vector3.zero;
        foreach (var s in samples)
        {
            session += s.camDelta;
            origin += core.Step(origin + session, s.isStill);
        }

        Assert.AreEqual(-0.11236544f, origin.x, 1e-6f);
        Assert.AreEqual(0.0157373529f, origin.y, 1e-6f);
        Assert.AreEqual(-0.0631216f, origin.z, 1e-6f);
        Assert.AreEqual(0.11236544f, core.TotalSuppressed.x, 1e-6f);
        Assert.AreEqual(-0.0157373529f, core.TotalSuppressed.y, 1e-6f);
        Assert.AreEqual(0.0631216f, core.TotalSuppressed.z, 1e-6f);
    }

    [Test]
    public void MotionGateCore_FirstStep_OnlySeedsReference()
    {
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        Assert.AreEqual(Vector3.zero, core.Step(new Vector3(5f, 0f, 5f), isStill: true));
        Assert.IsFalse(core.SuppressedLastStep);
    }

    [Test]
    public void MotionGateCore_BelowMinDelta_IsIgnored()
    {
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        core.Step(Vector3.zero, true);
        var shift = core.Step(new Vector3(0.0004f, 0f, 0f), true);   // < 0.0005
        Assert.AreEqual(Vector3.zero, shift);
        Assert.IsFalse(core.SuppressedLastStep);
    }

    [Test]
    public void MotionGateCore_AboveJumpThreshold_PassesThrough()
    {
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        core.Step(Vector3.zero, true);
        var shift = core.Step(new Vector3(0.5f, 0f, 0f), true);      // > 0.35
        Assert.AreEqual(Vector3.zero, shift, "relocalization must not be suppressed");
        Assert.IsFalse(core.SuppressedLastStep);
    }

    [Test]
    public void MotionGateCore_ZeroJumpThreshold_SuppressesEverything()
    {
        var cfg = MotionGateCore.Settings.Defaults;
        cfg.relocalizationJumpThreshold = 0f;
        var core = new MotionGateCore { Config = cfg };
        core.Step(Vector3.zero, true);
        var shift = core.Step(new Vector3(9f, 0f, 0f), true);
        Assert.AreEqual(-9f, shift.x, 1e-6f, "0 means suppress everything, however large");
        Assert.IsTrue(core.SuppressedLastStep);
    }

    [Test]
    public void MotionGateCore_NotStill_IsTransparent()
    {
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        core.Step(Vector3.zero, false);
        Assert.AreEqual(Vector3.zero, core.Step(new Vector3(0.05f, 0f, 0f), false));
        Assert.AreEqual(Vector3.zero, core.TotalSuppressed);
    }

    [Test]
    public void MotionGateCore_LastCamPos_IsThePostCorrectionPosition()
    {
        // Suppress frame 1, then the tracker reports the SAME world position
        // again. Because lastCamPos stores where the camera effectively stayed
        // (not where the tracker claimed), frame 2 sees the motion undone and
        // cancels it back — this is the bookkeeping that keeps the gate stable
        // instead of ratcheting.
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        Vector3 origin = Vector3.zero;
        origin += core.Step(origin + new Vector3(0f, 0f, 0f), true);      // seed at 0
        origin += core.Step(origin + new Vector3(0f, 0f, 0.05f), true);   // suppress +0.05
        Assert.AreEqual(-0.05f, origin.z, 1e-6f);
        // Session pose holds at 0.05 => camera world = origin + 0.05 = 0.0 => no delta.
        origin += core.Step(origin + new Vector3(0f, 0f, 0.05f), true);
        Assert.AreEqual(-0.05f, origin.z, 1e-6f, "a held session pose must not ratchet");
    }

    [Test]
    public void MotionGateCore_ResetTracking_DoesNotTreatTheGapAsAJump()
    {
        var core = new MotionGateCore { Config = MotionGateCore.Settings.Defaults };
        core.Step(Vector3.zero, true);
        core.ResetTracking();
        Assert.AreEqual(Vector3.zero, core.Step(new Vector3(100f, 0f, 0f), true));
    }

    // =====================================================================
    // DriftFusionCore
    // =====================================================================

    [Test]
    public void DriftFusionCore_MatchesReference_FrameByFrame()
    {
        var samples = ArRefSequences.Fusion();
        var firstFix = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        const float headingSeed = 17.5f;

        var reference = new RefFusion();
        reference.Initialize(firstFix, Vector3.zero, headingSeed, 1);
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        core.Initialize(firstFix, Vector3.zero, headingSeed);

        Vector3 refOrigin = Vector3.zero, coreOrigin = Vector3.zero;
        int refFixCount = 1, coreFixCount = 1;
        int corrections = 0, updates = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            reference.Update(refOrigin + s.camPos, s.fixCount, s.fix);

            bool hasNewFix = s.fixCount != coreFixCount;
            if (hasNewFix) { coreFixCount = s.fixCount; updates++; }
            core.Step(coreOrigin + s.camPos, hasNewFix, s.fix);
            refFixCount = s.fixCount;

            if (s.applyCorrection)
            {
                if (reference.ApplyDriftCorrection(s.dt, out var refShift)) refOrigin += refShift;
                if (core.TryComputeDriftCorrection(s.dt, out var coreShift)) { coreOrigin += coreShift; corrections++; }
            }

            Assert.AreEqual(reference.x.x, core.FusedEnu.x, 0f, $"fused.x @{i}");
            Assert.AreEqual(reference.x.y, core.FusedEnu.y, 0f, $"fused.y @{i}");
            Assert.AreEqual(reference.arEnu.x, core.ArOnlyEnu.x, 0f, $"arEnu.x @{i}");
            Assert.AreEqual(reference.p.x, core.EstimateStdDev.x * core.EstimateStdDev.x, 1e-5f, $"p.x @{i}");
            Assert.AreEqual(reference.headingDeg, core.HeadingDeg, 0f, $"heading @{i}");
            Assert.AreEqual(reference.HeadingRefined, core.HeadingRefined, $"refined @{i}");
            Assert.AreEqual(refOrigin.x, coreOrigin.x, 0f, $"origin.x @{i}");
            Assert.AreEqual(refOrigin.z, coreOrigin.z, 0f, $"origin.z @{i}");
        }

        Assert.That(updates, Is.GreaterThan(30), "sequence must deliver fixes");
        Assert.That(corrections, Is.GreaterThan(100), "sequence must apply corrections");
        Assert.IsTrue(core.HeadingRefined, "sequence must refine the heading at least once");
        Assert.AreEqual(refFixCount, coreFixCount);

        Console.WriteLine($"AR-GOLDEN fusion heading={core.HeadingDeg:R} fused=({core.FusedEnu.x:R},{core.FusedEnu.y:R}) " +
                          $"drift=({core.DriftError.x:R},{core.DriftError.y:R}) " +
                          $"sd=({core.EstimateStdDev.x:R},{core.EstimateStdDev.y:R}) " +
                          $"origin=({coreOrigin.x:R},{coreOrigin.z:R}) corrections={corrections} updates={updates}");
    }

    [Test]
    public void DriftFusionCore_Goldens()
    {
        var samples = ArRefSequences.Fusion();
        var firstFix = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        core.Initialize(firstFix, Vector3.zero, 17.5f);

        Vector3 origin = Vector3.zero;
        int fixCount = 1;
        foreach (var s in samples)
        {
            bool hasNewFix = s.fixCount != fixCount;
            if (hasNewFix) fixCount = s.fixCount;
            core.Step(origin + s.camPos, hasNewFix, s.fix);
            if (s.applyCorrection && core.TryComputeDriftCorrection(s.dt, out var shift)) origin += shift;
        }

        // Captured from the reference transcription at a FIXED dt of 1/30 s.
        // (The shipped-component capture in ArLegacyEquivalenceTests ran at the
        // editor's own Time.deltaTime, which is machine-dependent, so its
        // numbers are not reusable here — only the gate's are, since the gate
        // has no dt in it at all. The frame-by-frame equivalence test above is
        // what actually pins the fusion.)
        Assert.AreEqual(18.342041f, core.HeadingDeg, 1e-4f);
        Assert.AreEqual(42.394043f, core.FusedEnu.x, 1e-3f);
        Assert.AreEqual(17.1775742f, core.FusedEnu.y, 1e-3f);
        Assert.AreEqual(1.25849915f, core.EstimateStdDev.x, 1e-4f);
        Assert.IsTrue(core.HeadingRefined);
    }

    [Test]
    public void DriftFusionCore_NotReady_DoesNothing()
    {
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        core.Step(new Vector3(10f, 0f, 10f), false, default);
        Assert.AreEqual(Vector2.zero, core.FusedEnu);
        Assert.IsFalse(core.TryComputeDriftCorrection(Dt30, out var shift));
        Assert.AreEqual(Vector3.zero, shift);
    }

    [Test]
    public void DriftFusionCore_PoorFix_IsConsumedButNotApplied()
    {
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        core.Initialize(new GnssFix { latitude = 0, longitude = 0, horizontalAccuracy = 5f }, Vector3.zero, 0f);
        var before = core.FusedEnu;
        // 40 m accuracy > maxUsableAccuracy (25) — must not move the estimate.
        core.Step(Vector3.zero, true, new GnssFix { latitude = 0.001, longitude = 0.001, horizontalAccuracy = 40f });
        Assert.AreEqual(before, core.FusedEnu);
        Assert.IsFalse(core.HeadingRefined, "a rejected fix must not feed heading refinement");
    }

    [Test]
    public void DriftFusionCore_KalmanUpdate_PullsTowardTheFix()
    {
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        var origin = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        core.Initialize(origin, Vector3.zero, 0f);

        // A fix 20 m north of the origin, tight accuracy: p0 = 25, r = 9,
        // so k = 25/34 ≈ 0.735 and the state should land at ~14.7 m.
        DriftFusionCore.EnuToGeo(new Vector2(0f, 20f), origin.latitude, origin.longitude,
            out double lat, out double lon);
        core.Step(Vector3.zero, true,
            new GnssFix { latitude = lat, longitude = lon, horizontalAccuracy = 3f });

        Assert.AreEqual(20f * (25f / 34f), core.FusedEnu.y, 0.05f);
        Assert.That(core.EstimateStdDev.y, Is.LessThan(5f), "variance must shrink after an update");
        Assert.AreEqual(core.FusedEnu.y, core.DriftError.y, 1e-4f, "AR contributed nothing, so all of it is drift");
    }

    [Test]
    public void DriftFusionCore_DriftCorrection_IsSpeedCapped()
    {
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        var origin = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        core.Initialize(origin, Vector3.zero, 0f);
        DriftFusionCore.EnuToGeo(new Vector2(0f, 50f), origin.latitude, origin.longitude,
            out double lat, out double lon);
        core.Step(Vector3.zero, true,
            new GnssFix { latitude = lat, longitude = lon, horizontalAccuracy = 3f });

        Assert.IsTrue(core.TryComputeDriftCorrection(Dt30, out var shift));
        Assert.AreEqual(DriftFusionCore.Settings.Defaults.maxCorrectionSpeed * Dt30,
            shift.magnitude, 1e-5f, "one frame of correction must not exceed maxCorrectionSpeed*dt");
    }

    [Test]
    public void DriftFusionCore_DriftCorrection_ShrinksDriftAndDoesNotPolluteThePredictStep()
    {
        var core = new DriftFusionCore { Config = DriftFusionCore.Settings.Defaults };
        var origin = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        core.Initialize(origin, Vector3.zero, 0f);
        DriftFusionCore.EnuToGeo(new Vector2(0f, 50f), origin.latitude, origin.longitude,
            out double lat, out double lon);
        core.Step(Vector3.zero, true,
            new GnssFix { latitude = lat, longitude = lon, horizontalAccuracy = 3f });

        float before = core.DriftError.magnitude;
        Vector3 rigOrigin = Vector3.zero;
        for (int i = 0; i < 100; i++)
        {
            // The camera never moves in the session frame; the rig does.
            if (core.TryComputeDriftCorrection(Dt30, out var shift)) rigOrigin += shift;
            core.Step(rigOrigin, false, default);
        }
        Assert.That(core.DriftError.magnitude, Is.LessThan(before - 0.5f),
            "bleeding the correction must actually reduce the believed drift");
    }

    [Test]
    public void DriftFusionCore_GeoEnuRoundTrip()
    {
        var core = new DriftFusionCore();
        core.Initialize(new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f },
            Vector3.zero, 0f);
        var enu = new Vector2(137.5f, -92.25f);
        core.EnuToGeo(enu, out double lat, out double lon);
        var back = core.GeoToEnu(lat, lon);
        Assert.AreEqual(enu.x, back.x, 0.01f);
        Assert.AreEqual(enu.y, back.y, 0.01f);
    }

    [Test]
    public void DriftFusionCore_UnityXZToEnu_RoundTripsAtEveryHeading()
    {
        for (float h = -180f; h <= 180f; h += 17f)
        {
            var v = new Vector2(3f, -7f);
            var back = DriftFusionCore.EnuToUnityXZ(DriftFusionCore.UnityXZToEnu(v, h), h);
            Assert.AreEqual(v.x, back.x, 1e-4f, $"x @heading {h}");
            Assert.AreEqual(v.y, back.y, 1e-4f, $"y @heading {h}");
        }
    }

    [Test]
    public void DriftFusionCore_UnityXZToEnu_NorthAtZeroHeading()
    {
        // Unity +Z pointing at azimuth 0 => forward is north, right is east.
        var enu = DriftFusionCore.UnityXZToEnu(new Vector2(0f, 1f), 0f);
        Assert.AreEqual(0f, enu.x, 1e-5f);
        Assert.AreEqual(1f, enu.y, 1e-5f);
        // Unity +Z pointing east => forward is east.
        enu = DriftFusionCore.UnityXZToEnu(new Vector2(0f, 1f), 90f);
        Assert.AreEqual(1f, enu.x, 1e-5f);
        Assert.AreEqual(0f, enu.y, 1e-5f);
    }

    // =====================================================================
    // HeadingSeedAccumulator
    // =====================================================================

    [Test]
    public void HeadingSeed_NoSamples_ReturnsFallback()
    {
        var seed = new HeadingSeedAccumulator();
        Assert.AreEqual(0f, seed.ResolveDeg(0f));
        Assert.AreEqual(42f, seed.ResolveDeg(42f));
    }

    [Test]
    public void HeadingSeed_AveragesAcrossNorthWithoutWrapping()
    {
        var seed = new HeadingSeedAccumulator();
        seed.Add(359f);
        seed.Add(1f);
        // The naive arithmetic mean would be 180 — the whole point of the
        // circular mean is that this is ~0.
        Assert.AreEqual(0f, Mathf.DeltaAngle(0f, seed.ResolveDeg()), 0.01f);
        Assert.AreEqual(2, seed.Samples);
    }

    [Test]
    public void HeadingSeed_AveragesOrdinaryHeadings()
    {
        var seed = new HeadingSeedAccumulator();
        for (int i = 0; i < 20; i++) seed.Add(120f + (i % 2 == 0 ? 3f : -3f));
        Assert.AreEqual(120f, seed.ResolveDeg(), 0.05f);
    }
}
