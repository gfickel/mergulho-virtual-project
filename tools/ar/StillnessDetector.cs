using UnityEngine;

/// <summary>
/// Detects whether the device is physically (near-)still using the IMU:
/// gyroscope rotation rate + linear user acceleration (gravity removed).
///
/// Any AR camera TRANSLATION that happens while IsStill == true is almost
/// certainly spurious — e.g. ocean waves fooling the visual side of the
/// VIO tracker. SpuriousMotionGate consumes this signal.
///
/// NOTE: a hand-held phone is never perfectly still (hand tremor), so the
/// thresholds are deliberately loose and smoothed with an EMA. Tune them
/// on a real device at the real location.
/// </summary>
public class StillnessDetector : MonoBehaviour
{
    [Header("Thresholds — tune on device!")]
    [Tooltip("Smoothed rotation rate (rad/s) below which the device counts as still. Hand tremor is typically 0.02–0.10 rad/s.")]
    public float gyroStillThreshold = 0.12f;

    [Tooltip("Smoothed linear acceleration (in g) below which the device counts as still.")]
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

    /// <summary>Smoothed values, exposed for on-screen debug/tuning UI.</summary>
    public float SmoothedGyro { get; private set; }
    public float SmoothedAccel { get; private set; }

    float quietTimer;

    void Start()
    {
        Input.gyro.enabled = true;
        Input.gyro.updateInterval = 1f / 100f;
    }

    void Update()
    {
        float rot = Input.gyro.rotationRateUnbiased.magnitude; // rad/s
        float acc = Input.gyro.userAcceleration.magnitude;     // in g, gravity removed

        SmoothedGyro  = Mathf.Lerp(SmoothedGyro, rot, emaAlpha);
        SmoothedAccel = Mathf.Lerp(SmoothedAccel, acc, emaAlpha);

        bool loud = rot > gyroStillThreshold * exitMultiplier
                 || acc > accelStillThreshold * exitMultiplier;

        bool quiet = SmoothedGyro < gyroStillThreshold
                  && SmoothedAccel < accelStillThreshold;

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
