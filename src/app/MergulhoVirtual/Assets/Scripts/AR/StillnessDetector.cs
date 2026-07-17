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
/// thresholds are deliberately loose and smoothed with an EMA. Tune them on
/// a real device at the real location via the AR tuning panel; the panel's
/// telemetry shows SmoothedGyro/SmoothedAccel live, so set thresholds from
/// what you actually measure rather than trusting the units blindly
/// (gyro ≈ rad/s, accel ≈ g, but backends differ slightly per platform).
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

    float quietTimer;
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

        SmoothedGyro  = Mathf.Lerp(SmoothedGyro, rot, emaAlpha);
        SmoothedAccel = Mathf.Lerp(SmoothedAccel, acc, emaAlpha);

        // Without a linear-acceleration sensor, fall back to gyro-only gating.
        bool loud = rot > gyroStillThreshold * exitMultiplier
                 || (hasAccelSensor && acc > accelStillThreshold * exitMultiplier);

        bool quiet = SmoothedGyro < gyroStillThreshold
                  && (!hasAccelSensor || SmoothedAccel < accelStillThreshold);

        if (loud)
        {
            // Real motion started: react immediately.
            quietTimer = 0f;
            IsStill = false;
        }
        else if (quiet)
        {
            quietTimer += Time.deltaTime;
            if (quietTimer >= enterStillTime)
                IsStill = true;
        }
        // In the hysteresis band between quiet and loud: keep current state.
    }
}
