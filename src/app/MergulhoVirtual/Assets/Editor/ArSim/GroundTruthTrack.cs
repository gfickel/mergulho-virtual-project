using System.Collections.Generic;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    internal enum MotionKind
    {
        /// <summary>Braced: the user holds the phone up and does not turn.
        /// Still not motionless — postural sway and breathing are always on.</summary>
        Stand,

        /// <summary>Travelling along the beach.</summary>
        Walk,

        /// <summary>Standing in place but sweeping the phone across the water.
        /// The DOMINANT real-world case, and the one the shipped gate switches
        /// itself off for (panning makes the gyro loud, so IsStill goes false).</summary>
        Pan,
    }

    /// <summary>One leg of a scenario's motion script.</summary>
    internal struct MotionSegment
    {
        public MotionKind Kind;
        public float DurationS;

        /// <summary>Walk: metres per second (1.1–1.4 is an unhurried beach walk).</summary>
        public float SpeedMps;

        /// <summary>Walk: azimuth of travel, degrees clockwise from Unity +Z.</summary>
        public float CourseDeg;

        /// <summary>Pan: yaw rate of the sweep, deg/s (20–60 is a real hand sweep).</summary>
        public float PanRateDegPerSec;

        /// <summary>Pan: total arc swept, degrees (30–120 for a real sweep).</summary>
        public float PanArcDeg;

        /// <summary>Pan: dwell at each end of the sweep, seconds. Users stop to
        /// look, which is exactly when they expect the content to be steady.</summary>
        public float PanPauseS;

        /// <summary>
        /// Pan: distance from the yaw axis to the camera, metres — THE reason a
        /// pan is not pure rotation. Nobody rotates a phone about its optical
        /// centre; the phone swings around the wrist (~0.05 m), the elbow
        /// (~0.15 m) or the torso (~0.30 m), so a sweep translates the camera
        /// along an arc of chord 2·r·sin(arc/2). At r = 0.12–0.18 m and a
        /// 50–110° arc that is 0.10–0.30 m of GENUINE translation mixed in with
        /// the phantom wave drift — which is what makes the scenario hard.
        /// </summary>
        public float PivotRadiusM;

        /// <summary>Walk: step cadence in Hz. 0 = the track's default
        /// (<see cref="GroundTruthTrack.StepCadenceHz"/>). One cycle of the
        /// VERTICAL bob is one step.</summary>
        public float StepCadenceHz;

        /// <summary>Walk: multiplier on the gait bob amplitude. 0 = 1.0. Sand damps
        /// and smears the vertical acceleration peak (doc SS5.1), which is the whole
        /// reason step detection is hard there.</summary>
        public float GaitAmplitudeScale;

        /// <summary>Walk: stride-to-stride variability, as a fraction. Drives BOTH
        /// cadence and amplitude jitter, PER STEP, from a seeded hash. The gait
        /// literature's finding for sand is "short-term stride variability
        /// increases" (doc SS5.1), and that is exactly what defeats fixed-threshold
        /// peak detection. 0 = the perfectly periodic default.</summary>
        public float StrideVariability;

        public static MotionSegment Standing(float seconds) =>
            new MotionSegment { Kind = MotionKind.Stand, DurationS = seconds };

        public static MotionSegment Walking(float seconds, float speed = 1.2f, float courseDeg = 75f) =>
            new MotionSegment { Kind = MotionKind.Walk, DurationS = seconds, SpeedMps = speed, CourseDeg = courseDeg };

        /// <summary>
        /// Slow, short, irregular steps on dry sand: the honest stress test for any
        /// step-detection-based gate. Direction from the doc's SS5.1 — dry sand drops
        /// self-selected speed (5.0 vs 5.6 km/h), increases step count and stride
        /// variability, and damps/smears the acceleration peak. The amplitude scale
        /// and the variability figure are [E]: nobody has published smartphone step
        /// detection on sand, which the doc flags as a genuine gap.
        /// </summary>
        public static MotionSegment Shuffling(float seconds, float speed = 0.75f, float courseDeg = 75f) =>
            new MotionSegment
            {
                Kind = MotionKind.Walk, DurationS = seconds, SpeedMps = speed, CourseDeg = courseDeg,
                StepCadenceHz = 1.5f, GaitAmplitudeScale = 0.6f, StrideVariability = 0.30f,
            };

        /// <summary>A deliberate, slow look-around: 50° arc at 22°/s, wrist/elbow pivot.</summary>
        public static MotionSegment SlowPan(float seconds) => new MotionSegment
        {
            Kind = MotionKind.Pan, DurationS = seconds,
            PanRateDegPerSec = 22f, PanArcDeg = 50f, PanPauseS = 1.5f, PivotRadiusM = 0.12f,
        };

        /// <summary>A brisk scan of the whole bay: 110° arc at 50°/s, torso pivot.</summary>
        public static MotionSegment FastPan(float seconds) => new MotionSegment
        {
            Kind = MotionKind.Pan, DurationS = seconds,
            // 110 deg about a 0.18 m torso pivot is a 0.30 m chord - the top of
            // the plausible 0.05-0.30 m range for a genuine in-place sweep.
            PanRateDegPerSec = 50f, PanArcDeg = 110f, PanPauseS = 0.6f, PivotRadiusM = 0.18f,
        };
    }

    /// <summary>
    /// The physically-true device motion for a scenario, sampled on a fixed
    /// frame grid. This is the reference every metric is measured against: the
    /// tracker's reported pose is this plus the wave model's phantom error.
    /// </summary>
    internal sealed class GroundTruthTrack
    {
        // --- provisional human-body constants --------------------------------
        // PROVISIONAL, but all in ranges reported for quiet standing / gait.

        /// <summary>Postural sway of a standing adult: a couple of centimetres at
        /// a few tenths of a hertz. Always on — nobody is ever truly still.</summary>
        const float SwayAmplitudeM = 0.012f;
        const float SwayHzA = 0.27f;
        const float SwayHzB = 0.19f;

        /// <summary>Breathing lifts the phone a few millimetres.</summary>
        const float BreathAmplitudeM = 0.004f;
        const float BreathHz = 0.25f;

        /// <summary>Gait: step cadence and the vertical/lateral bob it produces.
        /// The bob is in the GROUND TRUTH (not bolted onto the IMU) so the
        /// accelerations the stillness detector sees are consistent with the
        /// positions the gate sees.</summary>
        public const float StepCadenceHz = 1.9f;
        const float WalkBobVerticalM = 0.018f;
        const float WalkBobLateralM = 0.012f;

        /// <summary>Seconds to ramp in/out of walking speed, so a segment
        /// boundary is not an infinite acceleration spike.</summary>
        const float WalkRampS = 0.8f;

        /// <summary>Time constant over which a jittered gait's cadence and amplitude
        /// move toward their new per-step targets, seconds. Keeps the ground-truth
        /// acceleration finite — see the comment at the use site.</summary>
        const float GaitBlendTauS = 0.25f;

        /// <summary>Above this ground-truth speed the user is TRAVELLING:
        /// the mask realMotionFidelity is measured over.</summary>
        public const float WalkingSpeedThresholdMps = 0.35f;

        /// <summary>Above this ground-truth speed the device is genuinely moving
        /// at all (includes pan-arc translation): the mask falseSuppression is
        /// measured over.</summary>
        public const float MovingSpeedThresholdMps = 0.05f;

        public float Dt { get; private set; }
        public int FrameCount { get; private set; }

        public Vector3[] Pos { get; private set; }
        public float[] YawDeg { get; private set; }
        public Vector3[] LinAccel { get; private set; }   // m/s^2, gravity-free
        public float[] YawRateDegS { get; private set; }
        public float[] SpeedMps { get; private set; }
        public bool[] Walking { get; private set; }
        public bool[] Moving { get; private set; }
        public MotionKind[] Kind { get; private set; }

        /// <summary>Cumulative gait-bob phase measured in STEPS — the number of real
        /// steps taken by frame i. This is the reference a candidate step detector's
        /// count is scored against.</summary>
        public float[] StepsTaken { get; private set; }

        /// <summary>Total real steps in the whole track.</summary>
        public float TrueStepCount => StepsTaken == null || StepsTaken.Length == 0
            ? 0f : StepsTaken[StepsTaken.Length - 1];

        float[] cadenceHz;
        float[] gaitAmp;
        float[] strideVar;

        /// <summary>
        /// Per-frame envelope on the gait bob, 0–1 — THE FIX for a harness wart.
        ///
        /// The bob used to be switched on and off at a segment boundary at whatever
        /// phase it happened to be at. The bob amplitude is 0.018 m and the track's
        /// acceleration is a second central difference of position, so a
        /// full-amplitude appearance inside one 33 ms frame injected
        /// 0.018/dt² ≈ 16 m/s² into ground-truth acceleration at EVERY walk
        /// boundary. That was common-mode across stabilizers, so it never moved the
        /// comparison — but it handed every step detector a free, unphysical peak
        /// exactly where a walking bout starts and ends, i.e. at the two moments a
        /// step gate's behaviour is actually being measured.
        ///
        /// The envelope follows the same smoothstep the speed profile uses
        /// (<see cref="RampedDistance"/>), which is also the physically right shape:
        /// bob amplitude scales with gait speed, so a walk that ramps up from
        /// standstill ramps its bob up with it.
        /// </summary>
        float[] gaitEnvelope;

        /// <summary>Read-only view of <see cref="gaitEnvelope"/>, so a test can
        /// assert the bob against "closed form × envelope" instead of duplicating
        /// the ramp arithmetic.</summary>
        public float[] GaitEnvelope => gaitEnvelope;

        public static GroundTruthTrack Build(IReadOnlyList<MotionSegment> script, float rateHz, int seed)
        {
            float dt = 1f / rateHz;
            float total = 0f;
            foreach (var s in script) total += s.DurationS;
            int frames = Mathf.Max(2, Mathf.RoundToInt(total * rateHz));

            var track = new GroundTruthTrack
            {
                Dt = dt,
                FrameCount = frames,
                Pos = new Vector3[frames],
                YawDeg = new float[frames],
                LinAccel = new Vector3[frames],
                YawRateDegS = new float[frames],
                SpeedMps = new float[frames],
                Walking = new bool[frames],
                Moving = new bool[frames],
                Kind = new MotionKind[frames],
                StepsTaken = new float[frames],
            };
            track.cadenceHz = new float[frames];
            track.gaitAmp = new float[frames];
            track.strideVar = new float[frames];
            track.gaitEnvelope = new float[frames];

            var rng = new ArSimRng(seed);
            float swayPhaseA = rng.Range(0f, Mathf.PI * 2f);
            float swayPhaseB = rng.Range(0f, Mathf.PI * 2f);
            float breathPhase = rng.Range(0f, Mathf.PI * 2f);

            // --- pass 1: the intended body motion, segment by segment ---------
            int seg = 0;
            float segStartT = 0f;
            Vector3 segStartPos = Vector3.zero;
            float yaw = 0f;                 // carried across segments
            float segStartYaw = 0f;
            Vector3 panPivot = Vector3.zero;

            void EnterSegment(int index, float t, Vector3 pos, float currentYaw)
            {
                segStartT = t;
                segStartPos = pos;
                segStartYaw = currentYaw;
                if (index < script.Count && script[index].Kind == MotionKind.Pan)
                {
                    // Place the pivot so the camera is exactly where the previous
                    // segment left it when the sweep starts at -arc/2.
                    var s = script[index];
                    float startYaw = currentYaw;
                    panPivot = pos - Quaternion.Euler(0f, startYaw, 0f) * new Vector3(0f, 0f, s.PivotRadiusM);
                }
            }

            EnterSegment(0, 0f, Vector3.zero, 0f);

            for (int i = 0; i < frames; i++)
            {
                float t = i * dt;
                while (seg < script.Count - 1 && t >= segStartT + script[seg].DurationS)
                {
                    // Freeze the segment's final pose, then start the next one there.
                    seg++;
                    EnterSegment(seg, segStartT + script[seg - 1].DurationS, track.Pos[i > 0 ? i - 1 : 0], yaw);
                }

                var s = script[Mathf.Min(seg, script.Count - 1)];
                float localT = t - segStartT;
                Vector3 pos;

                switch (s.Kind)
                {
                    case MotionKind.Walk:
                    {
                        // Distance walked with smooth ramp-in (and ramp-out at the
                        // very end of the segment) so acceleration stays finite.
                        float dist = RampedDistance(localT, s.DurationS, s.SpeedMps);
                        var dir = Quaternion.Euler(0f, s.CourseDeg, 0f) * Vector3.forward;
                        pos = segStartPos + dir * dist;
                        yaw = s.CourseDeg;
                        break;
                    }
                    case MotionKind.Pan:
                    {
                        float arc = Mathf.Max(1f, s.PanArcDeg);
                        float rate = Mathf.Max(1f, s.PanRateDegPerSec);
                        float sweep = arc / rate;
                        float cycle = 2f * (sweep + s.PanPauseS);
                        // Phase-shifted by half a sweep so localT = 0 lands at
                        // offset 0: the arc is then CENTRED on the gaze the
                        // previous segment left, and yaw is still continuous
                        // across the boundary. (Starting at one end of the arc
                        // instead would swing the whole sweep off to one side —
                        // which quietly biased every pan away from the surf and
                        // suppressed the phantom amplitude for the wrong reason.)
                        float u = Mathf.Repeat(localT + sweep * 0.5f, cycle);
                        float offset;
                        if (u < sweep) offset = -arc * 0.5f + rate * u;
                        else if (u < sweep + s.PanPauseS) offset = arc * 0.5f;
                        else if (u < 2f * sweep + s.PanPauseS) offset = arc * 0.5f - rate * (u - sweep - s.PanPauseS);
                        else offset = -arc * 0.5f;

                        yaw = segStartYaw + offset;
                        pos = panPivot + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, s.PivotRadiusM);
                        break;
                    }
                    default:
                        pos = segStartPos;
                        break;
                }

                track.Kind[i] = s.Kind;
                track.Pos[i] = pos;
                track.YawDeg[i] = yaw;
                track.cadenceHz[i] = s.StepCadenceHz > 0f ? s.StepCadenceHz : StepCadenceHz;
                track.gaitAmp[i] = s.GaitAmplitudeScale > 0f ? s.GaitAmplitudeScale : 1f;
                track.strideVar[i] = s.StrideVariability;
                track.gaitEnvelope[i] = s.Kind == MotionKind.Walk
                    ? SpeedEnvelope(localT, s.DurationS) : 0f;
            }

            // --- pass 2: sway / breathing / gait bob on top -------------------
            // The legacy closed-form bob is preserved EXACTLY whenever no segment
            // asks for a gait override, so adding shuffle support cannot move any
            // existing scenario's numbers. Only an overriding scenario integrates.
            bool uniformGait = true;
            for (int i = 0; i < frames; i++)
                if (track.strideVar[i] != 0f || track.gaitAmp[i] != 1f ||
                    track.cadenceHz[i] != StepCadenceHz) { uniformGait = false; break; }

            float stepPhase = 0f;        // completed steps, integrated
            float smoothF = 0f, smoothAmp = 0f;
            float uniformWalkedS = 0f;   // walking seconds so far, for the legacy count

            for (int i = 0; i < frames; i++)
            {
                float t = i * dt;
                if (track.Kind[i] == MotionKind.Walk) uniformWalkedS += dt;
                float yawRad = track.YawDeg[i] * Mathf.Deg2Rad;
                var right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                var fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));

                Vector3 wobble =
                    right * (SwayAmplitudeM * Mathf.Sin(2f * Mathf.PI * SwayHzA * t + swayPhaseA)) +
                    fwd * (SwayAmplitudeM * Mathf.Sin(2f * Mathf.PI * SwayHzB * t + swayPhaseB));
                wobble.y += BreathAmplitudeM * Mathf.Sin(2f * Mathf.PI * BreathHz * t + breathPhase);

                if (track.Kind[i] == MotionKind.Walk)
                {
                    float env = track.gaitEnvelope[i];
                    if (uniformGait)
                    {
                        wobble.y += env * WalkBobVerticalM * Mathf.Sin(2f * Mathf.PI * StepCadenceHz * t);
                        wobble += right * (env * WalkBobLateralM * Mathf.Sin(Mathf.PI * StepCadenceHz * t));
                    }
                    else
                    {
                        // Integrated step phase, with cadence AND amplitude varying
                        // per step (keyed on the completed-step index, so a stride is
                        // long or short as a whole instead of jittering inside itself
                        // — which is what "stride variability" actually means).
                        int k = (int)stepPhase;
                        uint hk = ArSimRng.Hash((uint)(seed ^ 0x5EED), (uint)k);
                        float v = track.strideVar[i];
                        // Clamped to +/-50%: the Irwin-Hall draw reaches +/-3.46 sigma,
                        // and at v = 0.3 that would put a "shuffle" stride at 2x cadence
                        // AND 2x amplitude simultaneously — a sprint, not a shuffle, and
                        // (since the bob's acceleration goes as amp x f^2) an 8 m/s² peak
                        // that a step detector would find trivially. The clamp keeps the
                        // irregularity that makes the scenario hard without inventing a
                        // gait nobody has.
                        float targetF = track.cadenceHz[i] *
                                        Mathf.Clamp(1f + v * ArSimRng.GaussianFromHash(hk, 21), 0.6f, 1.5f);
                        float targetAmp = track.gaitAmp[i] *
                                          Mathf.Clamp(1f + v * ArSimRng.GaussianFromHash(hk, 22), 0.6f, 1.5f);

                        // Both are approached SMOOTHLY rather than stepped to. This is
                        // not cosmetic: the per-step targets change at integer
                        // stepPhase, where the bob's value is zero but its DERIVATIVE
                        // is maximal, so a discrete change is a velocity discontinuity
                        // — and the track's acceleration is a second difference of
                        // position, so each one became a multi-m/s² impulse. Those
                        // impulses handed a step detector a free, unphysical peak at
                        // every stride and made the "hard" shuffle EASIER to detect
                        // than a clean walk. Real cadence does not change
                        // instantaneously either.
                        float gaitAlpha = 1f - Mathf.Exp(-dt / GaitBlendTauS);
                        smoothF += (targetF - smoothF) * (smoothF > 0f ? gaitAlpha : 1f);
                        smoothAmp += (targetAmp - smoothAmp) * (smoothAmp > 0f ? gaitAlpha : 1f);

                        stepPhase += smoothF * dt;
                        float rad = 2f * Mathf.PI * stepPhase;
                        wobble.y += env * WalkBobVerticalM * smoothAmp * Mathf.Sin(rad);
                        wobble += right * (env * WalkBobLateralM * smoothAmp * Mathf.Sin(rad * 0.5f));
                    }
                }

                track.StepsTaken[i] = uniformGait ? uniformWalkedS * StepCadenceHz : stepPhase;
                track.Pos[i] += wobble;
            }

            // --- pass 3: derivatives + masks ----------------------------------
            for (int i = 0; i < frames; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(frames - 1, i + 1);
                float span = (b - a) * dt;
                Vector3 v = span > 0f ? (track.Pos[b] - track.Pos[a]) / span : Vector3.zero;
                track.SpeedMps[i] = v.magnitude;
                track.YawRateDegS[i] = span > 0f
                    ? Mathf.DeltaAngle(track.YawDeg[a], track.YawDeg[b]) / span
                    : 0f;

                // Second central difference for acceleration.
                Vector3 pPrev = track.Pos[Mathf.Max(0, i - 1)];
                Vector3 pNext = track.Pos[Mathf.Min(frames - 1, i + 1)];
                track.LinAccel[i] = (pNext - 2f * track.Pos[i] + pPrev) / (dt * dt);

                track.Walking[i] = track.SpeedMps[i] > WalkingSpeedThresholdMps;
                track.Moving[i] = track.SpeedMps[i] > MovingSpeedThresholdMps;
            }

            return track;
        }

        /// <summary>Distance covered by time <paramref name="localT"/> at
        /// <paramref name="speed"/>, with a smoothstep ramp in and out.</summary>
        static float RampedDistance(float localT, float duration, float speed)
        {
            float ramp = Mathf.Min(WalkRampS, duration * 0.4f);
            if (ramp <= 0f) return speed * localT;

            // Distance under a speed profile that smoothsteps from 0 to `speed`
            // over [0, ramp] and then holds. SmoothIntegral(1) = 0.5, i.e. the
            // ramp gives up exactly half a ramp-length of distance.
            float Integral(float t)
            {
                if (t <= 0f) return 0f;
                if (t < ramp) return speed * ramp * SmoothIntegral(t / ramp);
                return speed * (ramp * SmoothIntegral(1f) + (t - ramp));
            }

            float up = Integral(Mathf.Min(localT, duration));
            // Symmetric ramp-out at the tail of the segment.
            float tailStart = duration - ramp;
            if (localT > tailStart)
            {
                float u = Mathf.Clamp01((localT - tailStart) / ramp);
                float lost = speed * ramp * (u - SmoothIntegral(u));
                up -= lost;
            }
            return up;
        }

        /// <summary>∫₀ᵘ smoothstep(x) dx for smoothstep(x) = 3x²-2x³.</summary>
        static float SmoothIntegral(float u) => u * u * u - 0.5f * u * u * u * u;

        /// <summary>
        /// The walk segment's speed as a fraction of its cruise speed: smoothsteps
        /// 0→1 over the leading ramp, holds, then smoothsteps back to 0 over the
        /// trailing one — i.e. the derivative of <see cref="RampedDistance"/>,
        /// which is what makes the gait bob's envelope and the speed profile the
        /// same shape rather than two independently-authored curves.
        /// </summary>
        static float SpeedEnvelope(float localT, float duration)
        {
            float ramp = Mathf.Min(WalkRampS, duration * 0.4f);
            if (ramp <= 0f) return 1f;
            float up = Mathf.Clamp01(localT / ramp);
            float down = Mathf.Clamp01((duration - localT) / ramp);
            return Smooth(up) * Smooth(down);
            static float Smooth(float u) => u * u * (3f - 2f * u);
        }
    }
}
