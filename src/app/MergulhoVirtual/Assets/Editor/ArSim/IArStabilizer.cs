using System;
using System.Collections.Generic;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Everything the harness needs from a candidate stabilization algorithm.
    /// The only output channel is <c>rig.OriginPos</c> — the same single lever the
    /// shipped stack has on device — so nothing can cheat by reaching around the
    /// AR rig.
    ///
    /// To add a candidate: one new file implementing this, plus one line in
    /// <see cref="ArStabilizerRegistry"/>. Nothing else in the harness changes.
    /// </summary>
    internal interface IArStabilizer
    {
        /// <summary>Column label in the report. Keep it short.</summary>
        string Name { get; }

        /// <summary>One line describing the parameters actually in force, for the
        /// manifest. Return an empty string if there is nothing to say.</summary>
        string Describe();

        /// <summary>Clear all state. Called once before every scenario.</summary>
        void Reset();

        /// <summary>
        /// Advance one frame. May move <c>rig.OriginPos</c>; must not touch
        /// <c>rig.SessionPos</c>.
        /// </summary>
        void Step(in ArFrameSample sample, FakeArRig rig);
    }

    /// <summary>
    /// Optional second face of a stabilizer: what it believed while it ran.
    /// Nothing in the scoring reads it — it exists so the report's diagnostics
    /// table works for every candidate instead of only for the shipped stack, which
    /// is how the ONE cast to ShippedStabilizer that used to be in ArSimRunner got
    /// removed. A candidate without one simply reports dashes.
    /// </summary>
    internal interface IArStabilizerDiagnostics
    {
        /// <summary>True on this frame if the stabilizer considers itself in
        /// suppress mode. For the shipped stack that is the IMU stillness verdict;
        /// for a step-gated one it is "no step is firing". The report's supp%
        /// column is the fraction of frames this was true.</summary>
        bool SuppressingNow { get; }

        /// <summary>Path length of everything the stabilizer's GATE moved, so the
        /// report can separate it from a GPS drift correction. NaN if meaningless.</summary>
        float GateSuppressedPathM { get; }

        bool FusionReady { get; }
        bool HeadingRefined { get; }

        /// <summary>Azimuth the stabilizer believes Unity +Z points toward, deg.
        /// NaN when it has no opinion.</summary>
        float HeadingDeg { get; }

        /// <summary>One short line of end-of-run state for the diagnostics table
        /// (step counts, mean suppression weight, ...). Empty if nothing to say.</summary>
        string Summarize();
    }

    /// <summary>
    /// The baseline: do nothing at all. Establishes how bad the raw problem is,
    /// which is the only thing that makes another stabilizer's numbers mean
    /// anything.
    /// </summary>
    internal sealed class NoopStabilizer : IArStabilizer
    {
        public string Name => "noop";
        public string Describe() => "no correction (baseline)";
        public void Reset() { }
        public void Step(in ArFrameSample sample, FakeArRig rig) { }
    }

    /// <summary>
    /// The shipped stack, wired exactly as <c>ArStabilizationController</c> wires
    /// it at runtime:
    ///
    ///   Update       — StillnessDetector, then GpsArKalmanFusion (predict + update)
    ///   LateUpdate  100 — SpuriousMotionGate counter-shifts the origin
    ///   LateUpdate  200 — ArStabilizationController.ApplyDriftCorrection, gated
    ///                     on correctOnlyWhileMoving
    ///
    /// including the Start() coroutine's behaviour: nothing happens until the
    /// first GNSS fix, then the compass is averaged for compassAverageSeconds
    /// before the filter initialises.
    /// </summary>
    internal sealed class ShippedStabilizer : IArStabilizer, IArStabilizerDiagnostics
    {
        public string Name = "shipped";
        string IArStabilizer.Name => Name;

        // Scene/code defaults. A variant can construct this type with different
        // settings instead of writing a whole new stabilizer.
        public StillnessCore.Settings Stillness = StillnessCore.Settings.Defaults;
        public MotionGateCore.Settings Gate = MotionGateCore.Settings.Defaults;
        public DriftFusionCore.Settings Fusion = DriftFusionCore.Settings.Defaults;

        // ArStabilizationController's three switches.
        //
        // NOTE (2026-09-28): the SCENE and the component initializer now ship
        // driftCorrectionEnabled = FALSE, after this harness measured the drift
        // correction as a large net harm. These fields are the harness's own and are
        // deliberately NOT slaved to the component, so the "shipped" row keeps
        // measuring the AS-WAS configuration (gate + drift correction) that the
        // regression is quantified against. The AS-NOW shipped configuration is the
        // "gate-only" row. Renaming the two rows shipped-was / shipped-now is a
        // one-line change in the registry below if the table ever reads ambiguously.
        public bool gateEnabled = true;
        public bool driftCorrectionEnabled = true;
        public bool correctOnlyWhileMoving = true;

        /// <summary>
        /// CANDIDATE 5b, off by default (0 = the shipped behaviour, bit-exact).
        /// When positive, a GNSS fix is only forwarded to the fusion filter if the
        /// translation gate has NOT suppressed anything for this many seconds — i.e.
        /// only when the AR track over that window is trustworthy. The point is the
        /// heading refinement, which compares the AR displacement direction against
        /// the GNSS one and so learns a wrong azimuth from exactly the segments the
        /// waves corrupted. Withholding the fix also withholds the Kalman update;
        /// that is a real consequence of the cheapest implementable version, not an
        /// oversight.
        /// </summary>
        public float requireCleanGateSecondsForFix = 0f;

        /// <summary>GpsArKalmanFusion.compassAverageSeconds.</summary>
        public float compassAverageSeconds = 2f;

        readonly StillnessCore stillness = new StillnessCore();
        readonly MotionGateCore gate = new MotionGateCore();
        readonly DriftFusionCore fusion = new DriftFusionCore();
        readonly HeadingSeedAccumulator seed = new HeadingSeedAccumulator();

        GnssFix firstFix;
        bool sawFirstFix;
        float compassUntilT;
        float gateSuppressedPathM;
        float lastSuppressT;
        int withheldFixes;

        // --- read by the harness for diagnostics ------------------------------
        public bool IsStill => stillness.IsStill;
        public bool FusionReady => fusion.Ready;
        public Vector3 TotalSuppressed => gate.TotalSuppressed;

        /// <summary>Path length (not vector sum) of everything the GATE shifted,
        /// so the report can separate it from the GPS drift correction.</summary>
        public float GateSuppressedPathM => gateSuppressedPathM;
        public float HeadingDeg => fusion.HeadingDeg;
        public bool HeadingRefined => fusion.HeadingRefined;
        public Vector2 DriftError => fusion.DriftError;

        public void Reset()
        {
            stillness.Reset();
            gate.Reset();
            fusion.Reset();
            seed.Reset();
            gateSuppressedPathM = 0f;
            sawFirstFix = false;
            firstFix = default;
            compassUntilT = 0f;
            lastSuppressT = float.NegativeInfinity;
            withheldFixes = 0;
        }

        public void Step(in ArFrameSample s, FakeArRig rig)
        {
            stillness.Config = Stillness;
            gate.Config = Gate;
            fusion.Config = Fusion;

            // ---------- Update() ----------
            stillness.Step(s.Dt, s.GyroMag, s.AccelMag, s.HasAccelSensor);

            // Candidate 5b. The gate runs in LateUpdate, i.e. AFTER this, so the
            // decision necessarily uses the previous frame's suppression state.
            bool trustFix = requireCleanGateSecondsForFix <= 0f ||
                            s.TimeS - lastSuppressT >= requireCleanGateSecondsForFix;
            if (s.HasNewFix && !trustFix) withheldFixes++;
            bool hasUsableFix = s.HasNewFix && trustFix;

            if (!fusion.Ready)
            {
                // Mirror the Start() coroutine: wait for a fix, then average the
                // compass for compassAverageSeconds, then initialise.
                if (!sawFirstFix && hasUsableFix)
                {
                    sawFirstFix = true;
                    firstFix = s.Fix;
                    compassUntilT = s.TimeS + compassAverageSeconds;
                }
                if (sawFirstFix)
                {
                    if (s.TimeS < compassUntilT) seed.Add(s.CompassHeadingDeg);
                    else fusion.Initialize(firstFix, rig.CameraWorldPos, seed.ResolveDeg(0f));
                }
            }
            else
            {
                fusion.Step(rig.CameraWorldPos, hasUsableFix, s.Fix);
            }

            // ---------- LateUpdate, order 100: the gate ----------
            if (gateEnabled)
            {
                Vector3 shift = gate.Step(rig.CameraWorldPos, stillness.IsStill, s.Dt);
                if (gate.SuppressedLastStep)
                {
                    rig.OriginPos += shift;
                    gateSuppressedPathM += shift.magnitude;
                    lastSuppressT = s.TimeS;
                }
            }

            // ---------- LateUpdate, order 200: the controller ----------
            if (!driftCorrectionEnabled) return;
            if (correctOnlyWhileMoving && stillness.IsStill) return;
            if (fusion.TryComputeDriftCorrection(s.Dt, out Vector3 correction))
                rig.OriginPos += correction;
        }

        bool IArStabilizerDiagnostics.SuppressingNow => stillness.IsStill;
        float IArStabilizerDiagnostics.GateSuppressedPathM => gateEnabled ? gateSuppressedPathM : 0f;
        string IArStabilizerDiagnostics.Summarize()
        {
            string s = requireCleanGateSecondsForFix > 0f ? $"fixesWithheld={withheldFixes} " : "";
            if (Gate.correctionBudgetPerMeter > 0f)
                s += $"budget: earned={gate.BudgetAccruedM:F2}m admitted={gate.BudgetAdmittedM:F2}m " +
                     $"left={gate.CorrectionBudgetM:F2}m";
            return s.TrimEnd();
        }

        public string Describe() =>
            $"gate={(gateEnabled ? "on" : "off")} drift={(driftCorrectionEnabled ? "on" : "off")} " +
            (requireCleanGateSecondsForFix > 0f
                ? $"fixGate={requireCleanGateSecondsForFix:F1}s " : "") +
            $"onlyWhileMoving={correctOnlyWhileMoving} | " +
            $"stillness(gyro={Stillness.gyroStillThreshold:F3} accel={Stillness.accelStillThreshold:F3} " +
            $"enter={Stillness.enterStillTime:F2}s exitX{Stillness.exitMultiplier:F2} ema={Stillness.emaAlpha:F2}) | " +
            $"gate(minDelta={Gate.minDelta:F4}m jump={Gate.relocalizationJumpThreshold:F2}m" +
            (Gate.correctionBudgetPerMeter > 0f
                ? $" budget={Gate.correctionBudgetPerMeter:P1}/m cap={Gate.correctionBudgetCapM:F2}m " +
                  $"half={Gate.correctionBudgetHalfLifeS:F0}s floor={Gate.correctionFloorM:F3}m " +
                  $"bleed={Gate.correctionBleedSpeedMps:F2}m/s"
                : "") + ") | " +
            $"kalman(q={Fusion.processNoisePerMeter:F3} minAcc={Fusion.minGpsAccuracy:F1} " +
            $"maxAcc={Fusion.maxUsableAccuracy:F0} vmax={Fusion.maxCorrectionSpeed:F2}m/s " +
            $"seg={Fusion.headingSegmentMeters:F0}m blend={Fusion.headingBlend:F2})";
    }

    /// <summary>
    /// The stabilizers the sweep runs. ADD A CANDIDATE HERE — one line, and the
    /// runner, the report, the CSVs and the manifest all pick it up.
    /// </summary>
    internal static class ArStabilizerRegistry
    {
        public static IReadOnlyList<Func<IArStabilizer>> Factories { get; } = new Func<IArStabilizer>[]
        {
            () => new NoopStabilizer(),
            () => new ShippedStabilizer(),

            // The shipped stack decomposed. Not candidate algorithms — they exist
            // because the full stack's two halves can fail for opposite reasons in
            // the same scenario, and the totals alone cannot tell you which. Both
            // are one line each, which is the whole point of the interface.
            () => new ShippedStabilizer { Name = "gate-only", driftCorrectionEnabled = false },
            () => new ShippedStabilizer { Name = "drift-only", gateEnabled = false },

            // ---- candidates (Assets/Editor/ArSim/ArSimCandidates.cs) ----------
            // 2. accel channel only: the gate corrects TRANSLATION, and rotation
            //    does not imply translation, so gating on the gyro is the wrong
            //    test. Two-line change; the cheapest thing that could work.
            () => ArSimCandidates.AccelOnlyGate(),

            // 1. the primary candidate: translation is only possible if a step
            //    happened.
            () => new StepGatedStabilizer(),

            // 4. graded rather than binary suppression, from the band-limited IMU.
            () => new GradedGateStabilizer(),

            // 3. the fallback: discard AR translation entirely.
            () => new RotationOnlyStabilizer(),

            // 5a. absorb relocalisation jumps instead of banking them, measured on
            //     both gates so the fix is separable from the gate change.
            () => ArSimCandidates.GateOnlyAbsorbingJumps(),
            () => ArSimCandidates.AccelGateAbsorbingJumps(),

            // 1 x 5a. the two independent wins combined: decide on steps AND absorb
            //         jumps instead of banking them. This is the shipping candidate.
            () => new StepGatedStabilizer
            {
                Name = "step-absorb",
                Gate = ArSimCandidates.GateAbsorbingJumps(),
            },

            // The ONE place anything was tuned against a scenario, kept as a separate
            // row so the untuned number above stays visible. steppingTimeoutS = 0.85 s
            // is SHORTER than the slowest plausible stride interval on sand (a jittered
            // 1.5 Hz cadence dips to ~0.9 Hz = 1.1 s between steps), so the gate closes
            // mid-shuffle and eats real motion. 1.3 s is chosen to exceed that interval
            // rather than to optimise the score - but it was chosen after seeing the
            // score, which is what makes it tuned.
            () => new StepGatedStabilizer
            {
                Name = "step-absorb-slow",
                Gate = ArSimCandidates.GateAbsorbingJumps(),
                Detector = new StepDetectorCore.Settings
                {
                    highPassHz = StepDetectorCore.Settings.Defaults.highPassHz,
                    lowPassHz = StepDetectorCore.Settings.Defaults.lowPassHz,
                    minPeakMps2 = StepDetectorCore.Settings.Defaults.minPeakMps2,
                    peakFactor = StepDetectorCore.Settings.Defaults.peakFactor,
                    refractoryS = StepDetectorCore.Settings.Defaults.refractoryS,
                    steppingTimeoutS = 1.3f,
                    envelopeTauS = StepDetectorCore.Settings.Defaults.envelopeTauS,
                },
            },

            // 5b. gate the heading refinement on the translation gate agreeing the
            //     user was genuinely walking. Only matters if the GPS drift
            //     correction is ever re-enabled, which as of 2026-09-28 it is not.
            () => ArSimCandidates.ShippedWithHeadingGate(),

            // 6. THE CORRECTION BUDGET: suppress everything while standing, but let
            //    a walk earn back the right to a correction of the size that walk
            //    plausibly banked. Same gate, same everything, as `gate-absorb` —
            //    the only difference is the budget, so the two rows subtract.
            //
            //    ASSUMED RATE: 7 %/m. Deliberately NOT the model's own 3 %/m — that
            //    would be grading the gate against the homework it set itself. It is
            //    the upper half of the measured bracket (0.34 %/m static scene,
            //    14.4 %/m dynamic environment) on the argument that a surf scene IS
            //    a dynamic environment, and it is where the sweep's assumed x true
            //    grid shows the cost of a mismatch is flattest.
            () => ArSimCandidates.GateBudget(BudgetAssumedRatePerMeter),
            () => ArSimCandidates.AccelGateBudget(BudgetAssumedRatePerMeter),

            // The budget WITHOUT the bleed, i.e. an admitted correction applied in
            // the frame it arrived, the way the tracker emitted it. Kept as its own
            // row because the bleed is the larger of the two effects and the two
            // must stay separable: admitting a 1.5 m correction instantly is a 5.7
            // deg pop of everything anchored, which the perceptual cost charges more
            // than the error it removed.
            () => new ShippedStabilizer
            {
                Name = "budget-pop",
                driftCorrectionEnabled = false,
                Gate = ArSimCandidates.BudgetGate(BudgetAssumedRatePerMeter, bleedSpeedMps: 0f),
            },

            // The budget with the per-frame PATH accrual it started with, instead of
            // net displacement over a window. Kept so the accrual source stays a
            // measured choice: summed path credits pans and the wave oscillation as
            // if they were travel.
            () => new ShippedStabilizer
            {
                Name = "budget-path",
                driftCorrectionEnabled = false,
                Gate = ArSimCandidates.BudgetGate(BudgetAssumedRatePerMeter, windowS: 0f),
            },

            // The admission floor raised to 0.15 m. The floor decides what counts as
            // a candidate CORRECTION at all; the budget then decides how much of it
            // is owed. Raising it is the one lever that can bias admission toward
            // genuine corrections in the regimes where they are the larger of the
            // two populations.
            () => new ShippedStabilizer
            {
                Name = "budget-f15",
                driftCorrectionEnabled = false,
                Gate = ArSimCandidates.BudgetGate(BudgetAssumedRatePerMeter, floorM: 0.15f),
            },

            // The alternative pathDelta source, measured rather than argued: credit
            // the budget per DETECTED STEP instead of per metre of AR displacement.
            () => new StepGatedStabilizer
            {
                Name = "step-budget",
                Gate = ArSimCandidates.BudgetGate(BudgetAssumedRatePerMeter),
                budgetFromStepCount = true,
            },
        };

        /// <summary>The gate's ASSUMED non-wave error rate, per metre of path. See
        /// the registry comment on the budget rows for why it is not the model's
        /// own value.</summary>
        public const float BudgetAssumedRatePerMeter = 0.07f;

        public static IEnumerable<IArStabilizer> Create()
        {
            foreach (var f in Factories) yield return f();
        }
    }
}
