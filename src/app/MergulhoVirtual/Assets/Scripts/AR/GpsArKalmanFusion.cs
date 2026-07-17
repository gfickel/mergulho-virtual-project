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

    public bool Ready { get; private set; }

    /// <summary>Azimuth (deg clockwise from true north) that Unity +Z points toward.</summary>
    public float HeadingDeg => headingDeg;

    /// <summary>True once at least one GPS-track refinement replaced the compass seed.</summary>
    public bool HeadingRefined { get; private set; }

    /// <summary>Fused position, meters east/north of the geo origin (first fix).</summary>
    public Vector2 FusedEnu => x;

    /// <summary>Fused - AR-only estimate: the drift the filter believes has accumulated.</summary>
    public Vector2 DriftError => x - arEnu;

    /// <summary>Current estimate std-dev per axis (m), for telemetry.</summary>
    public Vector2 EstimateStdDev => new Vector2(Mathf.Sqrt(p.x), Mathf.Sqrt(p.y));

    // --- filter state ---
    Vector2 x;                 // state: ENU position
    Vector2 p;                 // estimate variance per axis
    Vector2 arEnu;             // AR-only dead reckoning in ENU
    double lat0, lon0;         // geo origin
    float headingDeg;          // azimuth of Unity +Z
    Vector3 lastCamPos;
    int lastFixCount;

    // --- heading refinement anchors ---
    Vector2 anchorGpsEnu;
    Vector3 anchorCamPos;
    bool anchorValid;

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
        lat0 = first.latitude;
        lon0 = first.longitude;
        lastFixCount = gnss.FixCount;

        // Seed the heading from a short compass average. Input.compass keeps
        // working under the new Input System (unlike Input.gyro), but guard
        // anyway — a failed seed just means we wait for the GPS-track
        // refinement while walking.
        headingDeg = 0f;
        try { Input.compass.enabled = true; }
        catch { }
        float sinSum = 0f, cosSum = 0f;
        int samples = 0;
        float tEnd = Time.realtimeSinceStartup + compassAverageSeconds;
        while (Time.realtimeSinceStartup < tEnd)
        {
            try
            {
                float h = Input.compass.trueHeading * Mathf.Deg2Rad;
                sinSum += Mathf.Sin(h);
                cosSum += Mathf.Cos(h);
                samples++;
            }
            catch { break; }
            yield return new WaitForSeconds(0.1f);
        }
        if (samples > 0)
            headingDeg = Mathf.Atan2(sinSum, cosSum) * Mathf.Rad2Deg;

        lastCamPos = arCamera.position;
        x = Vector2.zero;
        arEnu = Vector2.zero;
        float a0 = Mathf.Max(first.horizontalAccuracy, minGpsAccuracy);
        p = Vector2.one * (a0 * a0);

        anchorGpsEnu = Vector2.zero;
        anchorCamPos = arCamera.position;
        anchorValid = true;

        Ready = true;
        Debug.Log($"GpsArKalmanFusion: ready. origin=({lat0:F6},{lon0:F6}) heading seed={headingDeg:F1}°");
    }

    void Update()
    {
        if (!Ready) return;

        // ---------- PREDICT: AR displacement as the motion model ----------
        Vector3 camPos = arCamera.position;
        Vector3 d = camPos - lastCamPos;
        lastCamPos = camPos;

        Vector2 dEnu = UnityXZToEnu(new Vector2(d.x, d.z), headingDeg);
        x += dEnu;
        arEnu += dEnu;
        p += Vector2.one * (processNoisePerMeter * dEnu.magnitude);

        // ---------- UPDATE: new GNSS fix ----------
        if (gnss.FixCount == lastFixCount) return;
        lastFixCount = gnss.FixCount;
        var fix = gnss.Latest;
        if (fix.horizontalAccuracy > maxUsableAccuracy) return;

        Vector2 z = GeoToEnu(fix.latitude, fix.longitude);
        float acc = Mathf.Max(fix.horizontalAccuracy, minGpsAccuracy);
        float r = acc * acc;

        for (int i = 0; i < 2; i++)
        {
            float k = p[i] / (p[i] + r);   // Kalman gain
            x[i] += k * (z[i] - x[i]);
            p[i] *= (1f - k);
        }

        RefineHeading(z, camPos);
    }

    /// <summary>
    /// Compare the direction the GNSS track moved with the direction the AR
    /// camera moved over the same segment; the angle between them corrects
    /// the heading. Only trusted when both displacements roughly agree in
    /// length (rules out GPS jumps and gated/suppressed AR motion).
    /// </summary>
    void RefineHeading(Vector2 gpsEnu, Vector3 camPos)
    {
        if (!anchorValid)
        {
            anchorGpsEnu = gpsEnu;
            anchorCamPos = camPos;
            anchorValid = true;
            return;
        }

        Vector2 gpsDelta = gpsEnu - anchorGpsEnu;
        if (gpsDelta.magnitude < headingSegmentMeters) return;

        Vector3 camDelta = camPos - anchorCamPos;
        var arDeltaXZ = new Vector2(camDelta.x, camDelta.z);
        float ratio = arDeltaXZ.magnitude / gpsDelta.magnitude;
        if (ratio > 0.5f && ratio < 2f)
        {
            float gpsAz = Mathf.Atan2(gpsDelta.x, gpsDelta.y) * Mathf.Rad2Deg; // course over ground
            float arAz = Mathf.Atan2(arDeltaXZ.x, arDeltaXZ.y) * Mathf.Rad2Deg; // relative to Unity +Z
            float measured = gpsAz - arAz; // azimuth Unity +Z points toward

            headingDeg = HeadingRefined
                ? Mathf.LerpAngle(headingDeg, measured, headingBlend)
                : measured;
            HeadingRefined = true;
        }

        // Start the next segment regardless — a rejected segment (GPS jump,
        // user stood still while GPS wandered) shouldn't poison the next one.
        anchorGpsEnu = gpsEnu;
        anchorCamPos = camPos;
    }

    /// <summary>
    /// Call from LateUpdate (after SpuriousMotionGate) to gently shift the
    /// XR Origin so geo-anchored content re-aligns with the real world.
    /// Best called while the user is walking, when a slow shift is invisible.
    /// </summary>
    public void ApplyDriftCorrection()
    {
        if (!Ready || xrOrigin == null) return;

        Vector2 step = Vector2.ClampMagnitude(DriftError, maxCorrectionSpeed * Time.deltaTime);
        if (step.sqrMagnitude < 1e-10f) return;

        Vector2 unityXZ = EnuToUnityXZ(step, headingDeg);
        Vector3 shift = new Vector3(unityXZ.x, 0f, unityXZ.y);

        xrOrigin.transform.position += shift;
        lastCamPos += shift;    // don't let the shift pollute the next predict step
        anchorCamPos += shift;  // nor the heading-refinement segment
        arEnu += step;          // the AR-frame estimate now includes this correction
    }

    // ---------- coordinate helpers ----------

    Vector2 GeoToEnu(double lat, double lon)
    {
        const double R = 6371000.0; // equirectangular approx: fine for < a few km
        double dLat = (lat - lat0) * System.Math.PI / 180.0;
        double dLon = (lon - lon0) * System.Math.PI / 180.0;
        float north = (float)(dLat * R);
        float east  = (float)(dLon * R * System.Math.Cos(lat0 * System.Math.PI / 180.0));
        return new Vector2(east, north);
    }

    // Unity XZ (x = right, y-component = forward) -> ENU, given the azimuth
    // (degrees clockwise from true north) that Unity +Z points toward.
    static Vector2 UnityXZToEnu(Vector2 v, float headingDeg)
    {
        float h = headingDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(h), cos = Mathf.Cos(h);
        float east  =  v.x * cos + v.y * sin;
        float north = -v.x * sin + v.y * cos;
        return new Vector2(east, north);
    }

    static Vector2 EnuToUnityXZ(Vector2 enu, float headingDeg)
    {
        float h = headingDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(h), cos = Mathf.Cos(h);
        float xRight   = enu.x * cos - enu.y * sin;
        float zForward = enu.x * sin + enu.y * cos;
        return new Vector2(xRight, zForward);
    }
}
