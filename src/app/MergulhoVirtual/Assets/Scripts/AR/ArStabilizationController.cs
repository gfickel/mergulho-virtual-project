using System.Text;
using UnityEngine;

/// <summary>
/// Glue for the beach AR drift-mitigation stack
/// (StillnessDetector + SpuriousMotionGate + GnssProvider + GpsArKalmanFusion,
/// all on the same "ARStabilization" GameObject — built by
/// Tools > Mergulho Virtual > Setup AR Stabilization).
///
/// Responsibilities:
///  - Applies the GPS drift correction each LateUpdate, AFTER the gate has run
///    ([DefaultExecutionOrder(200)] vs the gate's 100) and, by default, only
///    while the user is walking — a slow world shift is invisible mid-stride
///    and the IMU gate already holds everything steady while standing still.
///  - Produces a telemetry string for debug readouts.
///
/// EVERY tunable lives on its own component as a [SerializeField]/public field
/// and is set in the Inspector (or from code). There is no runtime tuning
/// surface any more: the on-beach "AJUSTE AR" panel (ArTuningPanel) and the
/// &lt;persistentDataPath&gt;/ar_stabilization_tuning.json round-trip it saved to
/// were both removed on 2026-09-29, so the authored values are the values that
/// run — a stale tuning file can no longer silently override them. To change a
/// parameter, edit it on the component in MainScene (or its code default) and
/// rebuild.
/// </summary>
[DefaultExecutionOrder(200)]
public class ArStabilizationController : MonoBehaviour
{
    [Header("References (auto-wired by the builder)")]
    public StillnessDetector stillness;
    public SpuriousMotionGate gate;
    public GnssProvider gnss;
    public GpsArKalmanFusion fusion;

    [Header("Behavior")]
    [Tooltip("Master switch for the IMU stillness gate.")]
    public bool gateEnabled = true;

    // OFF by default since 2026-09-28. The offline wave-drift simulator
    // (Assets/Editor/ArSim/, `make ar-sim`) measured this layer as a large net
    // HARM in every scenario, including `walk-calm`, which contains no waves at
    // all: it dragged the world 9.4 m over 48 s and took camera position error
    // from 0.13 m to 2.65 m RMS. Two structural reasons, neither of which is a
    // tuning problem:
    //   1. Nothing here is geo-anchored. BeachSharkSpawner parents its
    //      instances to a scene ROOT, not to the XR Origin, so the animals live
    //      at fixed Unity world coordinates. Shifting the origin toward a GPS
    //      estimate cannot correct content that was never in GPS space -- it
    //      only slides the user relative to it.
    //   2. GPS noise (~1-4 m) is an order of magnitude larger than the
    //      wave-induced phantom drift it was meant to fix (0.05-0.3 m; see
    //      docs/ar-wave-drift-research.md section 3). The filter spends most of
    //      its time chasing that noise at the full maxCorrectionSpeed slew.
    // Re-enable it if geo-anchored content is ever added -- the layer is sound
    // for the problem it was written for (bounding minutes-scale geo drift),
    // just not for wave jitter.
    [Tooltip("Master switch for bleeding GPS drift corrections into the XR Origin. Default OFF: measured as a net harm while content is not geo-anchored (see comment above / make ar-sim).")]
    public bool driftCorrectionEnabled = false;

    [Tooltip("Only apply drift corrections while the IMU reports motion (recommended: shifts are invisible while walking).")]
    public bool correctOnlyWhileMoving = true;

    readonly StringBuilder telemetry = new StringBuilder(512);

    void LateUpdate()
    {
        if (gate != null && gate.enabled != gateEnabled)
            gate.enabled = gateEnabled;

        if (!driftCorrectionEnabled || fusion == null || !fusion.Ready) return;
        if (correctOnlyWhileMoving && stillness != null && stillness.IsStill) return;
        fusion.ApplyDriftCorrection();
    }

    // --------------------------------------------------------------- telemetry

    /// <summary>Multi-line live status of the whole stack, for a debug readout.</summary>
    public string BuildTelemetry()
    {
        telemetry.Length = 0;

        if (stillness != null)
            telemetry.AppendFormat("IMU  parado={0}  giro={1:F3}  acel={2:F3}{3}\n",
                stillness.IsStill ? "SIM" : "não",
                stillness.SmoothedGyro, stillness.SmoothedAccel,
                stillness.HasSensors ? "" : "  (sem sensores!)");

        if (gate != null)
        {
            telemetry.AppendFormat("Gate {0}  suprimido={1:F2} m\n",
                gateEnabled ? "ligado" : "DESLIGADO", gate.TotalSuppressed.magnitude);
            // Only shown while the budget is on, i.e. never at the shipped default.
            if (gate.correctionBudgetPerMeter > 0f)
                telemetry.AppendFormat("Orçamento  saldo={0:F2} m  aplicado={1:F2} m\n",
                    gate.CorrectionBudgetM, gate.BudgetAdmittedM);
        }

        if (gnss != null)
        {
            if (gnss.HasFix)
            {
                var f = gnss.Latest;
                telemetry.AppendFormat("GNSS {0}  acc={1:F1} m  idade={2:F1} s  sat={3}  fixes={4}\n",
                    gnss.UsingNativeGnss ? "nativo" : "unity",
                    f.horizontalAccuracy, gnss.FixAgeSeconds, f.satellites, gnss.FixCount);
                if (f.hasSpeed)
                    telemetry.AppendFormat("     vel={0:F1} m/s  rumo={1:F0}°\n", f.speedMps, f.bearingDeg);
            }
            else
                telemetry.Append("GNSS aguardando primeiro fix…\n");
        }

        if (fusion != null)
        {
            if (fusion.Ready)
            {
                var drift = fusion.DriftError;
                var sd = fusion.EstimateStdDev;
                telemetry.AppendFormat("Fusão drift=({0:F1}, {1:F1}) m  |{2:F1} m|  σ=({3:F1}, {4:F1})\n",
                    drift.x, drift.y, drift.magnitude, sd.x, sd.y);
                telemetry.AppendFormat("Rumo Unity+Z={0:F1}°  {1}\n",
                    fusion.HeadingDeg,
                    fusion.HeadingRefined ? "(refinado por GPS)" : "(bússola — ande ~8 m p/ refinar)");
            }
            else
                telemetry.Append("Fusão aguardando GNSS…\n");
        }

        telemetry.AppendFormat("Correção {0}{1}",
            driftCorrectionEnabled ? "ligada" : "DESLIGADA",
            correctOnlyWhileMoving ? " (só andando)" : "");

        return telemetry.ToString();
    }
}
