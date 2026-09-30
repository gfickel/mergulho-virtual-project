using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
// UnityEngine.InputSystem.Gyroscope clashes with UnityEngine.Gyroscope.
using InputSystemGyroscope = UnityEngine.InputSystem.Gyroscope;
#endif

/// <summary>
/// Detects whether the device is physically (near-)still using the IMU:
/// gyroscope rotation rate + linear acceleration (gravity removed).
///
/// Any AR camera TRANSLATION that happens while IsStill == true is almost
/// certainly spurious — e.g. ocean waves fooling the visual side of the
/// VIO tracker. SpuriousMotionGate consumes this signal.
///
/// This project runs the new Input System exclusively (activeInputHandler=1),
/// so the sensors come from UnityEngine.InputSystem (the legacy Input.gyro
/// API throws in this configuration). In the editor there are no IMU devices,
/// so HasSensors stays false and IsStill stays false — the gate is inert.
///
/// NOTE: a hand-held phone is never perfectly still (hand tremor), so the
/// thresholds are deliberately loose and smoothed with an EMA. They must be set
/// from values measured on a real device at the real location rather than from
/// the unit labels (gyro ≈ rad/s, accel ≈ g, but backends differ slightly per
/// platform): read SmoothedGyro/SmoothedAccel — logging them, or via
/// ArStabilizationController.BuildTelemetry() — then set the thresholds on this
/// component in the Inspector and rebuild. There is no on-device tuning UI.
///
/// This component is a thin shell: sensor plumbing lives here, the decision
/// itself lives in <see cref="StillnessCore"/> (Assets/Scripts/AR/Core) so it
/// can be measured offline by Assets/Editor/ArSim without a phone.
/// </summary>
public class StillnessDetector : MonoBehaviour
{
    [Header("Thresholds — tune on device!")]
    [Tooltip("Smoothed rotation rate (≈rad/s) below which the device counts as still. Hand tremor is typically 0.02–0.10.")]
    public float gyroStillThreshold = 0.12f;

    [Tooltip("Smoothed linear acceleration (≈g) below which the device counts as still.")]
    public float accelStillThreshold = 0.03f;

    [Tooltip("Seconds the IMU must stay quiet before stillness is declared (prevents flicker).")]
    public float enterStillTime = 0.4f;

    [Tooltip("Instantaneous readings above threshold * this multiplier break stillness immediately (fast exit, slow entry).")]
    public float exitMultiplier = 1.6f;

    [Header("Smoothing")]
    [Range(0.01f, 1f)]
    [Tooltip("EMA factor per frame. Lower = smoother but slower to react.")]
    public float emaAlpha = 0.25f;

    /// <summary>True while the IMU says the device is not translating/rotating.</summary>
    public bool IsStill { get; private set; }

    /// <summary>True when at least the gyroscope is present and enabled.</summary>
    public bool HasSensors { get; private set; }

    /// <summary>Smoothed values, exposed for the on-screen tuning/debug UI.</summary>
    public float SmoothedGyro { get; private set; }
    public float SmoothedAccel { get; private set; }

    readonly StillnessCore core = new StillnessCore();
    bool hasAccelSensor;

    void Start()
    {
#if ENABLE_INPUT_SYSTEM
        if (InputSystemGyroscope.current != null)
        {
            InputSystem.EnableDevice(InputSystemGyroscope.current);
            HasSensors = true;
        }
        if (LinearAccelerationSensor.current != null)
        {
            InputSystem.EnableDevice(LinearAccelerationSensor.current);
            hasAccelSensor = true;
        }
#endif
        if (!HasSensors)
            Debug.LogWarning("StillnessDetector: no gyroscope available — stillness gating disabled (expected in editor).");
    }

    void Update()
    {
        if (!HasSensors)
        {
            IsStill = false;
            return;
        }

        float rot = 0f, acc = 0f;
#if ENABLE_INPUT_SYSTEM
        var gyro = InputSystemGyroscope.current;
        if (gyro != null) rot = gyro.angularVelocity.ReadValue().magnitude;
        if (hasAccelSensor)
        {
            var lin = LinearAccelerationSensor.current;
            if (lin != null) acc = lin.acceleration.ReadValue().magnitude;
        }
#endif

        // Copied EVERY frame, not once: a value written to the public fields while
        // the app runs (an Inspector edit during Play) has to take effect on the
        // next frame exactly as it did before the extraction.
        core.Config = new StillnessCore.Settings
        {
            gyroStillThreshold = gyroStillThreshold,
            accelStillThreshold = accelStillThreshold,
            enterStillTime = enterStillTime,
            exitMultiplier = exitMultiplier,
            emaAlpha = emaAlpha,
        };

        core.Step(Time.deltaTime, rot, acc, hasAccelSensor);

        SmoothedGyro = core.SmoothedGyro;
        SmoothedAccel = core.SmoothedAccel;
        IsStill = core.IsStill;
    }
}
