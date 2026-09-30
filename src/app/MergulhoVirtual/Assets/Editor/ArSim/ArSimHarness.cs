using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Entry point for the offline AR wave-drift sweep: every scenario × every
    /// registered stabilizer, a legible metrics table on stdout, per-run CSV time
    /// series and a manifest recording seeds, model parameters and metrics.
    ///
    /// Headless:  make ar-sim          (or SHOT-style filtering: make ar-sim ARSIM=pan)
    /// In editor: Tools > Mergulho Virtual > Run AR Drift Simulation
    ///
    /// Nothing here ships to a device — this whole folder is Assembly-CSharp-Editor.
    /// </summary>
    internal static class ArSimHarness
    {
        const string TableBegin = "AR-SIM-TABLE-BEGIN";
        const string TableEnd = "AR-SIM-TABLE-END";

        [MenuItem("Tools/Mergulho Virtual/Run AR Drift Simulation", priority = 130)]
        public static void RunAll()
        {
            string outDir = Environment.GetEnvironmentVariable("MV_ARSIM_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = RepoPath(".arsim");
            string filter = Environment.GetEnvironmentVariable("MV_ARSIM_FILTER");

            var report = RunSweep(ArSimScenarios.All(), filter, outDir, writeFiles: true);
            foreach (var line in report) Console.WriteLine(line);
        }

        /// <summary>Batchmode entry: same as <see cref="RunAll"/> but never throws
        /// past the log, so `make ar-sim` always leaves a readable log.</summary>
        public static void RunAllHeadless()
        {
            try
            {
                RunAll();
            }
            catch (Exception e)
            {
                Console.WriteLine("AR-SIM-FAILED " + e);
                Debug.LogError("[ar-sim] " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// The sweep itself, factored out so the tests can call it with short
        /// scenarios and no file writes.
        /// </summary>
        public static List<string> RunSweep(IReadOnlyList<ArSimScenario> scenarios, string filter,
                                            string outDir, bool writeFiles)
        {
            int repeatOverride = 0;
            int.TryParse(Environment.GetEnvironmentVariable("MV_ARSIM_REPEATS") ?? "", out repeatOverride);

            var runs = new List<ArSimRun>();
            foreach (var scenario in scenarios)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    scenario.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                int repeats = Mathf.Max(1, repeatOverride > 0 ? repeatOverride : scenario.Repeats);

                foreach (var factory in ArStabilizerRegistry.Factories)
                {
                    var perRepeat = new List<ArSimResult>(repeats);
                    ArSimRun firstRun = null;
                    for (int rep = 0; rep < repeats; rep++)
                    {
                        // Only repeat 0 keeps its frames: the CSV is a time series
                        // for inspection, and one realisation is what you want to
                        // look at. The metrics are the mean over all repeats.
                        var run = ArSimRunner.Run(scenario, factory(),
                            keepFrames: writeFiles && rep == 0, seedOffset: rep);
                        if (rep == 0) firstRun = run;
                        perRepeat.Add(run.Result);
                    }
                    firstRun.Result = ArSimResult.Average(perRepeat);
                    runs.Add(firstRun);
                }
            }

            if (writeFiles && runs.Count > 0)
            {
                Directory.CreateDirectory(outDir);
                foreach (var run in runs)
                {
                    string file = Path.Combine(outDir,
                        $"{run.Result.ScenarioId}__{run.Result.StabilizerName}.csv");
                    File.WriteAllText(file, ArSimRunner.ToCsv(run));
                }
                File.WriteAllText(Path.Combine(outDir, "manifest.json"), BuildManifest(scenarios, runs));
            }

            return BuildReport(runs, outDir, writeFiles);
        }

        // =====================================================================
        // The report
        // =====================================================================

        static List<string> BuildReport(List<ArSimRun> runs, string outDir, bool wroteFiles)
        {
            var lines = new List<string> { TableBegin };
            if (runs.Count == 0)
            {
                lines.Add("(no runs - check the scenario filter)");
                lines.Add(TableEnd);
                return lines;
            }

            var stabilizers = runs.Select(r => r.Result.StabilizerName).Distinct().ToList();

            lines.Add("");
            lines.Add("AR WAVE-DRIFT SIMULATION - " + DateTime.UtcNow.ToString("u"));
            lines.Add("Model: docs/ar-wave-drift-research.md sec.3 (world-fixed seaward frame, 1/Z^2");
            lines.Add("       amplitude, 1/f^2 IMU ceiling, asymmetric seaward ratchet).");
            lines.Add("");
            lines.Add($"Metrics are PERCEPTUAL: a virtual animal is anchored {ArSimMetricsAccumulator.AnchorDistanceM:F0} m out in the water at");
            lines.Add("session start, and the columns describe how it APPEARS to move. Lower is better");
            lines.Add("everywhere except fidel (1.00 is ideal). A dash means not applicable.");
            lines.Add("");
            int repeatsShown = runs[0].Result.Repeats;
            lines.Add($"Every row is the mean of {repeatsShown} seeded repeat(s) of the scenario. Relocalisation");
            lines.Add("jumps are Poisson at ~0.07 Hz, so one 90 s run sees only a handful and their");
            lines.Add("arrival luck alone can reorder the SCENARIOS. It never affects the stabilizer");
            lines.Add("comparison: within a repeat every stabilizer sees a bit-identical world.");
            lines.Add("");
            lines.Add("  driftDeg    mean | p95 absolute apparent-BEARING error of the anchor");
            lines.Add($"  slide m     the p95 bearing error as lateral metres at {ArSimMetricsAccumulator.AnchorDistanceM:F0} m");
            lines.Add("  range%      p95 error in the anchor's apparent DISTANCE. The phantom drift is");
            lines.Add("              seaward and the user looks seaward, so most of the error lies ALONG");
            lines.Add("              the line of sight, where it changes the animal's apparent SIZE and");
            lines.Add("              barely moves its bearing at all. Bearing alone would miss it.");
            lines.Add($"  jitDeg/s    RMS rate of the bearing error over a {ArSimMetricsAccumulator.JitterWindowS * 1000f:F0} ms window - the");
            lines.Add("              OSCILLATION, which a user minds far more than slow drift");
            lines.Add("  posRMS/Max  camera world-position error vs ground truth, metres");
            lines.Add("  fidel       recovered / true motion while walking (1.00 = real motion survives,");
            lines.Add("              0.00 = the stabilizer froze the world)");
            lines.Add("  gate m      distance the stabilizer's GATE moved the world (path length)");
            lines.Add("  corr m      distance the GPS DRIFT CORRECTION moved it - separating these two");
            lines.Add("              is usually the whole diagnosis");
            lines.Add("  false m     the part of gate+corr applied while the device was GENUINELY moving");
            lines.Add("  cost        one number, equivalent degrees: driftP95 + " +
                      $"{ArSimScoring.JitterWeightSeconds:F0}*jit + {ArSimScoring.RangeWeightDeg:F1}*range");
            lines.Add($"              + {ArSimScoring.FidelityPenaltyDeg:F0}*|1-fidel|");
            lines.Add("");

            // ---- per-scenario table ------------------------------------------
            var header = new[] { "scenario", "stabilizer", "prof", "driftDeg", "driftP95",
                                 "slide m", "range%", "jitDeg/s", "posRMS", "posMax", "fidel",
                                 "gate m", "corr m", "false m", "cost" };
            var rows = new List<string[]> { header };

            string lastScenario = null;
            foreach (var run in runs)
            {
                var r = run.Result;
                if (lastScenario != null && lastScenario != r.ScenarioId) rows.Add(null);  // spacer
                lastScenario = r.ScenarioId;
                float gate = r.GateSuppressedM;
                rows.Add(new[]
                {
                    r.ScenarioId,
                    r.StabilizerName,
                    Abbrev(r.Profile),
                    F(r.DriftDegMean, 2),
                    F(r.DriftDegP95, 2),
                    F(r.SlideP95M, 3),
                    F(100f * r.RangeErrFracP95, 1),
                    F(r.JitterDegPerSec, 3),
                    F(r.PosErrRmsM, 3),
                    F(r.PosErrMaxM, 3),
                    float.IsNaN(r.RealMotionFidelity) ? "-" : F(r.RealMotionFidelity, 3),
                    float.IsNaN(gate) ? "-" : F(gate, 2),
                    float.IsNaN(gate) ? F(r.SuppressedM, 2) : F(r.SuppressedM - gate, 2),
                    F(r.FalseSuppressionM, 2),
                    F(r.PerceptualCost, 2),
                });
            }
            lines.AddRange(Render(rows));

            // ---- composite ----------------------------------------------------
            lines.Add("");
            lines.Add("TIME-WEIGHTED COMPOSITE - NOT an unweighted average of the rows above.");
            lines.Add("Scenarios are grouped by what the user is doing and weighted by the expected");
            lines.Add("share of session time (SessionTimeWeights, PROVISIONAL):");
            lines.Add($"    {SessionTimeWeights.Describe()}");
            lines.Add("Scenarios marked [excl] are diagnostics and carry no weight. The composite");
            lines.Add("includes the fidelity penalty, so a stabilizer cannot win it by freezing the world.");
            lines.Add("");

            var compositeRows = new List<string[]>
            {
                new[] { "stabilizer", "standing", "stand+pan", "walking", "COMPOSITE" },
            };
            foreach (var name in stabilizers)
            {
                var mine = runs.Where(r => r.Result.StabilizerName == name).Select(r => r.Result).ToList();
                compositeRows.Add(new[]
                {
                    name,
                    F(GroupMean(mine, SessionProfile.Standing), 2),
                    F(GroupMean(mine, SessionProfile.StandingPanning), 2),
                    F(GroupMean(mine, SessionProfile.Walking), 2),
                    F(ArSimScoring.Composite(mine), 2),
                });
            }
            lines.AddRange(Render(compositeRows));

            // ---- improvement vs the baseline -----------------------------------
            var baseline = runs.Where(r => r.Result.StabilizerName == "noop").Select(r => r.Result).ToList();
            if (baseline.Count > 0 && stabilizers.Count > 1)
            {
                lines.Add("");
                lines.Add("COST vs the noop baseline, per scenario (negative = better than doing nothing).");
                lines.Add("Shown in absolute equivalent-degrees where the baseline cost is under 0.25,");
                lines.Add("because a percentage of almost-zero says nothing:");
                var deltaRows = new List<string[]>();
                var deltaHeader = new List<string> { "scenario", "prof", "noop cost" };
                foreach (var n in stabilizers.Where(n => n != "noop")) deltaHeader.Add(n + " delta%");
                deltaRows.Add(deltaHeader.ToArray());

                foreach (var scenarioId in runs.Select(r => r.Result.ScenarioId).Distinct())
                {
                    var b = baseline.FirstOrDefault(r => r.ScenarioId == scenarioId);
                    if (b == null) continue;
                    var row = new List<string> { scenarioId, Abbrev(b.Profile), F(b.PerceptualCost, 2) };
                    foreach (var n in stabilizers.Where(x => x != "noop"))
                    {
                        var c = runs.Select(r => r.Result)
                                    .FirstOrDefault(r => r.ScenarioId == scenarioId && r.StabilizerName == n);
                        // A near-zero baseline makes a ratio meaningless (a calm
                        // scenario's noop cost is legitimately ~0), so report the
                        // absolute difference in equivalent degrees instead.
                        row.Add(c == null ? "-"
                            : b.PerceptualCost < 0.25f
                                ? (c.PerceptualCost - b.PerceptualCost >= 0f ? "+" : "") +
                                  F(c.PerceptualCost - b.PerceptualCost, 2) + "deg"
                                : (c.PerceptualCost - b.PerceptualCost >= 0f ? "+" : "") +
                                  F(100f * (c.PerceptualCost - b.PerceptualCost) / b.PerceptualCost, 1) + "%");
                    }
                    deltaRows.Add(row.ToArray());
                }
                lines.AddRange(Render(deltaRows));
            }

            // ---- footnotes ------------------------------------------------------
            lines.Add("");
            lines.Add("Scenario detail:");
            foreach (var scenarioId in runs.Select(r => r.Result.ScenarioId).Distinct())
            {
                var any = runs.First(r => r.Result.ScenarioId == scenarioId);
                lines.Add("  " + any.ScenarioDescription);
            }

            var shippedRuns = runs.Where(r => r.Result.StabilizerName != "noop").ToList();
            if (shippedRuns.Count > 0)
            {
                lines.Add("");
                lines.Add("Stillness / fusion diagnostics (non-baseline stabilizers):");
                lines.Add("  supp %     fraction of frames the stabilizer considered itself SUPPRESSING");
                lines.Add("             (IMU stillness for the shipped family, \"no step firing\" for step-gate)");
                lines.Add("  reacq s    seconds from the last genuinely-walking frame until it suppressed");
                lines.Add("             again - the gate's release time, only defined where walking stops");
                lines.Add("  steps      real steps in the scenario, for scoring a detector's count against");
                lines.Add("  genuine    of `jumps`, how many corrected the GENUINE (non-wave) error channel");
                lines.Add("             rather than the phantom ratchet - the only jumps a gate can lose");
                lines.Add("             anything real by suppressing (WaveDriftModel genuine-channel block)");
                lines.Add("  genShare   time-averaged |genuine| / (|genuine| + |ratchet|): the share of the");
                lines.Add("             accumulated pose error the genuine channel holds, i.e. the");
                lines.Add("             SELF-CONSISTENT value of genuineJumpFraction for this scenario");
                lines.Add("  phMedM     median magnitude of the phantom-correcting jumps, metres");
                lines.Add("  genMedM    median magnitude of the genuine-correcting jumps, metres. An");
                lines.Add("             INTERMEDIATE relocalizationJumpThreshold can only beat both 0 and");
                lines.Add("             0.35 m if these two distributions are separated - the gate keeps");
                lines.Add("             jumps SMALLER than the threshold and passes the rest, so it is a");
                lines.Add("             size filter and nothing else");
                var diag = new List<string[]>
                {
                    new[] { "scenario", "stabilizer", "supp %", "reacq s", "steps", "fixes", "jumps",
                            "genuine", "genShare", "phMedM", "genMedM",
                            "head ref", "headErrDeg", "state" },
                };
                foreach (var run in shippedRuns)
                {
                    var r = run.Result;
                    diag.Add(new[]
                    {
                        r.ScenarioId, r.StabilizerName,
                        F(100f * r.StillFraction, 1),
                        float.IsNaN(r.SuppressionReacquireS) ? "-" : F(r.SuppressionReacquireS, 2),
                        r.TrueStepCount > 0.5f ? F(r.TrueStepCount, 0) : "-",
                        r.FixCount.ToString(CultureInfo.InvariantCulture),
                        r.JumpCount.ToString(CultureInfo.InvariantCulture),
                        r.GenuineJumpCount.ToString(CultureInfo.InvariantCulture),
                        float.IsNaN(r.GenuineShareOfError) ? "-" : F(r.GenuineShareOfError, 2),
                        float.IsNaN(r.PhantomJumpMedianM) ? "-" : F(r.PhantomJumpMedianM, 2),
                        float.IsNaN(r.GenuineJumpMedianM) ? "-" : F(r.GenuineJumpMedianM, 2),
                        r.HeadingRefined ? "yes" : "no",
                        float.IsNaN(r.FinalHeadingErrorDeg) ? "-" : F(r.FinalHeadingErrorDeg, 1),
                        string.IsNullOrEmpty(r.StabilizerSummary) ? "-" : r.StabilizerSummary,
                    });
                }
                lines.AddRange(Render(diag));
            }

            if (wroteFiles)
            {
                lines.Add("");
                lines.Add($"CSV time series + manifest.json -> {outDir}");
            }
            lines.Add(TableEnd);
            return lines;
        }

        static float GroupMean(List<ArSimResult> results, SessionProfile profile)
        {
            float sum = 0f; int n = 0;
            foreach (var r in results)
                if (r.Profile == profile) { sum += r.PerceptualCost; n++; }
            return n > 0 ? sum / n : float.NaN;
        }

        static string Abbrev(SessionProfile p) => p switch
        {
            SessionProfile.Standing => "stand",
            SessionProfile.StandingPanning => "pan",
            SessionProfile.Walking => "walk",
            _ => "excl",
        };

        static string F(float v, int digits) =>
            float.IsNaN(v) ? "-" : v.ToString("F" + digits, CultureInfo.InvariantCulture);

        /// <summary>Column-aligned fixed-width table. A null row is a blank spacer.</summary>
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
                    // First two columns left-aligned (names), the rest right.
                    sb.Append(i < 2 ? cell.PadRight(width[i]) : cell.PadLeft(width[i]));
                    if (i < r.Length - 1) sb.Append("  ");
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

        // =====================================================================
        // Manifest
        // =====================================================================

        static string BuildManifest(IReadOnlyList<ArSimScenario> scenarios, List<ArSimRun> runs)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generated_utc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine("  \"model_source\": \"docs/ar-wave-drift-research.md section 3\",");
            sb.AppendLine("  \"anchor_distance_m\": " + ArSimMetricsAccumulator.AnchorDistanceM.ToString(c) + ",");
            sb.AppendLine("  \"repeats\": " + (runs.Count > 0 ? runs[0].Result.Repeats : 1) + ",");
            sb.AppendLine("  \"repeats_note\": \"each run row is the mean over seeded repeats; " +
                          "seed offsets are rep*1009 (wave), rep*2017 (gnss), rep*3011 (imu)\",");
            sb.AppendLine("  \"scoring\": {");
            sb.AppendLine("    \"jitter_weight_seconds\": " + ArSimScoring.JitterWeightSeconds.ToString(c) + ",");
            sb.AppendLine("    \"fidelity_penalty_deg\": " + ArSimScoring.FidelityPenaltyDeg.ToString(c) + ",");
            sb.AppendLine("    \"range_weight_deg\": " + ArSimScoring.RangeWeightDeg.ToString(c) + ",");
            sb.AppendLine("    \"jitter_window_s\": " + ArSimMetricsAccumulator.JitterWindowS.ToString(c));
            sb.AppendLine("  },");
            sb.AppendLine("  \"session_time_weights\": {");
            sb.AppendLine("    \"standing\": " + SessionTimeWeights.Standing.ToString(c) + ",");
            sb.AppendLine("    \"standing_panning\": " + SessionTimeWeights.StandingPanning.ToString(c) + ",");
            sb.AppendLine("    \"walking\": " + SessionTimeWeights.Walking.ToString(c));
            sb.AppendLine("  },");

            sb.AppendLine("  \"scenarios\": [");
            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                sb.Append("    { \"id\": ").Append(Q(s.Id))
                  .Append(", \"profile\": ").Append(Q(s.Profile.ToString()))
                  .Append(", \"notes\": ").Append(Q(s.Notes))
                  .Append(", \"duration_s\": ").Append(s.DurationS.ToString("F1", c))
                  .Append(", \"rate_hz\": ").Append(s.RateHz.ToString(c))
                  .Append(", \"seaward_azimuth_deg\": ").Append(s.seawardAzimuthDeg.ToString(c))
                  .Append(", \"wave\": ").Append(Q(s.Wave.Describe()))
                  .Append(", \"wave_alpha_instantaneous\": ").Append(s.Wave.AlphaInstantaneous.ToString("F4", c))
                  .Append(", \"wave_alpha_effective\": ").Append(s.Wave.AlphaEffective.ToString("F4", c))
                  .Append(", \"wave_amplitude_pp_m\": ").Append(s.Wave.AmplitudePpSeawardM.ToString("F4", c))
                  .Append(", \"wave_pulse_raw_per_wave_m\": ").Append(s.Wave.PulseRawPerWaveM.ToString("F4", c))
                  .Append(", \"wave_pulse_ceiling_m\": ").Append(JsonFloat(s.Wave.PulseCeilingM, c))
                  .Append(", \"wave_pulse_imu_limited\": ").Append(s.Wave.PulseIsImuLimited ? "true" : "false")
                  .Append(", \"wave_net_drift_per_wave_m\": ").Append(s.Wave.NetDriftPerWaveM.ToString("F4", c))
                  .Append(", \"wave_genuine_drift_per_meter\": ").Append(s.Wave.genuineDriftPerMeterPath.ToString("F4", c))
                  .Append(", \"wave_genuine_jump_fraction\": ").Append(s.Wave.genuineJumpFraction.ToString("F4", c))
                  .Append(", \"imu\": ").Append(Q(s.Imu.Describe()))
                  .Append(", \"gnss\": ").Append(Q(s.Gnss.Describe()))
                  .Append(" }");
                sb.AppendLine(i == scenarios.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"runs\": [");
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i].Result;
                sb.Append("    { \"scenario\": ").Append(Q(r.ScenarioId))
                  .Append(", \"stabilizer\": ").Append(Q(r.StabilizerName))
                  .Append(", \"stabilizer_config\": ").Append(Q(runs[i].StabilizerDescription))
                  .Append(", \"frames\": ").Append(r.Frames)
                  .Append(", \"drift_deg_mean\": ").Append(r.DriftDegMean.ToString("F4", c))
                  .Append(", \"drift_deg_p95\": ").Append(r.DriftDegP95.ToString("F4", c))
                  .Append(", \"slide_m_mean\": ").Append(r.SlideMeanM.ToString("F4", c))
                  .Append(", \"slide_m_p95\": ").Append(r.SlideP95M.ToString("F4", c))
                  .Append(", \"range_err_frac_mean\": ").Append(r.RangeErrFracMean.ToString("F4", c))
                  .Append(", \"range_err_frac_p95\": ").Append(r.RangeErrFracP95.ToString("F4", c))
                  .Append(", \"jitter_deg_per_sec\": ").Append(r.JitterDegPerSec.ToString("F4", c))
                  .Append(", \"pos_err_rms_m\": ").Append(r.PosErrRmsM.ToString("F4", c))
                  .Append(", \"pos_err_max_m\": ").Append(r.PosErrMaxM.ToString("F4", c))
                  .Append(", \"real_motion_fidelity\": ").Append(JsonFloat(r.RealMotionFidelity, c))
                  .Append(", \"suppressed_m\": ").Append(r.SuppressedM.ToString("F4", c))
                  .Append(", \"gate_suppressed_m\": ").Append(JsonFloat(r.GateSuppressedM, c))
                  .Append(", \"false_suppression_m\": ").Append(r.FalseSuppressionM.ToString("F4", c))
                  .Append(", \"still_fraction\": ").Append(r.StillFraction.ToString("F4", c))
                  .Append(", \"fix_count\": ").Append(r.FixCount)
                  .Append(", \"jump_count\": ").Append(r.JumpCount)
                  .Append(", \"genuine_jump_count\": ").Append(r.GenuineJumpCount)
                  .Append(", \"genuine_share_of_error\": ").Append(JsonFloat(r.GenuineShareOfError, c))
                  .Append(", \"phantom_jump_median_m\": ").Append(JsonFloat(r.PhantomJumpMedianM, c))
                  .Append(", \"genuine_jump_median_m\": ").Append(JsonFloat(r.GenuineJumpMedianM, c))
                  .Append(", \"heading_refined\": ").Append(r.HeadingRefined ? "true" : "false")
                  .Append(", \"final_heading_error_deg\": ").Append(JsonFloat(r.FinalHeadingErrorDeg, c))
                  .Append(", \"suppression_reacquire_s\": ").Append(JsonFloat(r.SuppressionReacquireS, c))
                  .Append(", \"true_step_count\": ").Append(r.TrueStepCount.ToString("F1", c))
                  .Append(", \"stabilizer_state\": ").Append(Q(r.StabilizerSummary))
                  .Append(", \"perceptual_cost\": ").Append(r.PerceptualCost.ToString("F4", c))
                  .Append(" }");
                sb.AppendLine(i == runs.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"composite\": {");
            var names = runs.Select(r => r.Result.StabilizerName).Distinct().ToList();
            for (int i = 0; i < names.Count; i++)
            {
                var mine = runs.Where(r => r.Result.StabilizerName == names[i]).Select(r => r.Result).ToList();
                sb.Append("    ").Append(Q(names[i])).Append(": ")
                  .Append(JsonFloat(ArSimScoring.Composite(mine), c));
                sb.AppendLine(i == names.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static string JsonFloat(float v, CultureInfo c) =>
            float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("F4", c);

        static string Q(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";

        /// <summary>&lt;repo root&gt;/relative — Application.dataPath is
        /// &lt;repo&gt;/src/app/MergulhoVirtual/Assets.</summary>
        static string RepoPath(string relative) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../..", relative));
    }
}
