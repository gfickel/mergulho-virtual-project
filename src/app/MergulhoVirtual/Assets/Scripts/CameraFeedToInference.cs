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

    public bool inferenceEnabled = true;

    private Texture2D cameraTexture;
    private Texture2D resizedTexture;
    private const float FRAME_INTERVAL = 5f;
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
        LoadClassLabels();

        if (modelAsset != null)
        {
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
    }

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
