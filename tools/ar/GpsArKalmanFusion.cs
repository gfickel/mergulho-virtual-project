using System.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Fuses AR dead-reckoning (fast, smooth, drifts) with GPS fixes
/// (slow, noisy, absolute) using two independent 1D Kalman filters on the
/// east/north axes.
///
/// PREDICT (every frame): the AR camera's displacement, rotated into
///   east/north coordinates, moves the state forward. Process noise grows
///   with distance walked — the more you move, the less we trust pure AR.
/// UPDATE (each new GPS fix): the fix pulls the state toward the measured
///   position, weighted by its reported horizontalAccuracy.
///
/// The difference between the fused estimate and the AR-only estimate is
/// the accumulated drift. ApplyDriftCorrection() bleeds it into the
/// XR Origin at a capped speed so the correction is invisible to the user.
///
/// Expectations: GPS fixes at ~1 Hz with 3–15 m accuracy can only correct
/// LOW-FREQUENCY drift (meters over minutes). High-frequency wave-induced
/// motion is SpuriousMotionGate's job.
/// </summary>
public class GpsArKalmanFusion : MonoBehaviour
{
    [Header("References")]
    public Transform arCamera;
    public XROrigin xrOrigin;                 // optional: only needed for ApplyDriftCorrection

    [Header("Kalman tuning")]
    [Tooltip("Variance (m^2) added per meter walked. Higher = trust GPS more when moving.")]
    public float processNoisePerMeter = 0.05f;

    [Tooltip("GPS accuracies better than this are clamped (phones over-report).")]
    public float minGpsAccuracy = 3f;

    [Tooltip("Ignore fixes with accuracy worse than this (meters).")]
    public float maxUsableAccuracy = 25f;

    [Header("Drift correction")]
    [Tooltip("Max speed (m/s) at which drift is bled into the XR Origin. Keep small so it is imperceptible.")]
    public float maxCorrectionSpeed = 0.25f;

    public bool Ready { get; private set; }

    /// <summary>Fused position, meters east/north of the geo origin (first fix).</summary>
    public Vector2 FusedEnu => x;

    /// <summary>Fused - AR-only estimate: the drift the filter believes has accumulated.</summary>
    public Vector2 DriftError => x - arEnu;

    // --- filter state ---
    Vector2 x;                 // state: ENU position
    Vector2 p;                 // estimate variance per axis
    Vector2 arEnu;             // AR-only dead reckoning in ENU
    double lat0, lon0;         // geo origin
    float headingDeg;          // azimuth of Unity +Z at calibration time
    Vector3 lastCamPos;
    double lastFixTime;

    IEnumerator Start()
    {
        // On Android also request Permission.FineLocation before this runs.
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("GpsArKalmanFusion: location services disabled by user.");
            yield break;
        }

        Input.compass.enabled = true;
        Input.location.Start(1f, 0.1f); // desiredAccuracy 1 m, update every 0.1 m

        while (Input.location.status == LocationServiceStatus.Initializing)
            yield return new WaitForSeconds(0.5f);

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("GpsArKalmanFusion: location service failed to start.");
            yield break;
        }

        var first = Input.location.lastData;
        lat0 = first.latitude;
        lon0 = first.longitude;
        lastFixTime = first.timestamp;

        // Assumes Unity +Z was aligned with the camera's facing direction when
        // the AR session started (default gravity alignment on both platforms).
        // On iOS you can instead set worldAlignment = gravityAndHeading and use
        // headingDeg = 0. For best results, average trueHeading over ~2 s or
        // refine by correlating AR motion with the GPS track while walking.
        headingDeg = Input.compass.trueHeading;

        lastCamPos = arCamera.position;
        x = Vector2.zero;
        arEnu = Vector2.zero;
        float a0 = Mathf.Max(first.horizontalAccuracy, minGpsAccuracy);
        p = Vector2.one * (a0 * a0);
        Ready = true;
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

        // ---------- UPDATE: new GPS fix ----------
        var fix = Input.location.lastData;
        if (fix.timestamp > lastFixTime && fix.horizontalAccuracy <= maxUsableAccuracy)
        {
            lastFixTime = fix.timestamp;
            Vector2 z = GeoToEnu(fix.latitude, fix.longitude);
            float acc = Mathf.Max(fix.horizontalAccuracy, minGpsAccuracy);
            float r = acc * acc;

            for (int i = 0; i < 2; i++)
            {
                float k = p[i] / (p[i] + r);   // Kalman gain
                x[i] += k * (z[i] - x[i]);
                p[i] *= (1f - k);
            }
        }
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
        lastCamPos += shift;   // don't let the shift pollute the next predict step
        arEnu += step;         // the AR-frame estimate now includes this correction
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
