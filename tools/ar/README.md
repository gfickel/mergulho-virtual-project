# Beach AR Drift Kit — Setup & Tuning

> **Integrated into the app 2026-07** — see `src/app/MergulhoVirtual/Assets/Scripts/AR/`
> and the `Tools > Mergulho Virtual > Setup AR Stabilization` editor menu. The app
> versions differ from these prototypes: StillnessDetector was rewritten for the new
> Input System (this project disables legacy `Input.gyro`), GPS fixes come from a raw
> Android GNSS `LocationManager` subscription (`GnssProvider.cs`) instead of
> `Input.location`, and heading is auto-refined from the GPS track while walking.
> The app briefly had an on-device tuning panel persisted to JSON; **both were removed on
> 2026-09-29**, so every parameter is now set on the component in the Inspector and needs a
> rebuild. This folder is kept as design notes; the *measurement* half of the field procedure
> below still applies, but "raise X on the spot" now means "log the telemetry at the beach, then
> change X and rebuild" — see CLAUDE.md, "What field tuning costs now".

Three scripts that work together to stop ocean waves from fooling ARKit/ARCore tracking, while preserving full 6DOF (walking around still works).

| Layer | Script | Rate | Fixes |
|---|---|---|---|
| IMU gate | `StillnessDetector` + `SpuriousMotionGate` | per frame | wave-induced phantom translation while standing still |
| AR tracking | ARKit/ARCore (untouched) | per frame | normal smooth 6DOF motion |
| GPS fusion | `GpsArKalmanFusion` | ~1 Hz fixes | slow accumulated drift (meters over minutes) |

## Scene setup

1. Standard AR Foundation scene: `XR Origin` with `Camera Offset > Main Camera` (TrackedPoseDriver).
2. Add an empty GameObject (e.g. `DriftMitigation`) and attach all three scripts.
3. Wire references:
   - `SpuriousMotionGate.stillness` → the `StillnessDetector`
   - `SpuriousMotionGate.xrOrigin` → your `XR Origin`
   - `SpuriousMotionGate.arCamera` → the AR `Main Camera`
   - `GpsArKalmanFusion.arCamera` / `xrOrigin` → same objects
4. If you want continuous geo drift correction, call `ApplyDriftCorrection()` from a small driver script in `LateUpdate`, ideally only when `!stillness.IsStill` (shifts are invisible while the user walks; the IMU gate already holds things steady while they stand).

Execution-order tip: set `StillnessDetector` to run before `SpuriousMotionGate` (Project Settings > Script Execution Order), and call `ApplyDriftCorrection` after the gate has run.

## Permissions

- **Android:** `ACCESS_FINE_LOCATION` in the manifest and request at runtime via `Permission.RequestUserPermission(Permission.FineLocation)` before `GpsArKalmanFusion` starts. "High accuracy" location mode must be on.
- **iOS:** set *Location Usage Description* in Player Settings (writes `NSLocationWhenInUseUsageDescription`). Gyro/compass need no permission.

## Heading calibration (the weakest link)

Converting AR-frame motion to east/north needs to know which real-world azimuth Unity's +Z points at. The sample grabs `Input.compass.trueHeading` once at startup, which is only good to ~5–15°. Better options:

- **iOS only:** set the ARKit session's `worldAlignment` to `gravityAndHeading` — the AR world is then already north-aligned and you can hardcode `headingDeg = 0`.
- Average `trueHeading` over ~2 s at startup instead of a single sample.
- Best: while the user walks 10–20 m, compare the AR displacement direction with the GPS displacement direction and refine `headingDeg` from the angle between them (GPS heading while walking is far better than the magnetometer).
- If your beaches have ARCore Geospatial VPS coverage, its heading output beats all of the above.

## Tuning in the field

Add a debug HUD showing `SmoothedGyro`, `SmoothedAccel`, `IsStill`, `TotalSuppressed`, and `DriftError`. Then, at the actual beach:

1. Stand still facing the ocean. `IsStill` should be solidly true and `TotalSuppressed` should grow whenever the world would previously have swum. If `IsStill` flickers, raise `gyroStillThreshold` / `accelStillThreshold` (hand tremor varies a lot between users — test with several people).
2. Walk normally. `IsStill` must drop within ~1 frame of starting to move; if it lags, lower `exitMultiplier`.
3. Watch for fighting between the gate and ARKit/ARCore relocalization: if content occasionally "fights back" while still, lower `relocalizationJumpThreshold` suppression range (i.e. let big corrections through).
4. Walk a 50–100 m loop and check `DriftError` stays bounded and corrections are imperceptible. If corrections lag, raise `processNoisePerMeter` (trust GPS more); if content wobbles with GPS noise, lower it or raise `minGpsAccuracy`.

## Known limitations

- The equirectangular geo→meters conversion is fine for a few km around the origin; don't reuse one origin across cities.
- The IMU gate suppresses translation only; wave-induced *rotation* error is rare (gravity anchors pitch/roll) but not impossible.
- Legacy `Input.gyro`/`Input.location` are used for brevity. If your project is on the new Input System only, swap in `UnityEngine.InputSystem.Gyroscope`, `LinearAccelerationSensor`, and keep `Input.location` (it works regardless) — enable each sensor with `InputSystem.EnableDevice(...)`.
- A 2-state-per-axis Kalman (position + heading bias, or a full EKF) would outperform this simple version, but this one is easy to reason about and usually enough when combined with the IMU gate.
