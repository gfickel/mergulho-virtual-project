using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Sweeps the wave model's weakest numbers and asks the only question that
    /// matters about the main table: <b>does the RANKING of the candidates
    /// survive?</b> A ranking that flips across a plausible parameter range is a
    /// ranking that means nothing, so results are reported as ORDERINGS wherever
    /// possible rather than as more numbers.
    ///
    /// FOUR BLOCKS, selected with <c>MV_ARSWEEP_BLOCK</c> (or <c>make ar-sweep
    /// BLOCK=threshold</c>); default runs all four.
    ///
    ///   device     a_th × relaxationFraction, the original 4×4 grid. Kept
    ///              unchanged so its numbers stay comparable, now that a_th
    ///              finally reaches the ratchet as well as the oscillator bands.
    ///   jumps      one-at-a-time over jumpRateHz, jumpMedianM, jumpTriggerM and
    ///              genuineJumpFraction. Jump handling DOMINATES the main table's
    ///              ranking and had never been swept at all, which was the larger
    ///              gap of the two. One-at-a-time rather than a grid because a
    ///              4-axis grid is 256 cells for a question that is about each
    ///              axis separately.
    ///   budget     THE ROBUSTNESS QUESTION for the path-based gate: the
    ///              correction budget's ASSUMED error-per-metre x the model's TRUE
    ///              error-per-metre, as independent axes. Setting the gate's belief
    ///              to the simulator's own value would be grading the gate against
    ///              homework it set itself, so the useful cell is the off-diagonal.
    ///   threshold  THE CROSSOVER. relocalizationJumpThreshold as a CONTINUUM
    ///              × genuineJumpFraction, which is the one measurement that can
    ///              falsify (or confirm) "set the threshold to 0". See the header
    ///              comment on that block below.
    ///
    /// Headless:  make ar-sweep   [BLOCK=device|jumps|threshold|budget]
    /// In editor: Tools > Mergulho Virtual > Run AR Drift Parameter Sweep
    /// </summary>
    internal static class ArSimSweep
    {
        const string TableBegin = "AR-SWEEP-TABLE-BEGIN";
        const string TableEnd = "AR-SWEEP-TABLE-END";

        /// <summary>The measured device range, plus the doc's reference value.</summary>
        static readonly float[] AccelBias = { 0.018f, 0.05f, 0.10f, 0.206f };

        /// <summary>Bounded ← → runaway, bracketing the doc's 0.65 reference.</summary>
        static readonly float[] Relaxation = { 0.30f, 0.50f, 0.65f, 0.85f };

        /// <summary>§3.5's own 1-per-5–30 s, which it calls "the weakest number in
        /// this document", extended a little at both ends.</summary>
        static readonly float[] JumpRate = { 0.02f, 0.033f, 0.07f, 0.16f, 0.30f };

        /// <summary>§3.5/§3.7's median 0.22 m bracketed by the measured spread
        /// (Here To Stay's per-action means run 1.2–43.2 cm).</summary>
        static readonly float[] JumpMedian = { 0.08f, 0.15f, 0.22f, 0.40f, 0.80f };

        /// <summary>§3.5's "correlate with the ratchet crossing ~0.3–0.5 m",
        /// widened because the bracket itself is [E].</summary>
        static readonly float[] JumpTrigger = { 0.15f, 0.25f, 0.40f, 0.80f, 1.50f };

        /// <summary>The mixture: 0 = every jump corrects the phantom ratchet (the
        /// pre-2026-09-28 behaviour), 1 = every jump corrects the genuine channel.
        /// Nothing in the research doc addresses this split at all.</summary>
        static readonly float[] GenuineFraction = { 0f, 0.05f, 0.10f, 0.20f, 0.35f, 0.50f, 0.75f, 1f };

        /// <summary>
        /// genuineDriftPerMeterPath, the genuine channel's RATE — how much
        /// registration error a metre of camera path buys. Spans the whole measured
        /// bracket: 0.0034 (Here To Stay's ARCore walk-and-return in an ordinary
        /// static scene) to 0.144 (Feigl's large dynamic environment). This axis
        /// matters as much as the mixture does, because it sets the MAGNITUDE of a
        /// genuine correction and the gate is a pure magnitude filter.
        /// </summary>
        static readonly float[] GenuineRate = { 0.0034f, 0.01f, 0.03f, 0.07f, 0.144f };

        /// <summary>Coarser mixture axis for the 2-D crossover grid.</summary>
        static readonly float[] GenuineFractionCoarse = { 0f, 0.20f, 0.40f, 0.70f, 1f };

        /// <summary>
        /// relocalizationJumpThreshold as a continuum, in metres.
        ///
        /// READ THE GATE'S ARITHMETIC BEFORE READING THIS AXIS. MotionGateCore
        /// suppresses when <c>mag &gt; minDelta &amp;&amp; (thr &lt;= 0 || mag &lt; thr)</c>, so the
        /// threshold is a HIGH-PASS on what gets through: T = 0 is the special case
        /// "suppress everything", and any T &gt; 0 passes jumps of magnitude ≥ T. The
        /// axis is therefore NOT monotone in severity — it runs from "pass almost
        /// everything" at T = 0.02 up to "suppress almost everything" at T = 3, and
        /// T = 0 sits at the same end as T = ∞. The shipped 0.35 m is inside the
        /// measured jump distribution (median 0.22, p90 0.55), which is exactly why
        /// an intermediate value could in principle win.
        /// </summary>
        static readonly float[] JumpThreshold = { 0f, 0.02f, 0.05f, 0.10f, 0.15f, 0.22f, 0.35f, 0.50f, 0.80f, 1.50f, 3f };

        /// <summary>Seeded repeats per cell. Lower than the main table's 3 because a
        /// cell is one point in a grid and the QUESTION is the ordering, not the third
        /// decimal of any cell. Override with MV_ARSIM_REPEATS.</summary>
        const int SweepRepeats = 2;

        [MenuItem("Tools/Mergulho Virtual/Run AR Drift Parameter Sweep", priority = 131)]
        public static void RunAll()
        {
            foreach (var line in Run()) Console.WriteLine(line);
        }

        public static void RunAllHeadless()
        {
            try
            {
                RunAll();
            }
            catch (Exception e)
            {
                Console.WriteLine("AR-SWEEP-FAILED " + e);
                Debug.LogError("[ar-sweep] " + e);
                EditorApplication.Exit(1);
            }
        }

        // =====================================================================
        // One measured point of any sweep axis
        // =====================================================================

        sealed class Point
        {
            public string Label;
            public Dictionary<string, float> Composite = new Dictionary<string, float>();
            public Dictionary<string, float> Standing = new Dictionary<string, float>();
            public Dictionary<string, float> Panning = new Dictionary<string, float>();
            public Dictionary<string, float> Walking = new Dictionary<string, float>();

            /// <summary>The two Excluded scenarios that carry the realistic
            /// walk-then-look sequence the composite's segregated scenarios cannot:
            /// travel banks error, the user stops, and only then does the gate's
            /// threshold decide whether anything can heal it.</summary>
            public Dictionary<string, float> MixedSession = new Dictionary<string, float>();
            public Dictionary<string, float> WalkThenStand = new Dictionary<string, float>();

            /// <summary>The two travelled-then-stood rows: `walk-then-look` is in
            /// the WEIGHTED standing group, `walk-look-harsh` is the same shape with
            /// the genuine channel at the top of its measured bracket (14.4 %/m) and
            /// is the bar a path-based gate has to clear.</summary>
            public Dictionary<string, float> WalkThenLook = new Dictionary<string, float>();
            public Dictionary<string, float> WalkLookHarsh = new Dictionary<string, float>();

            public float GenuineShare = float.NaN;      // self-consistent mixture
            public float PhantomJumpMedian = float.NaN;
            public float GenuineJumpMedian = float.NaN;
            public List<string> Ordering = new List<string>();
        }

        struct Family
        {
            public string Name;
            public IReadOnlyList<Func<IArStabilizer>> Factories;
            public List<string> Names;
        }

        static Family RegistryFamily() => new Family
        {
            Name = "registry",
            Factories = ArStabilizerRegistry.Factories,
            Names = ArStabilizerRegistry.Create().Select(s => s.Name).ToList(),
        };

        /// <summary>
        /// The threshold continuum, as stabilizers. Two gate questions × every
        /// threshold, so the answer can be checked for dependence on WHICH gate is
        /// asking — the shipped gyro-based stillness gate (`g`) and the step gate
        /// (`s`), which is the current shipping candidate.
        /// </summary>
        static Family ThresholdFamily()
        {
            var factories = new List<Func<IArStabilizer>>();
            var names = new List<string>();
            foreach (float t in JumpThreshold)
            {
                float thr = t;
                string n = "g" + thr.ToString("F2", CultureInfo.InvariantCulture);
                names.Add(n);
                factories.Add(() => new ShippedStabilizer
                {
                    Name = n,
                    driftCorrectionEnabled = false,
                    Gate = Threshold(thr),
                });
            }
            foreach (float t in JumpThreshold)
            {
                float thr = t;
                string n = "s" + thr.ToString("F2", CultureInfo.InvariantCulture);
                names.Add(n);
                factories.Add(() => new StepGatedStabilizer { Name = n, Gate = Threshold(thr) });
            }
            names.Add("noop");
            factories.Add(() => new NoopStabilizer());
            return new Family { Name = "threshold", Factories = factories, Names = names };
        }

        static MotionGateCore.Settings Threshold(float thresholdM) => new MotionGateCore.Settings
        {
            minDelta = MotionGateCore.Settings.Defaults.minDelta,
            relocalizationJumpThreshold = thresholdM,
        };

        static Point Measure(string label, Action<WaveDriftModel> apply, Family family,
                             int repeats, float durationScale, bool includeDiagnostics)
        {
            // Fresh scenario objects per point: All() builds new wave models each
            // call, so overriding them cannot leak into another point.
            var scenarios = ArSimScenarios.All(durationScale)
                .Where(s => s.Profile != SessionProfile.Excluded ||
                            (includeDiagnostics &&
                             (s.Id == "mixed-session" || s.Id == "walk-then-stand" ||
                              s.Id == "walk-look-harsh")))
                .ToList();
            foreach (var s in scenarios) apply(s.Wave);

            var point = new Point { Label = label };
            bool tookDiagnostics = false;

            foreach (var factory in family.Factories)
            {
                var results = new List<ArSimResult>();
                foreach (var scenario in scenarios)
                {
                    var perRepeat = new List<ArSimResult>(repeats);
                    for (int rep = 0; rep < repeats; rep++)
                        perRepeat.Add(ArSimRunner.Run(scenario, factory(),
                            keepFrames: false, seedOffset: rep).Result);
                    results.Add(ArSimResult.Average(perRepeat));
                }

                string name = results[0].StabilizerName;
                point.Composite[name] = ArSimScoring.Composite(results);
                point.Standing[name] = GroupMean(results, SessionProfile.Standing);
                point.Panning[name] = GroupMean(results, SessionProfile.StandingPanning);
                point.Walking[name] = GroupMean(results, SessionProfile.Walking);
                var mixed = results.FirstOrDefault(r => r.ScenarioId == "mixed-session");
                if (mixed != null) point.MixedSession[name] = mixed.PerceptualCost;
                var wts = results.FirstOrDefault(r => r.ScenarioId == "walk-then-stand");
                if (wts != null) point.WalkThenStand[name] = wts.PerceptualCost;
                var wtl = results.FirstOrDefault(r => r.ScenarioId == "walk-then-look");
                if (wtl != null) point.WalkThenLook[name] = wtl.PerceptualCost;
                var wlh = results.FirstOrDefault(r => r.ScenarioId == "walk-look-harsh");
                if (wlh != null) point.WalkLookHarsh[name] = wlh.PerceptualCost;

                if (!tookDiagnostics)
                {
                    // The wave model advances identically for every stabilizer (it
                    // only ever sees dt, yaw and ground-truth travel), so these are
                    // properties of the POINT, not of the candidate — read once.
                    point.GenuineShare = WeightedShare(results);
                    point.PhantomJumpMedian = WeightedMedian(results, r => r.PhantomJumpMedianM);
                    point.GenuineJumpMedian = WeightedMedian(results, r => r.GenuineJumpMedianM);
                    tookDiagnostics = true;
                }
            }

            point.Ordering = point.Composite.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            return point;
        }

        static float GroupMean(List<ArSimResult> results, SessionProfile profile)
        {
            float sum = 0f; int n = 0;
            foreach (var r in results)
                if (r.Profile == profile) { sum += r.PerceptualCost; n++; }
            return n > 0 ? sum / n : float.NaN;
        }

        /// <summary>Session-time-weighted mean of the per-scenario genuine share —
        /// i.e. the self-consistent value of genuineJumpFraction for a whole
        /// session, not for one posture.</summary>
        static float WeightedShare(List<ArSimResult> results)
        {
            float num = 0f, den = 0f;
            foreach (var profile in new[] { SessionProfile.Standing,
                                            SessionProfile.StandingPanning,
                                            SessionProfile.Walking })
            {
                float w = SessionTimeWeights.For(profile);
                float sum = 0f; int n = 0;
                foreach (var r in results)
                    if (r.Profile == profile && !float.IsNaN(r.GenuineShareOfError))
                    { sum += r.GenuineShareOfError; n++; }
                if (n == 0) continue;
                num += w * (sum / n);
                den += w;
            }
            return den > 0f ? num / den : float.NaN;
        }

        static float WeightedMedian(List<ArSimResult> results, Func<ArSimResult, float> pick)
        {
            float sum = 0f; int n = 0;
            foreach (var r in results)
            {
                float v = pick(r);
                if (!float.IsNaN(v)) { sum += v; n++; }
            }
            return n > 0 ? sum / n : float.NaN;
        }

        // =====================================================================
        // Driver
        // =====================================================================

        public static List<string> Run(float durationScale = 1f)
        {
            int repeatOverride = 0;
            int.TryParse(Environment.GetEnvironmentVariable("MV_ARSIM_REPEATS") ?? "", out repeatOverride);
            int repeats = Mathf.Max(1, repeatOverride > 0 ? repeatOverride : SweepRepeats);

            string block = (Environment.GetEnvironmentVariable("MV_ARSWEEP_BLOCK") ?? "").Trim().ToLowerInvariant();
            bool all = string.IsNullOrEmpty(block) || block == "all";

            var lines = new List<string> { TableBegin, "" };
            lines.Add("AR DRIFT PARAMETER SWEEP - " + DateTime.UtcNow.ToString("u"));
            lines.Add("");
            lines.Add("Each cell is a full time-weighted COMPOSITE (lower is better), the mean of");
            lines.Add($"{repeats} seeded repeat(s). The point of these tables is the ORDERING, not the");
            lines.Add("numbers.");
            lines.Add("");

            if (all || block == "device") lines.AddRange(DeviceBlock(repeats, durationScale));
            if (all || block == "jumps") lines.AddRange(JumpBlock(repeats, durationScale));
            if (all || block == "threshold") lines.AddRange(ThresholdBlock(repeats, durationScale));
            if (all || block == "budget") lines.AddRange(BudgetBlock(repeats, durationScale));

            lines.Add("");
            lines.Add(TableEnd);
            return lines;
        }

        // =====================================================================
        // Block 1 — device spread × relaxation
        // =====================================================================

        static List<string> DeviceBlock(int repeats, float durationScale)
        {
            var c = CultureInfo.InvariantCulture;
            var family = RegistryFamily();
            var points = new List<Point>();

            foreach (float bias in AccelBias)
                foreach (float relax in Relaxation)
                {
                    float b = bias, r = relax;
                    points.Add(Measure($"{b.ToString("F3", c)}/{r.ToString("F2", c)}",
                        w => { w.imuAccelBiasMps2 = b; w.relaxationFraction = r; },
                        family, repeats, durationScale, includeDiagnostics: false));
                }

            var lines = new List<string>
            {
                "=====================================================================",
                "BLOCK 1 - DEVICE SPREAD x RELAXATION  (a_th x relax)",
                "=====================================================================",
                "",
                "  a_th   imuAccelBiasMps2, MEASURED across five phones at 0.018-0.206 m/s2.",
                "         Sets the admissibility ceiling A_max = a_th/(2pi f)^2 (doc SS1.3).",
                "         SINCE 2026-09-28 that ceiling is applied to the route-1 PULSE as well",
                "         as to the three oscillator bands - see WaveDriftModel.PulseCeilingM.",
                "         Before that it touched only the oscillation, and since the oscillation",
                "         is nowhere near its ceiling in the nominal regime, the whole measured",
                "         11x device range moved the composite by under 0.5%.",
                "  relax  relaxationFraction, the doc's own \"one number I have no measurement",
                "         for\", which alone decides bounded drift (->1) vs a runaway ramp (->0).",
                "",
            };
            lines.AddRange(CompositeTable(points, family.Names, "a_th/relax"));
            lines.AddRange(Stability(points, family.Names,
                reference: points.FirstOrDefault(p => p.Label == "0.050/0.65"),
                referenceNote: "a_th=0.050, relax=0.65 - the doc's SS3.7 reference set"));
            return lines;
        }

        // =====================================================================
        // Block 2 — the jump axes, one at a time
        // =====================================================================

        static List<string> JumpBlock(int repeats, float durationScale)
        {
            var c = CultureInfo.InvariantCulture;
            var family = RegistryFamily();
            var points = new List<Point>();

            foreach (float v in JumpRate)
            {
                float x = v;
                points.Add(Measure("rate=" + x.ToString("F3", c), w => w.jumpRateHz = x,
                    family, repeats, durationScale, includeDiagnostics: false));
            }
            foreach (float v in JumpMedian)
            {
                float x = v;
                points.Add(Measure("median=" + x.ToString("F2", c), w => w.jumpMedianM = x,
                    family, repeats, durationScale, includeDiagnostics: false));
            }
            foreach (float v in JumpTrigger)
            {
                float x = v;
                points.Add(Measure("trigger=" + x.ToString("F2", c), w => w.jumpTriggerM = x,
                    family, repeats, durationScale, includeDiagnostics: false));
            }
            foreach (float v in GenuineFraction)
            {
                float x = v;
                points.Add(Measure("genFrac=" + x.ToString("F2", c), w => w.genuineJumpFraction = x,
                    family, repeats, durationScale, includeDiagnostics: false));
            }
            foreach (float v in GenuineRate)
            {
                float x = v;
                points.Add(Measure("genRate=" + x.ToString("F4", c), w => w.genuineDriftPerMeterPath = x,
                    family, repeats, durationScale, includeDiagnostics: false));
            }

            var lines = new List<string>
            {
                "",
                "=====================================================================",
                "BLOCK 2 - THE JUMP AXES, ONE AT A TIME (everything else at its default)",
                "=====================================================================",
                "",
                "Jump handling dominates the main table's ranking and had never been swept.",
                "One axis at a time rather than a 4-D grid: the question is each axis'",
                "influence, and 4 axes x 5 values would be 625 cells for the same answer.",
                "",
                "  rate      jumpRateHz. SS3.5 calls its own 1-per-5-30 s \"the weakest number in",
                "            this document\".",
                "  median    jumpMedianM, the lognormal median magnitude. [M] magnitudes, [E] shape.",
                "  trigger   jumpTriggerM, the accumulated error at which jumps become likely. [E]",
                "  genFrac   genuineJumpFraction: the share of jumps that correct the GENUINE",
                "            (non-wave) error channel rather than the phantom ratchet. 0 is the",
                "            pre-2026-09-28 behaviour. Nothing in the doc addresses this split;",
                "            it is [E] with no measurement behind it at all.",
                "  genRate   genuineDriftPerMeterPath, over the whole measured bracket 0.0034",
                "            (ARCore, ordinary static scene) to 0.144 (dynamic environment).",
                "",
            };
            lines.AddRange(CompositeTable(points, family.Names, "axis=value"));
            lines.AddRange(Stability(points, family.Names,
                reference: points.FirstOrDefault(p => p.Label == "rate=0.070"),
                referenceNote: "jumpRateHz = 0.07, i.e. every axis at its default"));
            return lines;
        }

        // =====================================================================
        // Block 3 — THE CROSSOVER
        // =====================================================================

        // =====================================================================
        // Block 4 — THE ROBUSTNESS QUESTION: assumed rate x true rate
        // =====================================================================

        /// <summary>
        /// Rates the correction budget can ASSUME, per metre of path. Same bracket
        /// as <see cref="GenuineRate"/> because the gate's belief and the world's
        /// behaviour are the same physical quantity — that is exactly why they have
        /// to be swept as INDEPENDENT axes rather than tied together.
        /// </summary>
        static readonly float[] AssumedRate = { 0.01f, 0.03f, 0.07f, 0.144f };

        /// <summary>
        /// The budget family: every assumed rate on the shipped stillness gate at
        /// threshold 0, plus the two reference gates (`g0.00` = today's shipped
        /// gate-absorb, `g0.35` = the pre-2026-09-28 value) and noop.
        /// </summary>
        static Family BudgetFamily()
        {
            var factories = new List<Func<IArStabilizer>>();
            var names = new List<string>();

            foreach (float r in AssumedRate)
            {
                float rr = r;
                string n = "b" + rr.ToString("F3", CultureInfo.InvariantCulture);
                names.Add(n);
                factories.Add(() =>
                {
                    var s = ArSimCandidates.GateBudget(rr);
                    s.Name = n;
                    return s;
                });
            }

            names.Add("g0.00");
            factories.Add(() => new ShippedStabilizer
            {
                Name = "g0.00", driftCorrectionEnabled = false, Gate = Threshold(0f),
            });
            names.Add("g0.35");
            factories.Add(() => new ShippedStabilizer
            {
                Name = "g0.35", driftCorrectionEnabled = false, Gate = Threshold(0.35f),
            });
            names.Add("noop");
            factories.Add(() => new NoopStabilizer());

            return new Family { Name = "budget", Factories = factories, Names = names };
        }

        static List<string> BudgetBlock(int repeats, float durationScale)
        {
            var c = CultureInfo.InvariantCulture;
            var family = BudgetFamily();
            var points = new List<Point>();

            // The TRUE rate is the model's genuineDriftPerMeterPath; the ASSUMED
            // rate is a column. Crossing them is the whole point: setting the gate's
            // belief to the simulator's own value would be grading the gate against
            // homework it set itself, and the useful result is not the best cell but
            // how much a MISMATCH costs.
            foreach (float rate in GenuineRate)
            {
                float rr = rate;
                points.Add(Measure(rr.ToString("F4", c),
                    w => w.genuineDriftPerMeterPath = rr,
                    family, repeats, durationScale, includeDiagnostics: true));
            }

            var lines = new List<string>
            {
                "",
                "=====================================================================",
                "BLOCK 4 - CORRECTION BUDGET: assumed rate x TRUE rate",
                "=====================================================================",
                "",
                "The correction budget decides on PATH TRAVELLED instead of magnitude: a walk",
                "earns `metres x assumedRate` of credit and a tracker correction is admitted only",
                "up to that credit (partially, if the credit runs out mid-jump). Standing earns",
                "nothing, so standing still suppresses everything - which is where g0.00 already",
                "won and is not what this block is about.",
                "",
                "ROWS are the model's TRUE genuineDriftPerMeterPath, the measured bracket from",
                "0.34 %/m (ARCore, static scene, Here To Stay) to 14.4 %/m (dynamic environment,",
                "Feigl GRAPP 2020). COLUMNS b<rate> are what the GATE ASSUMES. The diagonal is the",
                "matched case and is the LEAST interesting cell in the table; the off-diagonal is",
                "the result, because on a real beach nobody knows the true rate.",
                "",
                "  g0.00  the shipped gate (suppress every translation while still), no budget",
                "  g0.35  the pre-2026-09-28 magnitude threshold, for reference",
                "",
                "NOTE: `walk-look-harsh` carries its own 14.4 %/m in the main table; here the row",
                "axis overrides it, so in THIS block it is simply the long (~5 min) walk-and-look",
                "shape measured at the row's rate. The 0.1440 row is the as-named scenario.",
                "",
            };

            lines.Add("  COMPOSITE (standing 55 / panning 25 / walking 20), CORRECTED scenario set:");
            lines.Add("");
            lines.AddRange(CompositeTable(points, family.Names, "trueRate", extraColumns: true));

            lines.Add("");
            lines.Add("  walk-then-look - the new WEIGHTED travelled-then-stood row: ~46 m walked,");
            lines.Add("  then ~110 s of holding still, and only the holding is scored. This is the");
            lines.Add("  scenario the standing group was missing:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.WalkThenLook, "trueRate"));

            lines.Add("");
            lines.Add("  walk-look (long, ~5 min: 72 m walked then four minutes of looking and");
            lines.Add("  panning) - THE BAR. Its 0.1440 row is the known losing corner for g0.00:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.WalkLookHarsh, "trueRate"));

            lines.Add("");
            lines.Add("  STANDING group alone (which now CONTAINS walk-then-look, so unlike every");
            lines.Add("  previous version of this table it can charge a stabilizer for freezing in");
            lines.Add("  travel-banked error):");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.Standing, "trueRate"));

            lines.Add("");
            lines.Add("  WALKING group - the transparency check. The budget must not eat real");
            lines.Add("  motion: the gate is inert while the IMU reports travel, so these numbers");
            lines.Add("  should be indistinguishable from g0.00's:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.Walking, "trueRate"));

            // ---- the mismatch cost ------------------------------------------
            lines.Add("");
            lines.Add("MISMATCH COST - each budget column vs the BEST budget column in its row,");
            lines.Add("as a percentage. This is the robustness result: a small spread means the");
            lines.Add("assumed rate barely matters, which is the property worth having, because");
            lines.Add("nobody can measure the true rate on a beach:");
            lines.Add("");
            var budgetNames = family.Names.Where(n => n.StartsWith("b")).ToList();
            var mm = new List<string[]> { new[] { "trueRate" }.Concat(budgetNames)
                                            .Concat(new[] { "spread", "vs g0.00" }).ToArray() };
            foreach (var p in points)
            {
                float best = float.PositiveInfinity, worst = 0f;
                foreach (var n in budgetNames)
                    if (p.Composite.TryGetValue(n, out float v) && !float.IsNaN(v))
                    { best = Mathf.Min(best, v); worst = Mathf.Max(worst, v); }

                var row = new List<string> { p.Label };
                foreach (var n in budgetNames)
                    row.Add(p.Composite.TryGetValue(n, out float v) && !float.IsNaN(v) && best > 0f
                        ? "+" + (100f * (v - best) / best).ToString("F1", c) + "%" : "-");
                row.Add(best > 0f && !float.IsInfinity(best)
                    ? "+" + (100f * (worst - best) / best).ToString("F1", c) + "%" : "-");
                string bestName = budgetNames
                    .Where(n => p.Composite.ContainsKey(n))
                    .OrderBy(n => p.Composite[n]).FirstOrDefault();
                row.Add(bestName == null ? "-" : Delta(p.Composite, bestName, "g0.00"));
                mm.Add(row.ToArray());
            }
            lines.AddRange(Render(mm));

            lines.AddRange(Stability(points, family.Names,
                points.FirstOrDefault(p => p.Label.StartsWith("0.0300")),
                "true rate = the model's default 3 %/m"));

            return lines;
        }

        static List<string> ThresholdBlock(int repeats, float durationScale)
        {
            var c = CultureInfo.InvariantCulture;
            var family = ThresholdFamily();
            var points = new List<Point>();

            // A 2-D grid, because the mixture alone cannot answer the question: the
            // gate filters on MAGNITUDE, and the magnitude of a genuine correction
            // is set by the genuine channel's RATE (a correction removes ~80% of the
            // accumulated genuine error, so its size is rate x travel between
            // jumps). A mixture of 1.0 at 0.34%/m produces corrections far too small
            // for any threshold to distinguish; the same mixture at 14.4%/m produces
            // ones well above the shipped 0.35 m.
            foreach (float rate in GenuineRate)
                foreach (float f in GenuineFractionCoarse)
                {
                    float rr = rate, ff = f;
                    points.Add(Measure(
                        $"{rr.ToString("F4", c)}/{ff.ToString("F2", c)}",
                        w => { w.genuineDriftPerMeterPath = rr; w.genuineJumpFraction = ff; },
                        family, repeats, durationScale, includeDiagnostics: true));
                }

            var lines = new List<string>
            {
                "",
                "=====================================================================",
                "BLOCK 3 - THE CROSSOVER: relocalizationJumpThreshold x genuineJumpFraction",
                "=====================================================================",
                "",
                "THE ONE MEASUREMENT THAT CAN FALSIFY \"set relocalizationJumpThreshold to 0\".",
                "",
                "Rows are genuineRate/genuineJumpFraction; columns are gate variants <gate><threshold>,",
                "where g = the shipped gyro-stillness gate and s = the step gate, both with the",
                "GPS drift correction off. The threshold is swept as a CONTINUUM, not as {0, 0.35}.",
                "",
                "MIND THE GATE'S ARITHMETIC: MotionGateCore suppresses when the frame's delta is",
                "SMALLER than the threshold, so T=0 means \"suppress everything\" and any T>0 lets",
                "jumps of magnitude >= T through. The axis is therefore not monotone in severity:",
                "T=0 and T=3.00 sit at the same end, and the pass-band is [T, infinity).",
                "",
                "  genShare  the SELF-CONSISTENT value of genuineJumpFraction for this point:",
                "            time-weighted |genuine| / (|genuine| + |ratchet|), i.e. how the",
                "            error is actually apportioned between the two channels. A mapper",
                "            correcting the inconsistency it can see would split its corrections",
                "            in roughly this ratio, so this is where a defensible real-world",
                "            value sits - compare it against where the crossover lands.",
                "  phMed     median realised magnitude of the phantom-correcting jumps, m",
                "  genMed    median realised magnitude of the genuine-correcting jumps, m.",
                "            An INTERMEDIATE threshold can only beat both ends if these two are",
                "            SEPARATED, because the gate is a pure size filter.",
                "",
            };
            lines.Add("  COMPOSITE (standing 55 / panning 25 / walking 20):");
            lines.Add("");
            lines.AddRange(CompositeTable(points, family.Names, "rate/frac",
                extraColumns: true));

            lines.Add("");
            lines.Add("  STANDING group alone - where the composite's win lives, and where the");
            lines.Add("  genuine channel is structurally SILENT because a standing scenario in this");
            lines.Add("  table starts with a clean session and never travels, so no genuine error is");
            lines.Add("  ever banked for a jump to heal. That is the load-bearing caveat on the whole");
            lines.Add("  result, not a footnote:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.Standing, "rate/frac"));

            lines.Add("");
            lines.Add("  walk-then-stand (25 s walk, then 45 s of holding still) - the shortest");
            lines.Add("  scenario in the table that actually contains the sequence this question is");
            lines.Add("  about: travel banks error, the gate freezes it in, and only then does the");
            lines.Add("  threshold decide whether a correction can land:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.WalkThenStand, "rate/frac"));

            lines.Add("");
            lines.Add("  mixed-session (~3 min: mostly standing, 2 pans, 2 short walks) - the");
            lines.Add("  closest thing here to a real session:");
            lines.Add("");
            lines.AddRange(ScenarioTable(points, family.Names, p => p.MixedSession, "rate/frac"));

            // ---- the crossover itself -------------------------------------
            lines.Add("");
            lines.Add("BEST THRESHOLD PER MIXTURE - the crossover, read four ways.");
            lines.Add("");
            lines.Add("The COMPOSITE weights standing 55 / panning 25 / walking 20, and its walking");
            lines.Add("scenarios END as soon as the walk does, so travel-banked error never gets a");
            lines.Add("standing tail in which a jump could heal it. walk-then-stand (25 s walk then");
            lines.Add("45 s of holding) and mixed-session (~3 min, the realistic profile) do have");
            lines.Add("that tail, which is why they are reported alongside - they are Excluded from");
            lines.Add("the composite by design and are the sharper test of this particular question.");
            lines.Add("");
            var crossRows = new List<string[]>
            {
                new[] { "rate/frac", "genShare", "phMed", "genMed", "best COMPOSITE", "best standing",
                        "best walk-then-stand", "best mixed-session" },
            };
            foreach (var p in points)
                crossRows.Add(new[]
                {
                    p.Label,
                    float.IsNaN(p.GenuineShare) ? "-" : p.GenuineShare.ToString("F2", c),
                    float.IsNaN(p.PhantomJumpMedian) ? "-" : p.PhantomJumpMedian.ToString("F2", c),
                    float.IsNaN(p.GenuineJumpMedian) ? "-" : p.GenuineJumpMedian.ToString("F2", c),
                    BestOf(p.Composite, family.Names),
                    BestOf(p.Standing, family.Names),
                    BestOf(p.WalkThenStand, family.Names),
                    BestOf(p.MixedSession, family.Names),
                });
            lines.AddRange(Render(crossRows));

            lines.Add("");
            lines.Add("HEAD TO HEAD: suppress-everything (g0.00) vs the shipped 0.35 m (g0.35),");
            lines.Add("as a percentage - negative means g0.00 is better:");
            var h2h = new List<string[]>
            {
                new[] { "rate/frac", "COMPOSITE", "standing", "panning", "walking",
                        "walk-then-stand", "mixed-session" },
            };
            foreach (var p in points)
                h2h.Add(new[]
                {
                    p.Label,
                    Delta(p.Composite, "g0.00", "g0.35"),
                    Delta(p.Standing, "g0.00", "g0.35"),
                    Delta(p.Panning, "g0.00", "g0.35"),
                    Delta(p.Walking, "g0.00", "g0.35"),
                    Delta(p.WalkThenStand, "g0.00", "g0.35"),
                    Delta(p.MixedSession, "g0.00", "g0.35"),
                });
            lines.AddRange(Render(h2h));

            return lines;
        }

        static string BestOf(Dictionary<string, float> costs, List<string> names)
        {
            string best = null; float bv = float.PositiveInfinity;
            foreach (var n in names)
                if (costs.TryGetValue(n, out float v) && !float.IsNaN(v) && v < bv) { bv = v; best = n; }
            return best == null ? "-"
                : best + " (" + bv.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        static string Delta(Dictionary<string, float> costs, string a, string b)
        {
            if (!costs.TryGetValue(a, out float va) || !costs.TryGetValue(b, out float vb) ||
                float.IsNaN(va) || float.IsNaN(vb) || vb <= 1e-6f) return "-";
            float pct = 100f * (va - vb) / vb;
            return (pct >= 0f ? "+" : "") + pct.ToString("F1", CultureInfo.InvariantCulture) + "%";
        }

        // =====================================================================
        // Shared reporting
        // =====================================================================

        static List<string> CompositeTable(List<Point> points, List<string> names, string axisHeader,
                                           bool extraColumns = false)
        {
            var c = CultureInfo.InvariantCulture;
            var rows = new List<string[]>();
            var header = new List<string> { axisHeader };
            header.AddRange(names);
            if (extraColumns) { header.Add("genShare"); header.Add("phMed"); header.Add("genMed"); }
            header.Add("best");
            rows.Add(header.ToArray());

            foreach (var p in points)
            {
                var row = new List<string> { p.Label };
                foreach (var n in names)
                    row.Add(p.Composite.TryGetValue(n, out float v) && !float.IsNaN(v)
                        ? v.ToString("F2", c) : "-");
                if (extraColumns)
                {
                    row.Add(float.IsNaN(p.GenuineShare) ? "-" : p.GenuineShare.ToString("F2", c));
                    row.Add(float.IsNaN(p.PhantomJumpMedian) ? "-" : p.PhantomJumpMedian.ToString("F2", c));
                    row.Add(float.IsNaN(p.GenuineJumpMedian) ? "-" : p.GenuineJumpMedian.ToString("F2", c));
                }
                row.Add(p.Ordering.Count > 0 ? p.Ordering[0] : "-");
                rows.Add(row.ToArray());
            }
            return Render(rows);
        }

        /// <summary>The same column layout as the composite table, for one
        /// per-scenario (or per-profile) cost instead of the composite.</summary>
        static List<string> ScenarioTable(List<Point> points, List<string> names,
                                          Func<Point, Dictionary<string, float>> pick, string axisHeader)
        {
            var c = CultureInfo.InvariantCulture;
            var rows = new List<string[]>();
            var header = new List<string> { axisHeader };
            header.AddRange(names);
            header.Add("best");
            rows.Add(header.ToArray());

            foreach (var p in points)
            {
                var costs = pick(p);
                var row = new List<string> { p.Label };
                foreach (var n in names)
                    row.Add(costs.TryGetValue(n, out float v) && !float.IsNaN(v)
                        ? v.ToString("F2", c) : "-");
                row.Add(BestOf(costs, names));
                rows.Add(row.ToArray());
            }
            return Render(rows);
        }

        static List<string> Stability(List<Point> points, List<string> names,
                                      Point reference, string referenceNote)
        {
            var c = CultureInfo.InvariantCulture;
            var lines = new List<string> { "", "  RANKING STABILITY", "" };

            var winners = points.Select(x => x.Ordering.Count > 0 ? x.Ordering[0] : "-").Distinct().ToList();
            lines.Add($"  best-in-cell: {string.Join(", ", winners)}  " +
                      $"({winners.Count} distinct winner(s) across {points.Count} cells)");

            var orderings = points.Select(x => string.Join(" < ", x.Ordering)).Distinct().ToList();
            lines.Add($"  distinct full orderings: {orderings.Count} of {points.Count} cells");

            if (reference != null)
            {
                lines.Add("");
                lines.Add($"  Reference ordering ({referenceNote}):");
                lines.Add("    " + string.Join(" < ", reference.Ordering));
                lines.Add("");
                lines.Add("  Per-cell disagreement with that ordering, counted as PAIRWISE INVERSIONS");
                lines.Add($"  (0 = identical ranking; {names.Count * (names.Count - 1) / 2} = reversed):");

                var invRows = new List<string[]> { new[] { "cell", "inversions", "ordering" } };
                foreach (var p in points)
                    invRows.Add(new[]
                    {
                        p.Label,
                        Inversions(reference.Ordering, p.Ordering).ToString(c),
                        string.Join(" < ", p.Ordering),
                    });
                lines.AddRange(Render(invRows));
            }

            lines.Add("");
            lines.Add("  Rank of each stabilizer across all cells (1 = best):");
            var rankRows = new List<string[]> { new[] { "stabilizer", "best rank", "worst rank", "verdict" } };
            foreach (var n in names)
            {
                int best = int.MaxValue, worst = 0;
                foreach (var p in points)
                {
                    int r = p.Ordering.IndexOf(n) + 1;
                    if (r <= 0) continue;
                    if (r < best) best = r;
                    if (r > worst) worst = r;
                }
                if (worst == 0) continue;
                rankRows.Add(new[]
                {
                    n,
                    best.ToString(c),
                    worst.ToString(c),
                    best == worst ? "fixed" : $"moves {worst - best} place(s)",
                });
            }
            lines.AddRange(Render(rankRows));
            return lines;
        }

        /// <summary>Number of pairs whose relative order differs between two
        /// permutations of the same set — i.e. Kendall's tau distance. Reported raw
        /// rather than normalised because the raw count is the thing a reader can
        /// check by eye against the ordering printed alongside it.</summary>
        static int Inversions(List<string> reference, List<string> other)
        {
            int n = 0;
            for (int i = 0; i < reference.Count; i++)
                for (int j = i + 1; j < reference.Count; j++)
                {
                    int a = other.IndexOf(reference[i]);
                    int b = other.IndexOf(reference[j]);
                    if (a >= 0 && b >= 0 && a > b) n++;
                }
            return n;
        }

        /// <summary>Column-aligned table; same shape as the harness's renderer.</summary>
        static List<string> Render(List<string[]> rows)
        {
            int cols = 0;
            foreach (var r in rows) if (r != null) cols = Mathf.Max(cols, r.Length);
            var width = new int[cols];
            foreach (var r in rows)
            {
                if (r == null) continue;
                for (int i = 0; i < r.Length; i++)
                    width[i] = Mathf.Max(width[i], (r[i] ?? "").Length);
            }

            var outLines = new List<string>();
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var r = rows[rowIndex];
                if (r == null) { outLines.Add(""); continue; }
                var sb = new StringBuilder();
                for (int i = 0; i < r.Length; i++)
                {
                    string cell = r[i] ?? "";
                    bool last = i == r.Length - 1;
                    sb.Append(last && cell.Contains(" < ") ? cell : cell.PadLeft(width[i]));
                    if (!last) sb.Append("  ");
                }
                outLines.Add("  " + sb.ToString().TrimEnd());
                if (rowIndex == 0)
                {
                    var rule = new StringBuilder();
                    for (int i = 0; i < cols; i++)
                    {
                        rule.Append(new string('-', width[i]));
                        if (i < cols - 1) rule.Append("  ");
                    }
                    outLines.Add("  " + rule);
                }
            }
            return outLines;
        }
    }
}
