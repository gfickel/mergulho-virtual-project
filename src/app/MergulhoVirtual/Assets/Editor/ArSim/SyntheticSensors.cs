using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Turns the ground-truth motion into the IMU magnitudes
    /// <see cref="StillnessCore"/> actually consumes.
    ///
    /// The point of this class is HAND TREMOR. A hand-held phone is never still:
    /// CLAUDE.md puts human tremor at 0.02–0.10 rad/s, which is the same order as
    /// the shipped gyroStillThreshold of 0.12 rad/s. If the simulator fed the
    /// detector clean zeros while standing, the stillness gate would look far more
    /// reliable than it is and the whole harness would be worthless. Tremor is
    /// modelled as a band-limited sum of three sinusoids (3–10 Hz, seeded phases)
    /// rather than white noise, because tremor is oscillatory, and the detector's
    /// per-frame EMA responds very differently to the two.
    ///
    /// PROVISIONAL numbers, all in published ranges for physiological tremor and
    /// consumer MEMS noise floors.
    /// </summary>
    internal sealed class SyntheticImu
    {
        /// <summary>Peak tremor rotation rate, rad/s. PROVISIONAL: mid-band of
        /// CLAUDE.md's 0.02–0.10 observation.</summary>
        public float tremorGyroRadS = 0.045f;

        /// <summary>Peak tremor linear acceleration, g. PROVISIONAL.</summary>
        public float tremorAccelG = 0.010f;

        /// <summary>Gyro white noise floor, rad/s RMS. PROVISIONAL.</summary>
        public float gyroNoiseRadS = 0.004f;

        /// <summary>Accel white noise floor, g RMS. PROVISIONAL.</summary>
        public float accelNoiseG = 0.002f;

        /// <summary>Extra tremor multiplier while walking — the whole body is
        /// moving, so the hand is noisier. PROVISIONAL.</summary>
        public float walkTremorMultiplier = 3.5f;

        /// <summary>Extra tremor multiplier while panning: a sweeping arm is
        /// less steady than a braced one. PROVISIONAL.</summary>
        public float panTremorMultiplier = 1.8f;

        public int seed = 909;

        const float G = 9.80665f;

        // Three incommensurate tremor tones in the 3–10 Hz physiological band.
        static readonly float[] TremorHz = { 3.1f, 5.7f, 9.3f };
        readonly float[] gyroPhase = new float[TremorHz.Length];
        readonly float[] accelPhase = new float[TremorHz.Length];

        /// <summary>One direction per tremor tone, for the VECTOR accel channel.
        /// Drawn AFTER the phase loops on purpose: appending to the RNG stream
        /// leaves every previously-drawn phase bit-identical, so adding this
        /// channel does not move the shipped stack's published numbers.</summary>
        readonly Vector3[] tremorDir = new Vector3[TremorHz.Length];
        bool built;

        void Build()
        {
            var rng = new ArSimRng(seed);
            for (int i = 0; i < TremorHz.Length; i++)
            {
                gyroPhase[i] = rng.Range(0f, Mathf.PI * 2f);
                accelPhase[i] = rng.Range(0f, Mathf.PI * 2f);
            }
            for (int i = 0; i < TremorHz.Length; i++)
            {
                // Tremor is not isotropic in practice (the wrist has axes), but a
                // seeded random direction per tone is enough to keep the vector
                // channel's magnitude in the same range as the scalar channel's.
                var d = new Vector3(rng.NextGaussian(), rng.NextGaussian(), rng.NextGaussian());
                tremorDir[i] = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
            }
            built = true;
        }

        float Tremor(float t, float[] phase)
        {
            float v = 0f;
            for (int i = 0; i < TremorHz.Length; i++)
                v += Mathf.Sin(2f * Mathf.PI * TremorHz[i] * t + phase[i]) / TremorHz.Length;
            return Mathf.Abs(v);
        }

        /// <summary>
        /// The gyro / linear-accel magnitudes for one frame.
        /// </summary>
        /// <param name="t">Seconds since session start.</param>
        /// <param name="yawRateDegS">Ground-truth yaw rate (the deliberate pan).</param>
        /// <param name="linAccel">Ground-truth linear acceleration, m/s², gravity-free.</param>
        /// <param name="kind">What the user is doing, for the tremor multiplier.</param>
        public void Sample(float t, float yawRateDegS, Vector3 linAccel, MotionKind kind,
                           out float gyroMag, out float accelMag) =>
            Sample(t, yawRateDegS, linAccel, kind, out gyroMag, out accelMag, out _);

        /// <param name="accelWorld">The same linear acceleration as a world-frame
        /// VECTOR in m/s² (see <see cref="ArFrameSample.AccelWorldMps2"/>) — what a
        /// step detector or a band-limited gate needs, and what the magnitude
        /// channel cannot provide because it rectifies.</param>
        public void Sample(float t, float yawRateDegS, Vector3 linAccel, MotionKind kind,
                           out float gyroMag, out float accelMag, out Vector3 accelWorld)
        {
            if (!built) Build();

            float mult = kind == MotionKind.Walk ? walkTremorMultiplier
                       : kind == MotionKind.Pan ? panTremorMultiplier
                       : 1f;

            uint h = ArSimRng.Hash((uint)seed, (uint)Mathf.RoundToInt(t * 1000f));

            // Deliberate rotation dominates during a pan; tremor dominates while braced.
            float deliberate = Mathf.Abs(yawRateDegS) * Mathf.Deg2Rad;
            gyroMag = Mathf.Max(0f,
                deliberate
                + tremorGyroRadS * mult * Tremor(t, gyroPhase)
                + gyroNoiseRadS * ArSimRng.GaussianFromHash(h, 11));

            accelMag = Mathf.Max(0f,
                linAccel.magnitude / G
                + tremorAccelG * mult * Tremor(t, accelPhase)
                + accelNoiseG * ArSimRng.GaussianFromHash(h, 12));

            // SIGNED vector channel. Deliberately NOT |accelWorld| == accelMag*G:
            // the scalar above is frozen bit-for-bit, so this is an independent
            // realisation of the same tremor/noise statistics rather than the same
            // draw. Lanes 13-15 are fresh, so nothing above changes.
            Vector3 tremorVec = Vector3.zero;
            for (int i = 0; i < TremorHz.Length; i++)
                tremorVec += tremorDir[i] *
                    (Mathf.Sin(2f * Mathf.PI * TremorHz[i] * t + accelPhase[i]) / TremorHz.Length);

            accelWorld = linAccel
                + tremorVec * (G * tremorAccelG * mult)
                + new Vector3(ArSimRng.GaussianFromHash(h, 13),
                              ArSimRng.GaussianFromHash(h, 14),
                              ArSimRng.GaussianFromHash(h, 15)) * (G * accelNoiseG);
        }

        public string Describe() =>
            $"imu(tremorGyro={tremorGyroRadS:F3}rad/s tremorAccel={tremorAccelG:F3}g " +
            $"noise={gyroNoiseRadS:F4}/{accelNoiseG:F4} walkX{walkTremorMultiplier:F1} panX{panTremorMultiplier:F1} seed={seed})";
    }

    /// <summary>
    /// Synthesises the GNSS stream <see cref="DriftFusionCore"/> consumes:
    /// ~1 Hz fixes carrying a slowly-varying correlated error plus white noise,
    /// and a reported <c>horizontalAccuracy</c> that is deliberately OPTIMISTIC
    /// most of the time (consumer phones under-report) with the occasional
    /// garbage fix. That combination is what exercises the filter's
    /// <c>minGpsAccuracy</c> clamp and its <c>maxUsableAccuracy</c> reject —
    /// both of which are dead code against a well-behaved synthetic feed.
    ///
    /// PROVISIONAL numbers; broadly consistent with single-frequency GNSS on a
    /// phone near open sky with some multipath.
    /// </summary>
    internal sealed class SyntheticGnss
    {
        public float fixIntervalS = 1.0f;

        /// <summary>1-sigma of the slowly-varying (multipath / ionospheric) error, m.</summary>
        public float biasSigmaM = 4.0f;

        /// <summary>Time constant of that correlated error, s.</summary>
        public float biasTimeConstantS = 60f;

        /// <summary>1-sigma of the per-fix white error, m.</summary>
        public float whiteSigmaM = 1.5f;

        /// <summary>Phones report better accuracy than they achieve; the reported
        /// figure is the true 1-sigma times roughly this. PROVISIONAL.</summary>
        public float optimismFactor = 0.55f;

        /// <summary>Fraction of fixes that come back as junk with a large
        /// reported accuracy (canopy, building, cold start).</summary>
        public float badFixProbability = 0.07f;

        public float badFixAccuracyM = 45f;
        public float badFixErrorM = 30f;

        /// <summary>Seconds before the first fix arrives (the fusion filter is
        /// not Ready until then, exactly as on device).</summary>
        public float firstFixDelayS = 0.8f;

        public int seed = 4242;

        /// <summary>The geo origin the true track is expressed relative to —
        /// Fernando de Noronha, matching GPSHandler's editor stub.</summary>
        public double originLat = -3.85;
        public double originLon = -32.44;

        /// <summary>Azimuth of Unity +Z in the simulated world, degrees from true
        /// north. The fusion filter has to DISCOVER this (compass seed + GPS-track
        /// refinement); the generator knows it.</summary>
        public float trueHeadingDeg = 35f;

        /// <summary>Magnetometer error the compass seed suffers. PROVISIONAL:
        /// CLAUDE.md quotes 5–15° for the compass seed.</summary>
        public float compassBiasDeg = 11f;
        public float compassNoiseDeg = 4f;

        ArSimRng rng;
        Vector2 bias;
        float nextFixT;
        int emitted;

        public void Reset()
        {
            rng = new ArSimRng(seed);
            bias = Vector2.zero;
            nextFixT = firstFixDelayS;
            emitted = 0;
        }

        /// <summary>Compass reading for this frame (a biased, noisy true heading).</summary>
        public float CompassHeading(float t)
        {
            uint h = ArSimRng.Hash((uint)(seed ^ 0x5bd1), (uint)Mathf.RoundToInt(t * 1000f));
            return trueHeadingDeg + compassBiasDeg + compassNoiseDeg * ArSimRng.GaussianFromHash(h, 3);
        }

        /// <summary>
        /// Emit a fix if one is due at time <paramref name="t"/>.
        /// </summary>
        /// <param name="truePos">Ground-truth device position in Unity world metres.</param>
        public bool TryFix(float t, Vector3 truePos, out GnssFix fix)
        {
            fix = default;
            if (rng == null) Reset();
            if (t < nextFixT) return false;
            nextFixT += fixIntervalS;

            // Ornstein-Uhlenbeck step for the correlated component.
            float a = Mathf.Exp(-fixIntervalS / Mathf.Max(0.01f, biasTimeConstantS));
            float kick = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a)) * biasSigmaM;
            bias = new Vector2(
                bias.x * a + kick * rng.NextGaussian(),
                bias.y * a + kick * rng.NextGaussian());

            bool bad = rng.Next01() < badFixProbability;
            Vector2 white = new Vector2(rng.NextGaussian(), rng.NextGaussian()) *
                            (bad ? badFixErrorM : whiteSigmaM);

            // True ENU of the device, then add the error, then go to lat/lon.
            Vector2 trueEnu = DriftFusionCore.UnityXZToEnu(new Vector2(truePos.x, truePos.z), trueHeadingDeg);
            Vector2 measured = trueEnu + bias + white;
            DriftFusionCore.EnuToGeo(measured, originLat, originLon, out double lat, out double lon);

            float trueSigma = Mathf.Sqrt(biasSigmaM * biasSigmaM + whiteSigmaM * whiteSigmaM);
            float reported = bad
                ? badFixAccuracyM * rng.Range(0.85f, 1.3f)
                : Mathf.Max(1f, trueSigma * optimismFactor * rng.Range(0.6f, 1.4f));

            fix = new GnssFix
            {
                latitude = lat,
                longitude = lon,
                horizontalAccuracy = reported,
                timestampMs = t * 1000.0,
                satellites = bad ? 4 : 11,
            };
            emitted++;
            return true;
        }

        public int Emitted => emitted;

        public string Describe() =>
            $"gnss(interval={fixIntervalS:F1}s bias={biasSigmaM:F1}m/tau{biasTimeConstantS:F0}s " +
            $"white={whiteSigmaM:F1}m optimism={optimismFactor:F2} bad={badFixProbability:P0}@{badFixAccuracyM:F0}m " +
            $"trueHeading={trueHeadingDeg:F0}deg compassBias={compassBiasDeg:F0}deg seed={seed})";
    }
}
