using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>One frame of a completed run, for the CSV time series.</summary>
    internal struct ArSimFrameRecord
    {
        public int Frame;
        public float TimeS;
        public Vector3 GroundTruth;
        public Vector3 Reported;      // the tracker's session pose (raw, uncorrected)
        public Vector3 Corrected;     // the camera world pose the user actually gets
        public float BearingErrDeg;
        public float RangeErrFrac;
        public float PosErrM;
        public bool IsStill;
        public float SuppressionM;    // |origin shift| this frame
        public float GyroMag;
        public float AccelMag;
        public float AccelVertMps2;
        public bool HasFix;
        public float ViewFactor;
        public float RatchetM;
        public float GenuineErrM;
        public float YawErrDeg;
    }

    internal sealed class ArSimRun
    {
        public ArSimResult Result;
        public List<ArSimFrameRecord> Frames;
        public string ScenarioDescription;
        public string StabilizerDescription;
    }

    /// <summary>
    /// Drives one scenario through one stabilizer and measures the result.
    ///
    /// The frame loop reproduces the shipped execution order exactly — see
    /// <see cref="ShippedStabilizer.Step"/> — and the stabilizer only ever sees
    /// what the real one sees: IMU magnitudes, GNSS fixes, and the AR camera's
    /// WORLD position through <see cref="FakeArRig"/>. Ground truth is never
    /// handed to it.
    ///
    /// NOTE on <see cref="ArSimScenario.MetricsStartS"/>: the prologue is simulated
    /// in full and only the SCORING is windowed, so on a windowed scenario
    /// <see cref="ArSimResult.SuppressedM"/> (accumulated inside the window) and
    /// <see cref="ArSimResult.GateSuppressedM"/> (the stabilizer's own whole-run
    /// counter) are not directly comparable. On every unwindowed scenario — which is
    /// all of them bar the two travelled-then-stood rows — they still are.
    /// </summary>
    internal static class ArSimRunner
    {
        /// <param name="seedOffset">Shifts every stochastic model's seed, so the
        /// same scenario can be replayed in a different realisation of the same
        /// statistics. All stabilizers must be run at the SAME offset to stay
        /// comparable.</param>
        public static ArSimRun Run(ArSimScenario scenario, IArStabilizer stabilizer, bool keepFrames,
                                   int seedOffset = 0)
        {
            // Seeds are restored before returning so the scenario object stays
            // reusable and the manifest keeps reporting the base configuration.
            int waveSeed = scenario.Wave.seed, gnssSeed = scenario.Gnss.seed, imuSeed = scenario.Imu.seed;
            if (seedOffset != 0)
            {
                scenario.Wave.seed = waveSeed + seedOffset * 1009;
                scenario.Gnss.seed = gnssSeed + seedOffset * 2017;
                scenario.Imu.seed = imuSeed + seedOffset * 3011;
            }
            try
            {
                return RunOnce(scenario, stabilizer, keepFrames, seedOffset);
            }
            finally
            {
                scenario.Wave.seed = waveSeed;
                scenario.Gnss.seed = gnssSeed;
                scenario.Imu.seed = imuSeed;
            }
        }

        static ArSimRun RunOnce(ArSimScenario scenario, IArStabilizer stabilizer, bool keepFrames,
                                int seedOffset)
        {
            var track = GroundTruthTrack.Build(scenario.Motion, scenario.RateHz,
                                               scenario.Gnss.seed ^ 0x51ED);
            var wave = scenario.Wave;
            var gnss = scenario.Gnss;
            var imu = scenario.Imu;

            wave.seawardAzimuthDeg = scenario.seawardAzimuthDeg;
            wave.Reset();
            gnss.Reset();
            stabilizer.Reset();

            var rig = new FakeArRig();
            var diag = stabilizer as IArStabilizerDiagnostics;
            var metrics = new ArSimMetricsAccumulator();
            var frames = keepFrames ? new List<ArSimFrameRecord>(track.FrameCount) : null;

            // The virtual animal: anchored in WORLD space, out in the water in front
            // of the user, at the moment SCORING starts. For an ordinary scenario
            // that is session start (MetricsStartS = 0, so this is bit-identical to
            // the anchor-at-frame-0 it replaces). For a travelled-then-stood scenario
            // it is where the user stopped — which is the whole point: the animal is
            // the one they see once they have settled, 15 m out from THERE, so the
            // perceptual geometry the metrics assume (and the 15 m the slide columns
            // divide by) stays true instead of being 60 m behind them.
            Vector3 gt0 = track.Pos[0];
            int anchorFrame = Mathf.Clamp(
                scenario.MetricsStartS > 0f ? Mathf.CeilToInt(scenario.MetricsStartS / track.Dt) : 0,
                0, track.FrameCount - 1);
            var anchor = track.Pos[anchorFrame] +
                         Quaternion.Euler(0f, track.YawDeg[anchorFrame], 0f) *
                         new Vector3(0f, 0f, ArSimMetricsAccumulator.AnchorDistanceM);

            Vector3 prevCam = Vector3.zero;
            Vector3 prevGt = gt0;
            bool hasPrev = false;
            int fixCount = 0;
            float lastWalkingT = float.NaN;
            float reacquiredS = float.NaN;
            float dt = track.Dt;

            for (int i = 0; i < track.FrameCount; i++)
            {
                float t = i * dt;
                Vector3 gt = track.Pos[i];

                // ---- the tracker's believed session pose --------------------
                // The true displacement drives the GENUINE error channel only
                // (odometry error grows with path length, doc §1.6). It is an
                // input to the ERROR MODEL, not to any stabilizer — nothing
                // downstream of `sample` ever sees ground truth.
                Vector3 gtDelta = i > 0 ? gt - track.Pos[i - 1] : Vector3.zero;
                var phantom = wave.Advance(dt, track.YawDeg[i], gtDelta);
                // Scale error acts on displacement FROM the session origin
                // (doc §3.6): the tracker gets the shape right and the size wrong.
                Vector3 scaled = gt0 + (gt - gt0) * phantom.ScaleFactor;
                rig.SessionPos = scaled + phantom.PositionErrorM - gt0;
                // SessionPos is relative to the session origin, and the camera
                // world pose is OriginPos + SessionPos, so at t=0 with OriginPos
                // at gt0 the camera sits exactly on ground truth.
                if (i == 0) rig.OriginPos = gt0;

                // ---- sensors ------------------------------------------------
                imu.Sample(t, track.YawRateDegS[i], track.LinAccel[i], track.Kind[i],
                           out float gyroMag, out float accelMag, out Vector3 accelWorld);

                bool hasNewFix = gnss.TryFix(t, gt, out GnssFix fix);
                if (hasNewFix) fixCount++;

                var sample = new ArFrameSample
                {
                    Frame = i,
                    TimeS = t,
                    Dt = dt,
                    GyroMag = gyroMag,
                    AccelMag = accelMag,
                    AccelWorldMps2 = accelWorld,
                    HasAccelSensor = true,
                    HasNewFix = hasNewFix,
                    Fix = fix,
                    CompassHeadingDeg = gnss.CompassHeading(t),
                };

                // ---- the stabilizer gets its one lever ----------------------
                Vector3 originBefore = rig.OriginPos;
                stabilizer.Step(sample, rig);
                Vector3 originShift = rig.OriginPos - originBefore;

                Vector3 cam = rig.CameraWorldPos;

                // ---- metrics ------------------------------------------------
                float bearingErr = BearingErrorDeg(anchor, gt, cam);
                float rangeErr = RangeErrorFraction(anchor, gt, cam);
                float posErr = (cam - gt).magnitude;

                bool isStill = diag?.SuppressingNow ?? false;

                // How long after the user stops does the stabilizer get back to
                // suppressing? Measured from the last genuinely-walking frame, which
                // is what `walk-then-stand` exists to charge. A gate that releases
                // slowly leaves the world unlocked exactly when the user has settled
                // to look at something.
                if (track.Walking[i]) { lastWalkingT = t; reacquiredS = float.NaN; }
                else if (float.IsNaN(reacquiredS) && !float.IsNaN(lastWalkingT) && isStill)
                    reacquiredS = t - lastWalkingT;

                // Everything above runs on every frame — the wave model, the sensors
                // and the stabilizer all need the prologue. Only the SCORING is
                // windowed.
                if (i >= anchorFrame)
                    metrics.Add(dt, bearingErr, rangeErr, posErr, isStill,
                                hasPrev ? gt - prevGt : Vector3.zero,
                                hasPrev ? cam - prevCam : Vector3.zero,
                                originShift,
                                track.Walking[i], track.Moving[i]);

                frames?.Add(new ArSimFrameRecord
                {
                    Frame = i,
                    TimeS = t,
                    GroundTruth = gt,
                    Reported = gt0 + rig.SessionPos,
                    Corrected = cam,
                    BearingErrDeg = bearingErr,
                    RangeErrFrac = rangeErr,
                    PosErrM = posErr,
                    IsStill = isStill,
                    SuppressionM = originShift.magnitude,
                    GyroMag = gyroMag,
                    AccelMag = accelMag,
                    AccelVertMps2 = accelWorld.y,
                    HasFix = hasNewFix,
                    ViewFactor = phantom.ViewFactor,
                    RatchetM = phantom.RatchetM.magnitude,
                    GenuineErrM = phantom.GenuineErrorM.magnitude,
                    YawErrDeg = phantom.YawErrorDeg,
                });

                prevCam = cam;
                prevGt = gt;
                hasPrev = true;
            }

            var result = new ArSimResult
            {
                ScenarioId = scenario.Id,
                StabilizerName = stabilizer.Name,
                Profile = scenario.Profile,
                RateHz = scenario.RateHz,
                DurationS = track.FrameCount * dt,
                FixCount = fixCount,
                JumpCount = wave.JumpCount,
                GenuineJumpCount = wave.GenuineJumpCount,
                GenuineShareOfError = wave.GenuineShareOfError,
                PhantomJumpMedianM = wave.PhantomJumpMedianM,
                GenuineJumpMedianM = wave.GenuineJumpMedianM,
                SuppressionReacquireS = reacquiredS,
                TrueStepCount = track.TrueStepCount,
            };
            metrics.Fill(result);

            if (diag != null)
            {
                result.GateSuppressedM = diag.GateSuppressedPathM;
                result.HeadingRefined = diag.HeadingRefined;
                result.StabilizerSummary = diag.Summarize();
                if (diag.FusionReady && !float.IsNaN(diag.HeadingDeg))
                    result.FinalHeadingErrorDeg =
                        Mathf.DeltaAngle(diag.HeadingDeg, gnss.trueHeadingDeg);
            }

            return new ArSimRun
            {
                Result = result,
                Frames = frames,
                ScenarioDescription = scenario.Describe(),
                StabilizerDescription = stabilizer.Describe(),
            };
        }

        /// <summary>
        /// Signed horizontal bearing error of the anchored animal, degrees.
        ///
        /// The camera's ROTATION is assumed correct (VIO orientation is
        /// gravity-pinned and neither the gate nor the drift correction ever
        /// writes it; the model's yaw channel is recorded separately and is
        /// common-mode across stabilizers, so folding it in would add the same
        /// offset to every row and hide the translation comparison). The whole
        /// error is therefore the camera's world POSITION error projected onto an
        /// angle at the anchor.
        /// </summary>
        public static float BearingErrorDeg(Vector3 anchor, Vector3 groundTruthCam, Vector3 believedCam)
        {
            var trueDir = new Vector2(anchor.x - groundTruthCam.x, anchor.z - groundTruthCam.z);
            var believedDir = new Vector2(anchor.x - believedCam.x, anchor.z - believedCam.z);
            if (trueDir.sqrMagnitude < 1e-8f || believedDir.sqrMagnitude < 1e-8f) return 0f;

            // NOT Vector2.SignedAngle: it is Acos(dot/|a||b|) * sign(cross), and
            // Acos is catastrophically ill-conditioned for near-parallel vectors
            // (acos(1-eps) ~ sqrt(2 eps), so float epsilon alone yields ~0.03 deg
            // of pure noise on two 15 m vectors). That floor was large enough to
            // dominate the jitter metric in the calm scenarios. Atan2(cross, dot)
            // in double precision is exact near zero and has the same sign
            // convention.
            double cross = (double)trueDir.x * believedDir.y - (double)trueDir.y * believedDir.x;
            double dot = (double)trueDir.x * believedDir.x + (double)trueDir.y * believedDir.y;
            return (float)(System.Math.Atan2(cross, dot) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Fractional error in the anchor's APPARENT RANGE: positive when the
        /// animal looks further away than it is, negative when it has crept
        /// toward the viewer.
        ///
        /// This is the other half of the perceptual story and the half a bearing
        /// metric is blind to. The research doc's §3.3 rendered consequence is
        /// "content creeps TOWARD the viewer and grows, then snaps away seaward"
        /// — and because the phantom error is seaward while the user is looking
        /// seaward, most of it lies ALONG the line of sight, where it changes the
        /// animal's apparent size and almost nothing else. Without this metric a
        /// stabilizer could score perfectly on bearing while the shark swims into
        /// the user's face.
        /// </summary>
        public static float RangeErrorFraction(Vector3 anchor, Vector3 groundTruthCam, Vector3 believedCam)
        {
            var trueDir = new Vector2(anchor.x - groundTruthCam.x, anchor.z - groundTruthCam.z);
            var believedDir = new Vector2(anchor.x - believedCam.x, anchor.z - believedCam.z);
            float trueRange = trueDir.magnitude;
            if (trueRange < 1e-4f) return 0f;
            return (believedDir.magnitude - trueRange) / trueRange;
        }

        /// <summary>The per-run CSV time series.</summary>
        public static string ToCsv(ArSimRun run)
        {
            var sb = new StringBuilder(run.Frames.Count * 160);
            sb.AppendLine("# " + run.ScenarioDescription);
            sb.AppendLine("# stabilizer: " + run.Result.StabilizerName + " -- " + run.StabilizerDescription);
            sb.AppendLine("frame,t_s,gt_x,gt_y,gt_z,reported_x,reported_y,reported_z," +
                          "corrected_x,corrected_y,corrected_z,bearing_err_deg,range_err_frac,pos_err_m," +
                          "suppressing,suppression_m,gyro_rad_s,accel_g,accel_vert_mps2,has_fix," +
                          "view_factor,ratchet_m,genuine_err_m,yaw_err_deg");
            var c = CultureInfo.InvariantCulture;
            foreach (var f in run.Frames)
            {
                sb.Append(f.Frame).Append(',').Append(f.TimeS.ToString("F4", c)).Append(',')
                  .Append(f.GroundTruth.x.ToString("F5", c)).Append(',')
                  .Append(f.GroundTruth.y.ToString("F5", c)).Append(',')
                  .Append(f.GroundTruth.z.ToString("F5", c)).Append(',')
                  .Append(f.Reported.x.ToString("F5", c)).Append(',')
                  .Append(f.Reported.y.ToString("F5", c)).Append(',')
                  .Append(f.Reported.z.ToString("F5", c)).Append(',')
                  .Append(f.Corrected.x.ToString("F5", c)).Append(',')
                  .Append(f.Corrected.y.ToString("F5", c)).Append(',')
                  .Append(f.Corrected.z.ToString("F5", c)).Append(',')
                  .Append(f.BearingErrDeg.ToString("F4", c)).Append(',')
                  .Append(f.RangeErrFrac.ToString("F5", c)).Append(',')
                  .Append(f.PosErrM.ToString("F5", c)).Append(',')
                  .Append(f.IsStill ? 1 : 0).Append(',')
                  .Append(f.SuppressionM.ToString("F6", c)).Append(',')
                  .Append(f.GyroMag.ToString("F5", c)).Append(',')
                  .Append(f.AccelMag.ToString("F5", c)).Append(',')
                  .Append(f.AccelVertMps2.ToString("F5", c)).Append(',')
                  .Append(f.HasFix ? 1 : 0).Append(',')
                  .Append(f.ViewFactor.ToString("F3", c)).Append(',')
                  .Append(f.RatchetM.ToString("F4", c)).Append(',')
                  .Append(f.GenuineErrM.ToString("F4", c)).Append(',')
                  .Append(f.YawErrDeg.ToString("F3", c))
                  .Append('\n');
            }
            return sb.ToString();
        }
    }
}
