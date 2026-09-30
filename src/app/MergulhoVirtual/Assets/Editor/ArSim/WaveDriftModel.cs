using System.Collections.Generic;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>What the tracker's pose error looks like on one frame.</summary>
    internal struct PhantomState
    {
        /// <summary>Phantom position error in WORLD metres, to be added to the
        /// ground-truth position to get the tracker's reported session pose.</summary>
        public Vector3 PositionErrorM;

        /// <summary>Multiplicative scale error on reported displacement (1 = none).</summary>
        public float ScaleFactor;

        /// <summary>Accumulated yaw error, degrees. DIAGNOSTIC ONLY — see the note
        /// on the rotation channel in <see cref="WaveDriftModel"/>.</summary>
        public float YawErrorDeg;

        /// <summary>True on the frame a relocalisation jump fired.</summary>
        public bool JumpedThisFrame;
        public float JumpMagnitudeM;

        /// <summary>How much of the near water is in frame this instant (0–1).</summary>
        public float ViewFactor;

        /// <summary>The accumulated seaward ratchet, world metres.</summary>
        public Vector3 RatchetM;

        /// <summary>The accumulated GENUINE registration error, world metres — the
        /// part of the pose error that is not wave-induced and that a translation
        /// gate therefore cannot legitimately cancel. See the genuine-channel block
        /// in <see cref="WaveDriftModel"/>.</summary>
        public Vector3 GenuineErrorM;

        /// <summary>True when the jump that fired this frame was a correction of
        /// the GENUINE channel rather than of the phantom ratchet.</summary>
        public bool JumpWasGenuine;
    }

    /// <summary>
    /// The phantom pose error an ARCore-class VIO tracker accumulates while
    /// pointed at breaking surf. Parameters and structure are taken from
    /// <c>docs/ar-wave-drift-research.md</c>; section references below are to
    /// that document, and every constant carries its evidence tag:
    ///
    ///   [M] measured / cited     [D] derived from measured values
    ///   [E] extrapolated — a defensible guess with no measurement behind it
    ///
    /// A large share of §3 is [D] or [E]: nobody has published a VIO-pointed-at-
    /// surf dataset. Treat [E] numbers as soft and re-run the sweep when §3.8's
    /// on-beach falsification tests produce real values.
    ///
    /// Four things make this model different from a sine wave, and all four
    /// matter for the results:
    ///
    ///  1. THE ERROR FRAME IS WORLD-FIXED, NOT CAMERA-LOCAL (§3.0). The phantom
    ///     translation lives in {ŝ seaward, l̂ alongshore, û up}, which does not
    ///     rotate with the phone. Panning away lowers the AMPLITUDE (the near
    ///     water leaves the frame) but never changes the DIRECTION. Emitting
    ///     drift along the camera's forward axis would be wrong the moment the
    ///     user turns — and panning is this app's commonest posture.
    ///  2. THE SIGN IS SEAWARD (§1.4, §3.3). Foam flows shoreward, so the tracker
    ///     infers the camera moved seaward. Rendered: world-anchored content
    ///     creeps TOWARD the viewer and grows, then snaps away on relocalisation.
    ///  3. AMPLITUDE IS SET BY THE NEAREST MOVING WATER, VIA 1/Z² (§1.2, §3.1).
    ///     Translation information per feature scales as 1/Z², so the swash 3 m
    ///     from your feet outweighs the whole bay. Camera pitch therefore matters
    ///     enormously: <see cref="waterRangeNearM"/> is the dominant knob.
    ///  4. THE IMU IMPOSES A 1/f² CEILING (§1.3, §3.2). The accelerometer can only
    ///     veto ACCELERATING phantom motion, so admissible amplitude is
    ///     A_max = a_th/(2πf)². Long swell therefore hurts far more than short
    ///     wind sea, and a phone with a worse accel bias drifts far more.
    ///
    /// STATEFUL BY DESIGN: call <see cref="Reset"/> once, then
    /// <see cref="Advance"/> exactly once per frame in increasing time order. The
    /// ratchet integrates, the scale is a random walk, and jump direction is
    /// biased against the ACCUMULATED ratchet (§3.5) — none of which can be a
    /// pure function of t. Determinism comes from the seed plus the call order.
    /// </summary>
    internal sealed class WaveDriftModel
    {
        public string Name = "custom";
        public int seed = 12345;

        // =====================================================================
        // Frames — §3.0
        // =====================================================================

        /// <summary>Azimuth of ŝ (SEAWARD, anti-parallel to wave travel) in the
        /// Unity frame: degrees clockwise from Unity +Z. World-fixed. [D]</summary>
        public float seawardAzimuthDeg = 0f;

        // =====================================================================
        // Scene geometry — the dominant amplitude knob. §1.2, §3.1, §3.7
        // =====================================================================

        /// <summary>Range to the NEAREST moving water in frame, metres. This one
        /// number dominates everything: information about camera translation goes
        /// as 1/Z², so the regime (benign → catastrophic) is essentially a
        /// function of it. Reference 15 m. [D]</summary>
        public float waterRangeNearM = 15f;

        /// <summary>Range to the far edge of the moving water, metres. [D]</summary>
        public float waterRangeFarM = 35f;

        /// <summary>Range to the static scene (dry sand in the lower frame), m. [D]</summary>
        public float staticRangeM = 5f;

        public float waterFeatureCount = 150f;   // [E] §3.7
        public float staticFeatureCount = 150f;  // [E] §3.7

        /// <summary>Sustained α is a fraction of the instantaneous α because a
        /// robust kernel eventually drops each diverging water track (§1.2). The
        /// doc gives 0.2 for scenes where the water is a minority, and "close to
        /// 1" where it is the consensus set. [E]</summary>
        public float robustRejectionFactor = 0.2f;

        /// <summary>α above which robust rejection starts losing (the water is
        /// becoming the consensus set), and where it has fully lost. [E]</summary>
        const float RejectionFailsFromAlpha = 0.3f;
        const float RejectionFailsToAlpha = 0.9f;

        // =====================================================================
        // Amplitude — §3.1
        // =====================================================================

        /// <summary>
        /// Peak-to-peak phantom translation along ŝ at the REFERENCE range.
        /// §3.7's reference set: 0.15 m at water_range_near = 15 m. [D]
        /// </summary>
        public const float ReferenceAmplitudePpM = 0.15f;
        public const float ReferenceRangeM = 15f;

        /// <summary>
        /// Exponent of the amplitude-vs-range power law. Fitted to §3.1's four
        /// regime rows (benign ~30 m → 0.04 m pp; nominal 15 m → 0.15;
        /// bad ~10 m → 0.25; catastrophic ~3.5 m → 1.75), which a single power
        /// law reproduces to within the width of each band. The mechanism (§1.2)
        /// predicts 2.0; the fitted 1.69 is slightly shallower because the STATIC
        /// feature range also shortens as the user tilts down, partially
        /// offsetting. [D from the doc's own table]
        ///
        /// Why not extrapolate amplitude straight from α: route 1 (d = α·c·t)
        /// is linear in α, and α explodes from 4.5% to 78% between the nominal
        /// and catastrophic geometries — which would predict ~10 m of phantom
        /// translation. It does not happen because the IMU ceiling (§3.2) caps
        /// it. The doc's tabulated bands already fold that in, so they are taken
        /// as authoritative for amplitude, and α is used where the doc uses it:
        /// for the RATCHET (below), where its own arithmetic checks out.
        /// </summary>
        const float AmplitudeRangeExponent = 1.69f;

        /// <summary>Axis anisotropy ŝ : û : l̂ (§3.4). The reference yaml's
        /// 0.15/0.07/0.04 is 1 : 0.47 : 0.27, i.e. this. [E]</summary>
        const float UpRatio = 0.5f;
        const float AlongshoreRatio = 0.25f;

        /// <summary>Hard ceiling on the seaward peak-to-peak amplitude. §3.1 puts
        /// the catastrophic regime at 0.5–3 m and notes tracking is effectively
        /// lost there, so there is nothing to model above it. [E]</summary>
        const float MaxAmplitudePpM = 3f;

        /// <summary>Multiply the derived amplitude (1 = the doc's value). A knob
        /// for sensitivity sweeps, not for calibration.</summary>
        public float amplitudeScale = 1f;

        // =====================================================================
        // Frequency content — §3.2, §3.7
        // =====================================================================

        /// <summary>Groundswell / incident bore: the carrier. Noronha Dec–Mar
        /// N-swell, Tp 12–18 s. §3.7 reference 14 s. [M periods / D weight]</summary>
        public float groundswellPeriodS = 14f;
        public float groundswellWeight = 1.0f;

        /// <summary>SE trade wind sea, Tp 5–9 s (Noronha mode 8–10 s). Weighted
        /// BELOW the swell despite similar Hs, because the IMU rejects it 2.6–4×
        /// harder (§1.3). §3.7 reference 9 s / 0.35. [M periods / D weight]</summary>
        public float windSeaPeriodS = 9f;
        public float windSeaWeight = 0.35f;

        /// <summary>Swash / bore-capture on the sand, T 12–35 s (measured mean
        /// 24 s, §2.4). NOT infragravity — Cacimba do Padre is reflective, so the
        /// swash is incident-band. §3.7 reference 22 s / 0.8. [M period / E weight]</summary>
        public float swashPeriodS = 22f;
        public float swashWeight = 0.8f;

        /// <summary>Wave-group envelope, T 70–150 s. This AMPLITUDE-MODULATES the
        /// three bands above; it is deliberately NOT a fourth oscillator (§3.2).
        /// §3.7 reference 110 s, depth 0.6. [M period / E depth]</summary>
        public float groupPeriodS = 110f;
        public float groupDepth = 0.6f;

        /// <summary>Components per band. Each band is a narrowband process (a
        /// random-phase sum spread across the band), not a pure sine — real swell
        /// is a spectrum, and the groupiness is what the tracker actually sees
        /// (§3.2). [E]</summary>
        const int ComponentsPerBand = 5;

        /// <summary>Fractional half-width of each band around its centre
        /// frequency. Keeps the realisation inside the doc's quoted f ranges. [E]</summary>
        const float BandHalfWidth = 0.18f;

        /// <summary>How much of each component's phase is locked to the single
        /// wave train vs. independent. The bands are ONE forcing, not three
        /// noises (§3.2). §3.7 reference 0.25. [E]</summary>
        public float coherentPhaseFraction = 0.25f;

        /// <summary>
        /// Device accelerometer bias, m/s² — the 1/f² ceiling's only input:
        /// A_max = a_th/(2πf)². Measured range across phones 0.018 (best) to
        /// 0.206 (worst), a ~17× spread in admissible phantom amplitude, which
        /// makes this the most valuable sweep parameter in the model. §3.7
        /// reference 0.05. [M]
        /// </summary>
        public float imuAccelBiasMps2 = 0.05f;

        /// <summary>Nothing above this is simulated at all: the admissible
        /// amplitude there is 2–21 mm, below ARCore's own jitter floor, so adding
        /// it would make the simulator LESS faithful (§3.2). [D]</summary>
        public const float MaxSimulatedHz = 0.5f;

        // =====================================================================
        // Ratchet — §3.3
        // =====================================================================

        /// <summary>Bore celerity across the surf zone, m/s. 2–5 across the zone;
        /// §3.7 reference 4.0. [M]</summary>
        public float boreCelerityMps = 4f;

        /// <summary>Fraction of the wave period the bore sweep stays coherent
        /// enough to bias the solver (0.2–0.4). §3.7 reference 0.25. [E]</summary>
        public float coherentPhaseFractionOfPeriod = 0.25f;

        /// <summary>
        /// Fraction of each seaward pulse that relaxes back. THE SOFTEST NUMBER
        /// IN THE MODEL — §3.3 flags it as having no measurement behind it at
        /// all, and it alone decides whether the drift is bounded (→1) or a
        /// runaway ramp (→0). §3.7 reference 0.65, i.e. 35% of each pulse is
        /// kept. [E — sweep this]
        /// </summary>
        public float relaxationFraction = 0.65f;

        /// <summary>Relaxation time constant as a fraction of the wave period
        /// (0.3–1.0). [E]</summary>
        public float relaxTauFractionOfPeriod = 0.5f;

        /// <summary>Cap on free-running accumulation, metres: beyond this ARCore
        /// will have relocalised or declared tracking lost (§3.3). §3.7 1.5 m. [E]</summary>
        public float accumulationCapM = 1.5f;

        // =====================================================================
        // Relocalisation jumps — §3.5
        // =====================================================================

        /// <summary>Base jump rate under sustained corruption: ~1 per 14 s.
        /// §3.5 calls its own 1-per-5–30 s the weakest number in the document. [E]</summary>
        public float jumpRateHz = 0.07f;

        /// <summary>Lognormal magnitude fit to §3.5/§3.7's median 0.22 m,
        /// p90 0.55 m, p99 1.4 m: median = exp(mu), sigma = 0.78 reproduces
        /// p90 ≈ 0.60 and p99 ≈ 1.35. Magnitudes themselves are [M] (Here To
        /// Stay ranges); the lognormal shape is [E].</summary>
        public float jumpMedianM = 0.22f;
        public float jumpLogSigma = 0.78f;

        /// <summary>Share of the jump that corrects the accumulated ratchet
        /// (anti-parallel); the rest is isotropic "of similar size" (§3.5). [D]</summary>
        public float jumpDirectionBias = 0.5f;

        /// <summary>Accumulated ratchet at which jumps become most likely
        /// (§3.5: "correlate with accumulated ratchet crossing ~0.3–0.5 m"). [E]</summary>
        public float jumpTriggerM = 0.4f;

        // =====================================================================
        // GENUINE (non-wave) registration error — the channel a stabilizer
        // cannot legitimately cancel. §1.6, §1.7, §3.8 test 6.
        // =====================================================================
        //
        // WHY THIS EXISTS, and what it fixes. Before it, EVERY jump was by
        // construction a correction of the model's own phantom ratchet (see
        // dcErrorM below). The translation gate has already cancelled that ratchet
        // out of the camera's world position, so from the gate's point of view an
        // incoming jump was pure noise and discarding it was free — which made
        // `relocalizationJumpThreshold = 0` ("suppress every translation while
        // still") look like the largest single win in the whole harness while the
        // simulator had no mechanism able to charge it anything. That was a
        // MODELLING GAP, not a result.
        //
        // In reality ARCore's corrections are a MIXTURE. COM's mapper corrects the
        // total odometry error, and part of that error is nothing to do with the
        // waves: it is accumulated dead-reckoning / scale / heading error, and it
        // accrues in proportion to how far the camera has TRAVELLED. The evidence:
        //
        //   [M] Feigl et al. GRAPP 2020 measured AR systems in a large dynamic
        //       environment at "a scaling error of up to 14.4 cm/m ... quasi-
        //       directly proportional to the path length", ~17 m per 120 m
        //       travelled across ARCore/ARKit/HoloLens — i.e. ~14 % of path.
        //   [M] Here To Stay (arXiv:2109.14757) measured ARCore hologram drift of
        //       4.8 cm after "walk away ~7 m and return" (~14 m of path) in an
        //       ORDINARY static scene — i.e. ~0.34 % of path. Means across systems
        //       were far worse (43.2 cm) and the range reached 323 cm.
        //   [M] §3.8 test 6 ("dry-sand control") exists precisely because ARCore
        //       has a nonzero baseline drift that must be subtracted before
        //       anything is attributed to the waves. The model had no such channel.
        //
        // WHY IT IS STRUCTURALLY UNCANCELLABLE, which is the whole point. This
        // error accrues in proportion to REAL travel, and the gate is inert by
        // design while the IMU reports travel — so it goes straight into the
        // camera's world position, and when the user stops, the gate freezes it in
        // permanently. A jump against it is then the only thing that can heal it.
        // Suppress it and the error persists and keeps costing the metric for the
        // rest of the session. (Pinned by
        // ArSimTests.GenuineJump_Suppressed_IsNotFree — if that test ever passes
        // trivially, this channel has stopped working.)
        //
        // Set genuineDriftPerMeterPath = 0 to recover the pre-2026-09-28 model
        // bit-exactly.

        /// <summary>
        /// Genuine registration error accumulated per METRE of camera path, as a
        /// fraction of that path. Bracketed by the two measurements above:
        /// 0.0034 (ARCore, benign static scene) to 0.144 (dynamic environment).
        /// A surf scene IS a dynamic environment, so the default sits deliberately
        /// in the middle of that two-order-of-magnitude bracket rather than at
        /// either end. [E — bracketed by M]
        /// </summary>
        public float genuineDriftPerMeterPath = 0.03f;

        /// <summary>Azimuth diffusion of the genuine error's direction, degrees per
        /// √metre of path. 0 = a perfectly systematic bias (|error| ∝ path, which
        /// is what Feigl measured); large = a random walk (|error| ∝ √path). The
        /// default keeps it mostly systematic over a 50 m beach walk. [E]</summary>
        public float genuineDirDiffusionDegPerSqrtM = 15f;

        /// <summary>Cap on the genuine channel, metres — same reasoning as
        /// <see cref="accumulationCapM"/>: past this ARCore has relocalised or
        /// declared tracking lost, so there is nothing to model above it. [E]</summary>
        public float genuineErrorCapM = 2f;

        /// <summary>
        /// Share of relocalisation jumps that correct the GENUINE channel rather
        /// than the phantom ratchet. 0 = the pre-2026-09-28 behaviour (every jump
        /// is a phantom correction, which a translation gate can discard for free);
        /// 1 = every jump is a genuine correction.
        ///
        /// THE DOC SAYS NOTHING ABOUT THIS SPLIT. §3.5 already calls the jump RATE
        /// "the weakest number in this document" and does not address the mixture
        /// at all, so this is [E] with no measurement behind it whatsoever — it is
        /// the parameter to sweep, not a value to trust.
        ///
        /// A physically SELF-CONSISTENT value is not free, though, and the default
        /// is taken from it rather than guessed: the mapper corrects whatever the
        /// odometry got wrong, in proportion to how much of the error each channel
        /// holds, so <see cref="GenuineShareOfError"/> measures that apportionment
        /// per run. Session-time-weighted at the default
        /// <see cref="genuineDriftPerMeterPath"/> it comes out at 0.38-0.42
        /// (`make ar-sweep BLOCK=threshold`, genShare column), hence 0.40. It rises
        /// to ~0.53 at the top of the measured rate bracket and falls to ~0.14 at
        /// the bottom, so the sweep reports the crossover against the share MEASURED
        /// in each cell rather than against this one number.
        /// </summary>
        public float genuineJumpFraction = 0.40f;

        /// <summary>
        /// Share of the genuine error a single correction removes. Deliberately
        /// NOT an independent magnitude draw: a map correction moves the pose by
        /// roughly the accumulated inconsistency, so a 0.4 m jump against a 0.05 m
        /// error would be 0.35 m of pure fabrication. The magnitude of a genuine
        /// jump is therefore SET BY the error it corrects (spread by the same
        /// lognormal sigma the phantom branch uses). [E]
        /// </summary>
        public float genuineCorrectionFraction = 0.8f;

        // =====================================================================
        // Scale + rotation channels — §3.6
        // =====================================================================

        /// <summary>1-sigma multiplicative scale random walk per MINUTE (±2–10%,
        /// worst while perfectly still). §3.7 reference 0.05. [M upper bound]</summary>
        public float scaleDriftPerMinuteFrac = 0.05f;

        /// <summary>Yaw bias drift, deg/min. Measured gyro biases give 2.2
        /// (Pixel 7 Pro) to 22.7 (OnePlus 7 Pro) — a 10× device spread, so this
        /// is a sweep parameter. §3.7 reference 2.5. [D from M]</summary>
        public float yawDriftDegPerMin = 2.5f;

        /// <summary>Wave-frequency yaw wobble, degrees. 0.05–0.3 and NO MORE —
        /// the gyro forbids multi-degree attitude wobble (§3.6). [E]</summary>
        public float yawWobbleDeg = 0.15f;

        // =====================================================================
        // View factor — how much the near water is in frame. §3.0
        // =====================================================================

        /// <summary>Half the camera's horizontal FOV, degrees. Inside this the
        /// near water fills the frame. [D] typical phone HFOV ≈ 65°.</summary>
        const float HalfFovDeg = 33f;

        /// <summary>Degrees beyond the FOV edge over which the near water leaves
        /// the frame. [E]</summary>
        const float ViewFalloffDeg = 55f;

        /// <summary>Residual corruption with the surf fully out of frame: distant
        /// sea at the frame edge, wet sand, reflections. [E]</summary>
        const float ViewFloor = 0.12f;

        // =====================================================================
        // Presets — the §3.1 regime table
        // =====================================================================

        /// <summary>No moving water at all: a lagoon or an indoor sanity run.
        /// Only ARCore's own jitter floor remains.</summary>
        public static WaveDriftModel Calm(int seed = 1) => new WaveDriftModel
        {
            Name = "calm",
            seed = seed,
            amplitudeScale = 0f,
            jumpRateHz = 0f,
            boreCelerityMps = 0f,
            scaleDriftPerMinuteFrac = 0.005f,
            yawDriftDegPerMin = 0.3f,
        };

        /// <summary>§3.1 BENIGN: distant surf ≥30 m, plenty of dry sand low in
        /// the frame. Expect ~0.02–0.08 m pp.</summary>
        public static WaveDriftModel Benign(int seed = 2) => new WaveDriftModel
        {
            Name = "benign",
            seed = seed,
            waterRangeNearM = 30f,
            waterRangeFarM = 60f,
            staticRangeM = 4f,
            jumpRateHz = 0.02f,
        };

        /// <summary>§3.1 NOMINAL — the default. Surf 15–35 m with sand 3–8 m
        /// below it. Expect ~0.05–0.30 m pp. This is §3.7's reference set.</summary>
        public static WaveDriftModel Nominal(int seed = 3) => new WaveDriftModel
        {
            Name = "nominal",
            seed = seed,
        };

        /// <summary>§3.1 BAD: phone at the horizon with little sand in frame, or
        /// surf inside 15 m. Expect ~0.2–0.8 m pp.</summary>
        public static WaveDriftModel Bad(int seed = 4) => new WaveDriftModel
        {
            Name = "bad",
            seed = seed,
            waterRangeNearM = 10f,
            waterRangeFarM = 40f,
            staticRangeM = 20f,       // almost no near static structure in frame
            staticFeatureCount = 70f,
            waterFeatureCount = 180f,
            jumpRateHz = 0.10f,
        };

        /// <summary>§3.1 CATASTROPHIC: tilted down onto the swash at 2–5 m.
        /// α ≈ 78–87% — the water IS the consensus set, RANSAC picks it, and the
        /// tracker is measuring the wave instead of the phone. Expect 0.5–3 m pp
        /// and effectively lost tracking.</summary>
        public static WaveDriftModel Catastrophic(int seed = 5) => new WaveDriftModel
        {
            Name = "catastrophic",
            seed = seed,
            waterRangeNearM = 3.5f,
            waterRangeFarM = 10f,
            staticRangeM = 5f,
            waterFeatureCount = 140f,
            staticFeatureCount = 60f,
            jumpRateHz = 0.18f,
            accumulationCapM = 2.5f,
        };

        /// <summary>Mild optics but a tracker that keeps re-solving: isolates the
        /// gate's relocalisation branch. Not a §3.1 regime — a probe.</summary>
        public static WaveDriftModel RelocalizationProne(int seed = 6) => new WaveDriftModel
        {
            Name = "reloc-prone",
            seed = seed,
            waterRangeNearM = 26f,
            waterRangeFarM = 55f,
            staticRangeM = 5f,
            jumpRateHz = 0.16f,
            jumpMedianM = 0.5f,
            relaxationFraction = 0.9f,   // little ratchet; the jumps are the story
        };

        // =====================================================================
        // Derived quantities (public so the manifest can report them)
        // =====================================================================

        /// <summary>Instantaneous α = I_d/(I_s+I_d) with I ∝ Σ n/Z². Water
        /// features are taken uniform in range over [near, far], whose 1/Z²
        /// integral is exactly n/(Z_near·Z_far) — so the near edge dominates,
        /// which is the whole point (§1.2). [D]</summary>
        public float AlphaInstantaneous
        {
            get
            {
                float near = Mathf.Max(0.5f, waterRangeNearM);
                float far = Mathf.Max(near + 0.1f, waterRangeFarM);
                float id = waterFeatureCount / (near * far);
                float isx = staticFeatureCount / Mathf.Max(0.25f, staticRangeM * staticRangeM);
                return id / Mathf.Max(1e-6f, id + isx);
            }
        }

        /// <summary>Sustained α after robust rejection. Rejection works while the
        /// water is a minority and fails once it becomes the consensus set (§1.2's
        /// "close to them for B/D"). [E]</summary>
        public float AlphaEffective
        {
            get
            {
                float a = AlphaInstantaneous;
                float t = Mathf.Clamp01((a - RejectionFailsFromAlpha) /
                                        (RejectionFailsToAlpha - RejectionFailsFromAlpha));
                float s = t * t * (3f - 2f * t);
                return a * Mathf.Lerp(robustRejectionFactor, 1f, s);
            }
        }

        /// <summary>Peak-to-peak seaward amplitude for this geometry, metres.</summary>
        public float AmplitudePpSeawardM => Mathf.Min(MaxAmplitudePpM,
            amplitudeScale * ReferenceAmplitudePpM *
            Mathf.Pow(ReferenceRangeM / Mathf.Max(0.5f, waterRangeNearM), AmplitudeRangeExponent));

        /// <summary>
        /// Seaward displacement per bore sweep BEFORE the IMU ceiling, metres
        /// (§3.3 route 1: d = α_eff · c · t_coherent).
        /// </summary>
        public float PulseRawPerWaveM =>
            AlphaEffective * boreCelerityMps * coherentPhaseFractionOfPeriod * groundswellPeriodS;

        /// <summary>
        /// The IMU admissibility ceiling on the PULSE, metres — A_max = a_th/(2πf)²
        /// evaluated at the pulse train's own repetition rate f = 1/T (§1.3, §3.2).
        ///
        /// WHY THIS IS NEW, and why route 1 needed it. The comment on
        /// <see cref="AmplitudeRangeExponent"/> already said the quiet part out
        /// loud: route 1 (d = α·c·t) "would predict ~10 m of phantom translation.
        /// It does not happen because the IMU ceiling caps it." The ceiling was
        /// then applied only to the three OSCILLATOR bands and never to route 1's
        /// own output — so the model asserted the cap and did not enforce it, and
        /// `imuAccelBiasMps2` could only ever touch the oscillation. That is why
        /// the whole measured 11× device range used to move the composite by
        /// &lt;0.5 %: in the nominal regime NO band is anywhere near its ceiling, so
        /// the one term that does explode with geometry was left unconstrained.
        ///
        /// CHOICE OF EFFECTIVE FREQUENCY, stated plainly because it is a judgement
        /// call and it matters. Three readings are available:
        ///   (a) the doc's own sinusoid rule at the pulse train's repetition rate,
        ///       f = 1/T — what is implemented here;
        ///   (b) a corner-limited reading, a ≈ Δv/τ with Δv = A/t_coherent, giving
        ///       A_max = a_th·t_coherent·τ — about 5× looser at the defaults;
        ///   (c) the strict peak-|ẍ| of the model's ACTUAL waveform, which is a
        ///       ramp joined to an exponential relaxation and therefore only C0 —
        ///       the velocity corner makes that reading zero, i.e. it forbids the
        ///       pulse entirely, which is obviously wrong.
        /// (a) is the reading the doc uses everywhere else and the only one that is
        /// not an invention of this file, so it is what ships. (b) is the reading
        /// that would preserve the doc's tabulated CATASTROPHIC net drift; see the
        /// note on that regime in the harness report.
        ///
        /// NOT applied to: the accumulated DC ratchet (a constant-velocity phantom
        /// translation produces exactly the same accelerometer reading as standing
        /// still — §1.3's central point, so the accelerometer structurally cannot
        /// veto it); relocalisation jumps (a map correction is applied to the pose
        /// as a discontinuity by the MAPPER, outside the filter's IMU-veto path —
        /// §1.1's COM structure, and the reason the jumps exist as a separate
        /// channel at all); and the genuine channel (it accrues at walking pace in
        /// the same direction as real motion, so there is no phantom acceleration
        /// to object to).
        /// </summary>
        public float PulseCeilingM
        {
            get
            {
                if (groundswellPeriodS <= 0f || imuAccelBiasMps2 <= 0f ||
                    pulseCeilingFreqFactor <= 0f) return float.PositiveInfinity;
                float w = pulseCeilingFreqFactor * 2f * Mathf.PI / groundswellPeriodS;
                return imuAccelBiasMps2 / (w * w);
            }
        }

        /// <summary>
        /// Effective frequency at which <see cref="PulseCeilingM"/> is evaluated, as
        /// a multiple of the pulse train's repetition rate 1/T. Exposed because the
        /// choice is a genuine judgement call with a measurable consequence, not
        /// because it needs tuning:
        ///
        ///   1.0  (default) reading (a) above — the doc's own sinusoid rule at the
        ///        repetition rate. At the reference bias this caps the CATASTROPHIC
        ///        regime's pulse at 0.248 m/wave, i.e. a net ratchet of 0.087 m/wave,
        ///        which is ~3.4x BELOW §3.1's tabulated 0.3-2 m/wave for that
        ///        regime. Note §3.1's figure is itself route-1 arithmetic with no
        ///        ceiling applied, so the two cannot both be right; and the
        ///        UNCEILINGED model was far worse in the other direction — it put
        ///        tilt-down-swash's phantom excursion at 8.3 m against a tabulated
        ///        0.5-3 m peak-to-peak.
        ///   ~0.45 reading (b) — the corner-limited A_max = a_th·t_coherent·tau,
        ///        about 5x looser, which caps the catastrophic pulse at ~1.2 m/wave
        ///        (net 0.43 m/wave) and so lands back inside §3.1's band.
        /// </summary>
        public float pulseCeilingFreqFactor = 1f;

        /// <summary>Seaward displacement per bore sweep after the IMU ceiling, m.</summary>
        public float PulsePerWaveM => Mathf.Min(PulseRawPerWaveM, PulseCeilingM);

        /// <summary>True when the IMU ceiling is the binding constraint on the
        /// pulse — i.e. when this scene's device bias actually matters.</summary>
        public bool PulseIsImuLimited => PulseRawPerWaveM > PulseCeilingM;

        /// <summary>Net seaward drift kept per wave after relaxation. At the §3.7
        /// reference set this computes to ≈0.044 m against the doc's tabulated
        /// 0.05 — two independent routes agreeing to ~12%, which is the main
        /// reason to believe either.</summary>
        public float NetDriftPerWaveM => PulsePerWaveM * (1f - relaxationFraction);

        // =====================================================================
        // State
        // =====================================================================

        struct Component { public float freqHz, amp, phase; }

        readonly List<Component> seaward = new List<Component>();
        readonly List<Component> up = new List<Component>();
        readonly List<Component> along = new List<Component>();

        ArSimRng rng;

        /// <summary>
        /// The genuine channel gets its OWN streams so that adding it cannot move
        /// the phantom channel's realisation: with genuineDriftPerMeterPath = 0 the
        /// phantom draws come off <see cref="rng"/> in exactly the order they did
        /// before this channel existed, which is what makes "set the rate to 0 to
        /// recover the old model bit-exactly" a true statement rather than a hope.
        /// Same discipline as SyntheticImu's tremorDir block.
        /// </summary>
        ArSimRng genuineRng, jumpKindRng;

        float groupPhase, wobblePhase;
        bool built;

        /// <summary>
        /// The accumulated DC pose error, world metres. ONE accumulator, because
        /// a relocalisation jump is a CORRECTION of exactly this quantity (§3.5):
        /// the ratchet banks error into it and a jump takes error back out. An
        /// earlier version kept the ratchet and the jumps as two independent sums,
        /// which double-counted every jump — the ratchet shrank AND a permanent
        /// opposite offset grew, so a 90 s run ended up drifting SHOREWARD, the
        /// wrong way round.
        /// </summary>
        Vector3 dcErrorM;
        float wavePhase;            // position within the current bore cycle, s
        float pulseNow;             // current within-wave pulse contribution
        int jumpCount;
        int genuineJumpCount;
        float nextJumpCandidateT;   // Poisson inter-arrival, see the jump block
        float scaleFactor = 1f;
        float yawErrorDeg;
        float timeS;
        Vector3 zeroOffset;
        bool hasZero;

        /// <summary>The genuine channel: accumulated non-wave registration error
        /// and the azimuth it is accumulating along.</summary>
        Vector3 genuineErrorM;
        float genuineAzimuthDeg;
        double genuineShareSum;
        int genuineShareFrames;

        /// <summary>Realised jump magnitudes, split by channel. Kept because the
        /// only way an intermediate relocalizationJumpThreshold can beat both 0 and
        /// 0.35 m is if the two distributions are SEPARATED — so the harness has to
        /// report them rather than let the question be argued.</summary>
        readonly List<float> phantomJumpMags = new List<float>();
        readonly List<float> genuineJumpMags = new List<float>();

        public void Reset()
        {
            rng = new ArSimRng(seed);
            genuineRng = new ArSimRng(seed ^ 0x6E01);
            jumpKindRng = new ArSimRng(seed ^ 0x4A11);
            seaward.Clear(); up.Clear(); along.Clear();
            built = false;
            dcErrorM = Vector3.zero;
            wavePhase = 0f;
            pulseNow = 0f;
            jumpCount = 0;
            genuineJumpCount = 0;
            nextJumpCandidateT = float.NaN;   // drawn on the first Advance
            scaleFactor = 1f;
            yawErrorDeg = 0f;
            timeS = 0f;
            zeroOffset = Vector3.zero;
            hasZero = false;
            genuineErrorM = Vector3.zero;
            // Seeded start azimuth: a session's odometry bias has *a* direction,
            // and which one is not knowable. Drawn from the genuine stream so the
            // phantom realisation is untouched.
            genuineAzimuthDeg = genuineRng.Range(0f, 360f);
            genuineShareSum = 0.0;
            genuineShareFrames = 0;
            phantomJumpMags.Clear();
            genuineJumpMags.Clear();
        }

        void Build()
        {
            if (rng == null) rng = new ArSimRng(seed);
            float carrier = rng.Range(0f, Mathf.PI * 2f);
            groupPhase = rng.Range(0f, Mathf.PI * 2f);
            wobblePhase = rng.Range(0f, Mathf.PI * 2f);

            float peak = AmplitudePpSeawardM * 0.5f;
            BuildAxis(seaward, peak, carrier);
            BuildAxis(up, peak * UpRatio, carrier + 0.4f);
            BuildAxis(along, peak * AlongshoreRatio, carrier + 2.1f);
            built = true;
        }

        void BuildAxis(List<Component> into, float axisPeak, float carrier)
        {
            // Band weights normalised so the three bands sum to the axis peak
            // BEFORE the IMU ceiling is applied; the ceiling then removes what
            // the accelerometer would have vetoed, which is exactly how §3.2
            // wants it (and why long swell survives and short sea does not).
            float wSum = groundswellWeight + windSeaWeight + swashWeight;
            if (wSum <= 0f) return;

            AddBand(into, 1f / groundswellPeriodS, axisPeak * groundswellWeight / wSum, carrier);
            AddBand(into, 1f / windSeaPeriodS, axisPeak * windSeaWeight / wSum, carrier + 1.1f);
            AddBand(into, 1f / swashPeriodS, axisPeak * swashWeight / wSum, carrier + 2.3f);
        }

        void AddBand(List<Component> into, float centreHz, float bandPeak, float carrier)
        {
            if (bandPeak <= 0f || centreHz <= 0f) return;

            // §3.2: A_max = a_th/(2πf)². Applied literally, per band, on the
            // band's own peak amplitude. At nominal amplitudes this is inactive
            // (the visual forcing asks for less than the IMU would tolerate); it
            // becomes the binding constraint in the bad/catastrophic regimes,
            // which is precisely where the 17× device spread shows up.
            float w = 2f * Mathf.PI * centreHz;
            float aMax = imuAccelBiasMps2 / (w * w);
            float admitted = Mathf.Min(bandPeak, aMax);
            if (admitted <= 0f) return;

            // Narrowband realisation: ComponentsPerBand random-phase sinusoids
            // spread ±BandHalfWidth around the centre, amplitudes tapered so the
            // centre dominates. Phases are part-locked to the single wave train.
            float norm = 0f;
            var amps = new float[ComponentsPerBand];
            for (int i = 0; i < ComponentsPerBand; i++)
            {
                float u = ComponentsPerBand == 1 ? 0f : (2f * i / (ComponentsPerBand - 1f) - 1f);
                amps[i] = Mathf.Exp(-2f * u * u);
                norm += amps[i];
            }

            for (int i = 0; i < ComponentsPerBand; i++)
            {
                float u = ComponentsPerBand == 1 ? 0f : (2f * i / (ComponentsPerBand - 1f) - 1f);
                float f = centreHz * (1f + BandHalfWidth * u);
                if (f > MaxSimulatedHz) continue;   // §3.2 hard ceiling
                float randomPhase = rng.Range(0f, Mathf.PI * 2f);
                into.Add(new Component
                {
                    freqHz = f,
                    amp = admitted * amps[i] / norm,
                    phase = coherentPhaseFraction * carrier + (1f - coherentPhaseFraction) * randomPhase,
                });
            }
        }

        static float Evaluate(List<Component> comps, float t)
        {
            float v = 0f;
            for (int i = 0; i < comps.Count; i++)
                v += comps[i].amp * Mathf.Sin(2f * Mathf.PI * comps[i].freqHz * t + comps[i].phase);
            return v;
        }

        /// <summary>
        /// Advance one frame. Call exactly once per frame, in increasing time.
        /// </summary>
        /// <param name="dt">Frame delta, seconds.</param>
        /// <param name="cameraYawDeg">Where the phone is pointing (Unity frame,
        /// degrees clockwise from +Z). Only the AMPLITUDE depends on it.</param>
        /// <param name="groundTruthDeltaM">The camera's TRUE displacement since the
        /// previous frame. Drives the genuine channel only (odometry error grows
        /// with path length, §1.6). Defaulted so a caller that only wants the
        /// phantom channel — every existing unit test — needs no change.</param>
        public PhantomState Advance(float dt, float cameraYawDeg, Vector3 groundTruthDeltaM = default)
        {
            if (!built) Build();
            timeS += dt;

            // ---- genuine (non-wave) registration error ---------------------
            // Accumulates with real TRAVEL, which is exactly when a translation
            // gate is inert, which is what makes it uncancellable. See the
            // genuine-channel block above for the evidence and the mechanism.
            if (genuineDriftPerMeterPath > 0f)
            {
                float step = groundTruthDeltaM.magnitude;
                if (step > 1e-7f)
                {
                    genuineAzimuthDeg += genuineDirDiffusionDegPerSqrtM *
                                         Mathf.Sqrt(step) * genuineRng.NextGaussian();
                    float ga = genuineAzimuthDeg * Mathf.Deg2Rad;
                    var gDir = new Vector3(Mathf.Sin(ga), 0f, Mathf.Cos(ga));
                    genuineErrorM = Vector3.ClampMagnitude(
                        genuineErrorM + gDir * (genuineDriftPerMeterPath * step), genuineErrorCapM);
                }
            }

            // ---- world-fixed basis (§3.0) --------------------------------
            float az = seawardAzimuthDeg * Mathf.Deg2Rad;
            var sHat = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
            var lHat = new Vector3(Mathf.Cos(az), 0f, -Mathf.Sin(az));

            // ---- view factor: panning lowers amplitude, never direction ----
            float off = Mathf.Abs(Mathf.DeltaAngle(cameraYawDeg, seawardAzimuthDeg));
            float x = Mathf.Clamp01((off - HalfFovDeg) / ViewFalloffDeg);
            float smooth = 1f - x * x * (3f - 2f * x);
            float view = ViewFloor + (1f - ViewFloor) * smooth;

            // ---- group envelope amplitude-modulates every band (§3.2) ------
            float envelope = 1f + groupDepth * Mathf.Sin(2f * Mathf.PI * timeS / Mathf.Max(1f, groupPeriodS) + groupPhase);
            envelope = Mathf.Max(0f, envelope);

            float gain = view * envelope;
            Vector3 oscillation =
                sHat * (Evaluate(seaward, timeS) * gain) +
                lHat * (Evaluate(along, timeS) * gain) +
                Vector3.up * (Evaluate(up, timeS) * gain);

            // ---- ratchet: asymmetric sawtooth, seaward (§3.3) --------------
            // Ramp seaward for the coherent fraction of the wave, then relax,
            // keeping (1 - relaxationFraction) of the pulse. The ACCUMULATED part
            // is not scaled by the view factor: turning away stops new forcing
            // but does not undo error the tracker has already baked in.
            if (boreCelerityMps > 0f && groundswellPeriodS > 0f)
            {
                float T = groundswellPeriodS;
                float tc = Mathf.Max(0.01f, coherentPhaseFractionOfPeriod * T);
                float tau = Mathf.Max(0.05f, relaxTauFractionOfPeriod * T);
                float pulse = PulsePerWaveM * view;

                wavePhase += dt;
                if (wavePhase >= T)
                {
                    wavePhase -= T;
                    // Bank the unrecovered part of the pulse just completed.
                    dcErrorM = Vector3.ClampMagnitude(
                        dcErrorM + sHat * (pulseNow * (1f - relaxationFraction)), accumulationCapM);
                    pulseNow = 0f;
                }

                if (wavePhase < tc)
                {
                    pulseNow = pulse * (wavePhase / tc);
                }
                else
                {
                    float decayed = 1f - relaxationFraction *
                        (1f - Mathf.Exp(-(wavePhase - tc) / tau));
                    pulseNow = pulse * decayed;
                }
            }

            Vector3 ratchetVec = dcErrorM + sHat * pulseNow;

            // ---- relocalisation jumps (§3.5) -------------------------------
            bool jumped = false;
            bool jumpGenuine = false;
            float jumpMag = 0f;
            if (jumpRateHz > 0f)
            {
                // POISSON THINNING, not a per-frame Bernoulli trial. Candidate
                // times come from an exponential inter-arrival at the MAXIMUM
                // rate and are then accepted with probability rate(t)/rateMax —
                // which is the textbook way to simulate a time-varying Poisson
                // process. The per-frame coin flip it replaces was statistically
                // equivalent in the limit but spent one RNG draw every frame and
                // made the arrival times depend on the frame rate.
                if (float.IsNaN(nextJumpCandidateT))
                    nextJumpCandidateT = -Mathf.Log(1f - rng.Next01()) / jumpRateHz;

                // Rate rises as the accumulated error approaches the trigger
                // (§3.5: jumps correlate with the ratchet crossing ~0.3-0.5 m).
                // TOTAL error, not just the ratchet: the mapper reacts to the
                // inconsistency it can see, and it cannot tell the two channels
                // apart. With genuineDriftPerMeterPath = 0 this is bit-identical to
                // the ratchet-only form it replaces.
                float load = Mathf.Clamp01((dcErrorM.magnitude + genuineErrorM.magnitude) /
                                           Mathf.Max(0.01f, jumpTriggerM));
                float acceptance = 0.3f + 0.7f * load;

                if (timeS >= nextJumpCandidateT)
                {
                    nextJumpCandidateT += -Mathf.Log(1f - rng.Next01()) / jumpRateHz;
                    jumped = rng.Next01() < acceptance;
                }

                if (jumped)
                {
                    // WHICH CHANNEL does this correction address? The mapper cannot
                    // tell them apart, but the harness must, because a stabilizer
                    // that gates on jump SIZE can only ever affect one of them: a
                    // phantom correction is redundant with what the gate already
                    // did, a genuine one is the only healing mechanism there is.
                    jumpGenuine = genuineJumpFraction > 0f &&
                                  genuineErrorM.sqrMagnitude > 1e-8f &&
                                  jumpKindRng.Next01() < genuineJumpFraction;

                    if (jumpGenuine)
                    {
                        // Magnitude SET BY the error corrected, not drawn
                        // independently — see genuineCorrectionFraction. Half the
                        // phantom branch's sigma, because a correction's size is
                        // anchored to a measured inconsistency rather than free.
                        float frac = Mathf.Clamp(genuineCorrectionFraction *
                            Mathf.Exp(jumpLogSigma * 0.5f * genuineRng.NextGaussian()), 0.05f, 1.2f);
                        Vector3 jump = -frac * genuineErrorM;
                        jumpMag = jump.magnitude;
                        genuineErrorM = Vector3.ClampMagnitude(genuineErrorM + jump, genuineErrorCapM);
                        genuineJumpMags.Add(jumpMag);
                        genuineJumpCount++;
                    }
                    else
                    {
                        jumpMag = jumpMedianM * Mathf.Exp(jumpLogSigma * rng.NextGaussian());

                        Vector3 ratchetDir = ratchetVec.sqrMagnitude > 1e-8f
                            ? ratchetVec.normalized : sHat;
                        float ang = rng.Range(0f, Mathf.PI * 2f);
                        var isotropic = new Vector3(Mathf.Cos(ang), rng.Range(-0.3f, 0.3f), Mathf.Sin(ang)).normalized;

                        Vector3 jump = jumpMag * (
                            -jumpDirectionBias * ratchetDir +
                            (1f - jumpDirectionBias) * isotropic);

                        // A jump IS the correction: it moves the accumulated error,
                        // it is not a second offset layered on top of it.
                        dcErrorM = Vector3.ClampMagnitude(dcErrorM + jump, accumulationCapM);
                        phantomJumpMags.Add(jumpMag);
                    }
                    jumpCount++;
                }
            }

            // ---- scale + rotation channels (§3.6) --------------------------
            if (scaleDriftPerMinuteFrac > 0f && dt > 0f)
            {
                float sigma = scaleDriftPerMinuteFrac / Mathf.Sqrt(60f);
                scaleFactor *= Mathf.Exp(sigma * Mathf.Sqrt(dt) * rng.NextGaussian());
            }
            yawErrorDeg += yawDriftDegPerMin / 60f * dt;
            float yawTotal = yawErrorDeg +
                yawWobbleDeg * Mathf.Sin(2f * Mathf.PI * timeS / Mathf.Max(1f, groundswellPeriodS) + wobblePhase);

            // Time-average of how much of the total pose error the genuine channel
            // holds. This is the SELF-CONSISTENT value of genuineJumpFraction: a
            // mapper correcting the inconsistency it can see would split its
            // corrections between the channels in roughly this ratio. Reported so
            // the crossover can be located against something other than a guess.
            float ratchetMag = ratchetVec.magnitude;
            float genuineMag = genuineErrorM.magnitude;
            // 1 cm floor: below that the run has accumulated nothing worth
            // apportioning, and a ratio of two near-zeros is noise reported as a
            // number (the calm presets used to come out at a confident 1.00).
            if (ratchetMag + genuineMag > 0.01f)
            {
                genuineShareSum += genuineMag / (ratchetMag + genuineMag);
                genuineShareFrames++;
            }

            Vector3 total = oscillation + ratchetVec + genuineErrorM;
            if (!hasZero) { zeroOffset = total; hasZero = true; }

            return new PhantomState
            {
                PositionErrorM = total - zeroOffset,
                ScaleFactor = scaleFactor,
                YawErrorDeg = yawTotal,
                JumpedThisFrame = jumped,
                JumpMagnitudeM = jumpMag,
                JumpWasGenuine = jumpGenuine,
                ViewFactor = view,
                RatchetM = ratchetVec,
                GenuineErrorM = genuineErrorM,
            };
        }

        /// <summary>Relocalisation jumps fired so far this run.</summary>
        public int JumpCount => jumpCount;

        /// <summary>Of those, how many corrected the GENUINE channel.</summary>
        public int GenuineJumpCount => genuineJumpCount;

        /// <summary>
        /// Time-averaged |genuine| / (|genuine| + |ratchet|) over the run — the
        /// share of the total accumulated pose error the genuine channel holds, and
        /// therefore the physically self-consistent value of
        /// <see cref="genuineJumpFraction"/> for this scenario. NaN if the run
        /// accumulated no error at all.
        /// </summary>
        public float GenuineShareOfError =>
            genuineShareFrames > 0 ? (float)(genuineShareSum / genuineShareFrames) : float.NaN;

        /// <summary>Median magnitude of the PHANTOM-correcting jumps this run, m.
        /// NaN if none fired.</summary>
        public float PhantomJumpMedianM => Median(phantomJumpMags);

        /// <summary>Median magnitude of the GENUINE-correcting jumps this run, m.
        /// NaN if none fired.</summary>
        public float GenuineJumpMedianM => Median(genuineJumpMags);

        static float Median(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            var copy = new List<float>(v);
            copy.Sort();
            return copy[copy.Count / 2];
        }

        public string Describe() =>
            $"{Name}(seaward={seawardAzimuthDeg:F0}deg nearWater={waterRangeNearM:F1}m far={waterRangeFarM:F0}m " +
            $"static={staticRangeM:F1}m alpha={AlphaInstantaneous:P1}->{AlphaEffective:P1} " +
            $"amp_pp={AmplitudePpSeawardM:F3}m [1:{UpRatio:F2}:{AlongshoreRatio:F2}] " +
            $"bands=Tp{groundswellPeriodS:F0}/{windSeaPeriodS:F0}/{swashPeriodS:F0}s group={groupPeriodS:F0}s " +
            $"aTh={imuAccelBiasMps2:F3}m/s2 relax={relaxationFraction:F2} " +
            $"pulse={PulseRawPerWaveM:F3}->{PulsePerWaveM:F3}m/wave(fc x{pulseCeilingFreqFactor:F2})" +
            $"{(PulseIsImuLimited ? "[IMU-LIMITED]" : "")} " +
            $"netDrift={NetDriftPerWaveM:F3}m/wave cap={accumulationCapM:F1}m " +
            $"jump={jumpRateHz:F2}Hz median={jumpMedianM:F2}m trigger={jumpTriggerM:F2}m " +
            $"genuine={genuineDriftPerMeterPath:P1}/m frac={genuineJumpFraction:F2} " +
            $"scale={scaleDriftPerMinuteFrac:P0}/min " +
            $"yaw={yawDriftDegPerMin:F1}deg/min seed={seed})";
    }
}
