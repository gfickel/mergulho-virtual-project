"""
Export the MobileCLIP / MobileCLIP2 IMAGE encoder to ONNX for Unity Inference Engine.

Key design choices for Unity friendliness:
  - Normalization (mean/std) is baked INTO the ONNX graph, so Unity only needs
    to feed RGB values in [0, 1] (exactly what TextureConverter.ToTensor gives you).
  - Output is the L2-NORMALIZED image embedding, so Unity only computes dot products.
  - Fixed batch size of 1, static input shape (best for Sentis/Inference Engine).

Usage:
    pip install torch torchvision open_clip_torch onnx
    # optional but recommended (fuses MobileOne branches -> faster inference):
    pip install git+https://github.com/apple/ml-mobileclip.git
    # optional graph cleanup:
    pip install onnxsim

    python export_mobileclip_onnx.py

If the hf-hub identifier below fails, check the exact repo name on the model card:
https://huggingface.co/collections/apple/mobileclip2
(e.g. 'hf-hub:apple/MobileCLIP2-S0' or fall back to MobileCLIP v1: 'hf-hub:apple/MobileCLIP-S0')
"""

import torch
import open_clip

# ----------------------------------------------------------------------------- config
MODEL_ID = "hf-hub:apple/MobileCLIP2-S0"   # smallest/fastest; try S2 or B for accuracy
OUTPUT_PATH = "mobileclip_image_encoder.onnx"
OPSET = 17                                  # Inference Engine supports opset 7-25
FALLBACK_IMAGE_SIZE = 256                   # S0/S1/S2 use 256x256; B uses 224x224
# -----------------------------------------------------------------------------


class ImageTower(torch.nn.Module):
    """Wraps the CLIP image encoder with baked-in preprocessing + L2 normalization.

    Expected input:  float32 tensor [1, 3, H, W], RGB, values in [0, 1]
    Output:          float32 tensor [1, D], L2-normalized embedding
    """

    def __init__(self, clip_model, mean, std):
        super().__init__()
        self.model = clip_model
        self.register_buffer("mean", torch.tensor(mean, dtype=torch.float32).view(1, 3, 1, 1))
        self.register_buffer("std", torch.tensor(std, dtype=torch.float32).view(1, 3, 1, 1))

    def forward(self, x):
        x = (x - self.mean) / self.std
        feats = self.model.encode_image(x)
        return feats / feats.norm(dim=-1, keepdim=True)


def get_normalization(preprocess):
    """Pull mean/std out of the open_clip preprocessing pipeline."""
    from torchvision.transforms import Normalize
    for t in getattr(preprocess, "transforms", []):
        if isinstance(t, Normalize):
            return tuple(float(v) for v in t.mean), tuple(float(v) for v in t.std)
    print("WARNING: no Normalize transform found; assuming mean=0, std=1")
    return (0.0, 0.0, 0.0), (1.0, 1.0, 1.0)


def get_image_size(model):
    size = getattr(getattr(model, "visual", None), "image_size", None)
    if size is None:
        return FALLBACK_IMAGE_SIZE
    if isinstance(size, (tuple, list)):
        return int(size[0])
    return int(size)


def try_reparameterize(model):
    """Fuse MobileOne-style training branches into single convs (big speedup).
    Safe to skip if the mobileclip package isn't installed."""
    try:
        from mobileclip.modules.common.mobileone import reparameterize_model
        model = reparameterize_model(model)
        print("Reparameterized MobileOne blocks (inference-optimized).")
    except ImportError:
        print("NOTE: 'mobileclip' package not installed - skipping reparameterization.")
        print("      The model still works, just slower. pip install git+https://github.com/apple/ml-mobileclip.git")
    except Exception as e:  # architecture without reparam blocks, etc.
        print(f"NOTE: reparameterization skipped ({e}).")
    return model


def main():
    print(f"Loading {MODEL_ID} ...")
    model, _, preprocess = open_clip.create_model_and_transforms(MODEL_ID)
    model.eval()
    model = try_reparameterize(model)

    mean, std = get_normalization(preprocess)
    image_size = get_image_size(model)
    print(f"Image size: {image_size}x{image_size} | mean={mean} std={std}")

    tower = ImageTower(model, mean, std).eval()

    dummy = torch.rand(1, 3, image_size, image_size)
    with torch.no_grad():
        out = tower(dummy)
    embed_dim = out.shape[-1]
    print(f"Embedding dim: {embed_dim}")

    torch.onnx.export(
        tower,
        dummy,
        OUTPUT_PATH,
        input_names=["image"],
        output_names=["embedding"],
        opset_version=OPSET,
        do_constant_folding=True,
    )
    print(f"Exported -> {OUTPUT_PATH}")

    # Optional: simplify the graph (helps ONNX importers)
    try:
        import onnx
        from onnxsim import simplify
        m, ok = simplify(onnx.load(OUTPUT_PATH))
        if ok:
            onnx.save(m, OUTPUT_PATH)
            print("Simplified ONNX graph with onnxsim.")
    except ImportError:
        pass

    # Sanity check with onnxruntime if available
    try:
        import onnxruntime as ort
        import numpy as np
        sess = ort.InferenceSession(OUTPUT_PATH, providers=["CPUExecutionProvider"])
        ort_out = sess.run(None, {"image": dummy.numpy()})[0]
        diff = float(np.abs(ort_out - out.numpy()).max())
        print(f"onnxruntime parity check: max abs diff = {diff:.6f}")
    except ImportError:
        print("(install onnxruntime to run a parity check)")

    print("\nRemember these values for Unity / the embeddings script:")
    print(f"  inputSize    = {image_size}")
    print(f"  embeddingDim = {embed_dim}")


if __name__ == "__main__":
    main()
