using System;
using System.Collections.Generic;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// What a run of one scenario × one stabilizer produced.
    ///
    /// The headline numbers are deliberately PERCEPTUAL, not raw pose error. A
    /// user standing at the water does not experience "12 cm of camera position
    /// error"; they experience the shark they are looking at sliding sideways and
    /// jittering. So the primary metrics are computed on the APPARENT BEARING of
    /// a virtual animal anchored ~15 m out at session start:
    ///
    ///   trueBearing     = direction from the ground-truth camera to the anchor
    ///   believedBearing = direction from the BELIEVED camera pose to the anchor
    ///
    /// The anchor is fixed in world space (that is what "anchored" means), the
    /// camera's ROTATION is assumed correct (VIO orientation is well-conditioned
    /// and neither the gate nor the drift correction ever touches it), so the
    /// whole error is the camera's world POSITION error projected to an angle.
    ///
    /// driftDeg and jitterDegPerSec are separated on purpose: a slow 3° wander
    /// over a minute is barely noticeable, while 3° of oscillation at wave period
    /// is the thing that makes the app look broken. Collapsing them into one
    /// number would hide exactly the distinction the stabilizer is supposed to
    /// make.
    /// </summary>
    internal sealed class ArSimResult
    {
        public string ScenarioId;
        public string StabilizerName;
        public SessionProfile Profile;
        public float RateHz;
        public float DurationS;
        public int Frames;

        /// <summary>Mean |apparent bearing error| of the anchored animal, degrees.</summary>
        public float DriftDegMean;

        /// <summary>95th percentile of the same, degrees.</summary>
        public float DriftDegP95;

        /// <summary>The bearing error expressed as lateral slide at the anchor
        /// distance (arc length, metres): how far the animal appears to move.</summary>
        public float SlideMeanM;
        public float SlideP95M;

        /// <summary>Mean | p95 fractional error in the anchor's APPARENT RANGE.
        /// 0.10 means the animal looks 10% nearer or further than it is — which
        /// is how the seaward phantom drift actually shows up when the user is
        /// looking seaward (doc §3.3: content "creeps toward the viewer and
        /// grows"). A bearing metric alone is blind to it.</summary>
        public float RangeErrFracMean;
        public float RangeErrFracP95;

        /// <summary>RMS of d(bearing error)/dt, deg/s — the OSCILLATION, which is
        /// what a user actually complains about. Measured over a FIXED TIME
        /// window rather than per frame, so 30 Hz and 60 Hz runs are comparable
        /// (a per-frame difference of the same signal doubles at double the
        /// frame rate purely as a sampling artefact).</summary>
        public float JitterDegPerSec;

        /// <summary>Camera world-position error vs ground truth, metres.</summary>
        public float PosErrRmsM;
        public float PosErrMaxM;

        /// <summary>Recovered motion ÷ true motion over frames where the user is
        /// genuinely travelling. 1.0 = perfect; &lt;1 = the stabilizer is eating
        /// real motion (a gate that froze the world would score ~0 here while
        /// looking flawless on drift); &gt;1 = it is adding phantom motion.
        /// NaN when the scenario contains no walking.</summary>
        public float RealMotionFidelity;

        /// <summary>Total distance the stabilizer moved the world (gate
        /// counter-shift + drift correction), metres of path.</summary>
        public float SuppressedM;

        /// <summary>The part of that the GATE contributed, for stabilizers that
        /// have one. SuppressedM - GateSuppressedM is the GPS drift correction,
        /// and separating them is usually the whole diagnosis. NaN when the
        /// stabilizer does not report it.</summary>
        public float GateSuppressedM = float.NaN;

        /// <summary>The part of that applied while the device was GENUINELY
        /// moving — i.e. corrections that fought real motion.</summary>
        public float FalseSuppressionM;

        /// <summary>Fraction of frames the stillness detector reported still.</summary>
        public float StillFraction;

        /// <summary>Diagnostics for the report footnotes.</summary>
        public int FixCount;

        /// <summary>Relocalisation jumps the wave model fired during the run.
        /// Worth watching: the shipped gate's relocalizationJumpThreshold of
        /// 0.35 m sits squarely inside the doc's jump-magnitude distribution
        /// (median 0.22, p90 0.55), so it necessarily both lets real
        /// relocalisations through AND suppresses phantom ones.</summary>
        public int JumpCount;

        /// <summary>Of those, how many were corrections of the GENUINE (non-wave)
        /// error channel rather than of the phantom ratchet. A phantom correction
        /// is redundant with what a translation gate already did; a genuine one is
        /// the only mechanism that can heal error the gate has frozen in. See the
        /// genuine-channel block in <c>WaveDriftModel</c>.</summary>
        public int GenuineJumpCount;

        /// <summary>Time-averaged share of the accumulated pose error held by the
        /// genuine channel — the physically self-consistent value of
        /// <c>WaveDriftModel.genuineJumpFraction</c> for this scenario.</summary>
        public float GenuineShareOfError = float.NaN;

        /// <summary>Median realised magnitude of each jump channel, metres. The
        /// question "can an intermediate relocalizationJumpThreshold pass the
        /// genuine corrections while still absorbing the phantom ones?" has no
        /// answer unless these two are separated, so they are measured.</summary>
        public float PhantomJumpMedianM = float.NaN;
        public float GenuineJumpMedianM = float.NaN;

        public bool HeadingRefined;
        public float FinalHeadingErrorDeg = float.NaN;

        /// <summary>Seconds from the last genuinely-walking frame until the
        /// stabilizer reported itself suppressing again. NaN when the scenario never
        /// stops walking, or when the stabilizer never re-acquires. A slow
        /// re-acquisition leaves the world unlocked exactly when the user has
        /// stopped to look at something.</summary>
        public float SuppressionReacquireS = float.NaN;

        /// <summary>Real steps in the scenario, for scoring a step detector's count
        /// against (the doc's SS5.1: step COUNT degrades far less than step LENGTH on
        /// sand, so the count is the part worth trusting).</summary>
        public float TrueStepCount;

        /// <summary>One line of end-of-run stabilizer state for the diagnostics
        /// table; whatever <c>IArStabilizerDiagnostics.Summarize()</c> returned.</summary>
        public string StabilizerSummary = "";

        /// <summary>
        /// Single "how bad was this" number, in equivalent degrees of apparent
        /// slide. Lower is better. See <see cref="ArSimScoring"/> for the weights.
        /// </summary>
        public float PerceptualCost;

        /// <summary>How many seeded repeats this row is the mean of.</summary>
        public int Repeats = 1;

        /// <summary>
        /// Mean of several seeded repeats of the same scenario.
        ///
        /// Needed because relocalisation jumps are a Poisson process at ~0.07 Hz,
        /// so a 90 s run sees only 2-6 of them and they dominate the error. A
        /// single seed therefore has enough realisation variance to reorder the
        /// SCENARIOS (one run drew 9 jumps, another 0). Comparing STABILIZERS was
        /// never affected — both always see a bit-identical world — but comparing
        /// scenarios was, so the report averages repeats.
        ///
        /// Note the percentiles are means-of-percentiles, not the percentile of
        /// the pooled sample. That is the ordinary way to summarise repeated runs
        /// and is what the column headers mean.
        /// </summary>
        public static ArSimResult Average(IReadOnlyList<ArSimResult> runs)
        {
            if (runs.Count == 1) return runs[0];
            var first = runs[0];
            var avg = new ArSimResult
            {
                ScenarioId = first.ScenarioId,
                StabilizerName = first.StabilizerName,
                Profile = first.Profile,
                RateHz = first.RateHz,
                DurationS = first.DurationS,
                Repeats = runs.Count,
                TrueStepCount = first.TrueStepCount,
                StabilizerSummary = first.StabilizerSummary,
            };

            float n = runs.Count;
            int fidelityCount = 0, headingCount = 0, gateCount = 0, reacquireCount = 0, genuineShareCount = 0, phantomMedCount = 0, genuineMedCount = 0;
            // These two default to NaN on a fresh result, and NaN += x stays NaN,
            // so they have to be zeroed before accumulating.
            avg.GateSuppressedM = 0f;
            avg.FinalHeadingErrorDeg = 0f;
            avg.SuppressionReacquireS = 0f;
            avg.GenuineShareOfError = 0f;
            avg.PhantomJumpMedianM = 0f;
            avg.GenuineJumpMedianM = 0f;
            foreach (var r in runs)
            {
                avg.Frames += r.Frames;
                avg.DriftDegMean += r.DriftDegMean;
                avg.DriftDegP95 += r.DriftDegP95;
                avg.SlideMeanM += r.SlideMeanM;
                avg.SlideP95M += r.SlideP95M;
                avg.RangeErrFracMean += r.RangeErrFracMean;
                avg.RangeErrFracP95 += r.RangeErrFracP95;
                avg.JitterDegPerSec += r.JitterDegPerSec;
                avg.PosErrRmsM += r.PosErrRmsM;
                avg.PosErrMaxM += r.PosErrMaxM;
                avg.SuppressedM += r.SuppressedM;
                avg.FalseSuppressionM += r.FalseSuppressionM;
                avg.StillFraction += r.StillFraction;
                avg.FixCount += r.FixCount;
                avg.JumpCount += r.JumpCount;
                avg.GenuineJumpCount += r.GenuineJumpCount;
                avg.PerceptualCost += r.PerceptualCost;
                if (!float.IsNaN(r.GenuineShareOfError)) { avg.GenuineShareOfError += r.GenuineShareOfError; genuineShareCount++; }
                if (!float.IsNaN(r.PhantomJumpMedianM)) { avg.PhantomJumpMedianM += r.PhantomJumpMedianM; phantomMedCount++; }
                if (!float.IsNaN(r.GenuineJumpMedianM)) { avg.GenuineJumpMedianM += r.GenuineJumpMedianM; genuineMedCount++; }
                if (!float.IsNaN(r.RealMotionFidelity)) { avg.RealMotionFidelity += r.RealMotionFidelity; fidelityCount++; }
                if (!float.IsNaN(r.FinalHeadingErrorDeg)) { avg.FinalHeadingErrorDeg += r.FinalHeadingErrorDeg; headingCount++; }
                if (!float.IsNaN(r.GateSuppressedM)) { avg.GateSuppressedM += r.GateSuppressedM; gateCount++; }
                if (!float.IsNaN(r.SuppressionReacquireS)) { avg.SuppressionReacquireS += r.SuppressionReacquireS; reacquireCount++; }
                avg.HeadingRefined |= r.HeadingRefined;
            }

            avg.Frames = Mathf.RoundToInt(avg.Frames / n);
            avg.DriftDegMean /= n; avg.DriftDegP95 /= n;
            avg.SlideMeanM /= n; avg.SlideP95M /= n;
            avg.RangeErrFracMean /= n; avg.RangeErrFracP95 /= n;
            avg.JitterDegPerSec /= n; avg.PosErrRmsM /= n; avg.PosErrMaxM /= n;
            avg.SuppressedM /= n; avg.FalseSuppressionM /= n;
            avg.StillFraction /= n; avg.PerceptualCost /= n;
            avg.FixCount = Mathf.RoundToInt(avg.FixCount / n);
            avg.JumpCount = Mathf.RoundToInt(avg.JumpCount / n);
            avg.GenuineJumpCount = Mathf.RoundToInt(avg.GenuineJumpCount / n);
            avg.GenuineShareOfError = genuineShareCount > 0
                ? avg.GenuineShareOfError / genuineShareCount : float.NaN;
            avg.PhantomJumpMedianM = phantomMedCount > 0 ? avg.PhantomJumpMedianM / phantomMedCount : float.NaN;
            avg.GenuineJumpMedianM = genuineMedCount > 0 ? avg.GenuineJumpMedianM / genuineMedCount : float.NaN;
            avg.RealMotionFidelity = fidelityCount > 0 ? avg.RealMotionFidelity / fidelityCount : float.NaN;
            avg.FinalHeadingErrorDeg = headingCount > 0 ? avg.FinalHeadingErrorDeg / headingCount : float.NaN;
            avg.GateSuppressedM = gateCount > 0 ? avg.GateSuppressedM / gateCount : float.NaN;
            avg.SuppressionReacquireS = reacquireCount > 0 ? avg.SuppressionReacquireS / reacquireCount : float.NaN;
            return avg;
        }
    }

    /// <summary>
    /// Turns a run's metrics into one comparable number, and combines runs into
    /// the time-weighted composite.
    /// </summary>
    internal static class ArSimScoring
    {
        /// <summary>
        /// How many seconds of oscillation to charge at the same rate as a
        /// standing offset. Oscillation is far more objectionable than slow
        /// drift, and this is the exchange rate between deg/s and deg.
        /// PROVISIONAL: 4 s is roughly how long a user holds a look at one animal.
        /// </summary>
        public const float JitterWeightSeconds = 4f;

        /// <summary>
        /// Flat charge, in equivalent degrees, for 100% loss (or 100% excess) of
        /// real motion. A stabilizer that wins by freezing the world must lose
        /// here, so this is large on purpose. PROVISIONAL.
        /// </summary>
        public const float FidelityPenaltyDeg = 30f;

        /// <summary>
        /// Exchange rate from fractional range error to equivalent degrees: the
        /// angular size of the anchored animal. A 3 m shark at the 15 m anchor
        /// distance subtends 2·atan(1.5/15) ≈ 11.4°, so a 10% range error changes
        /// its apparent size by ~1.1° — that is the charge. PROVISIONAL (the
        /// animal's assumed length is the soft part).
        /// </summary>
        public const float RangeWeightDeg = 11.4f;

        public static float Cost(ArSimResult r)
        {
            float cost = r.DriftDegP95
                       + JitterWeightSeconds * r.JitterDegPerSec
                       + RangeWeightDeg * r.RangeErrFracP95;
            if (!float.IsNaN(r.RealMotionFidelity))
                cost += FidelityPenaltyDeg * Mathf.Abs(1f - r.RealMotionFidelity);
            return cost;
        }

        /// <summary>
        /// The time-weighted composite for one stabilizer: the mean cost within
        /// each session profile, weighted by that profile's share of session time.
        /// Profiles with no scenarios drop out and the remaining weights are
        /// renormalised, so the number stays comparable if a scenario is filtered.
        /// </summary>
        public static float Composite(IEnumerable<ArSimResult> runs)
        {
            var sum = new Dictionary<SessionProfile, float>();
            var count = new Dictionary<SessionProfile, int>();
            foreach (var r in runs)
            {
                if (r.Profile == SessionProfile.Excluded) continue;
                sum.TryGetValue(r.Profile, out float s);
                count.TryGetValue(r.Profile, out int c);
                sum[r.Profile] = s + r.PerceptualCost;
                count[r.Profile] = c + 1;
            }

            float weighted = 0f, weightTotal = 0f;
            foreach (var kv in count)
            {
                float w = SessionTimeWeights.For(kv.Key);
                if (w <= 0f || kv.Value == 0) continue;
                weighted += w * (sum[kv.Key] / kv.Value);
                weightTotal += w;
            }
            return weightTotal > 0f ? weighted / weightTotal : float.NaN;
        }
    }

    /// <summary>Accumulates the per-frame series into an <see cref="ArSimResult"/>.</summary>
    internal sealed class ArSimMetricsAccumulator
    {
        /// <summary>Where the virtual animal is anchored, metres out from the
        /// camera at session start. 15 m is a typical "shark in the water in
        /// front of you" distance for this app.</summary>
        public const float AnchorDistanceM = 15f;

        /// <summary>
        /// Window over which the bearing-error RATE is measured, seconds. A
        /// per-frame difference would make the metric frame-rate dependent (the
        /// 60 Hz diagnostic run would report ~2× the jitter for an identical
        /// physical signal), and 100 ms is roughly the timescale at which a
        /// sliding overlay reads as "jitter" rather than "drift". PROVISIONAL.
        /// </summary>
        public const float JitterWindowS = 0.1f;

        readonly List<float> absBearing = new List<float>();
        readonly List<float> signedBearing = new List<float>();
        readonly List<float> absRange = new List<float>();

        double posErrSq;
        float posErrMax;
        int frames;
        int stillFrames;

        float sampleDt;

        double trueWalkPath;
        double believedWalkPath;

        double suppressed;
        double falseSuppressed;

        public void Add(float dt, float bearingErrDeg, float rangeErrFrac, float posErrM, bool isStill,
                        Vector3 trueDelta, Vector3 believedDelta, Vector3 originShift,
                        bool walkingNow, bool movingNow)
        {
            frames++;
            sampleDt = dt;
            absBearing.Add(Mathf.Abs(bearingErrDeg));
            signedBearing.Add(bearingErrDeg);
            absRange.Add(Mathf.Abs(rangeErrFrac));
            posErrSq += (double)posErrM * posErrM;
            if (posErrM > posErrMax) posErrMax = posErrM;
            if (isStill) stillFrames++;

            if (walkingNow)
            {
                float trueLen = trueDelta.magnitude;
                if (trueLen > 1e-6f)
                {
                    trueWalkPath += trueLen;
                    // Project the believed motion onto the true direction: a
                    // stabilizer that halves the step scores 0.5, one that adds
                    // phantom motion in the same direction scores > 1, and one
                    // that moves sideways contributes ~0.
                    believedWalkPath += Vector3.Dot(believedDelta, trueDelta / trueLen);
                }
            }

            float shift = originShift.magnitude;
            suppressed += shift;
            if (movingNow) falseSuppressed += shift;
        }

        public void Fill(ArSimResult r)
        {
            r.Frames = frames;

            // Rate over a fixed TIME window, so the metric does not change with
            // the frame rate. Done here rather than in Add() because the lookback
            // needs the signed series.
            int lag = Mathf.Max(1, Mathf.RoundToInt(JitterWindowS / Mathf.Max(1e-4f, sampleDt)));
            float window = lag * sampleDt;
            double sq = 0.0; int rateCount = 0;
            for (int i = lag; i < signedBearing.Count; i++)
            {
                double rate = (signedBearing[i] - signedBearing[i - lag]) / window;
                sq += rate * rate;
                rateCount++;
            }
            r.JitterDegPerSec = rateCount > 0 ? (float)Math.Sqrt(sq / rateCount) : 0f;

            absBearing.Sort();
            r.DriftDegMean = Mean(absBearing);
            r.DriftDegP95 = Percentile(absBearing, 0.95f);
            r.SlideMeanM = r.DriftDegMean * Mathf.Deg2Rad * AnchorDistanceM;
            r.SlideP95M = r.DriftDegP95 * Mathf.Deg2Rad * AnchorDistanceM;

            absRange.Sort();
            r.RangeErrFracMean = Mean(absRange);
            r.RangeErrFracP95 = Percentile(absRange, 0.95f);

            r.PosErrRmsM = frames > 0 ? (float)Math.Sqrt(posErrSq / frames) : 0f;
            r.PosErrMaxM = posErrMax;
            r.StillFraction = frames > 0 ? stillFrames / (float)frames : 0f;

            r.RealMotionFidelity = trueWalkPath > 0.5
                ? (float)(believedWalkPath / trueWalkPath)
                : float.NaN;

            r.SuppressedM = (float)suppressed;
            r.FalseSuppressionM = (float)falseSuppressed;
            r.PerceptualCost = ArSimScoring.Cost(r);
        }

        static float Mean(List<float> v)
        {
            if (v.Count == 0) return 0f;
            double s = 0.0;
            foreach (var x in v) s += x;
            return (float)(s / v.Count);
        }

        /// <summary>Nearest-rank percentile on an already-sorted list.</summary>
        static float Percentile(List<float> sorted, float q)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(q * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[i];
        }
    }
}
