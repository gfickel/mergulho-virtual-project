using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Classic accelerometer step detection: band-pass the vertical specific force
    /// around the step band, peak-detect against an adaptive threshold, enforce a
    /// refractory period.
    ///
    /// WHY THIS EXISTS. The shipped gate asks "is the phone quiet?"; this asks the
    /// physically stronger question "did the user actually take a step?".
    /// Translation of a person on a beach is only possible via steps, and waves can
    /// fake optical flow but cannot fake a gait signature. Standing braced and
    /// standing-while-panning both produce NO steps, so both suppress — and the
    /// second is exactly where the shipped gyro-inclusive gate gives up.
    ///
    /// It reads the VERTICAL component of linear acceleration, not the magnitude
    /// the shipped StillnessDetector consumes: a magnitude rectifies, so a 1.9 Hz
    /// gait bob appears at 3.8 Hz plus DC and its fundamental — the thing being
    /// detected — is not in that channel at all. On device the vertical component
    /// is `LinearAccelerationSensor` projected onto `GravitySensor.gravity`; no new
    /// sensor and no new permission.
    ///
    /// Engine-free apart from Mathf: dt and one scalar per frame, nothing else.
    /// Lives in the EDITOR assembly because it is a candidate, not shipped code. If
    /// it is adopted, move it to Assets/Scripts/AR/Core/ next to StillnessCore.
    /// </summary>
    internal sealed class StepDetectorCore
    {
        internal struct Settings
        {
            /// <summary>Lower band edge, Hz. Removes DC, attitude changes and the
            /// 0.2-0.3 Hz postural-sway band.</summary>
            public float highPassHz;

            /// <summary>Upper band edge, Hz. Removes the 3-10 Hz physiological
            /// tremor that dominates a hand-held phone at rest.</summary>
            public float lowPassHz;

            /// <summary>Absolute floor on a peak, m/s². Below this nothing counts
            /// as a step however the adaptive threshold has adapted, so a quiet
            /// standing run can never manufacture one.</summary>
            public float minPeakMps2;

            /// <summary>Adaptive threshold = this × the running mean |band-passed|.
            /// Standard practice; the point is that a heavy walker and a light one
            /// both trip it.
            ///
            /// SIZE IT AGAINST THE ARITHMETIC, NOT BY FEEL. For a sinusoid of
            /// amplitude A, mean|x| = (2/π)A = 0.637A — so a factor of 1.6 puts the
            /// threshold at 1.019A, i.e. ABOVE the peak, and the detector can never
            /// fire on a clean periodic gait at all. (That was the first version of
            /// this file; it found 23 of 80 real steps on `walk-calm` and, tellingly,
            /// 40 of 55 on the *irregular* `shuffle-walk`, because a varying
            /// amplitude occasionally overshoots a threshold a constant one sits
            /// exactly on.) 0.8 puts it at 0.51A — the conventional "about half the
            /// peak" — and the absolute floor is what keeps noise out.</summary>
            public float peakFactor;

            /// <summary>Minimum time between steps, s. 0.28 s = 3.6 steps/s, above
            /// any real cadence, so one bob cannot be counted twice.</summary>
            public float refractoryS;

            /// <summary>No step for this long ⇒ not stepping ⇒ suppress. This is
            /// the gate's release time and therefore its over-permissive tail: the
            /// world stays unlocked for up to this long after the user stops.</summary>
            public float steppingTimeoutS;

            /// <summary>Time constant of the adaptive-threshold envelope, s.</summary>
            public float envelopeTauS;

            /// <summary>
            /// Defaults, all from the task brief / the standard literature ranges:
            /// 1.5-2.5 Hz step band (passband set a little wider so a 1.2 Hz shuffle
            /// and a 2.4 Hz brisk walk both survive), ~250-300 ms refractory.
            /// NOT tuned against the scenarios.
            /// </summary>
            public static Settings Defaults => new Settings
            {
                highPassHz = 0.7f,
                lowPassHz = 3.5f,
                minPeakMps2 = 0.35f,
                peakFactor = 0.8f,
                refractoryS = 0.28f,
                steppingTimeoutS = 0.85f,
                envelopeTauS = 2.0f,
            };
        }

        public Settings Config = Settings.Defaults;

        /// <summary>Steps detected since <see cref="Reset"/>.</summary>
        public int StepCount { get; private set; }

        /// <summary>True while a step has fired within
        /// <see cref="Settings.steppingTimeoutS"/>.</summary>
        public bool Stepping { get; private set; }

        /// <summary>Band-passed vertical accel this frame, m/s² (for the CSV).</summary>
        public float BandPassed { get; private set; }

        /// <summary>The adaptive threshold in force this frame, m/s².</summary>
        public float Threshold { get; private set; }

        /// <summary>True on the frame a step was detected.</summary>
        public bool StepThisFrame { get; private set; }

        float lpFast, lpSlow, envelope;
        float timeS, lastStepT;
        float prev, prevPrev;
        int primed;

        public void Reset()
        {
            StepCount = 0;
            Stepping = false;
            StepThisFrame = false;
            BandPassed = 0f;
            Threshold = 0f;
            lpFast = lpSlow = envelope = 0f;
            timeS = 0f;
            lastStepT = float.NegativeInfinity;
            prev = prevPrev = 0f;
            primed = 0;
        }

        /// <param name="dt">Frame delta, s.</param>
        /// <param name="accelVertMps2">Vertical linear acceleration (gravity
        /// removed), m/s², signed.</param>
        public void Step(float dt, float accelVertMps2)
        {
            StepThisFrame = false;
            timeS += dt;

            // Band-pass as the difference of two one-pole low-passes: cheap,
            // unconditionally stable at any dt, and its corner frequencies are
            // stated in Hz rather than in per-frame alphas (the dt-dependent-EMA
            // defect StillnessCore documents).
            lpFast = OnePole(lpFast, accelVertMps2, dt, Config.lowPassHz);
            lpSlow = OnePole(lpSlow, accelVertMps2, dt, Config.highPassHz);
            float bp = lpFast - lpSlow;
            BandPassed = bp;

            float envAlpha = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, Config.envelopeTauS));
            envelope += (Mathf.Abs(bp) - envelope) * envAlpha;
            Threshold = Mathf.Max(Config.minPeakMps2, Config.peakFactor * envelope);

            // Local maximum on the middle of three samples. primed guards the first
            // two frames, where prev/prevPrev are not real samples yet.
            if (primed >= 2 &&
                prev > prevPrev && prev >= bp &&
                prev > Threshold &&
                timeS - lastStepT >= Config.refractoryS)
            {
                StepCount++;
                lastStepT = timeS;
                StepThisFrame = true;
            }
            else if (primed < 2)
            {
                primed++;
            }

            prevPrev = prev;
            prev = bp;

            Stepping = StepCount > 0 && timeS - lastStepT < Config.steppingTimeoutS;
        }

        /// <summary>Seconds since the last detected step (∞ before the first).</summary>
        public float SinceLastStepS => timeS - lastStepT;

        static float OnePole(float state, float x, float dt, float cornerHz)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(1e-4f, cornerHz));
            return state + (x - state) * (1f - Mathf.Exp(-dt / Mathf.Max(1e-6f, tau)));
        }

        public string Describe() =>
            $"step(band={Config.highPassHz:F2}-{Config.lowPassHz:F1}Hz " +
            $"minPeak={Config.minPeakMps2:F2}m/s2 x{Config.peakFactor:F1}env " +
            $"refract={Config.refractoryS * 1000f:F0}ms timeout={Config.steppingTimeoutS:F2}s " +
            $"envTau={Config.envelopeTauS:F1}s)";
    }
}
