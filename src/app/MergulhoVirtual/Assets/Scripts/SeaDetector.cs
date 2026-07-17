// SeaDetector.cs
// Zero-shot "is the camera pointing at the sea?" detector,
// using a MobileCLIP2-S0 image encoder exported to ONNX
// (tools/sea_detector/export_mobileclip_onnx.py) and precomputed text
// embeddings (tools/sea_detector/precompute_text_embeddings.py).
//
// In this app it is driven by CameraFeedToInference, which calls Classify()
// with the AR camera texture every FRAME_INTERVAL seconds — so it inherits
// the same cadence and the same inferenceEnabled gating as the mobilenet
// classifier. The Update()-driven sourceTexture/webcam path below is kept
// for quick standalone testing (e.g. autoStartWebcam in the editor).
//
// Consume the result: poll IsSeaVisible / SmoothedScore, or wire the
// onSeaDetected / onSeaLost UnityEvents in the Inspector.

using System;
using UnityEngine;
using UnityEngine.Events;
using Unity.InferenceEngine;

public class SeaDetector : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("The exported mobileclip_image_encoder.onnx. If left empty, loads Resources/mobileclip_image_encoder.")]
    public ModelAsset modelAsset;

    [Tooltip("sea_embeddings.json from precompute_text_embeddings.py. If left empty, loads Resources/sea_embeddings.")]
    public TextAsset embeddingsJson;

    [Tooltip("Must match the export resolution (256 for MobileCLIP S0/S1/S2, 224 for B)")]
    public int inputSize = 256;

    public BackendType backend = BackendType.GPUCompute;

    [Header("Input (standalone testing only — in the app CameraFeedToInference drives Classify())")]
    [Tooltip("Optional self-driven source (WebCamTexture, RenderTexture...). Leave empty in the app.")]
    public Texture sourceTexture;

    [Tooltip("For quick testing: grab the device camera automatically")]
    public bool autoStartWebcam = false;

    [Tooltip("Self-driven mode only: run inference every N frames")]
    [Range(1, 120)] public int inferenceInterval = 30;

    [Header("Detection tuning")]
    // Defaults tuned on the repo's Noronha beach photos vs. non-sea images
    // (tools/sea_detector/test_local_images.py): sea scored 0.56-0.99,
    // not-sea 0.01-0.31, so the on/off thresholds sit inside that gap.
    [Tooltip("Exponential smoothing factor per inference (higher = reacts faster)")]
    [Range(0.05f, 1f)] public float smoothing = 0.5f;

    [Tooltip("Score above which 'sea' turns ON")]
    [Range(0f, 1f)] public float onThreshold = 0.5f;

    [Tooltip("Score below which 'sea' turns OFF (hysteresis, must be < onThreshold)")]
    [Range(0f, 1f)] public float offThreshold = 0.35f;

    [Header("Debug")]
    [Tooltip("On startup, classify Resources/sea_selftest.png once and log the result. Expected (from tools/sea_detector, praia_do_sancho 256px): seaScore ~82.6% raw / ~81.5% if GPU linearizes, top3 sea/beach_sand/lake. A very different value means the on-device pipeline diverges from Python. Verified 2026-07-09: device reported 82.7%.")]
    public bool runSelfTest = false;

    [Header("Events")]
    public UnityEvent onSeaDetected;
    public UnityEvent onSeaLost;

    // ---- Public state -------------------------------------------------------
    public bool IsSeaVisible { get; private set; }
    public float SmoothedScore { get; private set; }
    public float RawScore { get; private set; }

    // Schedule -> readback-done wall time of the last async inference, so it
    // includes GPU wait and frame granularity (same span the mobilenet
    // classifier used to report). 0 until the first inference completes.
    public double LastInferenceMs { get; private set; }

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

    Tensor pendingInput;
    Tensor<float> pendingOutput;
    bool inferenceInFlight;
    bool selfTestInFlight;
    System.Diagnostics.Stopwatch inferenceStopwatch;

    [Serializable]
    class ClassEmbedding { public string name; public bool isSea; public float[] embedding; }

    [Serializable]
    class EmbeddingSet { public int dim; public float logitScale; public ClassEmbedding[] classes; }

    void Start()
    {
        if (modelAsset == null)
            modelAsset = Resources.Load<ModelAsset>("mobileclip_image_encoder");
        if (embeddingsJson == null)
            embeddingsJson = Resources.Load<TextAsset>("sea_embeddings");
        if (modelAsset == null || embeddingsJson == null)
        {
            Debug.LogError("[SeaDetector] Missing model or embeddings (looked in Resources/mobileclip_image_encoder + Resources/sea_embeddings). Disabling.");
            enabled = false;
            return;
        }

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

        if (runSelfTest)
        {
            var testTexture = Resources.Load<Texture2D>("sea_selftest");
            if (testTexture != null)
            {
                selfTestInFlight = true;
                Classify(testTexture);
            }
            else
            {
                Debug.LogWarning("[SeaDetector] SELF-TEST skipped: Resources/sea_selftest.png not found");
            }
        }

        if (autoStartWebcam && sourceTexture == null)
        {
            webcam = new WebCamTexture();
            webcam.Play();
            sourceTexture = webcam;
        }
    }

    void Update()
    {
        // Finish an in-flight inference without blocking the main thread.
        if (inferenceInFlight && pendingOutput.IsReadbackRequestDone())
        {
            inferenceStopwatch.Stop();
            LastInferenceMs = inferenceStopwatch.Elapsed.TotalMilliseconds;

            float[] imageEmbedding = pendingOutput.DownloadToArray();
            pendingInput.Dispose();
            pendingInput = null;
            pendingOutput = null;
            inferenceInFlight = false;

            if (selfTestInFlight)
            {
                // Parity check only — must not feed the EMA/hysteresis state.
                selfTestInFlight = false;
                LogSelfTest(imageEmbedding);
            }
            else
            {
                UpdateScore(ComputeSeaProbability(imageEmbedding));
            }
        }

        // Self-driven mode (webcam / manual sourceTexture) — unused in the app,
        // where CameraFeedToInference calls Classify() instead.
        if (sourceTexture != null && ++frameCounter % inferenceInterval == 0)
            Classify(sourceTexture);
    }

    /// <summary>Schedule sea/not-sea inference on the given texture.
    /// Non-blocking; the score lands in IsSeaVisible / SmoothedScore a few
    /// frames later. Ignored if a previous inference is still in flight.</summary>
    public void Classify(Texture texture)
    {
        if (worker == null || texture == null || inferenceInFlight) return;

        // TextureConverter gives RGB floats in [0,1], NCHW — exactly what the
        // exported model expects (normalization is baked into the ONNX graph).
        var transform = new TextureTransform().SetDimensions(inputSize, inputSize, 3);
        Tensor<float> input = TextureConverter.ToTensor(texture, transform);

        inferenceStopwatch = System.Diagnostics.Stopwatch.StartNew();
        worker.Schedule(input);

        var output = worker.PeekOutput() as Tensor<float>;
        output.ReadbackRequest();

        pendingInput = input;
        pendingOutput = output;
        inferenceInFlight = true;
    }

    void UpdateScore(float rawScore)
    {
        RawScore = rawScore;

        // Exponential moving average -> stable score
        SmoothedScore = Mathf.Lerp(SmoothedScore, RawScore, smoothing);

        Debug.Log($"[SeaDetector] raw={RawScore:P0} smoothed={SmoothedScore:P0} top={GetTopClassName()} visible={IsSeaVisible} runtime={LastInferenceMs:F1} ms");

        // Hysteresis so the state doesn't flicker at the boundary
        if (!IsSeaVisible && SmoothedScore >= onThreshold)
        {
            IsSeaVisible = true;
            Debug.Log("[SeaDetector] Sea DETECTED");
            onSeaDetected?.Invoke();
        }
        else if (IsSeaVisible && SmoothedScore <= offThreshold)
        {
            IsSeaVisible = false;
            Debug.Log("[SeaDetector] Sea lost");
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

    /// <summary>Debug: classify synchronously (blocks on GPU readback) without
    /// touching the smoothed score / detection state. Returns (seaScore, topClass).</summary>
    public (float score, string top) ClassifyBlocking(Texture texture)
    {
        if (worker == null || texture == null || inferenceInFlight) return (-1f, "(busy)");

        var transform = new TextureTransform().SetDimensions(inputSize, inputSize, 3);
        using Tensor<float> input = TextureConverter.ToTensor(texture, transform);
        worker.Schedule(input);
        var output = worker.PeekOutput() as Tensor<float>;
        using Tensor<float> cpu = output.ReadbackAndClone();
        float score = ComputeSeaProbability(cpu.DownloadToArray());
        return (score, GetTopClassName());
    }

    // Detailed one-shot log to compare the on-device pipeline against the
    // Python reference (tools/sea_detector/test_local_images.py on sea_selftest.png).
    void LogSelfTest(float[] imageEmbedding)
    {
        float seaProb = ComputeSeaProbability(imageEmbedding); // fills logits[] with exp values
        float sum = 0f;
        for (int i = 0; i < logits.Length; i++) sum += logits[i];

        var sb = new System.Text.StringBuilder();
        sb.Append($"[SeaDetector] SELF-TEST seaScore={seaProb:P1} (expected ~82.6% raw / ~81.5% linearized)\n  classes:");
        for (int i = 0; i < logits.Length; i++)
            sb.Append($" {classNames[i]}={logits[i] / sum:P1}");
        sb.Append("\n  embedding[0:8] =");
        for (int d = 0; d < 8 && d < imageEmbedding.Length; d++)
            sb.Append($" {imageEmbedding[d]:F4}");
        sb.Append("\n  (python raw:  0.0723 -0.0193 0.0049 -0.0010 -0.0210 -0.0175 0.0032 -0.0011)");
        sb.Append("\n  (python lin.: 0.0642 -0.0231 0.0048 0.0002 -0.0260 -0.0094 0.0000 -0.0061)");
        Debug.Log(sb.ToString());
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
        pendingInput?.Dispose();
        worker?.Dispose();
        if (webcam != null) webcam.Stop();
    }
}
