using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>One simulated frame, as a stabilizer sees it.</summary>
    internal struct ArFrameSample
    {
        public int Frame;
        public float TimeS;
        public float Dt;

        /// <summary>Gyro angular-velocity magnitude (≈rad/s) — tremor included.</summary>
        public float GyroMag;

        /// <summary>Linear-acceleration magnitude (≈g), gravity removed. This is
        /// the ONLY accel channel the shipped <c>StillnessDetector</c> consumes.</summary>
        public float AccelMag;

        /// <summary>
        /// Linear acceleration as a WORLD-FRAME VECTOR, gravity removed, m/s².
        ///
        /// Not a new sensor: on device this is `LinearAccelerationSensor` (which
        /// already returns a Vector3 — the shipped StillnessDetector just throws
        /// the direction away) rotated into world by the AR camera's orientation,
        /// or equivalently projected onto `GravitySensor.gravity` for the vertical
        /// component alone. Candidates that need to BAND-LIMIT the accelerometer
        /// require it, because <see cref="AccelMag"/> is a magnitude and therefore
        /// RECTIFIES: a 1.9 Hz gait bob shows up at 3.8 Hz plus DC in the
        /// magnitude, so its fundamental — the thing a step detector looks for —
        /// is not in that channel at all.
        ///
        /// CAVEAT: this channel carries an INDEPENDENT tremor/noise realisation of
        /// the same statistics as <see cref="AccelMag"/>, not the same one, because
        /// AccelMag's arithmetic is kept bit-exact so the shipped stack's published
        /// numbers do not move. |AccelWorldMps2| ≉ AccelMag * g frame by frame.
        /// </summary>
        public Vector3 AccelWorldMps2;

        /// <summary>False to simulate a device with no LinearAccelerationSensor.</summary>
        public bool HasAccelSensor;

        /// <summary>True on the frame a new GNSS fix arrives.</summary>
        public bool HasNewFix;
        public GnssFix Fix;

        /// <summary>Noisy magnetometer heading, for the fusion filter's seed.</summary>
        public float CompassHeadingDeg;
    }

    /// <summary>
    /// The minimum AR rig a stabilizer needs, and — crucially — the FEEDBACK LOOP
    /// the real one has.
    ///
    /// On device the gate reads <c>arCamera.position</c>, which is a WORLD
    /// position: the XR Origin's transform composed with the tracker's session
    /// pose. It corrects by moving the ORIGIN, so its own correction changes what
    /// it measures on the next frame. A simulator that let the stabilizer see the
    /// raw session pose (or that applied corrections to a separate output stream)
    /// would be testing a different algorithm — the gate's `lastCamPos`
    /// bookkeeping only makes sense inside this loop.
    ///
    /// Translation only: the origin is never rotated on device (the gate and the
    /// drift correction both write .position), and the tracker owns rotation.
    /// </summary>
    internal sealed class FakeArRig
    {
        /// <summary>Written by the generator: what the tracker BELIEVES, i.e.
        /// ground truth + phantom wave error + relocalization jumps + scale/bias.</summary>
        public Vector3 SessionPos;

        /// <summary>Written by the stabilizer. This is the only thing it may move.</summary>
        public Vector3 OriginPos;

        /// <summary>What <c>arCamera.position</c> would read this frame.</summary>
        public Vector3 CameraWorldPos => OriginPos + SessionPos;

        public void Reset()
        {
            SessionPos = Vector3.zero;
            OriginPos = Vector3.zero;
        }
    }
}
