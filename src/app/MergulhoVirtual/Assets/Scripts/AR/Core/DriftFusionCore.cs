using UnityEngine;

/// <summary>
/// Circular mean of compass headings — the seed for
/// <see cref="DriftFusionCore.Initialize"/>. Averaging degrees naively wraps
/// badly around north (359° and 1° average to 180°), so samples accumulate as
/// unit vectors and resolve through Atan2.
///
/// Extracted from the compass loop in GpsArKalmanFusion.Start() so the maths is
/// testable; reading Input.compass stays in the MonoBehaviour.
/// </summary>
internal sealed class HeadingSeedAccumulator
{
    float sinSum, cosSum;

    public int Samples { get; private set; }

    public void Add(float headingDeg)
    {
        float h = headingDeg * Mathf.Deg2Rad;
        sinSum += Mathf.Sin(h);
        cosSum += Mathf.Cos(h);
        Samples++;
    }

    /// <summary>The averaged heading, or <paramref name="fallbackDeg"/> when no
    /// sample ever arrived (a failed seed just means waiting for the GPS-track
    /// refinement while walking).</summary>
    public float ResolveDeg(float fallbackDeg = 0f) =>
        Samples > 0 ? Mathf.Atan2(sinSum, cosSum) * Mathf.Rad2Deg : fallbackDeg;

    public void Reset() { sinSum = cosSum = 0f; Samples = 0; }
}

/// <summary>
/// The GPS↔AR drift filter, extracted from <see cref="GpsArKalmanFusion"/>: two
/// independent 1D Kalman filters on the east/north axes, AR displacement as the
/// predict step, GNSS fixes as the update step, plus the walked-segment heading
/// refinement and the equirectangular geo→ENU conversion.
///
/// PREDICT (every frame): the AR camera's displacement, rotated into east/north
///   coordinates, moves the state forward. Process noise grows with distance
///   walked — the more you move, the less we trust pure AR.
/// UPDATE (each new GNSS fix): the fix pulls the state toward the measured
///   position, weighted by its reported accuracy.
///
/// The difference between the fused estimate and the AR-only estimate is the
/// accumulated drift. <see cref="TryComputeDriftCorrection"/> hands the caller
/// the capped shift to apply to the XR Origin and back-annotates its own state
/// so the shift does not pollute the next predict step.
///
/// Engine-free by contract: no Transform, no GnssProvider, no Time. Fixes arrive
/// as a plain <see cref="GnssFix"/> plus a changed-flag; the caller owns the
/// FixCount bookkeeping and the Transform writes.
/// </summary>
internal sealed class DriftFusionCore
{
    internal struct Settings
    {
        /// <summary>Variance (m^2) added per meter walked. Higher = trust GNSS more when moving.</summary>
        public float processNoisePerMeter;

        /// <summary>GNSS accuracies better than this are clamped (phones over-report).</summary>
        public float minGpsAccuracy;

        /// <summary>Ignore fixes with accuracy worse than this (meters).</summary>
        public float maxUsableAccuracy;

        /// <summary>Max speed (m/s) at which drift is bled into the XR Origin.</summary>
        public float maxCorrectionSpeed;

        /// <summary>Walk this many meters (per GNSS track) before each heading
        /// refinement from the GPS course. Smaller = faster calibration, noisier.</summary>
        public float headingSegmentMeters;

        /// <summary>Blend factor for each heading refinement after the first
        /// (1 = jump to the new measurement).</summary>
        public float headingBlend;

        /// <summary>The values authored on GpsArKalmanFusion / in MainScene.</summary>
        public static Settings Defaults => new Settings
        {
            processNoisePerMeter = 0.05f,
            minGpsAccuracy = 3f,
            maxUsableAccuracy = 25f,
            maxCorrectionSpeed = 0.25f,
            headingSegmentMeters = 8f,
            headingBlend = 0.5f,
        };
    }

    public Settings Config = Settings.Defaults;

    /// <summary>True once <see cref="Initialize"/> has run (first fix + heading seed).</summary>
    public bool Ready { get; private set; }

    /// <summary>Azimuth (deg clockwise from true north) that Unity +Z points toward.</summary>
    public float HeadingDeg { get; private set; }

    /// <summary>True once at least one GPS-track refinement replaced the compass seed.</summary>
    public bool HeadingRefined { get; private set; }

    /// <summary>Fused position, meters east/north of the geo origin (first fix).</summary>
    public Vector2 FusedEnu => x;

    /// <summary>AR-only dead reckoning in ENU — the thing drift is measured against.</summary>
    public Vector2 ArOnlyEnu => arEnu;

    /// <summary>Fused - AR-only estimate: the drift the filter believes has accumulated.</summary>
    public Vector2 DriftError => x - arEnu;

    /// <summary>Current estimate std-dev per axis (m), for telemetry.</summary>
    public Vector2 EstimateStdDev => new Vector2(Mathf.Sqrt(p.x), Mathf.Sqrt(p.y));

    /// <summary>Geo origin (the first accepted fix), for logging.</summary>
    public double OriginLatitude => lat0;
    public double OriginLongitude => lon0;

    // --- filter state ---
    Vector2 x;                 // state: ENU position
    Vector2 p;                 // estimate variance per axis
    Vector2 arEnu;             // AR-only dead reckoning in ENU
    double lat0, lon0;         // geo origin
    Vector3 lastCamPos;

    // --- heading refinement anchors ---
    Vector2 anchorGpsEnu;
    Vector3 anchorCamPos;
    bool anchorValid;

    public void Reset()
    {
        Ready = false;
        HeadingRefined = false;
        HeadingDeg = 0f;
        x = p = arEnu = Vector2.zero;
        lat0 = lon0 = 0.0;
        lastCamPos = Vector3.zero;
        anchorGpsEnu = Vector2.zero;
        anchorCamPos = Vector3.zero;
        anchorValid = false;
    }

    /// <summary>
    /// Seed the filter from the first usable fix. The fix defines the geo origin
    /// and the initial variance; <paramref name="camPos"/> is the AR camera world
    /// position at that instant (the predict step is relative to it), and
    /// <paramref name="headingSeedDeg"/> is the compass average.
    /// </summary>
    public void Initialize(in GnssFix firstFix, Vector3 camPos, float headingSeedDeg)
    {
        lat0 = firstFix.latitude;
        lon0 = firstFix.longitude;
        HeadingDeg = headingSeedDeg;
        HeadingRefined = false;

        lastCamPos = camPos;
        x = Vector2.zero;
        arEnu = Vector2.zero;
        float a0 = Mathf.Max(firstFix.horizontalAccuracy, Config.minGpsAccuracy);
        p = Vector2.one * (a0 * a0);

        anchorGpsEnu = Vector2.zero;
        anchorCamPos = camPos;
        anchorValid = true;

        Ready = true;
    }

    /// <summary>
    /// Advance one frame: predict from the AR camera's displacement, then — only
    /// when <paramref name="hasNewFix"/> — run the Kalman update and the heading
    /// refinement.
    /// </summary>
    /// <param name="camPos">AR camera WORLD position this frame.</param>
    /// <param name="hasNewFix">True on the frame a fix the caller has not yet
    /// consumed arrives (the caller owns the FixCount comparison).</param>
    /// <param name="fix">The new fix; ignored when <paramref name="hasNewFix"/> is false.</param>
    public void Step(Vector3 camPos, bool hasNewFix, in GnssFix fix)
    {
        if (!Ready) return;

        // ---------- PREDICT: AR displacement as the motion model ----------
        Vector3 d = camPos - lastCamPos;
        lastCamPos = camPos;

        Vector2 dEnu = UnityXZToEnu(new Vector2(d.x, d.z), HeadingDeg);
        x += dEnu;
        arEnu += dEnu;
        p += Vector2.one * (Config.processNoisePerMeter * dEnu.magnitude);

        // ---------- UPDATE: new GNSS fix ----------
        if (!hasNewFix) return;
        // A fix too poor to use is still CONSUMED (the original advanced
        // lastFixCount before this test), and it skips RefineHeading too.
        if (fix.horizontalAccuracy > Config.maxUsableAccuracy) return;

        Vector2 z = GeoToEnu(fix.latitude, fix.longitude);
        float acc = Mathf.Max(fix.horizontalAccuracy, Config.minGpsAccuracy);
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
        if (gpsDelta.magnitude < Config.headingSegmentMeters) return;

        Vector3 camDelta = camPos - anchorCamPos;
        var arDeltaXZ = new Vector2(camDelta.x, camDelta.z);
        float ratio = arDeltaXZ.magnitude / gpsDelta.magnitude;
        if (ratio > 0.5f && ratio < 2f)
        {
            float gpsAz = Mathf.Atan2(gpsDelta.x, gpsDelta.y) * Mathf.Rad2Deg; // course over ground
            float arAz = Mathf.Atan2(arDeltaXZ.x, arDeltaXZ.y) * Mathf.Rad2Deg; // relative to Unity +Z
            float measured = gpsAz - arAz; // azimuth Unity +Z points toward

            HeadingDeg = HeadingRefined
                ? Mathf.LerpAngle(HeadingDeg, measured, Config.headingBlend)
                : measured;
            HeadingRefined = true;
        }

        // Start the next segment regardless — a rejected segment (GPS jump,
        // user stood still while GPS wandered) shouldn't poison the next one.
        anchorGpsEnu = gpsEnu;
        anchorCamPos = camPos;
    }

    /// <summary>
    /// The body of ApplyDriftCorrection(): compute the speed-capped world shift
    /// that bleeds <see cref="DriftError"/> into the XR Origin and back-annotate
    /// the filter state so the shift does not pollute the next predict step or
    /// the heading-refinement segment.
    ///
    /// The caller applies <paramref name="shift"/> to the origin Transform — the
    /// core never touches it.
    /// </summary>
    /// <returns>False (and shift = zero) when not ready or the step is negligible.</returns>
    public bool TryComputeDriftCorrection(float dt, out Vector3 shift)
    {
        shift = Vector3.zero;
        if (!Ready) return false;

        Vector2 step = Vector2.ClampMagnitude(DriftError, Config.maxCorrectionSpeed * dt);
        if (step.sqrMagnitude < 1e-10f) return false;

        Vector2 unityXZ = EnuToUnityXZ(step, HeadingDeg);
        shift = new Vector3(unityXZ.x, 0f, unityXZ.y);

        lastCamPos += shift;    // don't let the shift pollute the next predict step
        anchorCamPos += shift;  // nor the heading-refinement segment
        arEnu += step;          // the AR-frame estimate now includes this correction
        return true;
    }

    // ---------- coordinate helpers ----------

    /// <summary>Equirectangular geo→ENU about the origin fix. Fine for &lt; a few km.</summary>
    public Vector2 GeoToEnu(double lat, double lon)
    {
        const double R = 6371000.0; // equirectangular approx: fine for < a few km
        double dLat = (lat - lat0) * System.Math.PI / 180.0;
        double dLon = (lon - lon0) * System.Math.PI / 180.0;
        float north = (float)(dLat * R);
        float east  = (float)(dLon * R * System.Math.Cos(lat0 * System.Math.PI / 180.0));
        return new Vector2(east, north);
    }

    /// <summary>The inverse of <see cref="GeoToEnu"/> about the same origin — used
    /// by the offline simulator to synthesise fixes from a ground-truth track.</summary>
    public void EnuToGeo(Vector2 enu, out double lat, out double lon) =>
        EnuToGeo(enu, lat0, lon0, out lat, out lon);

    /// <summary>Origin-explicit inverse, so a caller can build fixes before the
    /// filter has an origin of its own.</summary>
    public static void EnuToGeo(Vector2 enu, double lat0, double lon0, out double lat, out double lon)
    {
        const double R = 6371000.0;
        lat = lat0 + enu.y / R * 180.0 / System.Math.PI;
        lon = lon0 + enu.x / (R * System.Math.Cos(lat0 * System.Math.PI / 180.0)) * 180.0 / System.Math.PI;
    }

    // Unity XZ (x = right, y-component = forward) -> ENU, given the azimuth
    // (degrees clockwise from true north) that Unity +Z points toward.
    public static Vector2 UnityXZToEnu(Vector2 v, float headingDeg)
    {
        float h = headingDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(h), cos = Mathf.Cos(h);
        float east  =  v.x * cos + v.y * sin;
        float north = -v.x * sin + v.y * cos;
        return new Vector2(east, north);
    }

    public static Vector2 EnuToUnityXZ(Vector2 enu, float headingDeg)
    {
        float h = headingDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(h), cos = Mathf.Cos(h);
        float xRight   = enu.x * cos - enu.y * sin;
        float zForward = enu.x * sin + enu.y * cos;
        return new Vector2(xRight, zForward);
    }
}
