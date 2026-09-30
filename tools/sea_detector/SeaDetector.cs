// SeaDetector.cs
// Zero-shot "is the camera pointing at the sea?" detector for Unity,
// using a MobileCLIP image encoder exported to ONNX (see export_mobileclip_onnx.py)
// and precomputed text embeddings (see precompute_text_embeddings.py).
//
// Requires: Unity Inference Engine package (com.unity.ai.inference).
//   Older projects: install com.unity.sentis and change the using below to
//   `using Unity.Sentis;` - the 2.x API is the same.
//
// Setup:
//   1. Drop mobileclip_image_encoder.onnx into Assets/ and assign it to `modelAsset`.
//   2. Drop sea_embeddings.json into Assets/ (it imports as a TextAsset) and
//      assign it to `embeddingsJson`.
//   3. Assign a source texture (WebCamTexture, ARCameraBackground blit,
//      RenderTexture, etc.) to `sourceTexture`, or enable `autoStartWebcam`
//      for quick testing.
//   4. Read `IsSeaVisible` / `SmoothedScore`, or hook the UnityEvents.

using System;
using UnityEngine;
using UnityEngine.Events;
using Unity.InferenceEngine;

public class SeaDetector : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("The exported mobileclip_image_encoder.onnx")]
    public ModelAsset modelAsset;

    [Tooltip("sea_embeddings.json produced by precompute_text_embeddings.py")]
    public TextAsset embeddingsJson;

    [Tooltip("Must match the export resolution (256 for MobileCLIP S0/S1/S2, 224 for B)")]
    public int inputSize = 256;

    public BackendType backend = BackendType.GPUCompute;

    [Header("Input")]
    [Tooltip("Camera feed to classify (WebCamTexture, RenderTexture...)")]
    public Texture sourceTexture;

    [Tooltip("For quick testing: grab the device camera automatically")]
    public bool autoStartWebcam = false;

    [Header("Detection tuning")]
    [Tooltip("Run inference every N frames (saves battery; smoothing hides the gaps)")]
    [Range(1, 30)] public int inferenceInterval = 6;

    [Tooltip("Exponential smoothing factor per inference (higher = reacts faster)")]
    [Range(0.05f, 1f)] public float smoothing = 0.3f;

    [Tooltip("Score above which 'sea' turns ON")]
    [Range(0f, 1f)] public float onThreshold = 0.7f;

    [Tooltip("Score below which 'sea' turns OFF (hysteresis, must be < onThreshold)")]
    [Range(0f, 1f)] public float offThreshold = 0.4f;

    [Header("Events")]
    public UnityEvent onSeaDetected;
    public UnityEvent onSeaLost;

    // ---- Public state -------------------------------------------------------
    public bool IsSeaVisible { get; private set; }
    public float SmoothedScore { get; private set; }
    public float RawScore { get; private set; }

    // ---- Internals ----------------------------------------------------------
    Worker worker;
    float[][] classEmbeddings;
    bool[] classIsSea;
    string[] classNames;
    float[] logits;
    float logitScale;
    int embeddingDim;
    int frameCounter;
    WebCamTexture webcam;

    [Serializable]
    class ClassEmbedding { public string name; public bool isSea; public float[] embedding; }

    [Serializable]
    class EmbeddingSet { public int dim; public float logitScale; public ClassEmbedding[] classes; }

    void Start()
    {
        // --- load class embeddings ---
        var set = JsonUtility.FromJson<EmbeddingSet>(embeddingsJson.text);
        embeddingDim = set.dim;
        logitScale = set.logitScale;

        int n = set.classes.Length;
        classEmbeddings = new float[n][];
        classIsSea = new bool[n];
        classNames = new string[n];
        logits = new float[n];
        for (int i = 0; i < n; i++)
        {
            classEmbeddings[i] = set.classes[i].embedding;
            classIsSea[i] = set.classes[i].isSea;
            classNames[i] = set.classes[i].name;
        }
        Debug.Log($"[SeaDetector] Loaded {n} class embeddings (dim={embeddingDim}, scale={logitScale:F1})");

        // --- load model ---
        var model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, backend);

        if (autoStartWebcam && sourceTexture == null)
        {
            webcam = new WebCamTexture();
            webcam.Play();
            sourceTexture = webcam;
        }
    }

    void Update()
    {
        if (sourceTexture == null || worker == null) return;
        if (++frameCounter % inferenceInterval != 0) return;

        RunInference();
    }

    void RunInference()
    {
        // TextureConverter gives RGB floats in [0,1], NCHW - exactly what the
        // exported model expects (normalization is baked into the ONNX graph).
        var transform = new TextureTransform().SetDimensions(inputSize, inputSize, 3);
        using Tensor<float> input = TextureConverter.ToTensor(sourceTexture, transform);

        worker.Schedule(input);

        var output = worker.PeekOutput() as Tensor<float>;
        // NOTE: ReadbackAndClone blocks until the GPU finishes. With a small
        // encoder and inferenceInterval > 1 this is usually fine. If you see
        // frame hitches, switch to output.ReadbackRequest() and poll IsReadbackRequestDone.
        using Tensor<float> cpu = output.ReadbackAndClone();
        float[] imageEmbedding = cpu.DownloadToArray();

        RawScore = ComputeSeaProbability(imageEmbedding);

        // Exponential moving average -> stable score
        SmoothedScore = Mathf.Lerp(SmoothedScore, RawScore, smoothing);

        // Hysteresis so the state doesn't flicker at the boundary
        if (!IsSeaVisible && SmoothedScore >= onThreshold)
        {
            IsSeaVisible = true;
            onSeaDetected?.Invoke();
        }
        else if (IsSeaVisible && SmoothedScore <= offThreshold)
        {
            IsSeaVisible = false;
            onSeaLost?.Invoke();
        }
    }

    float ComputeSeaProbability(float[] imageEmbedding)
    {
        // Cosine similarity == dot product (both sides are L2-normalized),
        // scaled by CLIP's learned temperature, then softmax over classes.
        int n = classEmbeddings.Length;
        float maxLogit = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float dot = 0f;
            float[] cls = classEmbeddings[i];
            for (int d = 0; d < embeddingDim; d++) dot += imageEmbedding[d] * cls[d];
            logits[i] = dot * logitScale;
            if (logits[i] > maxLogit) maxLogit = logits[i];
        }

        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            logits[i] = Mathf.Exp(logits[i] - maxLogit); // stable softmax
            sum += logits[i];
        }

        float seaProb = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = logits[i] / sum;
            if (classIsSea[i]) seaProb += p;
        }
        return seaProb;
    }

    /// <summary>Optional: top class name for debugging overlays.</summary>
    public string GetTopClassName()
    {
        if (logits == null) return "";
        int best = 0;
        for (int i = 1; i < logits.Length; i++)
            if (logits[i] > logits[best]) best = i;
        return classNames[best];
    }

    void OnDestroy()
    {
        worker?.Dispose();
        if (webcam != null) webcam.Stop();
    }
}
