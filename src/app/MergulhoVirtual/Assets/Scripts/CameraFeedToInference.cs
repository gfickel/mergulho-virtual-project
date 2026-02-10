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

    private Texture2D cameraTexture;
    private Texture2D resizedTexture;
    private float lastFrameTime = 0f;
    private const float FRAME_INTERVAL = 5f;

    private Model runtimeModel;
    private Worker worker;
    private Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>();
    private List<string> classLabels = new List<string>();

    void Start()
    {
        LoadClassLabels();

        if (modelAsset != null)
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
            resizedTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
            Debug.Log($"[Inference] Model loaded - Input shape: {inputWidth}x{inputHeight}, Classes: {classLabels.Count}");
        }
        else
        {
            Debug.LogWarning("[Inference] No model asset assigned!");
        }
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
        worker?.Dispose();
        foreach (var input in inputs.Values)
        {
            input.Dispose();
        }
        inputs.Clear();
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
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
        if (worker == null || sourceTexture == null)
        {
            return;
        }

        // Resize texture to model input dimensions
        Graphics.ConvertTexture(sourceTexture, resizedTexture);

        // Create tensor from texture with 3 channels (RGB)
        var inputTensor = TextureConverter.ToTensor(resizedTexture, inputWidth, inputHeight, 3);

        // Run inference
        worker.Schedule(inputTensor);

        // Get output tensor
        var outputTensor = worker.PeekOutput() as Tensor<float>;

        // Process classification results
        ProcessClassificationResults(outputTensor);

        // Clean up
        inputTensor.Dispose();
    }

    private void ProcessClassificationResults(Tensor<float> outputTensor)
    {
        // Assuming the model outputs class probabilities
        // Find the class with highest probability
        int classCount = outputTensor.shape[1];
        float maxProbability = float.MinValue;
        int predictedClass = -1;

        // Download tensor data to read values
        var outputData = outputTensor.DownloadToArray();

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

        string result = $"[Inference] Predicted: {classLabel} (index: {predictedClass}), Confidence: {maxProbability:P2}";
        Debug.Log(result);

        if (classificationResultText != null)
        {
            classificationResultText.text = $"{classLabel}\nConfidence: {maxProbability:P2}";
        }
    }
}
