using System.Collections.Generic;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// What the user is doing during a scenario, for the time-weighted composite.
    /// </summary>
    internal enum SessionProfile
    {
        /// <summary>Braced, looking at one spot. Tremor only.</summary>
        Standing,

        /// <summary>Standing in place but sweeping the phone across the water.</summary>
        StandingPanning,

        /// <summary>Walking along the beach.</summary>
        Walking,

        /// <summary>Diagnostic run — kept out of the composite so it cannot skew
        /// the weighting (e.g. the 60 Hz frame-rate probe, or a stitched session
        /// that already contains all three profiles).</summary>
        Excluded,
    }

    /// <summary>
    /// SESSION TIME BUDGET — the weights for the composite score.
    ///
    /// From the user's description of how the app is really used at the beach:
    /// they stand and look at the water most of the time, sweep the phone across
    /// it fairly often, and only occasionally walk. This is the ONE place the
    /// weighting lives; change these three numbers and every composite in the
    /// report moves with them.
    ///
    /// PROVISIONAL — a guess at the profile, not a measurement. Re-base against
    /// real session telemetry when there is any.
    /// </summary>
    internal static class SessionTimeWeights
    {
        public const float Standing = 0.55f;
        public const float StandingPanning = 0.25f;
        public const float Walking = 0.20f;

        public static float For(SessionProfile p) => p switch
        {
            SessionProfile.Standing => Standing,
            SessionProfile.StandingPanning => StandingPanning,
            SessionProfile.Walking => Walking,
            _ => 0f,
        };

        public static string Describe() =>
            $"standing {Standing:P0} / standing+panning {StandingPanning:P0} / walking {Walking:P0}";
    }

    /// <summary>
    /// One reproducible simulation: a motion script, a wave model, a sensor model
    /// and a frame rate, all seeded.
    /// </summary>
    internal sealed class ArSimScenario
    {
        public string Id;
        public string Notes;
        public SessionProfile Profile = SessionProfile.Standing;

        /// <summary>Frames per second. 30 is the real cadence — ScreenManager pins
        /// Application.targetFrameRate to 30 on the AR route.</summary>
        public float RateHz = 30f;

        public List<MotionSegment> Motion = new List<MotionSegment>();
        public WaveDriftModel Wave = WaveDriftModel.Calm();
        public SyntheticImu Imu = new SyntheticImu();
        public SyntheticGnss Gnss = new SyntheticGnss();

        /// <summary>Azimuth of SEAWARD in the Unity frame, degrees clockwise
        /// from +Z (docs/ar-wave-drift-research.md §3.0). The user starts facing
        /// it, and the phantom drift is emitted along it no matter where the
        /// phone is subsequently pointed. Scenario-level because it is the beach
        /// normal, not a property of the wave model.</summary>
        public float seawardAzimuthDeg = 0f;

        /// <summary>
        /// Seeded repeats averaged into one report row. Relocalisation jumps are
        /// Poisson at ~0.07 Hz, so a single 90 s run sees only a handful and their
        /// arrival luck dominates the error — enough to reorder the scenarios.
        /// (It never affects the stabilizer comparison: every stabilizer sees a
        /// bit-identical world within a repeat.) Override with MV_ARSIM_REPEATS.
        /// </summary>
        public int Repeats = 3;

        /// <summary>
        /// Seconds of the run that are SIMULATED but not SCORED — a prologue.
        ///
        /// Exists for the travelled-then-stood scenarios. A standing scenario that
        /// starts from a clean session can never charge a stabilizer for freezing in
        /// accumulated non-wave odometry error, because there is none: the genuine
        /// channel accrues with travel (see the genuine-channel block in
        /// <see cref="WaveDriftModel"/>) and a scenario that never travels banks
        /// nothing. Standing-from-a-clean-session is a MODELLING ERROR — the real
        /// usage profile is "stands still most of the time, but can also walk" — and
        /// it happens to favour suppress-everything, so it had to go.
        ///
        /// The fix is a walk prologue that is simulated but not measured, so the
        /// scenario scores the STANDING posture while the tracker carries the error
        /// a real walk would have left it holding. What the window does NOT do is
        /// excuse eating the walk: the metrics are camera-position error against
        /// ground truth, so a stabilizer that suppressed the prologue is still
        /// standing tens of metres from where it thinks it is when scoring starts.
        ///
        /// 0 (the default) scores the whole run, so every pre-existing scenario is
        /// bit-identical.
        /// </summary>
        public float MetricsStartS = 0f;

        public float DurationS
        {
            get { float t = 0f; foreach (var s in Motion) t += s.DurationS; return t; }
        }

        public string Describe() =>
            $"{Id}  [{Profile}]  {DurationS:F0}s @{RateHz:F0}Hz" +
            (MetricsStartS > 0f ? $"  (scored from {MetricsStartS:F0}s)" : "") +
            $"  {Wave.Describe()}  {Imu.Describe()}  {Gnss.Describe()}";
    }

    /// <summary>
    /// THE SCENARIO TABLE. One entry per row; adding a scenario is a single
    /// entry and nothing else in the harness changes.
    ///
    /// Ordered by how much the answer matters, which follows the real usage
    /// profile rather than the difficulty of the maths:
    ///
    ///   1. standing and PANNING at the surf — the commonest thing the user does,
    ///      and the case where the shipped gate turns itself off (panning makes
    ///      the gyro loud, so StillnessDetector.IsStill goes false, so the
    ///      translation gate stops suppressing even though the user never
    ///      travelled anywhere).
    ///   2. standing braced at the surf — what the gate was actually built for.
    ///   3. walking — secondary; the gate is inert by design while the IMU reports
    ///      motion, so a failure here is expected, not a bug in the harness.
    ///
    /// Wave presets are the four amplitude regimes of
    /// docs/ar-wave-drift-research.md §3.1, keyed on the range to the nearest
    /// moving water, which is the dominant driver (§1.2's 1/Z² weighting).
    /// </summary>
    internal static class ArSimScenarios
    {
        /// <summary>Everyone faces the sea at Unity +Z; the beach runs east-west
        /// across the frame, so walking courses are near ±90°.</summary>
        const float SeawardAzimuthDeg = 0f;

        /// <summary>The same wave model with relocalisation jumps switched off.</summary>
        static WaveDriftModel Jumpless(WaveDriftModel m)
        {
            m.Name = m.Name + "-nojump";
            m.jumpRateHz = 0f;
            return m;
        }

        /// <summary>
        /// The same wave model with the GENUINE (non-wave, path-proportional)
        /// odometry error at the top of its measured bracket: 14.4 %/m, from Feigl
        /// et al. GRAPP 2020's "scaling error of up to 14.4 cm/m … quasi-directly
        /// proportional to the path length" for AR systems in a large DYNAMIC
        /// environment. The other end of the bracket is 0.34 %/m (Here To Stay, an
        /// ordinary static scene); the model's default 3 %/m sits between them.
        ///
        /// This is the corner where suppress-everything is known to lose, so a
        /// scenario using it is the sharp test, not a stress test for its own sake.
        /// </summary>
        static WaveDriftModel HarshOdometry(WaveDriftModel m)
        {
            m.Name = m.Name + "-harshodo";
            m.genuineDriftPerMeterPath = 0.144f;
            return m;
        }

        /// <summary>Test builds shorten every scenario through this factor so the
        /// suite stays a few seconds; the ar-sim runner uses 1.</summary>
        public static IReadOnlyList<ArSimScenario> All(float durationScale = 1f)
        {
            var list = new List<ArSimScenario>();

            float D(float seconds) => Mathf.Max(2f, seconds * durationScale);

            // ---- 1. standing + panning: the dominant real case ---------------

            // Static-but-panning: the second half of the still-waves split. A
            // deliberate, slow look-around with dwells at the ends of the sweep,
            // which is exactly when the user is looking at an animal and expects
            // it to hold position.
            list.Add(new ArSimScenario
            {
                Id = "still-waves-pan",
                Profile = SessionProfile.StandingPanning,
                Notes = "standing at the surf, slow deliberate sweeps with dwells",
                Motion =
                {
                    MotionSegment.Standing(D(8f)),
                    MotionSegment.SlowPan(D(74f)),
                    MotionSegment.Standing(D(8f)),
                },
                Wave = WaveDriftModel.Nominal(seed: 21),
                Gnss = new SyntheticGnss { seed = 121 },
                Imu = new SyntheticImu { seed = 221 },
            });

            // The headline case: both a slow deliberate sweep AND a faster scan of
            // the whole bay, so the gyro spends time on both sides of every
            // threshold the detector has.
            list.Add(new ArSimScenario
            {
                Id = "pan-waves",
                Profile = SessionProfile.StandingPanning,
                Notes = "standing, alternating slow and fast sweeps (the commonest real usage)",
                Motion =
                {
                    MotionSegment.Standing(D(6f)),
                    MotionSegment.SlowPan(D(40f)),
                    MotionSegment.Standing(D(6f)),
                    MotionSegment.FastPan(D(34f)),
                    MotionSegment.Standing(D(6f)),
                    MotionSegment.SlowPan(D(28f)),
                },
                Wave = WaveDriftModel.Nominal(seed: 22),
                Gnss = new SyntheticGnss { seed = 122 },
                Imu = new SyntheticImu { seed = 222 },
            });

            // ---- 2. standing braced ------------------------------------------

            // Sanity: a stabilizer must do approximately nothing here.
            list.Add(new ArSimScenario
            {
                Id = "still-calm",
                Profile = SessionProfile.Standing,
                Notes = "braced, no waves - sanity check: nothing should happen",
                Motion = { MotionSegment.Standing(D(60f)) },
                Wave = WaveDriftModel.Calm(seed: 23),
                Gnss = new SyntheticGnss { seed = 123 },
                Imu = new SyntheticImu { seed = 223 },
            });

            // The case the shipped gate was built for.
            list.Add(new ArSimScenario
            {
                Id = "still-waves",
                Profile = SessionProfile.Standing,
                Notes = "braced at the surf, tremor only - what the gate targets",
                Motion = { MotionSegment.Standing(D(90f)) },
                Wave = WaveDriftModel.Nominal(seed: 24),
                Gnss = new SyntheticGnss { seed = 124 },
                Imu = new SyntheticImu { seed = 224 },
            });

            list.Add(new ArSimScenario
            {
                Id = "reloc-jumps",
                Profile = SessionProfile.Standing,
                Notes = "braced, mild optics, tracker keeps re-solving",
                Motion = { MotionSegment.Standing(D(120f)) },
                Wave = WaveDriftModel.RelocalizationProne(seed: 25),
                Gnss = new SyntheticGnss { seed = 125 },
                Imu = new SyntheticImu { seed = 225 },
            });

            // §3.1's CATASTROPHIC regime, and a thing users really do: tilt the
            // phone down to look at the water washing near their feet. The swash
            // at 2-5 m puts alpha at 78-87% - the water becomes the consensus set
            // and the tracker is measuring the wave, not the phone. Expected to
            // visibly break every stabilizer; that is the point of having it.
            list.Add(new ArSimScenario
            {
                Id = "tilt-down-swash",
                Profile = SessionProfile.Standing,
                Notes = "phone tilted down onto the swash at ~3.5 m - catastrophic regime (doc SS3.1)",
                Motion =
                {
                    MotionSegment.Standing(D(30f)),
                    MotionSegment.SlowPan(D(30f)),
                    MotionSegment.Standing(D(30f)),
                },
                Wave = WaveDriftModel.Catastrophic(seed: 29),
                Gnss = new SyntheticGnss { seed = 129 },
                Imu = new SyntheticImu { seed = 229 },
            });

            // The SAME standing posture as still-waves, but reached the way a real
            // user reaches it: they walked there first. ~46 m of beach, then nearly
            // two minutes of holding still at the surf, and only the holding is
            // scored (MetricsStartS).
            //
            // WHY THIS SCENARIO HAD TO EXIST. Every other standing row starts from a
            // clean session, so the genuine (non-wave, path-proportional) error
            // channel is structurally silent in them and the standing group cannot
            // punish a stabilizer for freezing that error in — which made
            // "suppress every translation while still" look free. The user's real
            // profile is "stands still most of the time, but can also walk", so a
            // real standing period follows travel. Scored as Standing because the
            // posture under test IS standing; the walk is the prologue that loads
            // the tracker, not the thing being measured.
            {
                float lead = D(4f), walk = D(38f), settle = D(2f);
                list.Add(new ArSimScenario
                {
                    Id = "walk-then-look",
                    Profile = SessionProfile.Standing,
                    Notes = "walk ~46 m, then hold still at the surf - only the holding is scored",
                    Motion =
                    {
                        MotionSegment.Standing(lead),
                        MotionSegment.Walking(walk, 1.2f, 75f),
                        MotionSegment.Standing(settle + D(110f)),
                    },
                    MetricsStartS = lead + walk + settle,
                    Wave = WaveDriftModel.Nominal(seed: 33),
                    Gnss = new SyntheticGnss { seed = 133 },
                    Imu = new SyntheticImu { seed = 233 },
                    // The genuine channel's direction is a uniform azimuth draw per
                    // repeat, and whether the banked error lands across the line of
                    // sight (bearing) or along it (apparent range) changes the cost a
                    // lot. 6 repeats instead of 3 so the row is about the mechanism
                    // rather than about the draw.
                    Repeats = 6,
                });
            }

            // ---- 3. walking: secondary ---------------------------------------

            // The gate must be transparent here: real displacement must survive.
            list.Add(new ArSimScenario
            {
                Id = "walk-calm",
                Profile = SessionProfile.Walking,
                Notes = "~50 m along the beach, no waves - real motion must survive",
                Motion =
                {
                    MotionSegment.Standing(D(3f)),
                    MotionSegment.Walking(D(42f)),
                    MotionSegment.Standing(D(3f)),
                },
                Wave = WaveDriftModel.Calm(seed: 26),
                Gnss = new SyntheticGnss { seed = 126 },
                Imu = new SyntheticImu { seed = 226 },
            });

            // The shipped gate is INERT here by construction (IsStill is false
            // while walking). Expect it to fail; that is a finding, not a bug.
            list.Add(new ArSimScenario
            {
                Id = "walk-waves",
                Profile = SessionProfile.Walking,
                Notes = "~50 m along the surf - gate is inert while walking, expect no help",
                Motion =
                {
                    MotionSegment.Standing(D(3f)),
                    MotionSegment.Walking(D(42f)),
                    MotionSegment.Standing(D(3f)),
                },
                Wave = WaveDriftModel.Nominal(seed: 27),
                Gnss = new SyntheticGnss { seed = 127 },
                Imu = new SyntheticImu { seed = 227 },
            });

            // Slow, short, irregular steps on dry sand: the honest stress test for
            // any step-detection-based gate (doc SS5.1 — sand slows the walk,
            // increases stride variability, and damps/smears the vertical
            // acceleration peak, which is what defeats fixed-threshold peak
            // detection). A candidate that wins on `walk-*` and loses here has not
            // actually solved walking on a beach.
            list.Add(new ArSimScenario
            {
                Id = "shuffle-walk",
                Profile = SessionProfile.Walking,
                Notes = "slow irregular shuffling on dry sand - weakest case for step detection",
                Motion =
                {
                    MotionSegment.Standing(D(5f)),
                    MotionSegment.Shuffling(D(40f)),
                    MotionSegment.Standing(D(5f)),
                },
                Wave = WaveDriftModel.Nominal(seed: 31),
                Gnss = new SyntheticGnss { seed = 131 },
                Imu = new SyntheticImu { seed = 231 },
            });

            // ---- diagnostics (excluded from the composite) -------------------

            // Walk, then stop and hold: how fast does each candidate get back to
            // suppressing? The moment the user settles to look at an animal is
            // exactly when the world must lock, and every gate has a release time
            // (the shipped one's enterStillTime, a step gate's steppingTimeoutS).
            // Excluded from the composite because it is a transition probe, not a
            // usage profile - two thirds of its duration is standing, so scoring it
            // as "walking" would just dilute the walking group.
            list.Add(new ArSimScenario
            {
                Id = "walk-then-stand",
                Profile = SessionProfile.Excluded,
                Notes = "walk 30 m then stop and hold - measures suppression re-acquisition",
                Motion =
                {
                    MotionSegment.Standing(D(5f)),
                    MotionSegment.Walking(D(25f)),
                    MotionSegment.Standing(D(45f)),
                },
                Wave = WaveDriftModel.Nominal(seed: 32),
                Gnss = new SyntheticGnss { seed = 132 },
                Imu = new SyntheticImu { seed = 232 },
            });


            // ~3 min stitched session matching the real profile: mostly standing,
            // a couple of pans, two short walks.
            list.Add(new ArSimScenario
            {
                Id = "mixed-session",
                Profile = SessionProfile.Excluded,
                Notes = "realistic 3 min session: mostly standing, 2 pans, 2 short walks",
                Motion =
                {
                    MotionSegment.Standing(D(45f)),
                    MotionSegment.SlowPan(D(20f)),
                    MotionSegment.Standing(D(30f)),
                    MotionSegment.Walking(D(18f), 1.15f, 75f),
                    MotionSegment.Standing(D(25f)),
                    MotionSegment.FastPan(D(15f)),
                    MotionSegment.Standing(D(20f)),
                    MotionSegment.Walking(D(15f), 1.15f, 255f),   // back the other way
                },
                Wave = WaveDriftModel.Nominal(seed: 28),
                Gnss = new SyntheticGnss { seed = 128 },
                Imu = new SyntheticImu { seed = 228 },
            });

            // THE BAR. A realistic multi-minute walk-and-look session with the
            // non-wave odometry error at the TOP of its measured range (14.4 %/m,
            // Feigl GRAPP 2020) — the corner where "suppress every translation while
            // still" is known to lose, and therefore the one a path-based gate has to
            // win back. ~72 m walked, then four minutes of standing and panning at
            // the water, scored from the moment the walk ends.
            //
            // Excluded from the composite deliberately: it is one point of the
            // genuine-rate axis, not a share of session time, and folding a harsh
            // parameter choice into the weighted table would be reweighting toward a
            // conclusion. It is reported as its own column instead.
            {
                float lead = D(5f), walk = D(60f), settle = D(2f);
                list.Add(new ArSimScenario
                {
                    Id = "walk-look-harsh",
                    Profile = SessionProfile.Excluded,
                    Notes = "~72 m walked then 4 min of looking, odometry error at 14.4 %/m (the bar)",
                    Motion =
                    {
                        MotionSegment.Standing(lead),
                        MotionSegment.Walking(walk, 1.2f, 75f),
                        MotionSegment.Standing(settle + D(70f)),
                        MotionSegment.SlowPan(D(50f)),
                        MotionSegment.Standing(D(60f)),
                        MotionSegment.FastPan(D(30f)),
                        MotionSegment.Standing(D(40f)),
                    },
                    MetricsStartS = lead + walk + settle,
                    Wave = HarshOdometry(WaveDriftModel.Nominal(seed: 34)),
                    Gnss = new SyntheticGnss { seed = 134 },
                    Imu = new SyntheticImu { seed = 234 },
                    Repeats = 6,
                });
            }

            // Identical to still-waves but with relocalisation jumps disabled, so
            // the gate's effect on the pure wave OSCILLATION can be read without
            // the retained-jump accumulation on top. The two rows together are
            // what separates "the gate removes the oscillation" from "the gate
            // banks every jump it does not suppress into a permanent offset".
            list.Add(new ArSimScenario
            {
                Id = "still-waves-nojump",
                Profile = SessionProfile.Excluded,
                Notes = "still-waves with relocalisation jumps off - isolates oscillation suppression",
                Motion = { MotionSegment.Standing(D(90f)) },
                Wave = Jumpless(WaveDriftModel.Nominal(seed: 24)),
                Gnss = new SyntheticGnss { seed = 124 },
                Imu = new SyntheticImu { seed = 224 },
            });

            // Identical to still-waves but at 60 Hz. StillnessCore's EMA is
            // per-frame, not per-second, so this run exists to EXPOSE that: any
            // difference in the stillness behaviour between the two is the
            // frame-rate dependence documented in StillnessCore.Step.
            list.Add(new ArSimScenario
            {
                Id = "still-waves-60hz",
                Profile = SessionProfile.Excluded,
                Notes = "still-waves at 60 Hz - probes the per-frame (dt-dependent) EMA",
                RateHz = 60f,
                Motion = { MotionSegment.Standing(D(90f)) },
                Wave = WaveDriftModel.Nominal(seed: 24),
                Gnss = new SyntheticGnss { seed = 124 },
                Imu = new SyntheticImu { seed = 224 },
            });

            foreach (var s in list)
            {
                s.seawardAzimuthDeg = SeawardAzimuthDeg;
                s.Wave.seawardAzimuthDeg = SeawardAzimuthDeg;
            }
            return list;
        }
    }
}
