using UnityEngine;
using Unity.XR.CoreUtils; // XROrigin (com.unity.xr.core-utils, pulled in by AR Foundation)

/// <summary>
/// Blocks wave-induced phantom translation.
///
/// Every frame it measures how far the AR camera translated. If the
/// StillnessDetector says the device is physically still, that translation
/// cannot be real, so the XR Origin is counter-shifted by the same amount —
/// the camera stays put in world space and your content stops swimming.
///
/// Rotation is never touched, and while the IMU reports genuine motion the
/// gate is fully transparent, so walking around / parallax keeps working
/// exactly as before (full 6DOF preserved).
///
/// You cannot write to the AR camera's transform directly (the
/// TrackedPoseDriver owns it), which is why we move the rig instead.
///
/// Execution order: LateUpdate at +100 so it runs after regular scripts but
/// before ArStabilizationController (+200), which applies the GPS drift
/// correction on top.
///
/// This component is a thin shell: the decision and the bookkeeping live in
/// <see cref="MotionGateCore"/> (Assets/Scripts/AR/Core) so they can be
/// measured offline by Assets/Editor/ArSim without a phone.
/// </summary>
[DefaultExecutionOrder(100)]
public class SpuriousMotionGate : MonoBehaviour
{
    [Header("References")]
    public StillnessDetector stillness;
    public XROrigin xrOrigin;      // the rig we counter-shift
    public Transform arCamera;     // XR Origin > Camera Offset > Main Camera

    [Header("Tuning")]
    [Tooltip("Per-frame translation below this is ignored (meters). Filters float noise.")]
    public float minDelta = 0.0005f;

    // 0 (= suppress every translation while still) since 2026-09-28, was 0.35.
    // The offline wave-drift simulator (Assets/Editor/ArSim/, `make ar-sim`)
    // measured 0 as better than 0.35 in all 25 cells of a jump-rate x
    // jump-origin grid, by 18-52 % on the weighted composite, with no
    // intermediate value beating either end.
    //
    // The mechanism, which matters more than the value: this is a SIZE filter
    // and size is the wrong discriminator. Note the predicate below passes
    // deltas LARGER than the threshold. Measured jump magnitudes split as
    // phantom 0.27-0.35 m vs genuine tracker corrections 0.01-0.58 m -- genuine
    // sits BELOW phantom over most of the plausible range. So a non-zero
    // threshold keeps the large corrections (wave-induced error this gate has
    // already cancelled, i.e. redundant) and discards the small ones (the only
    // corrections that can heal error frozen in while the gate was inert during
    // walking). No threshold can separate the two.
    //
    // 0 is therefore the best available value, not a good mechanism. The path-based
    // alternative -- decide on distance TRAVELLED, since genuine error accrues in
    // proportion to it -- was built and measured (see the correction budget below)
    // and came out a wash, so 0 stands.
    //
    // CORRECTION, 2026-09-28: the "known losing corner" this comment used to
    // claim -- a multi-minute walk-and-look session with odometry error at the top
    // of its measured range (14.4 %/m, Feigl GRAPP 2020), where 0 was said to be
    // 5-13 % worse than 0.35 -- does not survive repetition. The genuine error's
    // direction is a uniform azimuth draw per run, and whether it lands across the
    // line of sight or along it changes the cost several-fold, so at 2 seeded
    // repeats the corner appears, at 6 it reverses, and at 24 it is gone: 0 comes
    // out 2.6 % BETTER than 0.35 there. There is no measured regime in which 0
    // loses.
    [Tooltip("A single-frame jump larger than this (meters) is treated as the tracker deliberately relocalizing itself and is allowed through even while still. 0 = suppress everything, which is the default: see the comment above. Magnitude does not distinguish phantom jumps from genuine corrections.")]
    public float relocalizationJumpThreshold = 0f;

    [Header("Correction budget (OFF by default — see below)")]

    // THE PATH-BASED MECHANISM, built and measured 2026-09-28, shipped DISABLED.
    //
    // The idea is sound and is the one the threshold comment above asks for:
    // non-wave odometry error accrues in proportion to distance TRAVELLED (Feigl
    // GRAPP 2020: "up to 14.4 cm/m ... quasi-directly proportional to the path
    // length"), the gate is inert while the user walks, so that error goes into
    // the camera pose and is frozen there the moment they stop. A walk therefore
    // earns the tracker the right to hand back `metres x correctionBudgetPerMeter`
    // of correction, and only that much is admitted.
    //
    // WHY IT IS OFF. The offline harness (`make ar-sim`, `make ar-sweep
    // BLOCK=budget`) measured it against the shipped gate across the whole
    // measured odometry-error bracket and it is a WASH: +-0.5 % on the weighted
    // composite in every cell of a 4 assumed-rate x 5 true-rate grid. The reason
    // is structural and worth knowing before anyone tries again -- the budget
    // bounds how MUCH correction is admitted but cannot tell WHICH correction is
    // genuine, and at the model's ~40 % genuine/phantom mixture most of the credit
    // is spent healing nothing. It is a better-founded mechanism than a magnitude
    // threshold and it is not a better gate.
    //
    // Turn it on by setting correctionBudgetPerMeter > 0 on this component in the
    // Inspector (there is no on-device panel any more), which is worth doing once
    // there is a real measurement of how fast this device's odometry drifts per
    // metre walked --
    // the one number the whole mechanism is a function of and that nobody has.
    // 0.07 (the upper half of the 0.0034-0.144 measured bracket, on the grounds
    // that surf is a dynamic environment) is the value the harness ran -- but note
    // the sweep's own answer: across every cell of the assumed x true grid the
    // LOWEST assumed rate tried (0.01) scored best, including where the true rate
    // was 14.4 %/m. Mismatching the rate costs at most 1.4 %, so the parameter
    // barely matters, and the direction it points is "credit less".

    [Tooltip("Metres of correction credit earned per metre TRAVELLED. 0 = the budget is off (default). Measured bracket: 0.0034 (static scene) to 0.144 (dynamic environment).")]
    public float correctionBudgetPerMeter = 0f;

    [Tooltip("Cap on accrued credit, metres. Past ~1.5-2 m the tracker has relocalized or lost tracking, so there is no larger outstanding error to owe against.")]
    public float correctionBudgetCapM = 1.5f;

    [Tooltip("Half-life of UNSPENT credit, seconds. Credit unclaimed for many jump intervals (~14 s each) is stale. 0 disables the decay.")]
    public float correctionBudgetHalfLifeS = 120f;

    [Tooltip("Per-frame translation at or above this (metres) counts as a candidate CORRECTION and may spend budget; below it is wave oscillation and is always suppressed. Also caps what one accrual window can credit.")]
    public float correctionFloorM = 0.05f;

    [Tooltip("Accrual window, seconds: credit comes from NET horizontal displacement over each window, not summed path — a pan and the wave both move the camera and both come back. 0 falls back to per-frame path.")]
    public float correctionBudgetWindowS = 2f;

    [Tooltip("A window only counts as travel at this average speed or above (m/s). Stops a phone sweep being paid as a walk. 0 accepts every window.")]
    public float correctionBudgetMinSpeedMps = 0.25f;

    [Tooltip("How fast an admitted correction is bled into the origin, m/s. 0 applies it in one frame, i.e. as the visible pop the tracker emitted. 0.25 mirrors GpsArKalmanFusion.maxCorrectionSpeed.")]
    public float correctionBleedSpeedMps = 0.25f;

    /// <summary>Correction credit currently available, metres — a debug readout
    /// (ArStabilizationController.BuildTelemetry). Always 0 while
    /// <see cref="correctionBudgetPerMeter"/> is 0.</summary>
    public float CorrectionBudgetM => core.CorrectionBudgetM;

    /// <summary>Total metres of tracker correction the budget has admitted.</summary>
    public float BudgetAdmittedM => core.BudgetAdmittedM;

    /// <summary>Total spurious translation cancelled this session (for debug HUDs).</summary>
    public Vector3 TotalSuppressed => core.TotalSuppressed;

    readonly MotionGateCore core = new MotionGateCore();

    void OnEnable() => core.ResetTracking();

    public void ResetSuppressed() => core.ResetSuppressed();

    void LateUpdate()
    {
        if (arCamera == null || xrOrigin == null) return;

        // Copied every frame, not once, so an Inspector edit made while the app
        // is running takes effect on the very next frame.
        core.Config = new MotionGateCore.Settings
        {
            minDelta = minDelta,
            relocalizationJumpThreshold = relocalizationJumpThreshold,
            correctionBudgetPerMeter = correctionBudgetPerMeter,
            correctionBudgetCapM = correctionBudgetCapM,
            correctionBudgetHalfLifeS = correctionBudgetHalfLifeS,
            correctionFloorM = correctionFloorM,
            correctionBudgetWindowS = correctionBudgetWindowS,
            correctionBudgetMinSpeedMps = correctionBudgetMinSpeedMps,
            accrueBudgetFromGatePath = true,
            correctionBleedSpeedMps = correctionBleedSpeedMps,
        };

        Vector3 shift = core.Step(arCamera.position, stillness != null && stillness.IsStill,
                                  Time.deltaTime);
        if (core.SuppressedLastStep)
            xrOrigin.transform.position += shift;
    }
}
