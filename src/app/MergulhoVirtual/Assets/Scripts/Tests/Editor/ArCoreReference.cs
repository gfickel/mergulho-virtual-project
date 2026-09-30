using UnityEngine;

// =============================================================================
// VERBATIM transcriptions of the AR stabilization algorithms as they existed
// BEFORE the Assets/Scripts/AR/Core extraction (git cf8010e:
// Assets/Scripts/AR/{StillnessDetector,SpuriousMotionGate,GpsArKalmanFusion}.cs).
//
// These exist for ONE reason: to pin behaviour across the refactor. Every
// expression below is copied line-for-line from the original MonoBehaviour
// (only the plumbing changed: sensor reads / Transform reads / Time.deltaTime
// became method arguments, so the maths can run in a test). Reviewers should be
// able to `git show cf8010e:.../StillnessDetector.cs` and diff by eye.
//
// DO NOT "improve" anything here — not the frame-rate-dependent EMA, not the
// duplicated Mathf.Lerp, not the Vector2 indexer loop. A divergence between
// these classes and Assets/Scripts/AR/Core is a bug in the CORE, never here.
// =============================================================================

/// <summary>Deterministic xorshift PRNG — the same sequence on every machine and
/// every Unity version (UnityEngine.Random is not contractually stable).</summary>
internal sealed class ArRefRng
{
    uint s;
    public ArRefRng(int seed) { s = seed == 0 ? 0x9E3779B9u : (uint)seed; }

    public uint NextUInt()
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return s;
    }

    /// <summary>Uniform in [0,1).</summary>
    public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform in [min,max).</summary>
    public float Range(float min, float max) => min + (max - min) * Next01();

    /// <summary>Approximately standard-normal (sum of 4 uniforms, Irwin–Hall).</summary>
    public float NextGaussian()
    {
        float sum = Next01() + Next01() + Next01() + Next01();
        return (sum - 2f) * 1.7320508f; // variance of Irwin-Hall(4)/... scaled to ~1
    }
}

/// <summary>Verbatim <c>StillnessDetector.Update()</c> (pre-refactor).</summary>
internal sealed class RefStillness
{
    public float gyroStillThreshold = 0.12f;
    public float accelStillThreshold = 0.03f;
    public float enterStillTime = 0.4f;
    public float exitMultiplier = 1.6f;
    public float emaAlpha = 0.25f;

    public bool IsStill;
    public float SmoothedGyro;
    public float SmoothedAccel;
    public float quietTimer;

    /// <summary>The body of StillnessDetector.Update() after the HasSensors guard.</summary>
    public void Update(float dt, float rot, float acc, bool hasAccelSensor)
    {
        SmoothedGyro  = Mathf.Lerp(SmoothedGyro, rot, emaAlpha);
        SmoothedAccel = Mathf.Lerp(SmoothedAccel, acc, emaAlpha);

        bool loud = rot > gyroStillThreshold * exitMultiplier
                 || (hasAccelSensor && acc > accelStillThreshold * exitMultiplier);

        bool quiet = SmoothedGyro < gyroStillThreshold
                  && (!hasAccelSensor || SmoothedAccel < accelStillThreshold);

        if (loud)
        {
            quietTimer = 0f;
            IsStill = false;
        }
        else if (quiet)
        {
            quietTimer += dt;
            if (quietTimer >= enterStillTime)
                IsStill = true;
        }
        // In the hysteresis band between quiet and loud: keep current state.
    }
}

/// <summary>Verbatim <c>SpuriousMotionGate.LateUpdate()</c> (pre-refactor).</summary>
internal sealed class RefGate
{
    public float minDelta = 0.0005f;
    public float relocalizationJumpThreshold = 0.35f;

    public Vector3 TotalSuppressed;
    public Vector3 lastCamPos;
    public bool hasLast;

    /// <summary>
    /// The body of SpuriousMotionGate.LateUpdate(). <paramref name="originPos"/>
    /// stands in for xrOrigin.transform.position and is mutated in place, exactly
    /// as the original mutated the Transform.
    /// </summary>
    public void LateUpdate(Vector3 camPosIn, bool isStill, ref Vector3 originPos)
    {
        Vector3 camPos = camPosIn;
        if (!hasLast)
        {
            lastCamPos = camPos;
            hasLast = true;
            return;
        }

        Vector3 delta = camPos - lastCamPos;
        float mag = delta.magnitude;

        bool suppress =
            isStill &&
            mag > minDelta &&
            (relocalizationJumpThreshold <= 0f || mag < relocalizationJumpThreshold);

        if (suppress)
        {
            originPos -= delta;
            camPos -= delta;
            TotalSuppressed += delta;
        }

        lastCamPos = camPos;
    }
}

/// <summary>Verbatim <c>GpsArKalmanFusion</c> (pre-refactor): Update(),
/// RefineHeading(), ApplyDriftCorrection() and the coordinate helpers.</summary>
internal sealed class RefFusion
{
    public float processNoisePerMeter = 0.05f;
    public float minGpsAccuracy = 3f;
    public float maxUsableAccuracy = 25f;
    public float maxCorrectionSpeed = 0.25f;
    public float headingSegmentMeters = 8f;
    public float headingBlend = 0.5f;

    public bool Ready;
    public bool HeadingRefined;

    public Vector2 x;
    public Vector2 p;
    public Vector2 arEnu;
    public double lat0, lon0;
    public float headingDeg;
    public Vector3 lastCamPos;
    public int lastFixCount;

    public Vector2 anchorGpsEnu;
    public Vector3 anchorCamPos;
    public bool anchorValid;

    public Vector2 DriftError => x - arEnu;
    public Vector2 EstimateStdDev => new Vector2(Mathf.Sqrt(p.x), Mathf.Sqrt(p.y));

    /// <summary>The tail of the original IEnumerator Start(), after the compass average.</summary>
    public void Initialize(GnssFix first, Vector3 camPos, float headingSeedDeg, int fixCount)
    {
        lat0 = first.latitude;
        lon0 = first.longitude;
        lastFixCount = fixCount;
        headingDeg = headingSeedDeg;

        lastCamPos = camPos;
        x = Vector2.zero;
        arEnu = Vector2.zero;
        float a0 = Mathf.Max(first.horizontalAccuracy, minGpsAccuracy);
        p = Vector2.one * (a0 * a0);

        anchorGpsEnu = Vector2.zero;
        anchorCamPos = camPos;
        anchorValid = true;

        Ready = true;
    }

    public void Update(Vector3 camPos, int fixCount, GnssFix fix)
    {
        if (!Ready) return;

        Vector3 d = camPos - lastCamPos;
        lastCamPos = camPos;

        Vector2 dEnu = UnityXZToEnu(new Vector2(d.x, d.z), headingDeg);
        x += dEnu;
        arEnu += dEnu;
        p += Vector2.one * (processNoisePerMeter * dEnu.magnitude);

        if (fixCount == lastFixCount) return;
        lastFixCount = fixCount;
        if (fix.horizontalAccuracy > maxUsableAccuracy) return;

        Vector2 z = GeoToEnu(fix.latitude, fix.longitude);
        float acc = Mathf.Max(fix.horizontalAccuracy, minGpsAccuracy);
        float r = acc * acc;

        for (int i = 0; i < 2; i++)
        {
            float k = p[i] / (p[i] + r);
            x[i] += k * (z[i] - x[i]);
            p[i] *= (1f - k);
        }

        RefineHeading(z, camPos);
    }

    public void RefineHeading(Vector2 gpsEnu, Vector3 camPos)
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
            float gpsAz = Mathf.Atan2(gpsDelta.x, gpsDelta.y) * Mathf.Rad2Deg;
            float arAz = Mathf.Atan2(arDeltaXZ.x, arDeltaXZ.y) * Mathf.Rad2Deg;
            float measured = gpsAz - arAz;

            headingDeg = HeadingRefined
                ? Mathf.LerpAngle(headingDeg, measured, headingBlend)
                : measured;
            HeadingRefined = true;
        }

        anchorGpsEnu = gpsEnu;
        anchorCamPos = camPos;
    }

    /// <summary>ApplyDriftCorrection(), returning the shift the original applied
    /// to xrOrigin.transform.position instead of applying it.</summary>
    public bool ApplyDriftCorrection(float dt, out Vector3 shift)
    {
        shift = Vector3.zero;
        if (!Ready) return false;

        Vector2 step = Vector2.ClampMagnitude(DriftError, maxCorrectionSpeed * dt);
        if (step.sqrMagnitude < 1e-10f) return false;

        Vector2 unityXZ = EnuToUnityXZ(step, headingDeg);
        shift = new Vector3(unityXZ.x, 0f, unityXZ.y);

        lastCamPos += shift;
        anchorCamPos += shift;
        arEnu += step;
        return true;
    }

    public Vector2 GeoToEnu(double lat, double lon)
    {
        const double R = 6371000.0;
        double dLat = (lat - lat0) * System.Math.PI / 180.0;
        double dLon = (lon - lon0) * System.Math.PI / 180.0;
        float north = (float)(dLat * R);
        float east  = (float)(dLon * R * System.Math.Cos(lat0 * System.Math.PI / 180.0));
        return new Vector2(east, north);
    }

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

/// <summary>
/// The deterministic input sequences the characterization tests replay through
/// both the reference and the extracted core. Kept here so a single definition
/// feeds every test (and, pre-refactor, the live MonoBehaviours via reflection).
/// </summary>
internal static class ArRefSequences
{
    internal struct ImuSample
    {
        public float dt, gyro, accel;
        public bool hasAccel;
    }

    /// <summary>
    /// 900 frames (~30 s at 30 Hz) that cross every branch of the stillness
    /// hysteresis: quiet-with-tremor, loud bursts, and the band in between.
    /// </summary>
    public static ImuSample[] Imu(int seed = 20260928, int frames = 900)
    {
        var rng = new ArRefRng(seed);
        var result = new ImuSample[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / 30f;
            // Three regimes so loud / quiet / hysteresis-band all get exercised.
            bool loudPhase = (i / 150) % 3 == 1;
            bool bandPhase = (i / 150) % 3 == 2;
            float baseGyro = loudPhase ? 0.9f : bandPhase ? 0.15f : 0.04f;
            float baseAcc = loudPhase ? 0.25f : bandPhase ? 0.035f : 0.008f;
            result[i] = new ImuSample
            {
                dt = i % 97 == 0 ? 1f / 60f : 1f / 30f,   // an occasional short frame
                gyro = Mathf.Max(0f, baseGyro + 0.03f * Mathf.Sin(t * 7.3f) + 0.02f * rng.NextGaussian()),
                accel = Mathf.Max(0f, baseAcc + 0.01f * Mathf.Sin(t * 11.1f) + 0.005f * rng.NextGaussian()),
                hasAccel = i < frames - 120,   // last 120 frames: gyro-only fallback
            };
        }
        return result;
    }

    internal struct GateSample
    {
        public Vector3 camDelta;   // tracker-reported camera motion this frame
        public bool isStill;
    }

    /// <summary>
    /// 600 frames crossing every gate branch: sub-minDelta noise, ordinary
    /// suppression, over-threshold relocalization jumps, and motion while
    /// the IMU reports movement.
    /// </summary>
    public static GateSample[] Gate(int seed = 777, int frames = 600)
    {
        var rng = new ArRefRng(seed);
        var result = new GateSample[frames];
        for (int i = 0; i < frames; i++)
        {
            float mag;
            if (i % 137 == 136) mag = rng.Range(0.4f, 1.2f);        // relocalization jump
            else if (i % 53 == 0) mag = rng.Range(0f, 0.0004f);     // below minDelta
            else mag = rng.Range(0.001f, 0.02f);                    // ordinary phantom drift
            float ang = rng.Range(0f, 6.2831853f);
            result[i] = new GateSample
            {
                camDelta = new Vector3(Mathf.Cos(ang) * mag, rng.Range(-0.2f, 0.2f) * mag, Mathf.Sin(ang) * mag),
                isStill = (i / 90) % 3 != 1,   // a third of the run reports real motion
            };
        }
        return result;
    }

    internal struct FusionSample
    {
        public Vector3 camPos;     // absolute AR camera world position
        public int fixCount;
        public GnssFix fix;
        public float dt;
        public bool applyCorrection;
    }

    /// <summary>
    /// 1200 frames of a walk with 1 Hz GNSS: exercises predict, the accuracy
    /// reject, the Kalman update, both heading-refinement branches (accepted +
    /// ratio-rejected) and the clamped drift correction.
    /// </summary>
    public static FusionSample[] Fusion(int seed = 31337, int frames = 1200)
    {
        var rng = new ArRefRng(seed);
        var result = new FusionSample[frames];
        var cam = Vector3.zero;
        int fixCount = 1;      // Initialize() consumed fix #1
        var fix = new GnssFix { latitude = -3.85, longitude = -32.44, horizontalAccuracy = 5f };
        const double R = 6371000.0;
        const double lat0 = -3.85, lon0 = -32.44;

        for (int i = 0; i < frames; i++)
        {
            // Walk north-east in Unity coords at ~1.3 m/s, standing still 400..600.
            bool standing = i >= 400 && i < 600;
            Vector3 step = standing
                ? new Vector3(0.0004f * rng.NextGaussian(), 0f, 0.0004f * rng.NextGaussian())
                : new Vector3(0.030f, 0f, 0.035f);
            cam += step;

            bool newFix = i % 30 == 29;
            if (newFix)
            {
                fixCount++;
                // True ENU ~ the walked path rotated by ~25 deg, plus noise.
                float east = cam.x * Mathf.Cos(25f * Mathf.Deg2Rad) + cam.z * Mathf.Sin(25f * Mathf.Deg2Rad);
                float north = -cam.x * Mathf.Sin(25f * Mathf.Deg2Rad) + cam.z * Mathf.Cos(25f * Mathf.Deg2Rad);
                east += 2.5f * rng.NextGaussian();
                north += 2.5f * rng.NextGaussian();
                double dLat = north / R * 180.0 / System.Math.PI;
                double dLon = east / (R * System.Math.Cos(lat0 * System.Math.PI / 180.0)) * 180.0 / System.Math.PI;
                fix = new GnssFix
                {
                    latitude = lat0 + dLat,
                    longitude = lon0 + dLon,
                    // Every 7th fix is deliberately unusable (> maxUsableAccuracy).
                    horizontalAccuracy = (fixCount % 7 == 0) ? 40f : rng.Range(2f, 12f),
                };
            }

            result[i] = new FusionSample
            {
                camPos = cam,
                fixCount = fixCount,
                fix = fix,
                dt = 1f / 30f,
                // Correction runs on most frames (mirrors correctOnlyWhileMoving).
                applyCorrection = !standing,
            };
        }
        return result;
    }
}
