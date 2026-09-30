using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Component-level tests for the refactored AR stabilization MonoBehaviours.
///
/// Two jobs, both of which the core-only tests cannot do:
///
///  1. Pin that the SHELLS still produce the pre-refactor behaviour end to end —
///     Transform reads, the XR Origin write, the feedback loop, the coroutine —
///     not just that the extracted maths matches. ArCoreCharacterizationTests
///     pins core == reference; this pins component == reference, so the chain
///     component == core == pre-refactor shipped code is closed permanently
///     rather than only at the moment of the refactor.
///
///  2. Prove the ON-DEVICE TUNING PANEL still works. The panel writes the public
///     fields live while the app runs, and the refactor introduced a copy step
///     (fields -> core settings). If that copy happened once instead of every
///     frame, every slider on the beach would silently stop doing anything — a
///     failure that would only show up in the field. So: change a field
///     mid-sequence and assert the behaviour changes on the next frame.
/// </summary>
public class ArStabilizationComponentTests
{
    GameObject originGo;
    XROrigin xrOrigin;
    Transform cam;

    [SetUp]
    public void SetUp()
    {
        // XROrigin logs about the rig it cannot find in an EditMode scene; none of
        // it matters here (only transform.position is read).
        LogAssert.ignoreFailingMessages = true;

        originGo = new GameObject("XR Origin (test)");
        xrOrigin = originGo.AddComponent<XROrigin>();
        var camGo = new GameObject("Main Camera (test)");
        camGo.transform.SetParent(originGo.transform, false);
        cam = camGo.transform;
    }

    [TearDown]
    public void TearDown()
    {
        if (originGo != null) UnityEngine.Object.DestroyImmediate(originGo);
        LogAssert.ignoreFailingMessages = false;
    }

    // ---------------------------------------------------------------- helpers

    static void Poke(object target, string name, object value)
    {
        var t = target.GetType();
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             ?? t.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(f, Is.Not.Null, $"no field '{name}' on {t.Name}");
        f.SetValue(target, value);
    }

    static object Call(object target, string name, params object[] args)
    {
        var m = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(m, Is.Not.Null, $"no method '{name}' on {target.GetType().Name}");
        return m.Invoke(target, args);
    }

    // =====================================================================
    // SpuriousMotionGate
    // =====================================================================

    (SpuriousMotionGate gate, StillnessDetector stillness) MakeGate()
    {
        var go = new GameObject("gate-host");
        go.transform.SetParent(originGo.transform, false);
        var stillness = go.AddComponent<StillnessDetector>();
        var gate = go.AddComponent<SpuriousMotionGate>();
        gate.stillness = stillness;
        gate.xrOrigin = xrOrigin;
        gate.arCamera = cam;
        Call(gate, "OnEnable");
        return (gate, stillness);
    }

    [Test]
    public void Gate_Component_MatchesTheReferenceImplementation()
    {
        var (gate, stillness) = MakeGate();

        // The golden below is the ALGORITHM's output at MotionGateCore.Settings.
        // Defaults, which is where ArCoreCharacterizationTests records it — NOT at
        // whatever the component happens to author today. Those two diverged the day
        // the shipped relocalizationJumpThreshold went to 0 (the harness measured 0
        // as better than 0.35), and this test silently started goldening a different
        // configuration than the one the number came from. Pinning the settings here
        // is what keeps the chain component == core == pre-refactor code closed
        // across future default changes; the component's own defaults are pinned
        // separately, by MotionGateBudgetTests and the tuning round-trip.
        gate.minDelta = MotionGateCore.Settings.Defaults.minDelta;
        gate.relocalizationJumpThreshold = MotionGateCore.Settings.Defaults.relocalizationJumpThreshold;
        gate.correctionBudgetPerMeter = MotionGateCore.Settings.Defaults.correctionBudgetPerMeter;

        var reference = new RefGate
        {
            minDelta = gate.minDelta,
            relocalizationJumpThreshold = gate.relocalizationJumpThreshold,
        };

        Vector3 refOrigin = Vector3.zero, session = Vector3.zero;
        var samples = ArRefSequences.Gate();
        for (int i = 0; i < samples.Length; i++)
        {
            session += samples[i].camDelta;

            // The camera is a CHILD of the origin, so the counter-shift feeds back
            // into what the gate measures next frame — the real rig, and the thing
            // MotionGateCore's lastCamPos bookkeeping only makes sense inside.
            cam.localPosition = session;
            Poke(stillness, "IsStill", samples[i].isStill);
            Call(gate, "LateUpdate");

            reference.LateUpdate(refOrigin + session, samples[i].isStill, ref refOrigin);

            Assert.AreEqual(refOrigin.x, originGo.transform.position.x, 1e-5f, $"origin.x @{i}");
            Assert.AreEqual(refOrigin.y, originGo.transform.position.y, 1e-5f, $"origin.y @{i}");
            Assert.AreEqual(refOrigin.z, originGo.transform.position.z, 1e-5f, $"origin.z @{i}");
            Assert.AreEqual(reference.TotalSuppressed.magnitude, gate.TotalSuppressed.magnitude, 1e-5f,
                $"TotalSuppressed @{i}");
        }

        // Same golden as ArCoreCharacterizationTests, reached through the real
        // component and a real XROrigin.
        Assert.AreEqual(-0.11236544f, originGo.transform.position.x, 1e-5f);
        Assert.AreEqual(0.0157373529f, originGo.transform.position.y, 1e-5f);
        Assert.AreEqual(-0.0631216f, originGo.transform.position.z, 1e-5f);
    }

    [Test]
    public void Gate_LiveFieldEdits_TakeEffectOnTheVeryNextFrame()
    {
        var (gate, stillness) = MakeGate();
        Poke(stillness, "IsStill", true);

        Vector3 session = Vector3.zero;
        void Frame(float dz) { session.z += dz; cam.localPosition = session; Call(gate, "LateUpdate"); }

        Frame(0f);                               // seed lastCamPos
        Frame(0.01f);                            // 1 cm: above the 0.5 mm minDelta
        float afterSuppression = originGo.transform.position.z;
        Assert.That(afterSuppression, Is.LessThan(-0.005f), "default settings must suppress 1 cm");

        // --- the tuning-panel move: raise minDelta above the motion -----------
        gate.minDelta = 0.5f;
        Frame(0.01f);
        Assert.AreEqual(afterSuppression, originGo.transform.position.z, 1e-6f,
            "raising minDelta must stop suppression on the NEXT frame — if the core's " +
            "settings were copied once instead of per frame, every on-device slider would " +
            "silently stop working");

        // --- and back again ---------------------------------------------------
        gate.minDelta = 0.0005f;
        Frame(0.01f);
        Assert.That(originGo.transform.position.z, Is.LessThan(afterSuppression - 0.005f),
            "lowering minDelta again must resume suppression immediately");

        // --- the other tunable ------------------------------------------------
        float before = originGo.transform.position.z;
        gate.relocalizationJumpThreshold = 0.005f;   // now 1 cm counts as a relocalization
        Frame(0.01f);
        Assert.AreEqual(before, originGo.transform.position.z, 1e-6f,
            "lowering relocalizationJumpThreshold must let the motion through at once");
    }

    [Test]
    public void Gate_MissingReferences_DoNothingAndDoNotSeedTracking()
    {
        var (gate, stillness) = MakeGate();
        Poke(stillness, "IsStill", true);
        gate.arCamera = null;

        cam.localPosition = new Vector3(0f, 0f, 5f);
        Call(gate, "LateUpdate");
        Assert.AreEqual(Vector3.zero, originGo.transform.position);

        // Re-wiring must not treat the gap as a 5 m jump.
        gate.arCamera = cam;
        Call(gate, "LateUpdate");
        Assert.AreEqual(Vector3.zero, originGo.transform.position, "first frame only seeds");
    }

    // =====================================================================
    // StillnessDetector
    // =====================================================================

    [Test]
    public void Stillness_WithoutSensors_IsInert()
    {
        // The documented editor behaviour: no IMU devices, so HasSensors stays
        // false, IsStill stays false and the gate is inert. Start() warns.
        var go = new GameObject("stillness-host");
        try
        {
            var detector = go.AddComponent<StillnessDetector>();
            Call(detector, "Start");
            Assert.IsFalse(detector.HasSensors);

            Poke(detector, "IsStill", true);   // pretend something set it
            Call(detector, "Update");
            Assert.IsFalse(detector.IsStill, "the no-sensor guard must force IsStill false");
            Assert.AreEqual(0f, detector.SmoothedGyro);
            Assert.AreEqual(0f, detector.SmoothedAccel);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // =====================================================================
    // GpsArKalmanFusion — driven through its real coroutine
    // =====================================================================

    [Test]
    public void Fusion_Component_MatchesTheReferenceImplementation_AndHonoursLiveEdits()
    {
        var hostGo = new GameObject("fusion-host");
        hostGo.transform.SetParent(originGo.transform, false);
        var gnss = hostGo.AddComponent<GnssProvider>();
        var fusion = hostGo.AddComponent<GpsArKalmanFusion>();
        fusion.arCamera = cam;
        fusion.xrOrigin = xrOrigin;
        fusion.gnss = gnss;
        // Skip the compass average so the heading seed is deterministically 0
        // (no samples -> the accumulator's fallback) and the coroutine completes
        // synchronously instead of spinning for two real seconds.
        fusion.compassAverageSeconds = 0f;

        var firstFix = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        Poke(gnss, "Latest", firstFix);
        Poke(gnss, "FixCount", 1);

        // Run the REAL Start() coroutine to completion.
        var routine = (IEnumerator)Call(fusion, "Start");
        int guard = 0;
        while (routine.MoveNext() && guard++ < 10000) { }
        Assert.IsTrue(fusion.Ready, "the coroutine should have initialised the filter");
        Assert.AreEqual(0f, fusion.HeadingDeg, 1e-6f, "no compass samples -> heading seed 0");

        var reference = new RefFusion
        {
            processNoisePerMeter = fusion.processNoisePerMeter,
            minGpsAccuracy = fusion.minGpsAccuracy,
            maxUsableAccuracy = fusion.maxUsableAccuracy,
            maxCorrectionSpeed = fusion.maxCorrectionSpeed,
            headingSegmentMeters = fusion.headingSegmentMeters,
            headingBlend = fusion.headingBlend,
        };
        reference.Initialize(firstFix, Vector3.zero, 0f, 1);

        float dt = Time.deltaTime;   // whatever the editor last measured; shared with the reference
        var samples = ArRefSequences.Fusion();
        bool tweaked = false;

        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            Poke(gnss, "Latest", s.fix);
            Poke(gnss, "FixCount", s.fixCount);
            cam.localPosition = s.camPos;

            // Halfway through, retune from "the panel" and push the identical
            // change into the reference. If the component only copied its fields
            // into the core once, the two would diverge from here on.
            if (!tweaked && i == samples.Length / 2)
            {
                tweaked = true;
                fusion.processNoisePerMeter = 0.4f;
                fusion.maxCorrectionSpeed = 0.05f;
                reference.processNoisePerMeter = 0.4f;
                reference.maxCorrectionSpeed = 0.05f;
            }

            Call(fusion, "Update");
            reference.Update(originGo.transform.position + s.camPos, s.fixCount, s.fix);

            if (s.applyCorrection)
            {
                fusion.ApplyDriftCorrection();
                // The reference's shift is DISCARDED on purpose: the component has
                // already moved the real XR Origin, and the reference reads that
                // same origin back at the top of the next iteration. The call is
                // still needed so the reference advances its own internal state
                // (arEnu, lastCamPos, anchorCamPos) exactly as the core does.
                reference.ApplyDriftCorrection(dt, out _);
            }

            // Looser tolerances than the core-vs-reference test (which is exact):
            // here the position round-trips through a Transform, so the two paths
            // accumulate float error differently.
            Assert.AreEqual(reference.DriftError.x, fusion.DriftError.x, 2e-3f, $"drift.x @{i}");
            Assert.AreEqual(reference.DriftError.y, fusion.DriftError.y, 2e-3f, $"drift.y @{i}");
            Assert.AreEqual(reference.headingDeg, fusion.HeadingDeg, 1e-2f, $"heading @{i}");
            Assert.AreEqual(reference.HeadingRefined, fusion.HeadingRefined, $"refined @{i}");
            Assert.AreEqual(reference.EstimateStdDev.x, fusion.EstimateStdDev.x, 1e-3f, $"sd.x @{i}");
        }

        Assert.IsTrue(tweaked);
        Assert.IsTrue(fusion.HeadingRefined, "the sequence should refine the heading at least once");

        // The retune must actually have changed the filter, or the test proves
        // nothing about live edits: 0.4 m^2/m grows the variance 8x faster than
        // the 0.05 default over the same walked distance.
        Assert.That(fusion.EstimateStdDev.x, Is.GreaterThan(1f),
            "with processNoisePerMeter raised to 0.4 the estimate variance must be visibly larger");

        UnityEngine.Object.DestroyImmediate(hostGo);
    }

    [Test]
    public void Fusion_WithoutReferences_DisablesItselfQuietly()
    {
        var go = new GameObject("fusion-bare");
        try
        {
            var fusion = go.AddComponent<GpsArKalmanFusion>();
            var routine = (IEnumerator)Call(fusion, "Start");
            while (routine.MoveNext()) { }
            Assert.IsFalse(fusion.Ready);

            // Every public accessor must stay safe to call from the tuning panel.
            Assert.AreEqual(Vector2.zero, fusion.DriftError);
            Assert.AreEqual(Vector2.zero, fusion.FusedEnu);
            Assert.AreEqual(0f, fusion.HeadingDeg);
            Assert.IsFalse(fusion.HeadingRefined);
            Assert.AreEqual(Vector2.zero, fusion.EstimateStdDev);
            fusion.ApplyDriftCorrection();   // must not throw
            Call(fusion, "Update");          // must not throw
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // =====================================================================
    // The controller's telemetry contract
    // =====================================================================

    [Test]
    public void Controller_Telemetry_ReadsEveryPropertyThePanelShows()
    {
        // BuildTelemetry() touches IsStill/SmoothedGyro/SmoothedAccel/HasSensors/
        // TotalSuppressed/Ready/DriftError/EstimateStdDev/HeadingDeg/
        // HeadingRefined/Latest/HasFix/FixCount/UsingNativeGnss/FixAgeSeconds.
        // Renaming any of them compiles fine here and breaks the panel, so the
        // cheapest guard is to actually call it.
        var go = new GameObject("ar-stab-telemetry");
        try
        {
            var controller = go.AddComponent<ArStabilizationController>();
            controller.stillness = go.AddComponent<StillnessDetector>();
            controller.gate = go.AddComponent<SpuriousMotionGate>();
            controller.gnss = go.AddComponent<GnssProvider>();
            controller.fusion = go.AddComponent<GpsArKalmanFusion>();

            string telemetry = controller.BuildTelemetry();
            Assert.That(telemetry, Does.Contain("IMU"));
            Assert.That(telemetry, Does.Contain("Gate"));
            Assert.That(telemetry, Does.Contain("GNSS"));
            Assert.That(telemetry, Does.Contain("Fus"));
            Assert.That(telemetry, Does.Contain("Corre"));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
