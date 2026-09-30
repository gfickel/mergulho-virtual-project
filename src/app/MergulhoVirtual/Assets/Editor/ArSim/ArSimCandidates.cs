using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Marker for a candidate that DELIBERATELY discards real translation, so the
    /// registry-wide "real displacement must survive" invariant does not apply to
    /// it. Losing that invariant is the candidate's whole premise, not a bug — and
    /// the composite already charges it via the fidelity penalty, which is the
    /// place that judgement belongs.
    /// </summary>
    internal interface IDiscardsTranslation { }

    // =========================================================================
    // 1. Step-gated — the primary candidate
    // =========================================================================

    /// <summary>
    /// Replace "is the phone quiet?" with "did the user take a step?".
    ///
    /// Two layers:
    ///   (a) GATE — AR translation passes only while steps are firing. Standing
    ///       braced: no steps, full suppression. Standing and PANNING: no steps,
    ///       still full suppression, which is exactly the case the shipped gate
    ///       abandons because panning makes the gyro loud. Walking: steps fire and
    ///       real motion passes.
    ///   (b) BUDGET — while stepping, the displacement allowed through is bounded
    ///       by stepsInBout × plausibleStepLengthM. The doc's SS5.1 is explicit
    ///       that on sand step LENGTH degrades much more than step COUNT, so this
    ///       is deliberately a loose ceiling on a plausible stride, not a
    ///       measurement of one: it exists to stop a wave ratchet riding along
    ///       inside a genuine walk, not to dead-reckon.
    ///
    /// The suppression arithmetic is <see cref="MotionGateCore"/> itself, driven by
    /// a different boolean — so the only difference from the shipped gate is WHICH
    /// question decides, and any win is attributable to that and nothing else. The
    /// relocalisation-jump passthrough is inherited unchanged for the same reason.
    ///
    /// Failure modes, both measured rather than argued:
    ///   • the first step of a bout is confirmed ~150-300 ms late, so a walk starts
    ///     with that much suppressed displacement — bounded lag, never a runaway;
    ///   • the gate stays open for steppingTimeoutS after the last step;
    ///   • shuffling on sand is where peak detection is weakest, which is what the
    ///     `shuffle-walk` scenario exists to charge it for.
    /// </summary>
    internal sealed class StepGatedStabilizer : IArStabilizer, IArStabilizerDiagnostics
    {
        public string Name = "step-gate";
        string IArStabilizer.Name => Name;

        public StepDetectorCore.Settings Detector = StepDetectorCore.Settings.Defaults;
        public MotionGateCore.Settings Gate = MotionGateCore.Settings.Defaults;

        /// <summary>
        /// THE ALTERNATIVE pathDelta SOURCE for the correction budget: credit the
        /// budget with <see cref="plausibleStepLengthM"/> per DETECTED STEP instead
        /// of with the AR displacement the gate already measures. Only meaningful
        /// when <c>Gate.correctionBudgetPerMeter &gt; 0</c>; it sets
        /// <c>accrueBudgetFromGatePath = false</c> and drives the accrual itself.
        ///
        /// It exists to be measured against the AR-displacement source, not because
        /// it is preferred: it needs a step detector in the runtime (which the app
        /// does not have), and the research doc's §5.1 is explicit that on sand step
        /// LENGTH degrades far more than step COUNT — so the one quantity this route
        /// must multiply by is the one that is least trustworthy exactly where the
        /// app is used.
        /// </summary>
        public bool budgetFromStepCount;

        /// <summary>Metres of displacement each detected step is allowed to justify.
        /// 0.8 m is the LOOSE end of the task brief's 0.6-0.8 m plausible stride,
        /// picked deliberately high so the bound never fights an ordinary walk. Not
        /// tuned against the scenarios.</summary>
        public float plausibleStepLengthM = 0.8f;

        /// <summary>Set false to measure the gate alone with no budget layer, which
        /// is how the two halves are separated in the report.</summary>
        public bool budgetEnabled = true;

        readonly StepDetectorCore steps = new StepDetectorCore();
        readonly MotionGateCore gate = new MotionGateCore();

        float suppressedPathM;
        int boutStartStepCount;
        float allowedPathM;
        bool wasStepping;
        Vector3 prevCam;
        bool hasPrevCam;
        int suppressFrames, frames;
        int overBudgetFrames;

        public void Reset()
        {
            steps.Reset();
            gate.Reset();
            suppressedPathM = 0f;
            boutStartStepCount = 0;
            allowedPathM = 0f;
            wasStepping = false;
            prevCam = Vector3.zero;
            hasPrevCam = false;
            suppressFrames = frames = overBudgetFrames = 0;
        }

        public void Step(in ArFrameSample s, FakeArRig rig)
        {
            steps.Config = Detector;
            gate.Config = Gate;
            if (budgetFromStepCount) gate.Config.accrueBudgetFromGatePath = false;
            frames++;

            int stepsBefore = steps.StepCount;
            steps.Step(s.Dt, s.AccelWorldMps2.y);
            if (budgetFromStepCount && steps.StepCount > stepsBefore)
                gate.AccrueCorrectionBudget((steps.StepCount - stepsBefore) * plausibleStepLengthM);

            bool stepping = steps.Stepping;
            if (stepping && !wasStepping)
            {
                // New walking bout: the budget starts from the step that opened it.
                boutStartStepCount = Mathf.Max(0, steps.StepCount - 1);
                allowedPathM = 0f;
            }
            wasStepping = stepping;

            bool overBudget = false;
            if (stepping && budgetEnabled)
            {
                float budget = (steps.StepCount - boutStartStepCount) * plausibleStepLengthM;
                overBudget = allowedPathM >= budget;
                if (overBudget) overBudgetFrames++;
            }

            bool suppress = !stepping || overBudget;
            if (suppress) suppressFrames++;

            Vector3 shift = gate.Step(rig.CameraWorldPos, suppress, s.Dt);
            if (gate.SuppressedLastStep)
            {
                rig.OriginPos += shift;
                suppressedPathM += shift.magnitude;
            }

            // Budget accounting on the NET camera movement, i.e. what actually got
            // through after the shift — not on what the tracker claimed.
            Vector3 cam = rig.CameraWorldPos;
            if (hasPrevCam && stepping && !suppress) allowedPathM += (cam - prevCam).magnitude;
            prevCam = cam;
            hasPrevCam = true;
        }

        // --- diagnostics -----------------------------------------------------
        public bool SuppressingNow => frames > 0 && !steps.Stepping;
        public float GateSuppressedPathM => suppressedPathM;
        public bool FusionReady => false;
        public bool HeadingRefined => false;
        public float HeadingDeg => float.NaN;

        public string Summarize() =>
            $"steps={steps.StepCount} suppressed={(frames > 0 ? 100f * suppressFrames / frames : 0f):F0}% " +
            $"overBudget={(frames > 0 ? 100f * overBudgetFrames / frames : 0f):F0}%" +
            (Gate.correctionBudgetPerMeter > 0f
                ? $" corr: earned={gate.BudgetAccruedM:F2}m admitted={gate.BudgetAdmittedM:F2}m"
                : "");

        public string Describe() =>
            $"gate on STEPS, no GPS drift correction | {steps.Describe()} | " +
            $"budget={(budgetEnabled ? plausibleStepLengthM.ToString("F2") + "m/step" : "off")} | " +
            $"gate(minDelta={Gate.minDelta:F4}m jump={Gate.relocalizationJumpThreshold:F2}m" +
            (Gate.correctionBudgetPerMeter > 0f
                ? $" corrBudget={Gate.correctionBudgetPerMeter:P1}/m from=" +
                  (budgetFromStepCount ? "stepcount" : "arpath")
                : "") + ")";
    }

    // =========================================================================
    // 3. Rotation-only — the fallback
    // =========================================================================

    /// <summary>
    /// Discard AR translation entirely: take orientation from AR (gravity-anchored,
    /// wave-immune) and hold the camera's world POSITION at wherever the session
    /// started.
    ///
    /// Implemented the only way it can be implemented in this app —
    /// counter-shifting the XR Origin so the camera's world position holds — which
    /// matters because ARCore does NOT honour `TrackingMode.RotationOnly` (doc
    /// SS4.1 A3: ARKit has `AROrientationTrackingConfiguration`, ARCore has no
    /// equivalent and the feature request went unanswered). So this is shippable on
    /// Android exactly as simulated, no API gamble.
    ///
    /// Expect it to dominate every jitter metric and to score ~0 on
    /// realMotionFidelity. That trade IS the finding, not a defect.
    ///
    /// CAVEAT ON WHAT THE HARNESS CAN SEE: the metrics assume the camera's rotation
    /// is correct for EVERY stabilizer (see ArSimRunner.BearingErrorDeg), so
    /// rotation-only gets no credit here for the thing that actually recommends it
    /// — that VIO orientation is well-conditioned while its translation is not.
    /// What is measured is purely the cost side.
    /// </summary>
    internal sealed class RotationOnlyStabilizer : IArStabilizer, IArStabilizerDiagnostics, IDiscardsTranslation
    {
        public string Name = "rotation-only";
        string IArStabilizer.Name => Name;

        Vector3 locked;
        bool has;
        float movedPathM;
        Vector3 prevOrigin;

        public void Reset() { has = false; locked = Vector3.zero; movedPathM = 0f; prevOrigin = Vector3.zero; }

        public void Step(in ArFrameSample s, FakeArRig rig)
        {
            if (!has) { locked = rig.CameraWorldPos; has = true; prevOrigin = rig.OriginPos; }
            rig.OriginPos = locked - rig.SessionPos;
            movedPathM += (rig.OriginPos - prevOrigin).magnitude;
            prevOrigin = rig.OriginPos;
        }

        public bool SuppressingNow => true;
        public float GateSuppressedPathM => movedPathM;
        public bool FusionReady => false;
        public bool HeadingRefined => false;
        public float HeadingDeg => float.NaN;
        public string Summarize() => "position frozen at session start";

        public string Describe() =>
            "AR translation discarded entirely; camera world position pinned to session start " +
            "(XR Origin counter-shift, the only lever ARCore leaves — doc SS4.1 A3/A4)";
    }

    // =========================================================================
    // 4. Graded gate
    // =========================================================================

    /// <summary>
    /// The research doc's RD-VIO / IMU-PARSAC principle: let the IMU prior be the
    /// arbiter, and make the weighting GRADED rather than a binary on/off gate.
    /// Suppression per frame is `w · delta` with w ∈ [0,1] from the band-limited
    /// accelerometer.
    ///
    /// WHY NOT THE A_max CEILING THE TASK SUGGESTS AS THE SCALE. Worth stating
    /// because the arithmetic kills the idea outright rather than merely
    /// disfavouring it. The doc's ceiling is A_max = a_th/(2πf)²: the largest
    /// phantom amplitude the accelerometer CANNOT veto. At the Noronha groundswell
    /// band (f ≈ 0.07 Hz) and the reference device bias a_th = 0.05 m/s² that is
    /// 0.05/(0.44)² ≈ 0.26 m — three to four times the nominal regime's 0.075 m
    /// phantom amplitude. A gate that suppresses only what exceeds A_max therefore
    /// suppresses NOTHING in the regime that matters: the ceiling certifies the
    /// phantom as admissible, which is precisely why the phantom exists at all
    /// (doc SS1.3). Grading on "excess over what the IMU can justify" is
    /// structurally unable to fire here, so this candidate grades on the other
    /// side of the same fact — how close the band-limited accelerometer is to its
    /// own noise floor, i.e. how confidently the IMU is saying "nothing is
    /// happening" — with a_th used as the floor of that scale.
    ///
    /// Band-limiting is load-bearing and is why this needs the accel VECTOR: the
    /// magnitude channel rectifies tremor into a DC pedestal that no low-pass can
    /// remove, so a magnitude-based band-limit can never reach the noise floor.
    /// </summary>
    internal sealed class GradedGateStabilizer : IArStabilizer, IArStabilizerDiagnostics
    {
        public string Name = "graded-gate";
        string IArStabilizer.Name => Name;

        /// <summary>One-pole corner for the accel vector, Hz. Above the wave bands
        /// (0.03-0.2 Hz) and below physiological tremor (3-10 Hz), which is the
        /// whole window available. 1.0 Hz also leaves roughly half the 1.9 Hz gait
        /// bob in, so walking reads as loud without a separate step detector.</summary>
        public float justifyCornerHz = 1.0f;

        /// <summary>Accel level at which suppression is FULL, m/s². The doc's
        /// measured smartphone accel-bias range is 0.018-0.206 m/s² and 0.05 is its
        /// reference value; below the floor the IMU cannot distinguish motion from
        /// its own bias, so nothing the tracker reports is justified.</summary>
        public float quietMps2 = 0.05f;

        /// <summary>Accel level at which suppression is ZERO, m/s².</summary>
        public float loudMps2 = 0.5f;

        public MotionGateCore.Settings Gate = MotionGateCore.Settings.Defaults;

        Vector3 lpAccel;
        Vector3 lastCam;
        bool hasLast;
        float suppressedPathM;
        double weightSum;
        int frames;
        float lastWeight;

        public void Reset()
        {
            lpAccel = Vector3.zero;
            lastCam = Vector3.zero;
            hasLast = false;
            suppressedPathM = 0f;
            weightSum = 0.0;
            frames = 0;
            lastWeight = 0f;
        }

        public void Step(in ArFrameSample s, FakeArRig rig)
        {
            frames++;

            float tau = 1f / (2f * Mathf.PI * Mathf.Max(1e-4f, justifyCornerHz));
            float a = 1f - Mathf.Exp(-s.Dt / Mathf.Max(1e-6f, tau));
            lpAccel += (s.AccelWorldMps2 - lpAccel) * a;

            float aBand = lpAccel.magnitude;
            float w = 1f - Mathf.Clamp01((aBand - quietMps2) /
                                         Mathf.Max(1e-4f, loudMps2 - quietMps2));
            lastWeight = w;
            weightSum += w;

            Vector3 cam = rig.CameraWorldPos;
            if (!hasLast) { lastCam = cam; hasLast = true; return; }

            Vector3 delta = cam - lastCam;
            float mag = delta.magnitude;

            // Same admissibility tests as MotionGateCore, so the only difference
            // from the binary gate is that the shift is scaled instead of all-or-
            // nothing. Written out rather than reused because MotionGateCore's
            // contract is binary by construction.
            bool eligible = mag > Gate.minDelta &&
                            (Gate.relocalizationJumpThreshold <= 0f ||
                             mag < Gate.relocalizationJumpThreshold);

            if (eligible && w > 0f)
            {
                Vector3 shift = -w * delta;
                rig.OriginPos += shift;
                suppressedPathM += shift.magnitude;
                lastCam = cam + shift;
            }
            else
            {
                lastCam = cam;
            }
        }

        public bool SuppressingNow => lastWeight > 0.5f;
        public float GateSuppressedPathM => suppressedPathM;
        public bool FusionReady => false;
        public bool HeadingRefined => false;
        public float HeadingDeg => float.NaN;

        public string Summarize() =>
            $"meanW={(frames > 0 ? weightSum / frames : 0.0):F2}";

        public string Describe() =>
            $"graded suppression w=1-clamp((|LP{justifyCornerHz:F1}Hz(accel)|-{quietMps2:F3})/" +
            $"({loudMps2:F2}-{quietMps2:F3})) m/s2, no GPS drift correction | " +
            $"gate(minDelta={Gate.minDelta:F4}m jump={Gate.relocalizationJumpThreshold:F2}m)";
    }

    // =========================================================================
    // Variant factories: candidates that are a SETTINGS change, not new code
    // =========================================================================

    /// <summary>
    /// Candidates 2 and 5a, both of which are configuration of the shipped stack
    /// rather than new algorithms — which is the most valuable thing they could
    /// possibly be. Exposed as named factories so a test can construct exactly what
    /// the registry runs.
    /// </summary>
    internal static class ArSimCandidates
    {
        /// <summary>A gyro threshold high enough that the gyro channel drops out of
        /// both of StillnessCore's tests, which is the only way to get an
        /// accel-only stillness signal without editing the shipped core.</summary>
        public const float GyroChannelDisabled = 1e6f;

        /// <summary>
        /// CANDIDATE 2 — the cheap baseline improvement. The gate corrects
        /// TRANSLATION only, and rotation does not imply translation, so including
        /// the gyro in the stillness test is simply the wrong question: it is what
        /// makes panning — 25% of session time — switch the gate off while the user
        /// has not travelled anywhere. This variant asks the accel channel alone.
        ///
        /// Everything else is the shipped stack's authored values, and the GPS
        /// drift correction is off so the comparison is against `gate-only`.
        /// </summary>
        public static ShippedStabilizer AccelOnlyGate() => new ShippedStabilizer
        {
            Name = "accel-gate",
            driftCorrectionEnabled = false,
            Stillness = new StillnessCore.Settings
            {
                gyroStillThreshold = GyroChannelDisabled,
                accelStillThreshold = StillnessCore.Settings.Defaults.accelStillThreshold,
                enterStillTime = StillnessCore.Settings.Defaults.enterStillTime,
                exitMultiplier = StillnessCore.Settings.Defaults.exitMultiplier,
                emaAlpha = StillnessCore.Settings.Defaults.emaAlpha,
            },
        };

        /// <summary>
        /// CANDIDATE 5a — jump re-centering. The shipped gate lets a single-frame
        /// delta above relocalizationJumpThreshold (0.35 m) pass straight through
        /// while still, which BANKS it as a permanent offset; the threshold sits
        /// inside the measured jump distribution (median 0.22, p90 0.55 m), so this
        /// happens routinely. Absorbing the jump instead is already expressible:
        /// MotionGateCore treats a threshold of 0 as "suppress everything".
        ///
        /// Measured on top of the accel-only gate as well as the shipped one, so
        /// the two fixes are separable.
        /// </summary>
        public static MotionGateCore.Settings GateAbsorbingJumps() => new MotionGateCore.Settings
        {
            minDelta = MotionGateCore.Settings.Defaults.minDelta,
            relocalizationJumpThreshold = 0f,
        };

        public static ShippedStabilizer GateOnlyAbsorbingJumps() => new ShippedStabilizer
        {
            Name = "gate-absorb",
            driftCorrectionEnabled = false,
            Gate = GateAbsorbingJumps(),
        };

        public static ShippedStabilizer AccelGateAbsorbingJumps()
        {
            var s = AccelOnlyGate();
            s.Name = "accel-gate-absorb";
            s.Gate = GateAbsorbingJumps();
            return s;
        }

        // =====================================================================
        // 6. CORRECTION BUDGET — decide on PATH TRAVELLED, not on magnitude
        // =====================================================================

        /// <summary>
        /// The mechanism the threshold could never be. Full rationale on
        /// <see cref="MotionGateCore.Settings.correctionBudgetPerMeter"/>; in one
        /// line: the gate suppresses everything while the user stands (exactly what
        /// threshold-0 does, and where it already wins), but a walk earns the
        /// tracker the right to hand back up to `metres walked × assumedRate` of
        /// correction, so the error a walk banked can actually heal.
        /// </summary>
        /// <param name="assumedRatePerMeter">The gate's ASSUMED error rate. Note
        /// this is the gate's belief, not the world's: the sweep's budget block
        /// crosses it against the model's true rate precisely so a match is not
        /// mistaken for a result.</param>
        public static MotionGateCore.Settings BudgetGate(float assumedRatePerMeter,
                                                         float thresholdM = 0f,
                                                         float bleedSpeedMps = 0.25f,
                                                         float floorM = 0.05f,
                                                         float windowS = 2f) =>
            new MotionGateCore.Settings
            {
                minDelta = MotionGateCore.Settings.Defaults.minDelta,
                relocalizationJumpThreshold = thresholdM,
                correctionBudgetPerMeter = assumedRatePerMeter,
                correctionBudgetCapM = 1.5f,
                correctionBudgetHalfLifeS = 120f,
                correctionFloorM = floorM,
                correctionBudgetWindowS = windowS,
                correctionBudgetMinSpeedMps = 0.25f,
                accrueBudgetFromGatePath = true,
                correctionBleedSpeedMps = bleedSpeedMps,
            };

        /// <summary>The shipping candidate: gate-absorb (the current best) plus the
        /// correction budget. Everything else is identical to `gate-absorb`, so any
        /// difference between the two rows is the budget and nothing else.</summary>
        public static ShippedStabilizer GateBudget(float assumedRatePerMeter) => new ShippedStabilizer
        {
            Name = "gate-budget",
            driftCorrectionEnabled = false,
            Gate = BudgetGate(assumedRatePerMeter),
        };

        /// <summary>The budget on the accel-only stillness question instead of the
        /// shipped gyro one. Separates "the budget helps" from "the budget helps
        /// BECAUSE panning stops accruing it" — the gyro gate calls a pan `moving`,
        /// so a pan earns budget it did not travel for.</summary>
        public static ShippedStabilizer AccelGateBudget(float assumedRatePerMeter)
        {
            var s = AccelOnlyGate();
            s.Name = "accel-budget";
            s.Gate = BudgetGate(assumedRatePerMeter);
            return s;
        }

        /// <summary>
        /// CANDIDATE 5b — heading-refinement gating. GpsArKalmanFusion re-measures
        /// the azimuth of Unity +Z by comparing the AR displacement direction
        /// against the GNSS displacement direction over each 8 m segment — and the
        /// AR side is exactly what the waves corrupt, which is how `tilt-down-swash`
        /// ends up with 38.8° of heading error.
        ///
        /// The implementable fix that needs no change to the shipped core: withhold
        /// a GNSS fix from the fusion unless the translation gate has been quiet
        /// (i.e. the user was genuinely moving, not being suppressed) for the whole
        /// preceding window. Phantom-drift segments then never teach the filter an
        /// azimuth. It withholds the Kalman UPDATE too, which is a real consequence
        /// and is reported rather than hidden.
        /// </summary>
        public static ShippedStabilizer ShippedWithHeadingGate() => new ShippedStabilizer
        {
            Name = "shipped+headgate",
            requireCleanGateSecondsForFix = 1.0f,
        };
    }
}
