using UnityEngine;
using Unity.XR.CoreUtils; // XROrigin (com.unity.xr.core-utils, pulled in by AR Foundation)

/// <summary>
/// Blocks wave-induced phantom translation.
///
/// Every frame it measures how far the AR camera translated. If the
/// StillnessDetector says the device is physically still, that translation
/// cannot be real, so the XR Origin is counter-shifted by the same amount —
/// the camera stays put in world space and your content stops swimming.
///
/// Rotation is never touched, and while the IMU reports genuine motion the
/// gate is fully transparent, so walking around / parallax keeps working
/// exactly as before (full 6DOF preserved).
///
/// You cannot write to the AR camera's transform directly (the
/// TrackedPoseDriver owns it), which is why we move the rig instead.
/// </summary>
public class SpuriousMotionGate : MonoBehaviour
{
    [Header("References")]
    public StillnessDetector stillness;
    public XROrigin xrOrigin;      // the rig we counter-shift
    public Transform arCamera;     // XR Origin > Camera Offset > Main Camera

    [Header("Tuning")]
    [Tooltip("Per-frame translation below this is ignored (meters). Filters float noise.")]
    public float minDelta = 0.0005f;

    [Tooltip("A single-frame jump larger than this (meters) is treated as the tracker deliberately relocalizing itself and is allowed through even while still. Set to 0 to suppress everything.")]
    public float relocalizationJumpThreshold = 0.35f;

    /// <summary>Total spurious translation cancelled this session (for debug HUDs).</summary>
    public Vector3 TotalSuppressed { get; private set; }

    Vector3 lastCamPos;
    bool hasLast;

    void OnEnable() => hasLast = false;

    void LateUpdate()
    {
        if (arCamera == null || xrOrigin == null) return;

        Vector3 camPos = arCamera.position;
        if (!hasLast)
        {
            lastCamPos = camPos;
            hasLast = true;
            return;
        }

        Vector3 delta = camPos - lastCamPos;
        float mag = delta.magnitude;

        bool suppress =
            stillness != null && stillness.IsStill &&
            mag > minDelta &&
            (relocalizationJumpThreshold <= 0f || mag < relocalizationJumpThreshold);

        if (suppress)
        {
            // IMU says we're not moving -> this translation is hallucinated.
            // Counter-shift the whole rig so the camera's world position holds.
            xrOrigin.transform.position -= delta;
            camPos -= delta;
            TotalSuppressed += delta;
        }

        lastCamPos = camPos;
    }
}
