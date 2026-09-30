using System.Collections.Generic;
using System.Linq;
using MergulhoVirtual.ArSim;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Scenario-level tests for the offline AR drift harness.
///
/// These deliberately assert INVARIANTS, not today's numbers: the metrics are
/// supposed to move when someone tunes an algorithm or re-bases the wave model
/// against new measurements, and a test that pins them would just have to be
/// rewritten every time. What must NOT move is "walking preserves real motion",
/// "a calm scenario suppresses nothing", "the phantom drift stays seaward when
/// you pan", "the same seed gives the same answer".
///
/// Durations are scaled down hard so the whole file runs in a couple of seconds;
/// `make ar-sim` runs the full-length versions.
/// </summary>
public class ArSimTests
{
    /// <summary>Short runs for tests: 90 s of scenario becomes ~9 s.</summary>
    const float TestScale = 0.1f;

    static ArSimScenario Scenario(string id, float scale = TestScale) =>
        ArSimScenarios.All(scale).First(s => s.Id == id);

    static ArSimResult Run(string id, IArStabilizer stabilizer, float scale = TestScale) =>
        ArSimRunner.Run(Scenario(id, scale), stabilizer, keepFrames: false).Result;

    // =====================================================================
    // Determinism — the property everything else depends on
    // =====================================================================

    [Test]
    public void Simulator_IsDeterministic_ForAFixedSeed()
    {
        foreach (var id in new[] { "still-waves", "pan-waves", "walk-waves" })
        {
            var a = Run(id, new ShippedStabilizer());
            var b = Run(id, new ShippedStabilizer());

            Assert.AreEqual(a.DriftDegMean, b.DriftDegMean, 0f, $"{id} driftMean");
            Assert.AreEqual(a.DriftDegP95, b.DriftDegP95, 0f, $"{id} driftP95");
            Assert.AreEqual(a.JitterDegPerSec, b.JitterDegPerSec, 0f, $"{id} jitter");
            Assert.AreEqual(a.PosErrRmsM, b.PosErrRmsM, 0f, $"{id} posRms");
            Assert.AreEqual(a.SuppressedM, b.SuppressedM, 0f, $"{id} suppressed");
            Assert.AreEqual(a.PerceptualCost, b.PerceptualCost, 0f, $"{id} cost");
        }
    }

    [Test]
    public void Simulator_DifferentStabilizers_ShareTheSameWorld()
    {
        // The ground truth and the phantom error must not depend on who is
        // correcting: otherwise the comparison is meaningless. Noop never moves
        // the origin, so its camera error IS the raw phantom error.
        var noop = Run("still-waves", new NoopStabilizer());
        var again = Run("still-waves", new NoopStabilizer());
        Assert.AreEqual(noop.PosErrRmsM, again.PosErrRmsM, 0f);
        Assert.That(noop.SuppressedM, Is.EqualTo(0f), "noop must never move the origin");
    }

    // =====================================================================
    // Scenario invariants
    // =====================================================================

    [Test]
    public void StillCalm_ShippedStabilizer_SuppressesApproximatelyNothing()
    {
        var r = Run("still-calm", new ShippedStabilizer());
        // Some suppression is expected and correct — postural sway and the
        // tracker's own jitter are real motion the gate sees while the IMU says
        // "still" — but it must stay in the centimetre range over a whole run.
        Assert.That(r.SuppressedM, Is.LessThan(0.5f),
            $"a calm scenario should barely move the world, moved {r.SuppressedM:F3} m");
        Assert.That(r.DriftDegP95, Is.LessThan(2f),
            $"calm p95 apparent drift should be small, was {r.DriftDegP95:F2} deg");
    }

    [Test]
    public void WalkCalm_RealDisplacementSurvives_ForEveryStabilizer()
    {
        foreach (var stabilizer in ArStabilizerRegistry.Create())
        {
            // A candidate that DECLARES it throws translation away (rotation-only)
            // is exempt: losing real motion is its premise, not a defect, and the
            // composite already charges it for that through the fidelity penalty.
            // Everything else must pass real displacement through.
            if (stabilizer is IDiscardsTranslation) continue;

            var r = Run("walk-calm", stabilizer);
            Assert.That(r.RealMotionFidelity, Is.Not.NaN,
                $"{stabilizer.Name}: walk-calm must contain walking");
            Assert.That(r.RealMotionFidelity, Is.InRange(0.85f, 1.15f),
                $"{stabilizer.Name} ate or invented real motion: fidelity {r.RealMotionFidelity:F3}");
        }
    }

    [Test]
    public void StillWavesNoJump_Gate_CutsThePositionErrorItWasBuiltFor()
    {
        // The gate's contract: while the IMU says the phone is still, cancel the
        // tracker's phantom translation. Measured on the JUMP-FREE variant so the
        // result is the gate's effect on the wave oscillation alone — with jumps
        // enabled the gate BANKS every above-threshold jump into a permanent
        // offset, which at nominal jump rates cancels out most of the win. That
        // interaction is a finding, not an invariant, so it is reported by
        // `make ar-sim` rather than asserted here.
        var noop = Run("still-waves-nojump", new NoopStabilizer());
        var gate = Run("still-waves-nojump", new ShippedStabilizer { driftCorrectionEnabled = false });

        Assert.That(gate.SuppressedM, Is.GreaterThan(0.05f),
            "the gate must actually fire in the scenario it targets");
        Assert.That(gate.PosErrRmsM, Is.LessThan(noop.PosErrRmsM * 0.5f),
            $"the gate must at least halve the wave-induced position error: " +
            $"noop {noop.PosErrRmsM:F3} m vs gate {gate.PosErrRmsM:F3} m");
    }

    [Test]
    public void PanWaves_TurnsTheStillnessGateOff()
    {
        // The finding the harness exists to make legible: sweeping the phone
        // makes the gyro loud, IsStill goes false, and the translation gate stops
        // suppressing even though the user never travelled anywhere.
        var still = Run("still-waves", new ShippedStabilizer());
        var pan = Run("pan-waves", new ShippedStabilizer());

        Assert.That(pan.StillFraction, Is.LessThan(still.StillFraction),
            $"panning must reduce the still fraction (still {still.StillFraction:P0}, " +
            $"pan {pan.StillFraction:P0})");
    }

    [Test]
    public void PanScenario_ProducesRealTranslation_NotPureRotation()
    {
        // A phone pivots around the wrist/elbow/torso, not its optical centre,
        // so a sweep genuinely translates the camera. Modelling a pan as pure
        // rotation would make the scenario unrealistically easy.
        var track = GroundTruthTrack.Build(Scenario("pan-waves", 1f).Motion, 30f, 7);
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < track.FrameCount; i++)
        {
            float x = track.Pos[i].x, z = track.Pos[i].z;
            min = Mathf.Min(min, x); max = Mathf.Max(max, x);
            min = Mathf.Min(min, z); max = Mathf.Max(max, z);
        }
        float span = max - min;
        Assert.That(span, Is.InRange(0.05f, 0.60f),
            $"pan translation span {span:F3} m outside the plausible wrist/elbow/torso range");
    }

    [Test]
    public void GroundTruth_StandingIsNeverPerfectlyStill()
    {
        var track = GroundTruthTrack.Build(Scenario("still-waves", 1f).Motion, 30f, 11);
        float maxSpeed = 0f;
        for (int i = 0; i < track.FrameCount; i++) maxSpeed = Mathf.Max(maxSpeed, track.SpeedMps[i]);
        Assert.That(maxSpeed, Is.GreaterThan(0.001f), "postural sway must be present");
        Assert.That(maxSpeed, Is.LessThan(GroundTruthTrack.WalkingSpeedThresholdMps),
            "standing must not register as walking");
    }

    [Test]
    public void WalkWaves_TheGateIsInertByConstruction()
    {
        // Documented expectation, not an aspiration: IsStill is false while
        // walking, so the gate cannot suppress anything and the phantom drift
        // passes straight through. If this ever starts improving substantially,
        // the gate's design changed and the claim needs rechecking.
        var noop = Run("walk-waves", new NoopStabilizer());
        var gate = Run("walk-waves", new ShippedStabilizer { driftCorrectionEnabled = false });
        float improvement = (noop.DriftDegP95 - gate.DriftDegP95) / Mathf.Max(1e-3f, noop.DriftDegP95);
        Assert.That(improvement, Is.LessThan(0.5f),
            $"the gate is not supposed to rescue walking; improvement was {improvement:P0}");
    }

    // =====================================================================
    // The wave model — the doc's structural claims
    // =====================================================================

    [Test]
    public void WaveModel_PhantomDriftIsWorldFixed_NotCameraLocal()
    {
        // docs/ar-wave-drift-research.md §3.0: panning lowers the AMPLITUDE but
        // must never rotate the error. Drive the same model twice with the same
        // time base, once facing the sea and once facing 90° away, and check the
        // direction holds while the magnitude falls.
        Vector3 facing = Accumulate(cameraYawDeg: 0f);
        Vector3 turned = Accumulate(cameraYawDeg: 90f);

        Assert.That(facing.magnitude, Is.GreaterThan(turned.magnitude),
            "panning away must reduce the phantom amplitude");

        // Both must still point seaward (+Z here), not along the camera.
        var facingDir = new Vector2(facing.x, facing.z).normalized;
        var turnedDir = new Vector2(turned.x, turned.z).normalized;
        Assert.That(facingDir.y, Is.GreaterThan(0.7f), "seaward is +Z in this fixture");
        Assert.That(Vector2.Dot(facingDir, turnedDir), Is.GreaterThan(0.9f),
            "the error direction must not follow the camera");

        Vector3 Accumulate(float cameraYawDeg)
        {
            var model = WaveDriftModel.Nominal(seed: 99);
            model.seawardAzimuthDeg = 0f;
            model.jumpRateHz = 0f;          // isolate the deterministic part
            model.Reset();
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < 3000; i++) sum += model.Advance(1f / 30f, cameraYawDeg).PositionErrorM;
            return sum / 3000f;
        }
    }

    [Test]
    public void WaveModel_RatchetPointsSeaward()
    {
        // §1.4/§3.3: foam flows shoreward, so the tracker infers SEAWARD motion.
        var model = WaveDriftModel.Nominal(seed: 101);
        model.seawardAzimuthDeg = 0f;   // seaward = Unity +Z
        model.jumpRateHz = 0f;
        model.Reset();
        PhantomState last = default;
        for (int i = 0; i < 6000; i++) last = model.Advance(1f / 30f, 0f);

        Assert.That(last.RatchetM.z, Is.GreaterThan(0.01f),
            $"the ratchet must accumulate seaward, got {last.RatchetM}");
        Assert.That(Mathf.Abs(last.RatchetM.x), Is.LessThan(Mathf.Abs(last.RatchetM.z)),
            "the ratchet is cross-shore by definition");
    }

    [Test]
    public void WaveModel_RatchetIsCapped_NotARunaway()
    {
        var model = WaveDriftModel.Nominal(seed: 102);
        model.jumpRateHz = 0f;
        model.Reset();
        PhantomState last = default;
        for (int i = 0; i < 30000; i++) last = model.Advance(1f / 30f, 0f);   // ~17 min
        Assert.That(last.RatchetM.magnitude,
            Is.LessThanOrEqualTo(model.accumulationCapM + model.PulsePerWaveM + 0.01f),
            "free-running accumulation must stay capped (doc §3.3)");
    }

    [Test]
    public void WaveModel_AmplitudeIsDrivenByTheNearestWater()
    {
        // §1.2: translation information goes as 1/Z², so the regime is set by
        // the nearest moving water — which is why camera pitch matters so much.
        float benign = WaveDriftModel.Benign().AmplitudePpSeawardM;
        float nominal = WaveDriftModel.Nominal().AmplitudePpSeawardM;
        float bad = WaveDriftModel.Bad().AmplitudePpSeawardM;
        float catastrophic = WaveDriftModel.Catastrophic().AmplitudePpSeawardM;

        Assert.That(benign, Is.LessThan(nominal));
        Assert.That(nominal, Is.LessThan(bad));
        Assert.That(bad, Is.LessThan(catastrophic));

        // Each must land inside the doc §3.1 band it is named after.
        Assert.That(benign, Is.InRange(0.02f, 0.08f), $"benign {benign:F3} m outside doc band");
        Assert.That(nominal, Is.InRange(0.05f, 0.30f), $"nominal {nominal:F3} m outside doc band");
        Assert.That(bad, Is.InRange(0.2f, 0.8f), $"bad {bad:F3} m outside doc band");
        Assert.That(catastrophic, Is.InRange(0.5f, 3f), $"catastrophic {catastrophic:F3} m outside doc band");
    }

    [Test]
    public void WaveModel_AlphaMatchesTheDocsSceneTable()
    {
        // §1.2's worked examples: the nominal scene is a few percent, the
        // tilted-down-onto-swash scene is a genuine majority.
        Assert.That(WaveDriftModel.Nominal().AlphaInstantaneous, Is.InRange(0.01f, 0.10f));
        Assert.That(WaveDriftModel.Catastrophic().AlphaInstantaneous, Is.GreaterThan(0.3f));
        // Robust rejection still buys something in the nominal scene, and very
        // little once the water is the consensus set.
        var nom = WaveDriftModel.Nominal();
        var cat = WaveDriftModel.Catastrophic();
        Assert.That(nom.AlphaEffective / nom.AlphaInstantaneous, Is.LessThan(0.35f));
        Assert.That(cat.AlphaEffective / cat.AlphaInstantaneous,
            Is.GreaterThan(nom.AlphaEffective / nom.AlphaInstantaneous));
    }

    [Test]
    public void WaveModel_NetDriftPerWave_AgreesWithTheDocsReferenceValue()
    {
        // Two independent routes: the doc's yaml tabulates 0.05 m/wave, and the
        // route-1 arithmetic (alpha_eff × c × t_coherent × (1-relaxation))
        // computes it from the geometry. They should agree to within the width
        // of the doc's own uncertainty.
        float computed = WaveDriftModel.Nominal().NetDriftPerWaveM;
        Assert.That(computed, Is.InRange(0.02f, 0.10f),
            $"computed {computed:F4} m/wave outside doc §3.3's nominal 0.02-0.10 band");
    }

    [Test]
    public void WaveModel_ImuAccelBias_SetsTheAmplitudeCeiling()
    {
        // §3.2/§1.3: A_max = a_th/(2πf)². A phone with a worse accel bias admits
        // more phantom motion — the ~17× device spread. The ceiling only binds
        // where the visual forcing asks for more than the IMU would tolerate, so
        // measure it in the catastrophic regime.
        float Best = Rms(0.018f), Worst = Rms(0.206f);
        Assert.That(Worst, Is.GreaterThan(Best * 1.5f),
            $"accel bias must matter in the catastrophic regime (best {Best:F3} m, worst {Worst:F3} m)");

        float Rms(float bias)
        {
            var model = WaveDriftModel.Catastrophic(seed: 55);
            model.imuAccelBiasMps2 = bias;
            model.jumpRateHz = 0f;
            model.scaleDriftPerMinuteFrac = 0f;
            model.boreCelerityMps = 0f;    // isolate the oscillation
            model.Reset();
            double sq = 0; int n = 3000;
            for (int i = 0; i < n; i++)
            {
                var p = model.Advance(1f / 30f, 0f).PositionErrorM;
                sq += p.sqrMagnitude;
            }
            return Mathf.Sqrt((float)(sq / n));
        }
    }

    [Test]
    public void WaveModel_NothingAboveTheHalfHertzCeiling()
    {
        // §3.2 forbids simulated phantom translation above ~0.5 Hz: the
        // admissible amplitude there is below ARCore's own jitter floor.
        var model = WaveDriftModel.Nominal();
        foreach (var period in new[] { model.groundswellPeriodS, model.windSeaPeriodS, model.swashPeriodS })
            Assert.That(1f / period, Is.LessThan(WaveDriftModel.MaxSimulatedHz),
                $"band at {1f / period:F3} Hz exceeds the doc's ceiling");
    }

    [Test]
    public void WaveModel_Calm_ProducesEssentiallyNothing()
    {
        var model = WaveDriftModel.Calm(seed: 7);
        model.Reset();
        float max = 0f;
        for (int i = 0; i < 3000; i++)
            max = Mathf.Max(max, model.Advance(1f / 30f, 0f).PositionErrorM.magnitude);
        Assert.That(max, Is.LessThan(0.02f), $"calm preset drifted {max:F3} m");
    }

    [Test]
    public void TiltDownSwash_IsMuchWorseThanNominal_ForEveryStabilizer()
    {
        // The 1/Z² prediction made operational: pointing at the swash near your
        // feet must be dramatically worse than pointing at surf 15 m out.
        //
        // Asserted on POSITION error, not bearing. The phantom drift is seaward
        // and the user is looking seaward, so almost all of it lies along the line
        // of sight, where it changes the animal's apparent SIZE rather than its
        // bearing — a bearing-only assertion can even come out backwards. This is
        // exactly why the report carries a separate range% column.
        foreach (var stabilizer in ArStabilizerRegistry.Create())
        {
            var nominal = Run("still-waves", stabilizer);
            var swash = Run("tilt-down-swash", stabilizer);

            // A stabilizer that suppresses essentially EVERY frame in both scenarios
            // (rotation-only by construction, a step gate because no step is ever
            // taken in either) has thrown away the very signal this assertion is
            // about, so demanding it still reproduce the 1/Z^2 difference would be
            // demanding it fail. The stronger claim is asserted instead: it must have
            // removed most of the catastrophic regime's error rather than merely not
            // seen it.
            bool blind = stabilizer is IDiscardsTranslation ||
                         (nominal.StillFraction > 0.95f && swash.StillFraction > 0.95f);
            if (blind)
            {
                var rawSwash = Run("tilt-down-swash", new NoopStabilizer());
                Assert.That(swash.PosErrRmsM, Is.LessThan(rawSwash.PosErrRmsM * 0.5f),
                    $"{stabilizer.Name}: suppresses everywhere, so it must at least have " +
                    $"cut the swash error: {swash.PosErrRmsM:F3} m vs raw {rawSwash.PosErrRmsM:F3} m");
                continue;
            }

            Assert.That(swash.PosErrRmsM, Is.GreaterThan(nominal.PosErrRmsM * 3f),
                $"{stabilizer.Name}: swash posRMS {swash.PosErrRmsM:F3} m vs nominal {nominal.PosErrRmsM:F3} m");
            Assert.That(swash.RangeErrFracP95, Is.GreaterThan(nominal.RangeErrFracP95 * 3f),
                $"{stabilizer.Name}: swash range p95 {swash.RangeErrFracP95:P1} vs nominal {nominal.RangeErrFracP95:P1}");
        }
    }

    [Test]
    public void Registry_ShipsTheDecompositionVariants()
    {
        // The shipped stack's two halves can fail for opposite reasons in the
        // same scenario, so the sweep must report them separately or the totals
        // are undiagnosable.
        var names = ArStabilizerRegistry.Create().Select(s => s.Name).ToList();
        Assert.That(names, Does.Contain("noop"));
        Assert.That(names, Does.Contain("shipped"));
        Assert.That(names, Does.Contain("gate-only"));
        Assert.That(names, Does.Contain("drift-only"));
        Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count), "stabilizer names must be unique");
    }

    [Test]
    public void GateOnly_NeverMovesTheWorldWhileTheDriftCorrectionIsOff()
    {
        // Sanity on the decomposition itself: with driftCorrectionEnabled off,
        // every metre the world moved must be the gate's, or the two halves are
        // not actually separable and the diagnosis columns lie.
        var r = Run("still-waves", new ShippedStabilizer { Name = "gate-only", driftCorrectionEnabled = false });
        Assert.That(r.GateSuppressedM, Is.EqualTo(r.SuppressedM).Within(1e-3f),
            "with the correction off, total world movement must equal the gate's");
    }

    [Test]
    public void DriftOnly_NeverSuppresses()
    {
        var r = Run("still-waves-pan", new ShippedStabilizer { Name = "drift-only", gateEnabled = false });
        Assert.That(r.GateSuppressedM, Is.EqualTo(0f).Within(1e-6f));
    }

    // =====================================================================
    // Scoring
    // =====================================================================

    /// <summary>A stabilizer that cancels ALL camera motion. It scores perfectly
    /// on drift and is completely useless, so it is the test for whether the
    /// composite can be gamed by freezing the world.</summary>
    sealed class FreezeStabilizer : IArStabilizer
    {
        Vector3 locked;
        bool has;
        public string Name => "freeze";
        public string Describe() => "cancels all camera motion (test fixture)";
        public void Reset() { has = false; locked = Vector3.zero; }
        public void Step(in ArFrameSample sample, FakeArRig rig)
        {
            if (!has) { locked = rig.CameraWorldPos; has = true; return; }
            rig.OriginPos += locked - rig.CameraWorldPos;
        }
    }

    [Test]
    public void Scoring_FreezingTheWorld_WinsOnDriftButLosesOnCost()
    {
        var noopWalk = Run("walk-waves", new NoopStabilizer());
        var freezeWalk = Run("walk-waves", new FreezeStabilizer());

        Assert.That(freezeWalk.RealMotionFidelity, Is.LessThan(0.15f),
            "the freeze fixture is supposed to eat real motion");
        Assert.That(freezeWalk.PerceptualCost, Is.GreaterThan(noopWalk.PerceptualCost),
            "a stabilizer that freezes the world must not beat doing nothing on cost");
    }

    [Test]
    public void Composite_IsTimeWeighted_NotAnUnweightedMean()
    {
        var results = new List<ArSimResult>
        {
            new ArSimResult { Profile = SessionProfile.Standing, PerceptualCost = 10f },
            new ArSimResult { Profile = SessionProfile.StandingPanning, PerceptualCost = 20f },
            new ArSimResult { Profile = SessionProfile.Walking, PerceptualCost = 30f },
            new ArSimResult { Profile = SessionProfile.Excluded, PerceptualCost = 1000f },
        };

        float expected = SessionTimeWeights.Standing * 10f
                       + SessionTimeWeights.StandingPanning * 20f
                       + SessionTimeWeights.Walking * 30f;
        Assert.AreEqual(expected, ArSimScoring.Composite(results), 1e-4f);
        Assert.That(Mathf.Abs(ArSimScoring.Composite(results) - 20f), Is.GreaterThan(1f),
            "an unweighted mean would be 20 — the weighting must actually bite");
    }

    [Test]
    public void SessionTimeWeights_SumToOne()
    {
        Assert.AreEqual(1f,
            SessionTimeWeights.Standing + SessionTimeWeights.StandingPanning + SessionTimeWeights.Walking,
            1e-5f);
    }

    // =====================================================================
    // The sweep plumbing
    // =====================================================================

    [Test]
    public void Sweep_ProducesARowForEveryScenarioAndStabilizer()
    {
        var scenarios = ArSimScenarios.All(TestScale);
        var report = ArSimHarness.RunSweep(scenarios, filter: null, outDir: null, writeFiles: false);
        string text = string.Join("\n", report);

        foreach (var s in scenarios)
            Assert.That(text, Does.Contain(s.Id), $"report is missing scenario {s.Id}");
        foreach (var st in ArStabilizerRegistry.Create())
            Assert.That(text, Does.Contain(st.Name), $"report is missing stabilizer {st.Name}");

        Assert.That(text, Does.Contain("COMPOSITE"), "the composite block must be in the report");
        Assert.That(text, Does.Contain(SessionTimeWeights.Describe()),
            "the weights must be printed so the composite is never read as an unweighted average");
    }

    [Test]
    public void Sweep_Filter_SelectsASubset()
    {
        var report = ArSimHarness.RunSweep(ArSimScenarios.All(TestScale), "pan", null, false);
        string text = string.Join("\n", report);
        Assert.That(text, Does.Contain("pan-waves"));
        Assert.That(text, Does.Not.Contain("walk-calm"));
    }

    [Test]
    public void EveryScenario_RunsAndProducesFiniteMetrics()
    {
        foreach (var scenario in ArSimScenarios.All(TestScale))
        {
            foreach (var stabilizer in ArStabilizerRegistry.Create())
            {
                var r = ArSimRunner.Run(scenario, stabilizer, keepFrames: false).Result;
                Assert.That(r.Frames, Is.GreaterThan(10), $"{scenario.Id}/{stabilizer.Name}: too few frames");
                Assert.IsFalse(float.IsNaN(r.DriftDegMean), $"{scenario.Id}/{stabilizer.Name}: NaN drift");
                Assert.IsFalse(float.IsNaN(r.JitterDegPerSec), $"{scenario.Id}/{stabilizer.Name}: NaN jitter");
                Assert.IsFalse(float.IsNaN(r.PerceptualCost), $"{scenario.Id}/{stabilizer.Name}: NaN cost");
                Assert.That(r.PosErrRmsM, Is.LessThan(100f), $"{scenario.Id}/{stabilizer.Name}: runaway");
            }
        }
    }

    // =====================================================================
    // The GENUINE error channel — the thing the harness could not previously
    // charge anything for. See the genuine-channel block in WaveDriftModel.
    // =====================================================================

    /// <summary>
    /// A walk-then-stand track: travel accumulates error while any translation
    /// gate is inert by design, then the user stops and the gate freezes whatever
    /// is there. Deliberately built here rather than taken from the scenario table
    /// so the test states its own premise and cannot be broken by a scenario edit.
    /// </summary>
    static List<MotionSegment> WalkThenStand() => new List<MotionSegment>
    {
        MotionSegment.Standing(2f),
        MotionSegment.Walking(25f),
        MotionSegment.Standing(60f),
    };

    static ArSimScenario GenuineChannelScenario(float genuineJumpFraction)
    {
        var wave = WaveDriftModel.Nominal(seed: 4242);
        wave.genuineJumpFraction = genuineJumpFraction;
        wave.jumpRateHz = 0.10f;
        // TOP of the measured bracket (Feigl's 14.4 cm/m), not the default 3 %/m.
        // Deliberate: the equilibrium genuine error is growth-rate x inter-jump
        // interval, so at the default rate it settles around 0.2 m and BOTH
        // thresholds suppress every genuine correction — which is itself a finding,
        // reported by the sweep, but it would make this test pass for the wrong
        // reason. The test's job is to prove the MECHANISM exists at all, so it is
        // run where the mechanism is unambiguously visible.
        wave.genuineDriftPerMeterPath = 0.10f;
        return new ArSimScenario
        {
            Id = "genuine-probe",
            Profile = SessionProfile.Excluded,
            Motion = WalkThenStand(),
            Wave = wave,
            Gnss = new SyntheticGnss { seed = 4243 },
            Imu = new SyntheticImu { seed = 4244 },
            Repeats = 1,
        };
    }

    static ShippedStabilizer GateWithThreshold(float thresholdM, string name) => new ShippedStabilizer
    {
        Name = name,
        driftCorrectionEnabled = false,
        Gate = new MotionGateCore.Settings
        {
            minDelta = MotionGateCore.Settings.Defaults.minDelta,
            relocalizationJumpThreshold = thresholdM,
        },
    };

    [Test]
    public void GenuineChannel_AccumulatesWithTravel_NotWithTime()
    {
        // The whole premise: odometry error grows with PATH (doc SS1.6, Feigl's
        // "quasi-directly proportional to the path length"), so standing still
        // must not grow it and walking must.
        var model = WaveDriftModel.Nominal(seed: 11);
        model.jumpRateHz = 0f;
        model.Reset();
        for (int i = 0; i < 900; i++) model.Advance(1f / 30f, 0f);          // 30 s, no travel
        float standing = LastGenuine(model);

        model.Reset();
        var step = new Vector3(0f, 0f, 1.2f / 30f);                          // 1.2 m/s
        for (int i = 0; i < 900; i++) model.Advance(1f / 30f, 0f, step);     // 30 s of walking
        float walked = LastGenuine(model);

        Assert.That(standing, Is.LessThan(1e-4f), $"standing grew the genuine channel to {standing:F4} m");
        // 36 m of path at the default 3 %/m, minus azimuth diffusion.
        Assert.That(walked, Is.GreaterThan(0.2f), $"36 m of walking only grew it to {walked:F4} m");

        float LastGenuine(WaveDriftModel m) => m.Advance(0f, 0f).GenuineErrorM.magnitude;
    }

    [Test]
    public void GenuineChannel_ZeroRate_ReproducesThePhantomOnlyModel()
    {
        // The escape hatch that makes "this channel is additive, not a rewrite" a
        // checkable claim rather than a comment: with the rate at 0 the phantom
        // realisation must be bit-identical however the mixture is set, because
        // the genuine draws come off their own RNG streams.
        Vector3 A = Sum(0f, 0f), B = Sum(0f, 1f);
        Assert.AreEqual(A.x, B.x, 0f, "genuineJumpFraction must not touch the phantom stream at rate 0");
        Assert.AreEqual(A.y, B.y, 0f);
        Assert.AreEqual(A.z, B.z, 0f);

        Vector3 Sum(float rate, float fraction)
        {
            var m = WaveDriftModel.Nominal(seed: 77);
            m.genuineDriftPerMeterPath = rate;
            m.genuineJumpFraction = fraction;
            m.Reset();
            Vector3 acc = Vector3.zero;
            var step = new Vector3(0f, 0f, 0.04f);
            for (int i = 0; i < 3000; i++) acc += m.Advance(1f / 30f, 0f, step).PositionErrorM;
            return acc;
        }
    }

    [Test]
    public void GenuineJump_Suppressed_IsNotFree()
    {
        // THE STRUCTURAL TEST, and the reason the channel exists at all.
        //
        // With every jump a GENUINE correction, a gate that suppresses everything
        // (relocalizationJumpThreshold = 0) can never heal the error that travel
        // baked in before it engaged, while a gate that lets the big ones through
        // can. If this test ever passes trivially — i.e. if the two gates end up
        // equal — the genuine channel has stopped working and every conclusion
        // about jump suppression in this harness is void again.
        var scenario = GenuineChannelScenario(genuineJumpFraction: 1f);

        var absorbAll = ArSimRunner.Run(scenario, GateWithThreshold(0f, "absorb"), keepFrames: false).Result;
        var passBig = ArSimRunner.Run(scenario, GateWithThreshold(0.35f, "pass"), keepFrames: false).Result;

        Assert.That(absorbAll.PosErrRmsM, Is.GreaterThan(passBig.PosErrRmsM * 1.1f),
            $"suppressing every genuine correction must COST something: " +
            $"absorb-all posRMS {absorbAll.PosErrRmsM:F3} m vs pass-big {passBig.PosErrRmsM:F3} m");
    }

    [Test]
    public void PhantomJump_Suppressed_IsStillFree()
    {
        // The other half of the same claim, and the artefact that made the
        // original "set the threshold to 0" recommendation look unbeatable: when
        // every jump corrects the model's own phantom ratchet, the gate has
        // already cancelled that ratchet out of the camera's world position, so
        // discarding the correction costs nothing. Both halves must hold, or the
        // mixture parameter does not separate two distinct behaviours.
        var scenario = GenuineChannelScenario(genuineJumpFraction: 0f);
        scenario.Wave.genuineDriftPerMeterPath = 0f;   // phantom channel only

        var absorbAll = ArSimRunner.Run(scenario, GateWithThreshold(0f, "absorb"), keepFrames: false).Result;
        var passBig = ArSimRunner.Run(scenario, GateWithThreshold(0.35f, "pass"), keepFrames: false).Result;

        Assert.That(absorbAll.PosErrRmsM, Is.LessThanOrEqualTo(passBig.PosErrRmsM),
            $"with only phantom jumps, suppressing them must not hurt: " +
            $"absorb-all posRMS {absorbAll.PosErrRmsM:F3} m vs pass-big {passBig.PosErrRmsM:F3} m");
    }

    [Test]
    public void GenuineShareOfError_IsReported_AndBracketsTheMixtureParameter()
    {
        // genuineJumpFraction is [E] with nothing behind it, so the run reports
        // the share of the accumulated error the genuine channel actually holds —
        // the value a mapper correcting the inconsistency it can see would
        // produce. That number, not the parameter, is what the crossover has to
        // be judged against.
        var scenario = GenuineChannelScenario(genuineJumpFraction: 0.35f);
        var r = ArSimRunner.Run(scenario, GateWithThreshold(0.35f, "pass"), keepFrames: false).Result;
        Assert.IsFalse(float.IsNaN(r.GenuineShareOfError), "the share must be reported");
        Assert.That(r.GenuineShareOfError, Is.InRange(0f, 1f));
        Assert.That(r.GenuineJumpCount, Is.GreaterThan(0), "a mixture of 0.35 over ~17 jumps must fire some");
        Assert.That(r.GenuineJumpCount, Is.LessThan(r.JumpCount), "and must not fire all of them");
    }

    // =====================================================================
    // The IMU ceiling, now applied to route 1 as well as the oscillator bands
    // =====================================================================

    [Test]
    public void PulseCeiling_FollowsTheDocsFormula_AtThePulseRepetitionRate()
    {
        // A_max = a_th/(2 pi f)^2 at f = 1/T (doc SS1.3). Checked against the
        // arithmetic rather than against a stored number so the formula, not a
        // snapshot, is what is pinned.
        var m = WaveDriftModel.Nominal();
        m.imuAccelBiasMps2 = 0.05f;
        m.groundswellPeriodS = 14f;
        float w = 2f * Mathf.PI / 14f;
        Assert.AreEqual(0.05f / (w * w), m.PulseCeilingM, 1e-4f);
    }

    [Test]
    public void PulseCeiling_BindsWhereTheForcingExceedsIt_AndNowhereElse()
    {
        // The honest reading of the doc's "~17x device spread" claim: the ceiling is
        // a CEILING, so it only matters where the visual forcing asks for more than
        // the accelerometer would tolerate.
        //
        // In the NOMINAL regime the pulse is 0.127 m/wave and the ceiling is
        // 0.248 m at the doc's reference bias, so nothing binds — and that is why
        // sweeping a_th barely moves the nominal numbers. It does bind at the BEST
        // phone (0.018 m/s^2 -> 0.089 m), which is the one place the device axis
        // reaches the nominal regime at all.
        //
        // In the CATASTROPHIC regime route 1 asks for 5.7 m/wave, ~23x the
        // reference ceiling, so it binds for every device — which is where the
        // device spread actually lives.
        var nom = WaveDriftModel.Nominal();
        nom.imuAccelBiasMps2 = 0.05f;
        Assert.IsFalse(nom.PulseIsImuLimited,
            $"nominal pulse {nom.PulseRawPerWaveM:F3} m vs ceiling {nom.PulseCeilingM:F3} m");

        nom.imuAccelBiasMps2 = 0.018f;
        Assert.IsTrue(nom.PulseIsImuLimited,
            "the best phone's ceiling should reach into the nominal regime");

        foreach (float bias in new[] { 0.018f, 0.05f, 0.206f })
        {
            var cat = WaveDriftModel.Catastrophic();
            cat.imuAccelBiasMps2 = bias;
            Assert.IsTrue(cat.PulseIsImuLimited,
                $"catastrophic pulse {cat.PulseRawPerWaveM:F3} m should exceed the " +
                $"{cat.PulseCeilingM:F3} m ceiling at a_th={bias}");
        }
    }

    [Test]
    public void PulseCeiling_MakesTheRatchetDeviceDependent()
    {
        // Before the ceiling was applied to route 1, imuAccelBiasMps2 could only
        // touch the three oscillator bands, so the whole measured device range
        // moved the DC ratchet by exactly nothing. It must now scale with it in
        // the regime where the ceiling binds.
        float best = Net(0.018f), worst = Net(0.206f);
        Assert.That(worst, Is.GreaterThan(best * 5f),
            $"catastrophic net drift must scale with the device bias: " +
            $"{best:F3} vs {worst:F3} m/wave");

        float Net(float bias)
        {
            var m = WaveDriftModel.Catastrophic();
            m.imuAccelBiasMps2 = bias;
            return m.NetDriftPerWaveM;
        }
    }

    // =====================================================================
    // Ground truth: the gait bob no longer steps in at a segment boundary
    // =====================================================================

    [Test]
    public void GaitBob_RampsInAndOut_WithNoAccelerationStepAtTheBoundary()
    {
        // The bob is 0.018 m and ground-truth acceleration is a second difference
        // of position, so switching it on inside one 33 ms frame used to inject
        // ~16 m/s^2 at every walk boundary — a free, unphysical peak handed to
        // every step detector exactly at the moments a step gate is being judged.
        var track = GroundTruthTrack.Build(new List<MotionSegment>
        {
            MotionSegment.Standing(3f),
            MotionSegment.Walking(6f),
            MotionSegment.Standing(3f),
        }, 30f, seed: 5);

        // Asserted on the VERTICAL channel specifically. That is the one the bob
        // owns (the only other y-term is smooth 0.25 Hz breathing) and the one
        // StepDetectorCore actually consumes, and it keeps the assertion clear of
        // the separate yaw-slew discontinuity the Stand->Walk boundary also has
        // (yaw snaps to the course, which rotates the horizontal postural-sway
        // vector; that is the scenario's own design and out of scope here).
        float peak = 0f;
        for (int i = 0; i < track.FrameCount; i++)
            peak = Mathf.Max(peak, Mathf.Abs(track.LinAccel[i].y));

        // A 1.9 Hz, 0.018 m bob peaks at 0.018*(2 pi*1.9)^2 = 2.6 m/s^2. The old
        // step-on behaviour measured 15.4 m/s^2 on walk-calm and 17.4 on
        // mixed-session against a p99 of 2.6, i.e. a 6x outlier one frame wide.
        Assert.That(peak, Is.LessThan(5f),
            $"peak ground-truth |accel.y| {peak:F2} m/s^2 - a boundary step is back");
    }

    [Test]
    public void BearingError_IsZeroWhenTheBelievedPoseIsCorrect()
    {
        var anchor = new Vector3(0f, 0f, 15f);
        Assert.AreEqual(0f, ArSimRunner.BearingErrorDeg(anchor, Vector3.zero, Vector3.zero), 1e-4f);
    }

    [Test]
    public void BearingError_SignAndMagnitudeAreGeometric()
    {
        // Anchor 15 m ahead (+Z). Believing the camera is 1 m to the +X side
        // makes the anchor appear 1/15 rad ≈ 3.8° to the -X side.
        var anchor = new Vector3(0f, 0f, 15f);
        float err = ArSimRunner.BearingErrorDeg(anchor, Vector3.zero, new Vector3(1f, 0f, 0f));
        Assert.AreEqual(Mathf.Atan2(1f, 15f) * Mathf.Rad2Deg, Mathf.Abs(err), 0.05f);
        float mirrored = ArSimRunner.BearingErrorDeg(anchor, Vector3.zero, new Vector3(-1f, 0f, 0f));
        Assert.That(err * mirrored, Is.LessThan(0f), "the error must be signed");
    }
}
