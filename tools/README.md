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

```bash
pip install torch torchvision open_clip_torch onnx
pip install git+https://github.com/apple/ml-mobileclip.git   # optional: reparameterization (faster inference)
pip install onnxsim onnxruntime                              # optional: graph cleanup + parity check

python export_mobileclip_onnx.py          # -> mobileclip_image_encoder.onnx
python precompute_text_embeddings.py      # -> sea_embeddings.json
```

Notes:
- Both scripts default to `hf-hub:apple/MobileCLIP2-S0` (smallest/fastest). If that
  identifier doesn't resolve in your open_clip version, check the exact repo name on
  the model card (https://huggingface.co/collections/apple/mobileclip2) or fall back
  to v1: `hf-hub:apple/MobileCLIP-S0`. **Both scripts must use the same model.**
- The export script prints `inputSize` (256 for S0/S1/S2, 224 for B) — you'll set this
  on the Unity component.
- Re-run only `precompute_text_embeddings.py` whenever you tweak prompts. The ONNX
  export doesn't change.

## 2. Unity side

1. **Install the Inference Engine**: Package Manager → *Add package by name* →
   `com.unity.ai.inference`. (On older Unity versions install `com.unity.sentis`
   and change the `using` in `SeaDetector.cs` to `Unity.Sentis` — the 2.x API is the same.)
2. Copy `mobileclip_image_encoder.onnx` and `sea_embeddings.json` into `Assets/`.
   The JSON imports as a `TextAsset` automatically.
3. Add `SeaDetector.cs` to a GameObject and assign:
   - **Model Asset** → the imported ONNX
   - **Embeddings Json** → the JSON TextAsset
   - **Input Size** → value printed by the export script (256 by default)
   - **Source Texture** → your camera feed (`WebCamTexture`, an AR camera blit
     to a `RenderTexture`, etc.), or tick **Auto Start Webcam** to test quickly.
4. Consume the result: poll `detector.IsSeaVisible` / `detector.SmoothedScore`,
   or wire the `onSeaDetected` / `onSeaLost` UnityEvents in the Inspector.

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
