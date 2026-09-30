# Zero-shot sea detection in Unity with MobileCLIP

Detect when the camera is pointing at the sea — **no dataset, no training**. A MobileCLIP image encoder runs on-device via the Unity Inference Engine, and "sea vs. everything else" is defined purely by text prompts whose embeddings are precomputed offline.

```
 OFFLINE (Python, once)                          RUNTIME (Unity, every N frames)
 ─────────────────────────                       ────────────────────────────────
 export_mobileclip_onnx.py                       camera texture
   └─> mobileclip_image_encoder.onnx  ──────────>  image encoder (Inference Engine)
 precompute_text_embeddings.py                       └─> image embedding [1×512]
   └─> sea_embeddings.json  ────────────────────>  dot products vs class embeddings
                                                     └─> softmax -> P(sea)
                                                       └─> smoothing + hysteresis
```

The text encoder never ships in the app — only a small JSON of class embeddings.

## 1. Python side (run once on your dev machine)

**As-built (2026-07-09):** a dedicated venv lives at `tools/sea_detector/.venv`
(CPU-only torch — the export doesn't need CUDA). Recreate it with:

```bash
cd tools/sea_detector
python3 -m venv .venv
.venv/bin/pip install torch torchvision --index-url https://download.pytorch.org/whl/cpu
.venv/bin/pip install open_clip_torch onnx onnxscript onnxsim onnxruntime pillow numpy
# optional (NOT installed here): reparameterization for faster on-device inference
# .venv/bin/pip install --no-deps git+https://github.com/apple/ml-mobileclip.git

.venv/bin/python export_mobileclip_onnx.py          # -> mobileclip_image_encoder.onnx
.venv/bin/python precompute_text_embeddings.py      # -> sea_embeddings.json
```

Notes:
- **`hf-hub:apple/MobileCLIP2-S0` 404s** (the HF repo carries no `open_clip_config.json`).
  Both scripts now use open_clip ≥ 3.x's built-in config instead:
  `create_model_and_transforms("MobileCLIP2-S0", pretrained="dfndr2b")` — weights still
  auto-download from HF. **Both scripts must use the same model + pretrained tag.**
- `onnxscript` is required by torch ≥ 2.13's ONNX exporter (not in the original list).
- The export script prints `inputSize` (256 for S0/S1/S2, 224 for B) — you'll set this
  on the Unity component. As exported: inputSize=256, embeddingDim=512, ~45 MB ONNX.
- Re-run only `precompute_text_embeddings.py` whenever you tweak prompts. The ONNX
  export doesn't change.
- Sanity results on the repo's own images (`test_local_images.py`, stretch-resize):
  the 14 `Resources/Beaches/*.jpg` score 0.56–0.99 (median 0.87; one outlier at 0.31
  where people dominate the frame), underwater shark close-ups 0.07–0.19 (top class
  "pool"), UI images/textures ≤ 0.03. Unity thresholds were set inside that gap
  (ON 0.5 / OFF 0.35).

## 2. Unity side

**As-built (2026-07-09) — already integrated into the app.** The version of
`SeaDetector.cs` in this folder is the generic original; the one that ships is
`Assets/Scripts/SeaDetector.cs`, adapted to this project:

- Lives on the **`ComputerVisionServices`** GameObject in `MainScene`, next to
  `CameraFeedToInference`, which calls `seaDetector.Classify(cameraTexture)` on the
  same AR frame it classifies every 5 s (so it inherits the `inferenceEnabled`
  screen gating for free). No self-polling in the app path.
- Loads `Resources/mobileclip_image_encoder.onnx` + `Resources/sea_embeddings.json`
  automatically when the Inspector slots are left empty.
- Async GPU readback (same `ReadbackRequest()` pattern as `CameraFeedToInference`)
  instead of the blocking `ReadbackAndClone`.
- Tuned defaults from the local-image tests: smoothing 0.5, ON 0.5, OFF 0.35.
- Consume the result: poll `detector.IsSeaVisible` / `detector.SmoothedScore`, wire
  the `onSeaDetected` / `onSeaLost` UnityEvents in the Inspector, or watch the
  `[SeaDetector]` lines in the Console / Android Logcat.

For a fresh project, the generic steps are: install `com.unity.ai.inference`, copy
the ONNX + JSON into `Assets/`, add `SeaDetector.cs` to a GameObject, assign the
two assets + a **Source Texture** (or tick **Auto Start Webcam**).

### Optional: quantize to shrink the model

In an Editor script, after loading the model:

```csharp
var model = ModelLoader.Load(modelAsset);
ModelQuantizer.QuantizeWeights(QuantizationType.Float16, ref model); // or Uint8
ModelWriter.Save("Assets/StreamingAssets/mobileclip_image_encoder.sentis", model);
```

Float16 roughly halves the size with negligible accuracy loss; Uint8 quarters it
(verify scores still look sane on a few test scenes).

## 3. Tuning

- **Prompts are your training data now.** If lakes trigger false positives, add more
  lake prompts; if sunsets over the ocean are missed, add "sunset over the ocean" to
  the sea class. Iterate: it's a 10-second re-run of the embeddings script.
- **Does dry beach sand count as "sea"?** There's a `beach_sand` negative class in the
  script — flip its `is_sea` flag depending on your product's definition.
- **Thresholds**: defaults are ON at 0.7 / OFF at 0.4 with EMA smoothing 0.3, running
  every 6 frames. Log `RawScore` while panning your phone around indoors, at a pool,
  and at the coast, then place the thresholds in the gap you observe.
- **Debugging**: `detector.GetTopClassName()` tells you which class is winning — very
  useful when something misfires.

## 4. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| ONNX import errors in Unity | Re-run export with onnxsim installed; keep opset 17. Inference Engine supports opsets 7–25, but simpler graphs import more reliably. |
| Scores look random | `inputSize` mismatch with the export, or source texture is upside down — try `new TextureTransform().SetDimensions(...).FlipY()` in `RunInference`. |
| Everything scores mid-range | Model/JSON mismatch — both Python scripts must load the *same* model ID. |
| Frame hitches on inference frames | Increase `inferenceInterval`, quantize to Float16/Uint8, or replace `ReadbackAndClone` with the async `ReadbackRequest()` pattern. |
| Too slow on low-end devices | Try `BackendType.CPU` (Burst) vs `GPUCompute` — the winner varies by phone. If still too slow, this is the moment to fall back to a fine-tuned MobileNetV4-Conv-S binary classifier. |

## Files

- `export_mobileclip_onnx.py` — exports the image encoder (normalization baked in, L2-normalized output)
- `precompute_text_embeddings.py` — builds `sea_embeddings.json` from prompt lists
- `SeaDetector.cs` — Unity component: inference + softmax + smoothing + hysteresis
