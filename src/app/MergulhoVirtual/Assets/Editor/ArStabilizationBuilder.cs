using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the beach AR drift-mitigation stack into the active scene:
///
///   ARStabilization (scene root)
///     ├ StillnessDetector        — IMU "is the phone physically still?"
///     ├ SpuriousMotionGate       — cancels wave-induced phantom translation
///     ├ GnssProvider             — raw Android GNSS (falls back to Input.location)
///     ├ GpsArKalmanFusion        — GPS↔AR Kalman drift estimate + heading calib
///     └ ArStabilizationController— glue (applies the drift correction)
///
/// Idempotent: prompts to replace the root if it already exists. All references
/// are wired via SerializedObject — no Inspector dragging needed.
///
/// NOTE: re-running this REPLACES the components, so every tunable goes back to
/// its code default. It also no longer builds any UI — the on-beach "AJUSTE AR"
/// tuning panel (ScreenUI/MainScreen/ArTuning) and its JSON persistence were
/// removed on 2026-09-29. Parameters are set on the components in the Inspector.
/// </summary>
public static class ArStabilizationBuilder
{
    [MenuItem("Tools/Mergulho Virtual/Setup AR Stabilization", priority = 120)]
    public static void Build()
    {
        // ------------------------------------------------ locate scene anchors
        var xrOriginGo = FindSceneRoot("XR Origin");
        var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("AR Stabilization",
                "Could not find an 'XR Origin' root GameObject with an XROrigin component. Open MainScene first.", "OK");
            return;
        }
        var arCamera = xrOrigin.Camera != null
            ? xrOrigin.Camera.transform
            : xrOriginGo.transform.Find("Camera Offset/Main Camera");
        if (arCamera == null)
        {
            EditorUtility.DisplayDialog("AR Stabilization",
                "Could not find the AR camera under XR Origin/Camera Offset/Main Camera.", "OK");
            return;
        }

        // ------------------------------------------------ replace previous run
        var existing = FindSceneRoot("ARStabilization");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("AR Stabilization",
                "ARStabilization already exists in the scene. Replace it? " +
                "Every tuned parameter value on it will go back to its code default.",
                "Replace", "Cancel")) return;
            Object.DestroyImmediate(existing);
        }

        // ------------------------------------------------ runtime components
        var root = new GameObject("ARStabilization");
        var stillness = root.AddComponent<StillnessDetector>();
        var gate = root.AddComponent<SpuriousMotionGate>();
        var gnss = root.AddComponent<GnssProvider>();
        var fusion = root.AddComponent<GpsArKalmanFusion>();
        var controller = root.AddComponent<ArStabilizationController>();

        Wire(gate, "stillness", stillness);
        Wire(gate, "xrOrigin", xrOrigin);
        Wire(gate, "arCamera", arCamera);

        Wire(fusion, "arCamera", arCamera);
        Wire(fusion, "xrOrigin", xrOrigin);
        Wire(fusion, "gnss", gnss);

        Wire(controller, "stillness", stillness);
        Wire(controller, "gate", gate);
        Wire(controller, "gnss", gnss);
        Wire(controller, "fusion", fusion);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[ArStabilizationBuilder] ARStabilization created. Save the scene (Ctrl+S). " +
                  "Tune the parameters on the components in the Inspector — there is no on-device panel.");
    }

    // ---------------------------------------------------------------- helpers

    static void Wire(Component target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[ArStabilizationBuilder] {target.GetType().Name} has no '{field}' field — recompile and re-run.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Root-object scan that also finds INACTIVE roots (GameObject.Find can't).</summary>
    static GameObject FindSceneRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }
}
