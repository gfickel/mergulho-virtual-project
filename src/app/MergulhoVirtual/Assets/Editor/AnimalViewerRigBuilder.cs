using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the <c>AnimalViewerRig</c> — the 3D turntable the Espécie screen renders
/// (Decision D1) — as a GameObject at the scene ROOT, far from world origin.
///
/// <para>Extracted from <c>AnimalsScreenBuilder</c> in Slice 6. That builder made the
/// rig and the uGUI Animais screen together; the screen is gone and the rig is not,
/// so the rig's construction moved here rather than being lost. The rig carries no
/// script of its own: <see cref="SpeciesModelViewerAdapter"/> drives it from the app
/// side, finding it by name through <c>AppUiHost.animalViewerRig</c>, which
/// <c>AppUiBuilder</c> wires.</para>
///
/// <para><b>Why a separate rig instead of an in-canvas world-space camera:</b>
/// rendering models in the AR camera means depth conflicts, lighting overlap and AR
/// session overhead. A RenderTexture isolates the viewer completely — the AR session
/// can stay paused on the Espécie route while this camera keeps rendering.</para>
///
/// <para><b>Three things here are load-bearing and easy to undo by accident</b> (all
/// of them documented at length in CLAUDE.md, "Anti-aliasing in the viewer"):</para>
/// <list type="bullet">
///   <item>the camera and all three lights restrict <c>cullingMask</c> to the
///   <c>AnimalViewer</c> layer, and the scene's main directional light clears the
///   same bit — that two-sided exclusion is what keeps the rig and the AR sun from
///   fighting;</item>
///   <item><c>AnimalViewer.renderTexture</c> must stay SINGLE-SAMPLE. URP resolves
///   its own MSAA into the target; a multisampled target produces "Attachment 0 was
///   created with N samples but M samples were requested" every frame on Android and
///   is un-sampleable on most mobile GPUs. This builder forces it back to 1;</item>
///   <item>SMAA only runs with <c>renderPostProcessing = true</c> — set without it,
///   it silently no-ops.</item>
/// </list>
///
/// <para>Idempotent: replaces any existing rig (searching the scene ROOT list, not
/// <c>GameObject.Find</c>, because the rig is normally INACTIVE and Find skips
/// inactive objects). Same iterate-in-code-XOR-in-Editor rule as every other builder.</para>
///
/// <para>Headless: <c>-executeMethod AnimalViewerRigBuilder.BuildHeadless</c>.</para>
/// </summary>
public static class AnimalViewerRigBuilder
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";
    const string RigName = "AnimalViewerRig";
    const string ViewerLayerName = "AnimalViewer";
    const string ViewerRtPath = "Assets/RenderTextures/AnimalViewer.renderTexture";

    /// <summary>Far from world origin so the rig is never within the AR camera's far plane.</summary>
    static readonly Vector3 RigWorldPosition = new Vector3(5000f, 0f, 0f);

    /// <summary>Deep-ocean clear color — lets the model "float" against an aquarium backdrop.</summary>
    static readonly Color ViewerClear = new Color(0.027f, 0.082f, 0.149f, 1f);  // #071526

    [MenuItem("Tools/Mergulho Virtual/Create Animal Viewer Rig", priority = 101)]
    public static void Build()
    {
        int viewerLayer = LayerMask.NameToLayer(ViewerLayerName);
        if (viewerLayer < 0)
        {
            Debug.LogError($"[AnimalViewerRigBuilder] Layer '{ViewerLayerName}' is not defined. " +
                           "Add it under Project Settings > Tags and Layers, then re-run.");
            return;
        }

        var existing = new System.Collections.Generic.List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == RigName) existing.Add(root);
        foreach (var old in existing) Object.DestroyImmediate(old);

        RenderTexture viewerRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(ViewerRtPath);
        if (viewerRT == null)
        {
            Debug.LogWarning($"[AnimalViewerRigBuilder] {ViewerRtPath} not found — the viewer " +
                             "camera will render to a default texture.");
        }
        else if (viewerRT.antiAliasing != 1)
        {
            if (viewerRT.IsCreated()) viewerRT.Release();
            viewerRT.antiAliasing = 1;
            EditorUtility.SetDirty(viewerRT);
        }

        var rig = new GameObject(RigName);
        rig.transform.position = RigWorldPosition;

        // Turntable — empty Transform; the adapter mounts models under it.
        var turntable = new GameObject("Turntable");
        turntable.transform.SetParent(rig.transform, worldPositionStays: false);
        turntable.transform.localPosition = Vector3.zero;
        turntable.layer = viewerLayer;

        // Viewer camera — the adapter re-fits its distance per species, so only the
        // direction, FOV and clear color are authored here.
        var camGo = new GameObject("ViewerCamera");
        camGo.transform.SetParent(rig.transform, worldPositionStays: false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -3.2f);
        camGo.transform.LookAt(rig.transform.position);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ViewerClear;
        cam.cullingMask = 1 << viewerLayer;
        cam.orthographic = false;
        cam.fieldOfView = 35f;        // gentle telephoto = product-render look
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 50f;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.targetTexture = viewerRT;
        camGo.layer = viewerLayer;

        var urpCamData = cam.GetUniversalAdditionalCameraData();
        urpCamData.renderPostProcessing = true;   // required, or SMAA no-ops
        urpCamData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        urpCamData.antialiasingQuality = AntialiasingQuality.High;

        // Three-point rig: warm key + neutral-cool fill + cool rim. Sun-like
        // direction from above on key and fill (no uplighting); rim is behind-above
        // to separate the silhouette from the deep-navy background.
        MakeLight(rig.transform, "KeyLight",  Quaternion.Euler(45f, -35f, 0f),
                  new Color(1f, 0.97f, 0.93f, 1f), 1.2f,  viewerLayer);
        MakeLight(rig.transform, "FillLight", Quaternion.Euler(25f, 140f, 0f),
                  new Color(0.85f, 0.92f, 1f, 1f), 0.45f, viewerLayer);
        MakeLight(rig.transform, "RimLight",  Quaternion.Euler(15f, 200f, 0f),
                  new Color(0.6f, 0.85f, 1f, 1f), 0.4f,   viewerLayer);

        // The adapter activates the rig when it mounts a model; it starts hidden so
        // it costs nothing on every other route.
        rig.SetActive(false);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[AnimalViewerRigBuilder] {RigName} created. Re-run `make ui-setup` so " +
                  "AppUiHost points at the new rig.");
    }

    /// <summary>Batchmode entry point (opens MainScene and saves it).</summary>
    public static void BuildHeadless()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Build();
        EditorSceneManager.SaveOpenScenes();
    }

    static void MakeLight(Transform parent, string name, Quaternion rotation,
                          Color color, float intensity, int layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = rotation;
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << layer;
        go.layer = layer;
    }
}
