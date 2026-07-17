using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.InferenceEngine;
using TMPro;
using Unity.Mathematics; 
using Unity.Burst; 
using Unity.Jobs; 


public class CameraFeedToInference : MonoBehaviour
{
    [SerializeField]
    private ARCameraManager cameraManager;

    [SerializeField]
    private RawImage debugDisplay;

    [SerializeField]
    private ModelAsset modelAsset;

    [SerializeField]
    private int inputWidth = 224;

    [SerializeField]
    private int inputHeight = 224;

    [SerializeField]
    private TMP_Text classificationResultText;

    [SerializeField]
    private SeaDetector seaDetector;

    [Tooltip("Debug: additionally classify each frame at 0/90/180/270 degrees, log all four sea scores, and dump the frames to persistentDataPath. Blocking — enable only to diagnose sensor orientation.")]
    public bool debugTryAllRotations = false;

    [Tooltip("Run the legacy mobilenet ImageNet classifier and show its prediction on the debug label. Off (default): the model is never loaded and the label shows the SeaDetector result instead.")]
    [SerializeField] private bool runImageNetClassifier = false;

    [Tooltip("Debug: spawn a button on the AR HUD that cycles the sea-detector frame rotation (auto/0/90/180/270) on the fly, to verify orientation in the field.")]
    [SerializeField] private bool showRotationDebugButton = true;

    // -1 = auto (derive from Screen.orientation); otherwise 0/90/180/270.
    private int rotationOverride = -1;
    private TextMeshProUGUI rotationButtonLabel;

    public bool inferenceEnabled = true;

    private Texture2D cameraTexture;
    private Texture2D resizedTexture;
    private const float FRAME_INTERVAL = 1f;
    private float lastFrameTime = -FRAME_INTERVAL;

    private Model runtimeModel;
    private Worker worker;
    private Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>();
    private List<string> classLabels = new List<string>();

    private Tensor pendingInput;
    private Tensor<float> pendingOutput;
    private bool inferenceInFlight = false;
    private System.Diagnostics.Stopwatch inferenceStopwatch;

    void Start()
    {
        if (seaDetector == null)
        {
            seaDetector = GetComponent<SeaDetector>();
        }

        if (showRotationDebugButton)
        {
            CreateRotationDebugButton();
        }

        if (!runImageNetClassifier)
        {
            Debug.Log("[Inference] ImageNet classifier disabled - debug label shows SeaDetector output");
        }
        else if (modelAsset != null)
        {
            LoadClassLabels();
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
            resizedTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
            Debug.Log($"[Inference] Model loaded - Input shape: {inputWidth}x{inputHeight}, Classes: {classLabels.Count}");
            WarmUp();
        }
        else
        {
            Debug.LogWarning("[Inference] No model asset assigned!");
        }
    }

    // Run one synchronous inference on a blank tensor so shader compile and GPU
    // allocations happen now (during splash) instead of stuttering the first
    // real camera frame.
    private void WarmUp()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var warmInput = TextureConverter.ToTensor(resizedTexture, inputWidth, inputHeight, 3);
        worker.Schedule(warmInput);
        var output = worker.PeekOutput() as Tensor<float>;
        output.DownloadToArray();
        sw.Stop();
        Debug.Log($"[Inference] Warmup completed in {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    private void LoadClassLabels()
    {
        TextAsset labelFile = Resources.Load<TextAsset>("class_desc");
        if (labelFile != null)
        {
            string[] lines = labelFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    classLabels.Add(trimmed);
                }
            }
            Debug.Log($"[Inference] Loaded {classLabels.Count} class labels");
        }
        else
        {
            Debug.LogWarning("[Inference] Could not load class_desc.txt from Resources/Models/");
        }
    }

    void OnEnable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnCameraFrameReceived;
        }
    }

    void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }

    void OnDestroy()
    {
        pendingInput?.Dispose();
        worker?.Dispose();
        foreach (var input in inputs.Values)
        {
            input.Dispose();
        }
        inputs.Clear();
    }

    void Update()
    {
        UpdateSeaLabel();

        if (!inferenceInFlight)
        {
            return;
        }

        if (!pendingOutput.IsReadbackRequestDone())
        {
            return;
        }

        // Readback is complete — pulling the data here does not block.
        var outputData = pendingOutput.DownloadToArray();
        inferenceStopwatch.Stop();
        double runtimeMs = inferenceStopwatch.Elapsed.TotalMilliseconds;

        ProcessClassificationResults(pendingOutput, outputData, runtimeMs);

        pendingInput.Dispose();
        pendingInput = null;
        pendingOutput = null;
        inferenceInFlight = false;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (!inferenceEnabled)
        {
            return;
        }

        if (inferenceInFlight)
        {
            return;
        }

        if (Time.time - lastFrameTime < FRAME_INTERVAL)
        {
            return;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            return;
        }

        lastFrameTime = Time.time;

        Debug.Log($"[CameraFeed] Frame received - Format: {image.format}, Size: {image.width}x{image.height}, Timestamp: {image.timestamp}");

        // Convert to Texture2D for display
        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, image.width, image.height),
            outputDimensions = new Vector2Int(image.width, image.height),
            outputFormat = TextureFormat.RGBA32,
            transformation = XRCpuImage.Transformation.MirrorY
        };

        int size = image.GetConvertedDataSize(conversionParams);
        var buffer = new NativeArray<byte>(size, Allocator.Temp);

        image.Convert(conversionParams, buffer);
        image.Dispose();

        if (cameraTexture == null || cameraTexture.width != image.width || cameraTexture.height != image.height)
        {
            cameraTexture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
        }

        cameraTexture.LoadRawTextureData(buffer);
        cameraTexture.Apply();

        buffer.Dispose();

        if (debugDisplay != null)
        {
            debugDisplay.texture = cameraTexture;
            debugDisplay.enabled = true;
        }

        Debug.Log($"[CameraFeed] Frame processed - Texture size: {cameraTexture.width}x{cameraTexture.height}");

        // Run inference on the captured frame
        RunInference(cameraTexture);

        // Same frame also feeds the zero-shot sea detector (async, own worker).
        // The CPU image is sensor-native landscape; hand the detector an
        // upright, display-aspect view of it instead.
        if (seaDetector != null)
        {
            if (debugTryAllRotations)
            {
                DebugClassifyAllRotations();
            }
            seaDetector.Classify(GetUprightCameraTexture());
        }
    }

    private Texture2D uprightTexture;

    // The XRCpuImage arrives in the sensor's native landscape orientation no
    // matter how the phone is held. Rotate it upright for the current display
    // orientation, then center-crop to the display aspect so the model sees
    // the same framing the user sees on screen (the AR background cover-crops
    // the wider sensor FOV).
    private Texture GetUprightCameraTexture()
    {
        // Rotations are in this pipeline's bottom-up (MirrorY-converted) coords,
        // which inverts the usual top-down convention: portrait needs 270, not 90.
        // Confirmed empirically via the rotation sweep (portrait: rot=270 -> 98% sea).
        int rotation;
        if (rotationOverride >= 0)
        {
            rotation = rotationOverride;
        }
        else switch (Screen.orientation)
        {
            case ScreenOrientation.Portrait: rotation = 270; break;
            case ScreenOrientation.PortraitUpsideDown: rotation = 90; break;
            case ScreenOrientation.LandscapeRight: rotation = 180; break;
            default: rotation = 0; break; // LandscapeLeft == sensor native
        }

        int w = cameraTexture.width, h = cameraTexture.height;
        Color32[] src = cameraTexture.GetPixels32();
        Color32[] rotated = RotatePixels(src, w, h, rotation, out int dw, out int dh);

        float displayAspect = (float)Screen.width / Screen.height;
        int cw = dw, ch = dh;
        if ((float)dw / dh > displayAspect) cw = Mathf.RoundToInt(dh * displayAspect);
        else ch = Mathf.RoundToInt(dw / displayAspect);
        cw = Mathf.Clamp(cw, 1, dw);
        ch = Mathf.Clamp(ch, 1, dh);
        int x0 = (dw - cw) / 2, y0 = (dh - ch) / 2;

        if (uprightTexture == null || uprightTexture.width != cw || uprightTexture.height != ch)
        {
            Destroy(uprightTexture);
            uprightTexture = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            Debug.Log($"[CameraFeed] Sea-detector input: rotation={rotation}, crop {cw}x{ch} from {dw}x{dh} (display {Screen.width}x{Screen.height})");
        }

        if (cw == dw && ch == dh)
        {
            uprightTexture.SetPixels32(rotated);
        }
        else
        {
            var cropped = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
                System.Array.Copy(rotated, (y0 + y) * dw + x0, cropped, y * cw, cw);
            uprightTexture.SetPixels32(cropped);
        }
        uprightTexture.Apply();
        return uprightTexture;
    }

    private static Color32[] RotatePixels(Color32[] src, int w, int h, int rotation, out int dw, out int dh)
    {
        bool quarterTurn = rotation == 90 || rotation == 270;
        dw = quarterTurn ? h : w;
        dh = quarterTurn ? w : h;
        if (rotation == 0) return src;

        var rotated = new Color32[src.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int dx, dy;
                if (rotation == 90) { dx = y; dy = w - 1 - x; }
                else if (rotation == 270) { dx = h - 1 - y; dy = x; }
                else { dx = w - 1 - x; dy = h - 1 - y; }
                rotated[dy * dw + dx] = src[row + x];
            }
        }
        return rotated;
    }

    private Texture2D debugRotTexture;

    // Debug: run the current frame through the sea detector at all four
    // rotations (full frame, no crop) and log the scores side by side.
    // Blocking — only for diagnosing sensor orientation; turn off afterwards.
    private void DebugClassifyAllRotations()
    {
        Color32[] src = cameraTexture.GetPixels32();
        int w = cameraTexture.width, h = cameraTexture.height;
        var sb = new System.Text.StringBuilder($"[CameraFeed] rotation sweep ({w}x{h}, orientation={Screen.orientation}):");
        foreach (int rot in new[] { 0, 90, 180, 270 })
        {
            Color32[] px = RotatePixels(src, w, h, rot, out int dw, out int dh);
            if (debugRotTexture == null || debugRotTexture.width != dw || debugRotTexture.height != dh)
            {
                Destroy(debugRotTexture);
                debugRotTexture = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            }
            debugRotTexture.SetPixels32(px);
            debugRotTexture.Apply();
            var (score, top) = seaDetector.ClassifyBlocking(debugRotTexture);
            sb.Append($"\n  rot={rot,3}: sea={score:P0} top={top}");
        }
        Debug.Log(sb.ToString());

        // Dump the latest raw frame + the production (upright, cropped) input
        // so they can be pulled off the device and inspected. Overwritten each tick.
        try
        {
            string rawPath = System.IO.Path.Combine(Application.persistentDataPath, "sea_debug_raw.png");
            System.IO.File.WriteAllBytes(rawPath, cameraTexture.EncodeToPNG());
            var upright = GetUprightCameraTexture() as Texture2D;
            string uprightPath = System.IO.Path.Combine(Application.persistentDataPath, "sea_debug_upright.png");
            System.IO.File.WriteAllBytes(uprightPath, upright.EncodeToPNG());
            if (!debugFramePathLogged)
            {
                debugFramePathLogged = true;
                Debug.Log($"[CameraFeed] Debug frames saved to {rawPath} (+ sea_debug_upright.png)");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CameraFeed] Could not save debug frames: {e.Message}");
        }
    }

    private bool debugFramePathLogged;

    private void RunInference(Texture2D sourceTexture)
    {
        if (worker == null || sourceTexture == null || inferenceInFlight)
        {
            return;
        }

        Graphics.ConvertTexture(sourceTexture, resizedTexture);
        var inputTensor = TextureConverter.ToTensor(resizedTexture, inputWidth, inputHeight, 3);

        inferenceStopwatch = System.Diagnostics.Stopwatch.StartNew();
        worker.Schedule(inputTensor);

        var outputTensor = worker.PeekOutput() as Tensor<float>;
        outputTensor.ReadbackRequest();

        pendingInput = inputTensor;
        pendingOutput = outputTensor;
        inferenceInFlight = true;
    }

    /// <summary>Cycle the sea-detector frame rotation: auto -> 0 -> 90 -> 180 -> 270 -> auto.
    /// Wired to the runtime debug button; safe to call from any UI Button.</summary>
    public void CycleRotationOverride()
    {
        rotationOverride = rotationOverride < 0 ? 0 : rotationOverride + 90;
        if (rotationOverride >= 360)
        {
            rotationOverride = -1;
        }

        string label = rotationOverride < 0 ? "auto" : $"{rotationOverride}°";
        if (rotationButtonLabel != null)
        {
            rotationButtonLabel.text = $"Rot: {label}";
        }
        Debug.Log($"[CameraFeed] Sea-detector rotation override: {label}");
    }

    // Debug-only control, so it's built from code instead of living in the
    // scene: spawned as a sibling of the HUD label's screen panel so
    // ScreenManager's SetActive toggling shows/hides it with the AR screen.
    private void CreateRotationDebugButton()
    {
        if (classificationResultText == null || classificationResultText.canvas == null)
        {
            Debug.LogWarning("[CameraFeed] Rotation debug button skipped: no HUD label/canvas to attach to");
            return;
        }

        // Walk up from the label to the screen panel directly under the canvas
        // (MainScreen), so the button belongs to the AR HUD.
        Transform canvasRoot = classificationResultText.canvas.transform;
        Transform screenRoot = classificationResultText.transform;
        while (screenRoot.parent != null && screenRoot.parent != canvasRoot)
        {
            screenRoot = screenRoot.parent;
        }

        var go = new GameObject("RotationDebugButton",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(screenRoot, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -180f);
        rt.sizeDelta = new Vector2(220f, 90f);

        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        go.GetComponent<LayoutElement>().ignoreLayout = true; // in case the screen root has a layout group
        go.GetComponent<Button>().onClick.AddListener(CycleRotationOverride);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        rotationButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
        rotationButtonLabel.text = "Rot: auto";
        rotationButtonLabel.fontSize = 34f;
        rotationButtonLabel.alignment = TextAlignmentOptions.Center;
        rotationButtonLabel.color = Color.white;
        rotationButtonLabel.raycastTarget = false;
    }

    private string lastSeaLabel;

    // With the ImageNet classifier off, the debug label mirrors the sea
    // detector instead. Its result lands asynchronously in SeaDetector.Update,
    // so poll the public state and only touch the TMP text when it changes.
    private void UpdateSeaLabel()
    {
        if (runImageNetClassifier || classificationResultText == null || seaDetector == null)
        {
            return;
        }

        string top = seaDetector.GetTopClassName();
        string text = string.IsNullOrEmpty(top)
            ? "Mar: analisando..."
            : $"Mar: {(seaDetector.IsSeaVisible ? "visível" : "não visível")} ({seaDetector.SmoothedScore:P0})\n{top} (raw {seaDetector.RawScore:P0})\nRuntime: {seaDetector.LastInferenceMs:F1} ms";

        if (text != lastSeaLabel)
        {
            lastSeaLabel = text;
            classificationResultText.text = text;
        }
    }

    private void ProcessClassificationResults(Tensor<float> outputTensor, float[] outputData, double runtimeMs)
    {
        // Assuming the model outputs class probabilities
        // Find the class with highest probability
        int classCount = outputTensor.shape[1];
        float maxProbability = float.MinValue;
        int predictedClass = -1;

        for (int i = 0; i < classCount; i++)
        {
            float probability = outputData[i];
            if (probability > maxProbability)
            {
                maxProbability = probability;
                predictedClass = i;
            }
        }

        // Get class label if available
        string classLabel = predictedClass >= 0 && predictedClass < classLabels.Count
            ? classLabels[predictedClass]
            : $"Class {predictedClass}";

        string result = $"[Inference] Predicted: {classLabel} (index: {predictedClass}), Confidence: {maxProbability:P2}, Runtime: {runtimeMs:F1} ms";
        Debug.Log(result);

        if (classificationResultText != null)
        {
            classificationResultText.text = $"{classLabel}\nConfidence: {maxProbability:P2}\nRuntime: {runtimeMs:F1} ms";
        }
    }
}
