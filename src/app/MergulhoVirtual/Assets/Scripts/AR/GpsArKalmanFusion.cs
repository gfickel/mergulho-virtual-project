using System.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Fuses AR dead-reckoning (fast, smooth, drifts) with GNSS fixes
/// (slow, noisy, absolute) using two independent 1D Kalman filters on the
/// east/north axes.
///
/// PREDICT (every frame): the AR camera's displacement, rotated into
///   east/north coordinates, moves the state forward. Process noise grows
///   with distance walked — the more you move, the less we trust pure AR.
/// UPDATE (each new GNSS fix from GnssProvider): the fix pulls the state
///   toward the measured position, weighted by its reported accuracy.
///
/// The difference between the fused estimate and the AR-only estimate is
/// the accumulated drift. ApplyDriftCorrection() bleeds it into the
/// XR Origin at a capped speed so the correction is invisible to the user.
///
/// Heading (the weakest link): converting AR-frame motion to east/north
/// needs the real-world azimuth of Unity's +Z. We seed it from a ~2 s
/// compass average at startup (good to ~5–15°), then keep refining it while
/// the user walks by comparing the AR displacement direction against the
/// GNSS track over the same segment — GPS course while walking beats the
/// magnetometer by a wide margin.
///
/// Expectations: GNSS fixes at 1–2 Hz with 3–15 m accuracy can only correct
/// LOW-FREQUENCY drift (meters over minutes). High-frequency wave-induced
/// motion is SpuriousMotionGate's job.
///
/// This component is a thin shell: the filter, the heading refinement and the
/// coordinate maths live in <see cref="DriftFusionCore"/>
/// (Assets/Scripts/AR/Core) so they can be measured offline by
/// Assets/Editor/ArSim without a phone; the compass average and the GnssProvider
/// / Transform plumbing stay here.
/// </summary>
public class GpsArKalmanFusion : MonoBehaviour
{
    [Header("References")]
    public Transform arCamera;
    public XROrigin xrOrigin;      // optional: only needed for ApplyDriftCorrection
    public GnssProvider gnss;

    [Header("Kalman tuning")]
    [Tooltip("Variance (m^2) added per meter walked. Higher = trust GNSS more when moving.")]
    public float processNoisePerMeter = 0.05f;

    [Tooltip("GNSS accuracies better than this are clamped (phones over-report).")]
    public float minGpsAccuracy = 3f;

    [Tooltip("Ignore fixes with accuracy worse than this (meters).")]
    public float maxUsableAccuracy = 25f;

    [Header("Drift correction")]
    [Tooltip("Max speed (m/s) at which drift is bled into the XR Origin. Keep small so it is imperceptible.")]
    public float maxCorrectionSpeed = 0.25f;

    [Header("Heading calibration")]
    [Tooltip("Seconds of compass averaging for the initial heading seed.")]
    public float compassAverageSeconds = 2f;

    [Tooltip("Walk this many meters (per GNSS track) before each heading refinement from the GPS course. Smaller = faster calibration, noisier.")]
    public float headingSegmentMeters = 8f;

    [Tooltip("Blend factor for each heading refinement after the first (1 = jump to the new measurement).")]
    [Range(0f, 1f)]
    public float headingBlend = 0.5f;

    public bool Ready => core.Ready;

    /// <summary>Azimuth (deg clockwise from true north) that Unity +Z points toward.</summary>
    public float HeadingDeg => core.HeadingDeg;

    /// <summary>True once at least one GPS-track refinement replaced the compass seed.</summary>
    public bool HeadingRefined => core.HeadingRefined;

    /// <summary>Fused position, meters east/north of the geo origin (first fix).</summary>
    public Vector2 FusedEnu => core.FusedEnu;

    /// <summary>Fused - AR-only estimate: the drift the filter believes has accumulated.</summary>
    public Vector2 DriftError => core.DriftError;

    /// <summary>Current estimate std-dev per axis (m), for telemetry.</summary>
    public Vector2 EstimateStdDev => core.EstimateStdDev;

    readonly DriftFusionCore core = new DriftFusionCore();
    int lastFixCount;

    IEnumerator Start()
    {
        if (gnss == null || arCamera == null)
        {
            Debug.LogWarning("GpsArKalmanFusion: missing gnss/arCamera reference — fusion disabled.");
            yield break;
        }

        // Wait for the first GNSS fix (GnssProvider handles permission waits).
        while (!gnss.HasFix)
            yield return new WaitForSeconds(0.5f);

        var first = gnss.Latest;
        lastFixCount = gnss.FixCount;

        // Seed the heading from a short compass average. Input.compass keeps
        // working under the new Input System (unlike Input.gyro), but guard
        // anyway — a failed seed just means we wait for the GPS-track
        // refinement while walking.
        var seed = new HeadingSeedAccumulator();
        try { Input.compass.enabled = true; }
        catch { }
        float tEnd = Time.realtimeSinceStartup + compassAverageSeconds;
        while (Time.realtimeSinceStartup < tEnd)
        {
            try { seed.Add(Input.compass.trueHeading); }
            catch { break; }
            yield return new WaitForSeconds(0.1f);
        }

        PushConfig();
        core.Initialize(first, arCamera.position, seed.ResolveDeg(0f));

        Debug.Log($"GpsArKalmanFusion: ready. origin=({core.OriginLatitude:F6},{core.OriginLongitude:F6}) heading seed={core.HeadingDeg:F1}°");
    }

    void Update()
    {
        if (!core.Ready) return;

        PushConfig();

        // The caller owns the fix bookkeeping: the core only wants a flag.
        bool hasNewFix = gnss != null && gnss.FixCount != lastFixCount;
        GnssFix fix = default;
        if (hasNewFix)
        {
            lastFixCount = gnss.FixCount;
            fix = gnss.Latest;
        }

        core.Step(arCamera.position, hasNewFix, fix);
    }

    /// <summary>
    /// Call from LateUpdate (after SpuriousMotionGate) to gently shift the
    /// XR Origin so geo-anchored content re-aligns with the real world.
    /// Best called while the user is walking, when a slow shift is invisible.
    /// </summary>
    public void ApplyDriftCorrection()
    {
        if (!core.Ready || xrOrigin == null) return;

        PushConfig();
        if (core.TryComputeDriftCorrection(Time.deltaTime, out Vector3 shift))
            xrOrigin.transform.position += shift;
    }

    /// <summary>Mirror the public fields into the core. Called on every entry
    /// point, not once, so a value written to those fields while the app runs (an
    /// Inspector edit during Play) takes effect on the next frame.</summary>
    void PushConfig()
    {
        core.Config = new DriftFusionCore.Settings
        {
            processNoisePerMeter = processNoisePerMeter,
            minGpsAccuracy = minGpsAccuracy,
            maxUsableAccuracy = maxUsableAccuracy,
            maxCorrectionSpeed = maxCorrectionSpeed,
            headingSegmentMeters = headingSegmentMeters,
            headingBlend = headingBlend,
        };
    }
}
