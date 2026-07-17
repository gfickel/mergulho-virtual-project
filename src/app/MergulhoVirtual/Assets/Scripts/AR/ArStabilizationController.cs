using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Glue + configuration hub for the beach AR drift-mitigation stack
/// (StillnessDetector + SpuriousMotionGate + GnssProvider + GpsArKalmanFusion,
/// all on the same "ARStabilization" GameObject — built by
/// Tools > Mergulho Virtual > Setup AR Stabilization).
///
/// Responsibilities:
///  - Applies the GPS drift correction each LateUpdate, AFTER the gate has run
///    ([DefaultExecutionOrder(200)] vs the gate's 100) and, by default, only
///    while the user is walking — a slow world shift is invisible mid-stride
///    and the IMU gate already holds everything steady while standing still.
///  - Loads every tunable from a JSON file at
///    Application.persistentDataPath/ar_stabilization_tuning.json on startup
///    and saves it back when the tuning panel changes something. That's what
///    lets the Kalman/gate/stillness parameters be tuned ON THE BEACH without
///    recompiling — values survive app restarts and can be pulled off the
///    device with adb for committing back as new code defaults.
///  - Produces the telemetry string the tuning panel displays.
/// </summary>
[DefaultExecutionOrder(200)]
public class ArStabilizationController : MonoBehaviour
{
    const string TuningFileName = "ar_stabilization_tuning.json";

    [Header("References (auto-wired by the builder)")]
    public StillnessDetector stillness;
    public SpuriousMotionGate gate;
    public GnssProvider gnss;
    public GpsArKalmanFusion fusion;

    [Header("Behavior")]
    [Tooltip("Master switch for the IMU stillness gate.")]
    public bool gateEnabled = true;

    [Tooltip("Master switch for bleeding GPS drift corrections into the XR Origin.")]
    public bool driftCorrectionEnabled = true;

    [Tooltip("Only apply drift corrections while the IMU reports motion (recommended: shifts are invisible while walking).")]
    public bool correctOnlyWhileMoving = true;

    /// <summary>Serializable mirror of every field the tuning panel can change.</summary>
    [Serializable]
    public class TuningData
    {
        public int version = 1;
        // StillnessDetector
        public float gyroStillThreshold;
        public float accelStillThreshold;
        public float enterStillTime;
        public float exitMultiplier;
        public float emaAlpha;
        // SpuriousMotionGate
        public float minDelta;
        public float relocalizationJumpThreshold;
        // GpsArKalmanFusion
        public float processNoisePerMeter;
        public float minGpsAccuracy;
        public float maxUsableAccuracy;
        public float maxCorrectionSpeed;
        public float headingSegmentMeters;
        public float headingBlend;
        // Controller
        public bool gateEnabled;
        public bool driftCorrectionEnabled;
        public bool correctOnlyWhileMoving;
    }

    TuningData defaults;   // component values as authored in the scene/code
    float saveDueAt = -1f; // debounced save
    readonly StringBuilder telemetry = new StringBuilder(512);

    public static string TuningFilePath =>
        Path.Combine(Application.persistentDataPath, TuningFileName);

    void Awake()
    {
        // Capture the authored values BEFORE the saved tuning overrides them,
        // so "Restaurar padrões" returns to the code/scene defaults.
        defaults = Capture();
        LoadTuning();
    }

    void LateUpdate()
    {
        if (gate != null && gate.enabled != gateEnabled)
            gate.enabled = gateEnabled;

        if (saveDueAt > 0f && Time.unscaledTime >= saveDueAt)
            SaveTuning();

        if (!driftCorrectionEnabled || fusion == null || !fusion.Ready) return;
        if (correctOnlyWhileMoving && stillness != null && stillness.IsStill) return;
        fusion.ApplyDriftCorrection();
    }

    // ------------------------------------------------------------------ tuning

    TuningData Capture()
    {
        var d = new TuningData();
        if (stillness != null)
        {
            d.gyroStillThreshold = stillness.gyroStillThreshold;
            d.accelStillThreshold = stillness.accelStillThreshold;
            d.enterStillTime = stillness.enterStillTime;
            d.exitMultiplier = stillness.exitMultiplier;
            d.emaAlpha = stillness.emaAlpha;
        }
        if (gate != null)
        {
            d.minDelta = gate.minDelta;
            d.relocalizationJumpThreshold = gate.relocalizationJumpThreshold;
        }
        if (fusion != null)
        {
            d.processNoisePerMeter = fusion.processNoisePerMeter;
            d.minGpsAccuracy = fusion.minGpsAccuracy;
            d.maxUsableAccuracy = fusion.maxUsableAccuracy;
            d.maxCorrectionSpeed = fusion.maxCorrectionSpeed;
            d.headingSegmentMeters = fusion.headingSegmentMeters;
            d.headingBlend = fusion.headingBlend;
        }
        d.gateEnabled = gateEnabled;
        d.driftCorrectionEnabled = driftCorrectionEnabled;
        d.correctOnlyWhileMoving = correctOnlyWhileMoving;
        return d;
    }

    void Apply(TuningData d)
    {
        if (stillness != null)
        {
            stillness.gyroStillThreshold = d.gyroStillThreshold;
            stillness.accelStillThreshold = d.accelStillThreshold;
            stillness.enterStillTime = d.enterStillTime;
            stillness.exitMultiplier = d.exitMultiplier;
            stillness.emaAlpha = d.emaAlpha;
        }
        if (gate != null)
        {
            gate.minDelta = d.minDelta;
            gate.relocalizationJumpThreshold = d.relocalizationJumpThreshold;
        }
        if (fusion != null)
        {
            fusion.processNoisePerMeter = d.processNoisePerMeter;
            fusion.minGpsAccuracy = d.minGpsAccuracy;
            fusion.maxUsableAccuracy = d.maxUsableAccuracy;
            fusion.maxCorrectionSpeed = d.maxCorrectionSpeed;
            fusion.headingSegmentMeters = d.headingSegmentMeters;
            fusion.headingBlend = d.headingBlend;
        }
        gateEnabled = d.gateEnabled;
        driftCorrectionEnabled = d.driftCorrectionEnabled;
        correctOnlyWhileMoving = d.correctOnlyWhileMoving;
    }

    void LoadTuning()
    {
        try
        {
            if (!File.Exists(TuningFilePath)) return;
            var d = JsonUtility.FromJson<TuningData>(File.ReadAllText(TuningFilePath));
            if (d != null)
            {
                Apply(d);
                Debug.Log("ArStabilization: loaded tuning from " + TuningFilePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ArStabilization: failed to load tuning — using defaults. " + e.Message);
        }
    }

    public void SaveTuning()
    {
        saveDueAt = -1f;
        try
        {
            File.WriteAllText(TuningFilePath, JsonUtility.ToJson(Capture(), prettyPrint: true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("ArStabilization: failed to save tuning. " + e.Message);
        }
    }

    /// <summary>Called by the tuning panel after any slider/toggle change.</summary>
    public void MarkTuningDirty() => saveDueAt = Time.unscaledTime + 1f;

    public void ResetToDefaults()
    {
        if (defaults == null) return;
        Apply(defaults);
        SaveTuning();
    }

    // --------------------------------------------------------------- telemetry

    /// <summary>Multi-line live status for the tuning panel HUD.</summary>
    public string BuildTelemetry()
    {
        telemetry.Length = 0;

        if (stillness != null)
            telemetry.AppendFormat("IMU  parado={0}  giro={1:F3}  acel={2:F3}{3}\n",
                stillness.IsStill ? "SIM" : "não",
                stillness.SmoothedGyro, stillness.SmoothedAccel,
                stillness.HasSensors ? "" : "  (sem sensores!)");

        if (gate != null)
            telemetry.AppendFormat("Gate {0}  suprimido={1:F2} m\n",
                gateEnabled ? "ligado" : "DESLIGADO", gate.TotalSuppressed.magnitude);

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
