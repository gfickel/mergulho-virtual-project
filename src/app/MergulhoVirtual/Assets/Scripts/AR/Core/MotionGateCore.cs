using UnityEngine;

/// <summary>
/// The anti-wave translation gate, extracted from <see cref="SpuriousMotionGate"/>.
///
/// Given the AR camera's WORLD position each frame and whether the IMU says the
/// device is physically still, it returns the counter-shift to apply to the XR
/// Origin so the camera's world position holds. Rotation is never touched.
///
/// Engine-free by contract: the caller reads the Transform and applies the
/// returned shift; this class only does the arithmetic and the bookkeeping.
///
/// FEEDBACK LOOP, deliberately preserved: <see cref="lastCamPos"/> stores the
/// POST-correction position, so a suppressed frame leaves the reference where the
/// camera effectively stayed rather than where the tracker claimed it went. The
/// caller must therefore pass the camera world position it reads AFTER the origin
/// moved (i.e. read the Transform fresh every frame), which is what the real
/// LateUpdate ordering does.
///
/// TWO FILTERS, and they compose (see <see cref="Step"/>):
///
///   1. <see cref="Settings.relocalizationJumpThreshold"/> — a pure MAGNITUDE
///      filter, and the wrong mechanism. Measured in the offline harness, phantom
///      jumps land at 0.27–0.35 m and genuine tracker corrections at 0.01–0.58 m,
///      i.e. genuine sits mostly BELOW phantom, so no size can separate them.
///      Shipped at 0 ("suppress every translation while still") because that is
///      the best available value of a bad knob.
///   2. <see cref="Settings.correctionBudgetPerMeter"/> — the CORRECTION BUDGET,
///      which decides on PATH TRAVELLED instead. Non-wave odometry error accrues
///      in proportion to distance walked (Feigl GRAPP 2020: "up to 14.4 cm/m …
///      quasi-directly proportional to the path length"; Here To Stay: ~0.34 %/m
///      for ARCore in an ordinary static scene), and the gate is inert by design
///      while the IMU reports travel — so that error goes straight into the
///      camera's world position and the gate freezes it in the moment the user
///      stops. The budget is the amount of correction the tracker is therefore
///      OWED, and only that much is let through.
/// </summary>
internal sealed class MotionGateCore
{
    /// <summary>Fallbacks used when a caller built <see cref="Settings"/> with an
    /// object initializer and left a budget field at its zero default. Only
    /// consulted while the budget is enabled, so a budget-off Settings is
    /// unaffected by them.</summary>
    const float FallbackCorrectionFloorM = 0.05f;
    const float FallbackBudgetCapM = 1.5f;

    /// <summary>Fastest a user is assumed to travel on a beach, m/s. Caps what one
    /// accrual window can credit, so a relocalisation jump that lands mid-walk
    /// cannot be banked as travel. 2 m/s is a brisk walk; the app's own walking
    /// scenarios run at 0.75–1.2 m/s.</summary>
    const float MaxTravelSpeedMps = 2f;

    internal struct Settings
    {
        /// <summary>Per-frame translation below this is ignored (meters). Filters float noise.</summary>
        public float minDelta;

        /// <summary>A single-frame jump larger than this (meters) is treated as the
        /// tracker deliberately relocalizing itself and is allowed through even
        /// while still. Set to 0 to suppress everything.</summary>
        public float relocalizationJumpThreshold;

        // ------------------------------------------------------ correction budget

        /// <summary>
        /// Metres of correction credit earned per metre of camera path travelled,
        /// i.e. the expected non-wave registration error rate. 0 DISABLES the
        /// budget entirely and the gate behaves exactly as it did before the budget
        /// existed (bit-identical — the whole budget block is behind this test).
        ///
        /// The measured bracket is 0.0034 (ARCore walk-and-return in an ordinary
        /// static scene, Here To Stay arXiv:2109.14757) to 0.144 (AR systems in a
        /// large dynamic environment, Feigl GRAPP 2020). A surf scene is a dynamic
        /// environment, so the useful range is the upper half of that.
        /// </summary>
        public float correctionBudgetPerMeter;

        /// <summary>Cap on the accrued budget, metres. Past ~1.5–2 m the tracker
        /// has relocalised or declared tracking lost, so there is no larger
        /// outstanding error to owe a correction against. &lt;= 0 uses the fallback
        /// (1.5 m).</summary>
        public float correctionBudgetCapM;

        /// <summary>Half-life of UNSPENT budget, seconds. Credit that has not been
        /// claimed within many jump intervals (the research model's jump rate is
        /// ~0.07 Hz, one per ~14 s) is stale, and spending it later would admit a
        /// phantom jump on the strength of a walk that finished minutes ago.
        /// &lt;= 0 disables the decay.</summary>
        public float correctionBudgetHalfLifeS;

        /// <summary>
        /// The DISCONTINUITY boundary, metres of per-frame camera translation. Two
        /// roles, deliberately one constant:
        ///
        ///   • the budget is only ever SPENT on a frame whose delta reaches it — a
        ///     map correction is a single-frame step, whereas the wave oscillation
        ///     slews at ~1 mm/frame (nominal) to ~13 mm/frame (catastrophic), so
        ///     without this floor the oscillation would drain the whole budget in
        ///     well under a minute and pass straight through;
        ///   • per-frame ACCRUAL is clamped to it, because a single-frame delta
        ///     larger than this is not travel. At 30 fps 0.05 m/frame is 1.5 m/s,
        ///     i.e. a brisk walk, so the clamp only ever bites on discontinuities
        ///     and errs toward under-crediting.
        ///
        /// This is still a magnitude test, but of a question magnitude can answer:
        /// the two populations it separates (oscillation slew vs. single-frame jump)
        /// are 1–2 orders of magnitude apart, unlike the two JUMP populations, which
        /// overlap and are what <see cref="relocalizationJumpThreshold"/> fails on.
        /// &lt;= 0 uses the fallback (0.05 m).
        /// </summary>
        public float correctionFloorM;

        /// <summary>
        /// Accrual window, seconds: budget is earned from the NET horizontal
        /// displacement over each window, not from summed per-frame path. 0 falls
        /// back to per-frame path.
        ///
        /// This is the difference between measuring TRAVEL and measuring MOTION,
        /// and the harness made it non-optional. Summed path credits a sweep of the
        /// phone (the wrist/elbow/torso pivot really does move the camera) and it
        /// credits the wave oscillation itself on every frame the IMU calls the
        /// device "moving" — so `tilt-down-swash`, a scenario in which the user
        /// never travels one metre, earned several metres of credit and spent it
        /// admitting phantom corrections. Net displacement over a couple of seconds
        /// is ~0 for a pan (it comes back), ~0 for oscillation (ditto), and the
        /// full stride length for a walk.
        /// </summary>
        public float correctionBudgetWindowS;

        /// <summary>
        /// A window only counts as TRAVEL if its net horizontal displacement
        /// reaches this average speed, m/s. Below it the window earns nothing.
        ///
        /// This is what stops a pan being paid as a walk, and it is the difference
        /// between the budget being free and being a small net harm: `tilt-down-swash`
        /// is 90 s in which the user never travels, but the gyro-based stillness
        /// verdict calls the pan segment "moving", and a 2 s window of a 44°
        /// sweep about an elbow displaces the camera ~0.15 m — enough credit to
        /// admit one phantom correction, which is pure damage. A walk covers 2.4 m
        /// in the same window. 0.25 m/s sits an order of magnitude below any real
        /// walking pace (the harness's slow shuffle on sand is 0.75 m/s) and an
        /// order of magnitude above a sweep, so it is not a tuned number.
        /// 0 accepts every window.
        /// </summary>
        public float correctionBudgetMinSpeedMps;

        /// <summary>When true (the shipped behaviour) the gate accrues budget from
        /// the camera path it already measures, on the frames it is inert. False is
        /// for a caller that wants to supply the path itself via
        /// <see cref="AccrueCorrectionBudget"/> — e.g. the offline harness's
        /// step-count variant, which is the alternative pathDelta source.</summary>
        public bool accrueBudgetFromGatePath;

        /// <summary>
        /// How fast an ADMITTED correction is bled into the origin, m/s. 0 applies
        /// it in the frame it arrived, i.e. as the pop the tracker emitted.
        ///
        /// Non-zero is strongly preferred and the harness says why: a correction
        /// that lands in one frame is a 1–2 m teleport of everything anchored in
        /// the world, and the perceptual cost model charges oscillation ~4 s of
        /// equivalent offset per deg/s — so admitting a jump instantly can cost
        /// MORE than the error it removed, which is the trap that makes
        /// "suppress everything" look good. Bleeding the same correction in at a
        /// walking-pace fraction removes the error and is invisible. 0.25 m/s
        /// mirrors GpsArKalmanFusion.maxCorrectionSpeed, which is the project's
        /// existing answer to the identical question for the GPS drift channel.
        /// </summary>
        public float correctionBleedSpeedMps;

        /// <summary>
        /// The PRE-BUDGET baseline: the gate as it behaved before the correction
        /// budget existed (and what every golden/characterization test pins).
        /// <see cref="correctionBudgetPerMeter"/> is 0 here, so this is NOT what the
        /// component authors — see <see cref="Shipped"/>.
        /// </summary>
        public static Settings Defaults => new Settings
        {
            minDelta = 0.0005f,
            relocalizationJumpThreshold = 0.35f,
            correctionBudgetPerMeter = 0f,
            accrueBudgetFromGatePath = true,
        };

        /// <summary>
        /// The values actually authored on <see cref="SpuriousMotionGate"/> and in
        /// MainScene. Kept next to <see cref="Defaults"/> so the divergence is
        /// visible in one place instead of only in the Inspector; pinned by
        /// ArStabilizationComponentTests.
        ///
        /// It differs from <see cref="Defaults"/> in exactly one value that matters
        /// —  relocalizationJumpThreshold, 0 rather than 0.35 — because the budget
        /// ships OFF: the harness measured it as a wash against the plain gate
        /// (±0.5 % of the weighted composite across the whole measured odometry-rate
        /// bracket), which is not enough to change a default on. The other budget
        /// fields carry their authored values so that turning the rate up on the
        /// beach gives a working configuration rather than a set of zeroes.
        /// </summary>
        public static Settings Shipped => new Settings
        {
            minDelta = 0.0005f,
            relocalizationJumpThreshold = 0f,
            correctionBudgetPerMeter = 0f,
            correctionBudgetCapM = 1.5f,
            correctionBudgetHalfLifeS = 120f,
            correctionFloorM = 0.05f,
            correctionBudgetWindowS = 2f,
            correctionBudgetMinSpeedMps = 0.25f,
            accrueBudgetFromGatePath = true,
            correctionBleedSpeedMps = 0.25f,
        };
    }

    public Settings Config = Settings.Defaults;

    /// <summary>Total spurious translation cancelled this session (for debug HUDs).</summary>
    public Vector3 TotalSuppressed { get; private set; }

    /// <summary>True when the most recent <see cref="Step"/> returned a shift the
    /// caller must apply — a suppression, or a slice of a correction being bled in.
    /// Callers use this instead of testing the returned shift against
    /// Vector3.zero — Vector3's == operator is approximate (~1e-5 m), which would
    /// silently drop a genuine sub-millimetre suppression when minDelta is 0.</summary>
    public bool SuppressedLastStep { get; private set; }

    /// <summary>Correction credit currently available, metres. A debug-HUD /
    /// tuning-panel readout, and the thing the invariants are stated about.</summary>
    public float CorrectionBudgetM { get; private set; }

    /// <summary>Total metres of camera translation the budget has ADMITTED — i.e.
    /// tracker corrections that were let through while the gate was otherwise
    /// suppressing. 0 means the budget never fired.</summary>
    public float BudgetAdmittedM { get; private set; }

    /// <summary>Total credit ever accrued, metres (before decay and spending).</summary>
    public float BudgetAccruedM { get; private set; }

    /// <summary>An admitted correction still being bled into the origin, metres.
    /// Zero unless <see cref="Settings.correctionBleedSpeedMps"/> is set.</summary>
    public Vector3 PendingCorrectionM { get; private set; }

    Vector3 lastCamPos;
    bool hasLast;

    Vector3 windowRefPos;
    float windowElapsedS;
    bool hasWindowRef;

    float BudgetCapM => Config.correctionBudgetCapM > 0f
        ? Config.correctionBudgetCapM : FallbackBudgetCapM;

    float CorrectionFloorM => Config.correctionFloorM > 0f
        ? Config.correctionFloorM : FallbackCorrectionFloorM;

    /// <summary>Forget the previous camera position — mirrors the component's
    /// OnEnable, so the first frame after a re-enable is never treated as a jump.
    /// The budget deliberately SURVIVES this: the error a walk banked does not stop
    /// being owed because the component was toggled.</summary>
    public void ResetTracking() => hasLast = false;

    public void ResetSuppressed()
    {
        TotalSuppressed = Vector3.zero;
        BudgetAdmittedM = 0f;
        BudgetAccruedM = 0f;
    }

    /// <summary>Full reset (tracking + counters + budget), for reuse across
    /// simulation runs.</summary>
    public void Reset()
    {
        ResetTracking();
        ResetSuppressed();
        SuppressedLastStep = false;
        lastCamPos = Vector3.zero;
        CorrectionBudgetM = 0f;
        PendingCorrectionM = Vector3.zero;
        hasWindowRef = false;
        windowRefPos = Vector3.zero;
        windowElapsedS = 0f;
    }

    static float FlatDistance(Vector3 a, Vector3 b) =>
        new Vector2(a.x - b.x, a.z - b.z).magnitude;

    /// <summary>Credit one accrual window, if its net displacement actually looks
    /// like travel rather than like a sweep or a wave.</summary>
    void AccrueTravelWindow(float netM, float elapsedS)
    {
        if (elapsedS <= 0f) return;
        if (netM < elapsedS * Config.correctionBudgetMinSpeedMps) return;
        AccrueCorrectionBudget(Mathf.Min(netM, elapsedS * MaxTravelSpeedMps));
    }

    /// <summary>
    /// Add <paramref name="pathM"/> metres of travel to the correction budget, for
    /// a caller that measures the path itself (<see cref="Settings.accrueBudgetFromGatePath"/>
    /// = false). No-op while the budget is disabled.
    /// </summary>
    public void AccrueCorrectionBudget(float pathM)
    {
        if (Config.correctionBudgetPerMeter <= 0f || pathM <= 0f) return;
        float earned = pathM * Config.correctionBudgetPerMeter;
        BudgetAccruedM += earned;
        CorrectionBudgetM = Mathf.Min(BudgetCapM, CorrectionBudgetM + earned);
    }

    /// <summary>
    /// Advance one frame.
    /// </summary>
    /// <param name="camWorldPos">The AR camera's world position this frame.</param>
    /// <param name="isStill">The IMU stillness verdict.</param>
    /// <param name="dt">Frame delta, seconds. Only used for the correction
    /// budget's decay; 0 (the default, for every pre-budget caller) simply skips
    /// the decay.</param>
    /// <returns>The shift to ADD to the XR Origin's position, or Vector3.zero
    /// when nothing is being suppressed.</returns>
    public Vector3 Step(Vector3 camWorldPos, bool isStill, float dt = 0f)
    {
        SuppressedLastStep = false;
        bool budgetOn = Config.correctionBudgetPerMeter > 0f;

        if (budgetOn && dt > 0f && Config.correctionBudgetHalfLifeS > 0f)
            CorrectionBudgetM *= Mathf.Exp(-0.6931472f * dt / Config.correctionBudgetHalfLifeS);

        Vector3 camPos = camWorldPos;
        if (!hasLast)
        {
            lastCamPos = camPos;
            hasLast = true;
            return Vector3.zero;
        }

        Vector3 delta = camPos - lastCamPos;
        float mag = delta.magnitude;

        // ACCRUE while the gate is inert, i.e. exactly when real travel is what the
        // tracker is reporting. Standing still accrues ~nothing, which is why the
        // budget stays ~0 through a standing session and every translation is
        // suppressed there — the case threshold-0 already won.
        //
        // HORIZONTAL and NET, not summed path. Registration error accrues with
        // TRAVEL: travel is horizontal (the vertical channel of a walking delta is
        // gait bob, which returns every step and credited ~2x the distance actually
        // walked), and travel is displacement, not motion (a pan and the wave
        // oscillation both move the camera and both come back).
        if (budgetOn && Config.accrueBudgetFromGatePath)
        {
            if (!isStill)
            {
                if (Config.correctionBudgetWindowS > 0f)
                {
                    if (!hasWindowRef) { windowRefPos = camPos; windowElapsedS = 0f; hasWindowRef = true; }
                    windowElapsedS += dt;
                    if (windowElapsedS >= Config.correctionBudgetWindowS)
                    {
                        AccrueTravelWindow(FlatDistance(camPos, windowRefPos), windowElapsedS);
                        windowRefPos = camPos;
                        windowElapsedS = 0f;
                    }
                }
                else
                {
                    AccrueCorrectionBudget(Mathf.Min(new Vector2(delta.x, delta.z).magnitude,
                                                     CorrectionFloorM));
                }
            }
            else if (hasWindowRef)
            {
                // Motion just stopped: close the part-window so the last leg of a
                // walk is credited instead of being dropped on the floor.
                AccrueTravelWindow(FlatDistance(camPos, windowRefPos),
                                   Mathf.Max(windowElapsedS, 1e-3f));
                hasWindowRef = false;
                windowElapsedS = 0f;
            }
        }

        bool suppress =
            isStill &&
            mag > Config.minDelta &&
            (Config.relocalizationJumpThreshold <= 0f || mag < Config.relocalizationJumpThreshold);

        Vector3 shift = Vector3.zero;
        if (suppress)
        {
            // PARTIAL ADMISSION. On a delta of magnitude `mag` with budget `b`,
            // min(mag, b) is admitted and the remainder is suppressed — an
            // all-or-nothing decision would either fabricate the difference (admit a
            // 1.4 m jump against 0.2 m of owed error) or freeze a correction the
            // tracker was right to make. The budget is charged only for what it
            // admitted.
            float admitted = 0f;
            if (budgetOn && CorrectionBudgetM > 0f && mag >= CorrectionFloorM)
            {
                admitted = Mathf.Min(mag, CorrectionBudgetM);
                CorrectionBudgetM -= admitted;
                BudgetAdmittedM += admitted;
            }

            // With a bleed configured the admitted part is still SUPPRESSED this
            // frame and queued instead, so the user never sees the pop; without one
            // it passes through in place, which is the tracker's own timing.
            bool queue = admitted > 0f && Config.correctionBleedSpeedMps > 0f;
            float suppressMag = queue ? mag : mag - admitted;
            if (queue) PendingCorrectionM += delta * (admitted / mag);

            if (suppressMag > 0f)
            {
                // IMU says we're not moving -> this translation is hallucinated.
                // Counter-shift the whole rig so the camera's world position holds.
                Vector3 cancelled = delta * (suppressMag / mag);
                shift = -cancelled;
                camPos -= cancelled;
                TotalSuppressed += cancelled;
                SuppressedLastStep = true;
            }
        }

        // ---- bleed a queued correction in, at walking pace ------------------
        // Deliberately NOT gated on isStill: the correction is owed either way, and
        // a shift applied while the user is moving is the least visible of all.
        if (PendingCorrectionM != Vector3.zero && dt > 0f && Config.correctionBleedSpeedMps > 0f)
        {
            Vector3 release = Vector3.ClampMagnitude(
                PendingCorrectionM, Config.correctionBleedSpeedMps * dt);
            PendingCorrectionM -= release;
            shift += release;
            // The camera moves with the origin, so the reference has to follow or
            // the next frame reads the bleed back as a phantom delta and cancels it.
            camPos += release;
            SuppressedLastStep = true;   // the caller must apply `shift`
        }

        lastCamPos = camPos;
        return shift;
    }
}
