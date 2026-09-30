using UnityEngine;

/// <summary>
/// The IMU stillness decision, extracted from <see cref="StillnessDetector"/> so
/// it can be driven offline (tests, Assets/Editor/ArSim) without a phone.
///
/// EMA-smooths the raw gyro/linear-accel magnitudes, then applies slow-entry /
/// fast-exit hysteresis: an instantaneous reading above threshold ×
/// <see cref="Settings.exitMultiplier"/> breaks stillness on the spot, while
/// declaring stillness requires the smoothed values to stay quiet for
/// <see cref="Settings.enterStillTime"/> seconds. Between those two bands the
/// current state is held.
///
/// Engine-free by contract: no Transform, Input, Time, MonoBehaviour or Debug.
/// Every input — including the frame delta and whether a linear-acceleration
/// sensor exists — arrives as an argument to <see cref="Step"/>.
/// </summary>
internal sealed class StillnessCore
{
    /// <summary>The five tunables, mirrored by value from the MonoBehaviour's
    /// public fields each frame so live tuning-panel edits take effect at once.</summary>
    internal struct Settings
    {
        public float gyroStillThreshold;
        public float accelStillThreshold;
        public float enterStillTime;
        public float exitMultiplier;
        public float emaAlpha;

        /// <summary>The values authored on StillnessDetector / in MainScene.</summary>
        public static Settings Defaults => new Settings
        {
            gyroStillThreshold = 0.12f,
            accelStillThreshold = 0.03f,
            enterStillTime = 0.4f,
            exitMultiplier = 1.6f,
            emaAlpha = 0.25f,
        };
    }

    public Settings Config = Settings.Defaults;

    /// <summary>True while the IMU says the device is not translating/rotating.</summary>
    public bool IsStill { get; private set; }

    /// <summary>Smoothed values, exposed for the on-screen tuning/debug UI.</summary>
    public float SmoothedGyro { get; private set; }
    public float SmoothedAccel { get; private set; }

    /// <summary>Seconds the IMU has been continuously quiet (0 once broken).</summary>
    public float QuietTimer => quietTimer;

    float quietTimer;

    public void Reset()
    {
        IsStill = false;
        SmoothedGyro = 0f;
        SmoothedAccel = 0f;
        quietTimer = 0f;
    }

    /// <summary>
    /// Advance one frame.
    /// </summary>
    /// <param name="dt">Frame delta in seconds (the caller's Time.deltaTime).</param>
    /// <param name="gyroMag">Raw gyroscope angular-velocity magnitude (≈rad/s).</param>
    /// <param name="accelMag">Raw linear-acceleration magnitude, gravity removed (≈g).</param>
    /// <param name="hasAccelSensor">False when only a gyroscope is available; the
    /// accel term then drops out of both the loud and the quiet test.</param>
    /// <returns><see cref="IsStill"/> after this step.</returns>
    public bool Step(float dt, float gyroMag, float accelMag, bool hasAccelSensor)
    {
        // ─── dt-DEPENDENT EMA — PRESERVED DELIBERATELY ────────────────────────
        // Mathf.Lerp(previous, raw, emaAlpha) applies the same weight per FRAME
        // regardless of how long the frame was, so the effective time constant
        // scales with frame rate: identical motion smooths ~2× faster at 60 fps
        // than at 30 fps, and every threshold tuned on a 30 fps device is wrong
        // on a 60 fps one. The shipped app pins targetFrameRate to 30 on the AR
        // route, which is why nobody has noticed.
        // This is a KNOWN DEFECT, kept bit-exact so the extraction is provably
        // behaviour-preserving. Fixing it (alpha = 1 - exp(-dt/tau)) is a
        // deliberate, separately-measured change — Assets/Editor/ArSim runs one
        // scenario at 60 Hz specifically to expose it. Do not "clean this up".
        SmoothedGyro  = Mathf.Lerp(SmoothedGyro, gyroMag, Config.emaAlpha);
        SmoothedAccel = Mathf.Lerp(SmoothedAccel, accelMag, Config.emaAlpha);

        // Without a linear-acceleration sensor, fall back to gyro-only gating.
        bool loud = gyroMag > Config.gyroStillThreshold * Config.exitMultiplier
                 || (hasAccelSensor && accelMag > Config.accelStillThreshold * Config.exitMultiplier);

        bool quiet = SmoothedGyro < Config.gyroStillThreshold
                  && (!hasAccelSensor || SmoothedAccel < Config.accelStillThreshold);

        if (loud)
        {
            // Real motion started: react immediately.
            quietTimer = 0f;
            IsStill = false;
        }
        else if (quiet)
        {
            quietTimer += dt;
            if (quietTimer >= Config.enterStillTime)
                IsStill = true;
        }
        // In the hysteresis band between quiet and loud: keep current state.

        return IsStill;
    }
}
