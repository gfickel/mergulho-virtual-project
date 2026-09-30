using System.Linq;
using MergulhoVirtual.ArSim;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the candidate stabilizers added to the offline drift harness.
///
/// Same rule as <see cref="ArSimTests"/>: assert INVARIANTS, never today's numbers.
/// The whole point of the harness is that the metrics move when someone re-bases the
/// wave model or retunes an algorithm, and a test that pinned a cost would have to be
/// rewritten every time and would quietly discourage exactly the retuning it is there
/// to support. What must not move is structural: a step gate with no steps suppresses
/// everything; a rotation-only stabilizer has zero position error and zero fidelity;
/// a graded gate's suppression is monotone in the IMU's quietness.
/// </summary>
public class ArSimCandidateTests
{
    const float TestScale = 0.1f;

    static ArSimScenario Scenario(string id, float scale = TestScale) =>
        ArSimScenarios.All(scale).First(s => s.Id == id);

    static ArSimResult Run(string id, IArStabilizer stabilizer, float scale = TestScale) =>
        ArSimRunner.Run(Scenario(id, scale), stabilizer, keepFrames: false).Result;

    // =====================================================================
    // Step detector
    // =====================================================================

    [Test]
    public void StepDetector_FindsNoStepsInAQuietSignal()
    {
        var d = new StepDetectorCore();
        d.Reset();
        // 20 s of physiological tremor only: 5 Hz at 0.1 m/s², an order of magnitude
        // above the noise floor but far outside the step band and well under the
        // absolute peak floor.
        for (int i = 0; i < 600; i++)
            d.Step(1f / 30f, 0.1f * Mathf.Sin(2f * Mathf.PI * 5f * i / 30f));

        Assert.That(d.StepCount, Is.EqualTo(0), "tremor must not be counted as steps");
        Assert.IsFalse(d.Stepping);
    }

    [Test]
    public void StepDetector_CountsAWalkWithinAFewPercent()
    {
        var d = new StepDetectorCore();
        d.Reset();
        const float cadence = 1.9f, dt = 1f / 30f, seconds = 20f;
        int frames = Mathf.RoundToInt(seconds / dt);
        for (int i = 0; i < frames; i++)
            d.Step(dt, 2.5f * Mathf.Sin(2f * Mathf.PI * cadence * i * dt));

        float expected = cadence * seconds;
        Assert.That(d.StepCount, Is.EqualTo(expected).Within(expected * 0.08f),
            $"expected ~{expected:F0} steps, counted {d.StepCount}");
        Assert.IsTrue(d.Stepping, "should still be stepping at the end of a walk");
    }

    [Test]
    public void StepDetector_ReleasesAfterTheTimeout()
    {
        var d = new StepDetectorCore();
        d.Reset();
        const float dt = 1f / 30f;
        for (int i = 0; i < 300; i++) d.Step(dt, 2.5f * Mathf.Sin(2f * Mathf.PI * 1.9f * i * dt));
        Assert.IsTrue(d.Stepping);

        int quiet = Mathf.CeilToInt((d.Config.steppingTimeoutS + 0.2f) / dt);
        for (int i = 0; i < quiet; i++) d.Step(dt, 0f);
        Assert.IsFalse(d.Stepping, "must release within steppingTimeoutS of the last step");
    }

    [Test]
    public void StepDetector_RespectsTheRefractoryPeriod()
    {
        var d = new StepDetectorCore();
        d.Reset();
        // 8 Hz at step-sized amplitude: physically impossible as a cadence, so the
        // refractory period must throw most of it away rather than counting 8/s.
        const float dt = 1f / 60f;
        for (int i = 0; i < 600; i++) d.Step(dt, 3f * Mathf.Sin(2f * Mathf.PI * 8f * i * dt));

        float seconds = 600 * dt;
        Assert.That(d.StepCount / seconds, Is.LessThan(1f / d.Config.refractoryS + 0.5f),
            "step rate must be capped by the refractory period");
    }

    // =====================================================================
    // 1. Step-gated
    // =====================================================================

    [Test]
    public void StepGate_SuppressesEverythingWhileNoStepsAreFiring()
    {
        // The core claim. Braced at the surf there are no steps, so every metre of
        // phantom translation must be cancelled.
        var r = Run("still-waves", new StepGatedStabilizer());
        Assert.That(r.StillFraction, Is.GreaterThan(0.95f),
            $"step gate should report itself suppressing nearly always while standing, " +
            $"was {r.StillFraction:P0}");
        Assert.That(r.GateSuppressedM, Is.GreaterThan(0f), "the gate must actually fire");
    }

    [Test]
    public void StepGate_StaysOnWhilePanning_WhereTheShippedGateGivesUp()
    {
        // The difference that motivates the candidate: panning makes the gyro loud,
        // so StillnessCore drops out of stillness — but the user has not travelled
        // anywhere, and no step has been taken, so a step gate must stay engaged.
        var shipped = Run("pan-waves", new ShippedStabilizer { driftCorrectionEnabled = false });
        var step = Run("pan-waves", new StepGatedStabilizer());

        Assert.That(step.StillFraction, Is.GreaterThan(shipped.StillFraction + 0.3f),
            $"step gate {step.StillFraction:P0} vs stillness gate {shipped.StillFraction:P0} " +
            "while panning: the whole point is that panning is not travelling");
    }

    [Test]
    public void StepGate_LetsRealWalkingThrough()
    {
        var r = Run("walk-calm", new StepGatedStabilizer());
        Assert.That(r.RealMotionFidelity, Is.Not.NaN);
        Assert.That(r.RealMotionFidelity, Is.GreaterThan(0.75f),
            $"real walking must largely survive a step gate, fidelity was {r.RealMotionFidelity:F3}");
    }

    [Test]
    public void StepGate_BudgetIsALooseBound_NotAMeasurement()
    {
        // The budget must not be the thing that limits an ordinary walk: with it on
        // and off, an unremarkable walk should look nearly the same. If turning the
        // budget off changes fidelity a lot, the bound is tight enough to be acting
        // as a (bad) step-length estimator, which the doc's SS5.1 says it cannot be.
        var withBudget = Run("walk-calm", new StepGatedStabilizer { budgetEnabled = true });
        var without = Run("walk-calm", new StepGatedStabilizer { budgetEnabled = false });

        Assert.That(withBudget.RealMotionFidelity,
            Is.EqualTo(without.RealMotionFidelity).Within(0.15f),
            $"budget on {withBudget.RealMotionFidelity:F3} vs off {without.RealMotionFidelity:F3}: " +
            "the budget is meant to be a loose ceiling, not a step-length model");
    }

    [Test]
    public void StepGate_NeverAppliesAGpsDriftCorrection()
    {
        // Attribution: any win must be the gate's, so there must be no second
        // mechanism moving the world.
        var r = Run("walk-waves", new StepGatedStabilizer());
        Assert.That(r.GateSuppressedM, Is.EqualTo(r.SuppressedM).Within(1e-3f),
            "every metre the step-gated stabilizer moved must be its gate's");
    }

    // =====================================================================
    // 2. Accel-only gate
    // =====================================================================

    [Test]
    public void AccelOnlyGate_IgnoresTheGyroChannelEntirely()
    {
        // Structural: whatever the gyro does, stillness must be decided by the accel
        // channel alone. Driven directly through StillnessCore so the assertion is
        // about the configuration, not about a scenario.
        var core = new StillnessCore { Config = ArSimCandidates.AccelOnlyGate().Stillness };
        for (int i = 0; i < 200; i++)
            core.Step(1f / 30f, gyroMag: 50f, accelMag: 0f, hasAccelSensor: true);
        Assert.IsTrue(core.IsStill, "a screaming gyro must not break an accel-only gate");

        core.Reset();
        for (int i = 0; i < 200; i++)
            core.Step(1f / 30f, gyroMag: 0f, accelMag: 5f, hasAccelSensor: true);
        Assert.IsFalse(core.IsStill, "a loud accel must still break it");
    }

    [Test]
    public void AccelOnlyGate_KeepsSuppressingWhilePanning()
    {
        var shipped = Run("pan-waves", new ShippedStabilizer { driftCorrectionEnabled = false });
        var accel = Run("pan-waves", ArSimCandidates.AccelOnlyGate());
        Assert.That(accel.StillFraction, Is.GreaterThan(shipped.StillFraction),
            $"accel-only {accel.StillFraction:P0} vs gyro+accel {shipped.StillFraction:P0} while panning");
    }

    // =====================================================================
    // 3. Rotation-only
    // =====================================================================

    [Test]
    public void RotationOnly_HoldsTheCameraWorldPositionExactly()
    {
        // The exact invariant, asserted on the rig rather than on a metric: whatever
        // the tracker reports, the camera's WORLD position never moves.
        //
        // Note what this is NOT: zero *position error vs ground truth*. The user sways
        // and pans, so a pinned camera necessarily accumulates error against a moving
        // body — that is the cost side of the trade and the table reports it.
        var st = new RotationOnlyStabilizer();
        st.Reset();
        var rig = new FakeArRig();
        rig.Reset();
        Vector3 first = Vector3.zero;
        for (int i = 0; i < 500; i++)
        {
            // Phantom drift, a relocalisation-sized jump, and scale error all at once.
            rig.SessionPos = new Vector3(0.01f * i, 0.003f * i, -0.02f * i) +
                             (i == 250 ? new Vector3(1.4f, 0f, -0.9f) : Vector3.zero);
            st.Step(new ArFrameSample { Frame = i, TimeS = i / 30f, Dt = 1f / 30f }, rig);
            if (i == 0) first = rig.CameraWorldPos;
            Assert.That((rig.CameraWorldPos - first).magnitude, Is.LessThan(1e-4f),
                $"frame {i}: camera world position moved");
        }
    }

    [Test]
    public void RotationOnly_RemovesTheWaveErrorItIsMeantTo()
    {
        // The perceptual claim, measured against the raw problem rather than against
        // an absolute: in the catastrophic regime it must remove most of the apparent
        // motion, because the only thing left is the user's own sway.
        //
        // RUN AT 0.4x, NOT THE FILE'S 0.1x. The claim is about the accumulated
        // ratchet, and 0.1x of tilt-down-swash is 9 s against a 14 s wave period —
        // less than ONE wave, so there is barely a ratchet to remove and the residual
        // is the 1.2 cm postural sway that rotation-only deliberately discards. At
        // 0.1x the assertion was always measuring sway rather than waves; applying
        // the IMU ceiling to route 1 (WaveDriftModel.PulseCeilingM) shrank the
        // catastrophic ratchet enough to make that visible. 0.4x is ~2.6 waves.
        const float LongEnoughForARatchet = 0.4f;
        var noop = Run("tilt-down-swash", new NoopStabilizer(), LongEnoughForARatchet);
        var rot = Run("tilt-down-swash", new RotationOnlyStabilizer(), LongEnoughForARatchet);
        Assert.That(rot.DriftDegP95, Is.LessThan(noop.DriftDegP95 * 0.5f),
            $"rotation-only p95 {rot.DriftDegP95:F2} vs noop {noop.DriftDegP95:F2} deg");
        Assert.That(rot.RangeErrFracP95, Is.LessThan(noop.RangeErrFracP95 * 0.25f),
            $"rotation-only range p95 {rot.RangeErrFracP95:P1} vs noop {noop.RangeErrFracP95:P1}");
    }

    [Test]
    public void RotationOnly_ScoresZeroFidelityWhenTheUserWalks()
    {
        var r = Run("walk-calm", new RotationOnlyStabilizer());
        Assert.That(r.RealMotionFidelity, Is.Not.NaN);
        Assert.That(r.RealMotionFidelity, Is.EqualTo(0f).Within(0.05f),
            $"discarding translation must score ~0 fidelity, was {r.RealMotionFidelity:F3}");
        Assert.That(r.PosErrMaxM, Is.GreaterThan(1f),
            "and the position error must be the whole distance walked");
    }

    [Test]
    public void RotationOnly_DeclaresThatItDiscardsTranslation()
    {
        // The exemption the registry-wide invariants key off must be declared by the
        // candidate, not inferred from its name.
        Assert.IsInstanceOf<IDiscardsTranslation>(new RotationOnlyStabilizer());
        Assert.That(ArStabilizerRegistry.Create().Count(s => s is IDiscardsTranslation), Is.EqualTo(1),
            "exactly one registered candidate should be exempt; a second one is probably a mistake");
    }

    // =====================================================================
    // 4. Graded gate
    // =====================================================================

    [Test]
    public void GradedGate_SuppressionIsMonotoneInTheImuQuietness()
    {
        // Structural property of grading: more measured acceleration must never mean
        // MORE suppression. Driven with a synthetic rig so the only variable is the
        // accel level.
        float Suppressed(float accelMps2)
        {
            var g = new GradedGateStabilizer();
            g.Reset();
            var rig = new FakeArRig();
            rig.Reset();
            float moved = 0f;
            for (int i = 0; i < 300; i++)
            {
                rig.SessionPos = new Vector3(0f, 0f, 0.002f * i);   // steady phantom creep
                Vector3 before = rig.OriginPos;
                g.Step(new ArFrameSample
                {
                    Frame = i,
                    TimeS = i / 30f,
                    Dt = 1f / 30f,
                    AccelWorldMps2 = new Vector3(0f, accelMps2, 0f),
                    HasAccelSensor = true,
                }, rig);
                moved += (rig.OriginPos - before).magnitude;
            }
            return moved;
        }

        float quiet = Suppressed(0f);
        float middling = Suppressed(0.25f);
        float loud = Suppressed(2f);

        Assert.That(quiet, Is.GreaterThan(middling),
            $"quiet {quiet:F3} m must suppress more than middling {middling:F3} m");
        Assert.That(middling, Is.GreaterThan(loud),
            $"middling {middling:F3} m must suppress more than loud {loud:F3} m");
        Assert.That(loud, Is.LessThan(1e-4f), "a loud IMU must suppress essentially nothing");
    }

    [Test]
    public void GradedGate_IsNotBinary()
    {
        // The distinguishing claim: there exists an accel level at which it partially
        // suppresses. A binary gate cannot produce one.
        var g = new GradedGateStabilizer();
        g.Reset();
        var rig = new FakeArRig();
        rig.Reset();
        float mid = 0.5f * (g.quietMps2 + g.loudMps2);
        float suppressed = 0f, reported = 0f;
        for (int i = 0; i < 300; i++)
        {
            rig.SessionPos = new Vector3(0f, 0f, 0.002f * i);
            Vector3 before = rig.OriginPos;
            g.Step(new ArFrameSample
            {
                Frame = i, TimeS = i / 30f, Dt = 1f / 30f,
                AccelWorldMps2 = new Vector3(0f, mid, 0f), HasAccelSensor = true,
            }, rig);
            suppressed += (rig.OriginPos - before).magnitude;
            reported = 0.002f * i;
        }
        float fraction = suppressed / Mathf.Max(1e-6f, reported);
        Assert.That(fraction, Is.InRange(0.15f, 0.85f),
            $"halfway between quiet and loud must suppress partially, suppressed {fraction:P0}");
    }

    // =====================================================================
    // 5. Targeted fixes
    // =====================================================================

    [Test]
    public void JumpAbsorbingGate_SuppressesJumpsTheShippedGateLetsThrough()
    {
        // The shipped gate passes a single-frame delta above 0.35 m straight through
        // and BANKS it. A threshold of 0 must absorb it instead, so the absorbing
        // variant necessarily moves the world at least as far in a jumpy scenario.
        // Full duration: jumps are Poisson, and a 12 s slice of `reloc-jumps` can
        // easily contain none above the 0.35 m threshold, in which case the two
        // configurations are identical and the test asserts nothing.
        var shipped = Run("reloc-jumps", new ShippedStabilizer { driftCorrectionEnabled = false }, 1f);
        var absorb = Run("reloc-jumps", ArSimCandidates.GateOnlyAbsorbingJumps(), 1f);
        Assert.That(absorb.GateSuppressedM, Is.GreaterThan(shipped.GateSuppressedM),
            $"absorbing jumps must suppress more than passing them: " +
            $"{absorb.GateSuppressedM:F2} m vs {shipped.GateSuppressedM:F2} m");
    }

    [Test]
    public void HeadingGate_WithholdsFixesWhileTheGateIsSuppressing()
    {
        // While braced, the gate suppresses continuously, so no fix should ever be
        // trusted and the fusion filter must never even initialise. That is what
        // stops a phantom-drift segment teaching the filter an azimuth.
        var r = Run("still-waves", ArSimCandidates.ShippedWithHeadingGate());
        Assert.IsFalse(r.HeadingRefined, "no heading refinement may happen while standing");
        Assert.That(r.StabilizerSummary, Does.Contain("fixesWithheld"),
            "the variant must report how many fixes it withheld");
    }

    [Test]
    public void HeadingGate_IsOffByDefaultOnTheShippedStabilizer()
    {
        // The shipped row must stay bit-exact: the candidate is opt-in.
        Assert.That(new ShippedStabilizer().requireCleanGateSecondsForFix, Is.EqualTo(0f));
        var a = Run("walk-waves", new ShippedStabilizer());
        var b = Run("walk-waves", new ShippedStabilizer { requireCleanGateSecondsForFix = 0f });
        Assert.That(a.PerceptualCost, Is.EqualTo(b.PerceptualCost).Within(0f));
    }

    // =====================================================================
    // Registry + scenarios
    // =====================================================================

    [Test]
    public void Registry_ShipsEveryCandidate_WithUniqueNames()
    {
        var names = ArStabilizerRegistry.Create().Select(s => s.Name).ToList();
        foreach (var expected in new[]
                 {
                     "noop", "shipped", "gate-only", "drift-only",
                     "accel-gate", "step-gate", "graded-gate", "rotation-only",
                     "gate-absorb", "accel-gate-absorb", "step-absorb", "step-absorb-slow",
                     "shipped+headgate",
                 })
            Assert.That(names, Does.Contain(expected), $"registry is missing {expected}");
        Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count), "names must be unique");
    }

    [Test]
    public void ShuffleWalk_HasIrregularSlowerGaitThanAnOrdinaryWalk()
    {
        // The stress test must actually be harder, not just differently named: fewer,
        // weaker vertical-acceleration peaks per second than an ordinary walk.
        // Measured over the INTERIOR of the walk only. The harness switches the gait
        // bob on and off at a segment boundary at whatever phase it happens to be at,
        // which puts a one- or two-frame ~12 m/s² step discontinuity in the
        // ground-truth acceleration there. That artefact predates these candidates and
        // is common to every walk scenario, so it is excluded here rather than
        // silently compared.
        // RMS, not the maximum: an irregular gait's MAXIMUM stride can legitimately
        // exceed a regular gait's constant one, so a max-vs-max comparison would be
        // measuring the tail of the variability rather than the level of the signal.
        // RMS is what a detector's adaptive threshold actually adapts to.
        float RmsAccel(string id)
        {
            var sc = Scenario(id, 1f);
            var track = GroundTruthTrack.Build(sc.Motion, sc.RateHz, 7);
            int guard = Mathf.CeilToInt(1f / track.Dt);
            double sq = 0.0; int n = 0;
            for (int i = guard; i < track.FrameCount - guard; i++)
            {
                bool interior = track.Kind[i] == MotionKind.Walk &&
                                track.Kind[i - guard] == MotionKind.Walk &&
                                track.Kind[i + guard] == MotionKind.Walk;
                if (!interior) continue;
                sq += (double)track.LinAccel[i].y * track.LinAccel[i].y;
                n++;
            }
            return n > 0 ? Mathf.Sqrt((float)(sq / n)) : 0f;
        }

        Assert.That(RmsAccel("shuffle-walk"), Is.LessThan(RmsAccel("walk-calm") * 0.8f),
            "a shuffle must produce a weaker vertical acceleration signal than a walk");

        var shuffle = Scenario("shuffle-walk", 1f);
        var walk = Scenario("walk-calm", 1f);
        var st = GroundTruthTrack.Build(shuffle.Motion, shuffle.RateHz, 7);
        var wt = GroundTruthTrack.Build(walk.Motion, walk.RateHz, 7);
        Assert.That(st.TrueStepCount, Is.GreaterThan(5f), "the shuffle must contain real steps");
        Assert.That(st.TrueStepCount / st.FrameCount,
            Is.LessThan(wt.TrueStepCount / wt.FrameCount),
            "and take them at a lower cadence");
    }

    [Test]
    public void GaitOverrides_DoNotDisturbTheExistingScenarios()
    {
        // The per-segment gait parameters were added with a bit-exact legacy path so
        // that the shuffle override cannot leak into an ordinary walk. Assert it
        // rather than trust it: an unoverridden walk must still be the closed-form
        // 1.9 Hz bob — now times the ramp ENVELOPE, which is a deliberate change
        // (see GroundTruthTrack.gaitEnvelope: switching the bob on at full
        // amplitude inside one frame injected ~16 m/s² into ground-truth
        // acceleration at every walk boundary, measured, and handed every step
        // detector a free unphysical peak there).
        var sc = Scenario("walk-calm", 1f);
        var track = GroundTruthTrack.Build(sc.Motion, sc.RateHz, 99);
        float dt = track.Dt;
        for (int i = 0; i < track.FrameCount; i++)
        {
            if (track.Kind[i] != MotionKind.Walk) continue;
            float t = i * dt;
            // The vertical bob is the only y-term other than breathing; reconstruct
            // the expected bob and check it is present with the legacy phase.
            float expected = track.GaitEnvelope[i] * 0.018f *
                             Mathf.Sin(2f * Mathf.PI * GroundTruthTrack.StepCadenceHz * t);
            float breathing = 0.004f;
            Assert.That(Mathf.Abs(track.Pos[i].y - expected), Is.LessThan(breathing + 1e-3f),
                $"frame {i}: vertical bob deviates from the legacy closed form");
        }
    }

    [Test]
    public void WalkThenStand_DefinesASuppressionReacquisitionTime()
    {
        // The metric the scenario exists for must actually be produced, and every
        // gate must eventually get back to suppressing once the user stops.
        foreach (var stabilizer in ArStabilizerRegistry.Create())
        {
            if (stabilizer.Name == "noop" || stabilizer.Name == "drift-only") continue;
            var r = Run("walk-then-stand", stabilizer, 0.3f);
            Assert.That(r.SuppressionReacquireS, Is.Not.NaN,
                $"{stabilizer.Name}: never re-acquired suppression after the walk stopped");
            Assert.That(r.SuppressionReacquireS, Is.LessThan(5f),
                $"{stabilizer.Name}: took {r.SuppressionReacquireS:F1} s to lock the world again");
        }
    }

    [Test]
    public void EveryCandidate_ProducesFiniteMetricsOnEveryScenario()
    {
        foreach (var scenario in ArSimScenarios.All(TestScale))
        {
            foreach (var stabilizer in ArStabilizerRegistry.Create())
            {
                var r = ArSimRunner.Run(scenario, stabilizer, keepFrames: false).Result;
                Assert.IsFalse(float.IsNaN(r.PerceptualCost), $"{scenario.Id}/{stabilizer.Name}: NaN cost");
                Assert.IsFalse(float.IsInfinity(r.PerceptualCost), $"{scenario.Id}/{stabilizer.Name}: inf cost");
                // A stabilizer with no diagnostics face reports gate m as NaN on
                // purpose (the report prints a dash); only the ones that claim to
                // have a gate must produce a number.
                if (stabilizer is IArStabilizerDiagnostics)
                    Assert.IsFalse(float.IsNaN(r.GateSuppressedM), $"{scenario.Id}/{stabilizer.Name}: NaN gate m");
            }
        }
    }

    [Test]
    public void Sweep_ReportsRankingStability()
    {
        // Runs the sweep at a tiny duration scale: the numbers are meaningless at 10%
        // duration, the point is that the driver produces an ordering report at all.
        var report = ArSimSweep.Run(0.05f);
        string text = string.Join("\n", report);
        Assert.That(text, Does.Contain("RANKING STABILITY"));
        Assert.That(text, Does.Contain("distinct full orderings"));
        foreach (var st in ArStabilizerRegistry.Create())
            Assert.That(text, Does.Contain(st.Name), $"sweep is missing {st.Name}");
    }
}
