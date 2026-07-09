"""
Test the exported sea detector against local images - BEFORE touching Unity.

This runs the exact same artifacts that ship in the app:
  - mobileclip_image_encoder.onnx  (normalization baked in, L2-normalized output)
  - sea_embeddings.json            (class embeddings + logit scale)
and reproduces the exact same math as SeaDetector.cs (dot products -> scaled
softmax -> sum of isSea class probabilities). If a photo scores well here,
it will score the same in Unity.

Only needs lightweight deps (no torch required):
    pip install onnxruntime pillow numpy

Usage:
    python test_local_images.py photos/                          # a folder
    python test_local_images.py img1.jpg img2.png beach/*.jpg    # files/globs
    python test_local_images.py photos/ --threshold 0.5          # PASS/FAIL marks
    python test_local_images.py photos/ --verbose                # per-class breakdown
    python test_local_images.py photos/ --center-crop            # CLIP-style crop
                                                                 # (default is plain
                                                                 # stretch-resize, to
                                                                 # match Unity's
                                                                 # TextureConverter)
"""

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"}


def collect_images(paths):
    files = []
    for p in paths:
        p = Path(p)
        if p.is_dir():
            files.extend(
                f for f in sorted(p.rglob("*"))
                if f.suffix.lower() in IMAGE_EXTENSIONS
            )
        elif p.is_file():
            files.append(p)
        else:
            print(f"WARNING: '{p}' not found, skipping")
    return files


def load_image(path, size, center_crop):
    """Produce a [1,3,size,size] float32 array in [0,1], RGB, NCHW.

    Default: plain stretch-resize -> matches Unity's TextureConverter.ToTensor.
    --center-crop: resize shorter side then crop -> matches CLIP's Python preprocess.
    """
    img = Image.open(path).convert("RGB")
    if center_crop:
        w, h = img.size
        scale = size / min(w, h)
        img = img.resize((round(w * scale), round(h * scale)), Image.BICUBIC)
        w, h = img.size
        left, top = (w - size) // 2, (h - size) // 2
        img = img.crop((left, top, left + size, top + size))
    else:
        img = img.resize((size, size), Image.BILINEAR)

    arr = np.asarray(img, dtype=np.float32) / 255.0      # HWC, [0,1]
    arr = arr.transpose(2, 0, 1)[None, ...]              # -> NCHW
    return np.ascontiguousarray(arr)


def sea_probability(image_embedding, classes, logit_scale):
    """Identical math to SeaDetector.ComputeSeaProbability in C#."""
    logits = np.array(
        [float(np.dot(image_embedding, c["embedding"])) * logit_scale for c in classes]
    )
    exp = np.exp(logits - logits.max())                  # stable softmax
    probs = exp / exp.sum()
    sea_prob = sum(p for p, c in zip(probs, classes) if c["isSea"])
    return sea_prob, probs


def main():
    ap = argparse.ArgumentParser(description="Test sea detection on local images")
    ap.add_argument("paths", nargs="+", help="Image files and/or folders")
    ap.add_argument("--onnx", default="mobileclip_image_encoder.onnx")
    ap.add_argument("--embeddings", default="sea_embeddings.json")
    ap.add_argument("--threshold", type=float, default=None,
                    help="Print SEA/not-sea verdicts against this score")
    ap.add_argument("--verbose", action="store_true",
                    help="Show all class probabilities per image")
    ap.add_argument("--center-crop", action="store_true",
                    help="CLIP-style center crop instead of Unity-style stretch resize")
    args = ap.parse_args()

    # --- load artifacts ---
    with open(args.embeddings) as f:
        data = json.load(f)
    classes = data["classes"]
    for c in classes:
        c["embedding"] = np.asarray(c["embedding"], dtype=np.float32)
    logit_scale = float(data["logitScale"])

    sess = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"])
    inp = sess.get_inputs()[0]
    size = int(inp.shape[-1])  # auto-detect input size from the ONNX graph
    print(f"Model: {args.onnx} (input {size}x{size}) | "
          f"{len(classes)} classes, logitScale={logit_scale:.1f}")
    print(f"Preprocess: {'center-crop (CLIP-style)' if args.center_crop else 'stretch resize (Unity-style)'}\n")

    files = collect_images(args.paths)
    if not files:
        sys.exit("No images found.")

    # --- run ---
    name_width = min(max(len(f.name) for f in files), 40)
    results = []
    for f in files:
        try:
            x = load_image(f, size, args.center_crop)
        except Exception as e:
            print(f"  {f.name}: unreadable ({e})")
            continue

        emb = sess.run(None, {inp.name: x})[0][0]        # [D], already L2-normalized
        score, probs = sea_probability(emb, classes, logit_scale)
        top = classes[int(np.argmax(probs))]["name"]
        results.append(score)

        verdict = ""
        if args.threshold is not None:
            verdict = "  << SEA" if score >= args.threshold else ""
        bar = "#" * int(score * 20)
        print(f"  {f.name[:name_width]:<{name_width}}  {score:6.1%}  |{bar:<20}|  top: {top}{verdict}")

        if args.verbose:
            order = np.argsort(probs)[::-1]
            for i in order:
                if probs[i] >= 0.01:
                    print(f"      {classes[i]['name']:<14} {probs[i]:6.1%}"
                          f"{'  [sea]' if classes[i]['isSea'] else ''}")

    # --- summary ---
    if results:
        r = np.array(results)
        print(f"\n{len(r)} images | sea score  min {r.min():.1%}  "
              f"median {np.median(r):.1%}  max {r.max():.1%}")
        if args.threshold is not None:
            print(f"{int((r >= args.threshold).sum())} above threshold {args.threshold:.0%}")
        print("\nTip: run this on a 'sea' folder and a 'not sea' folder separately -")
        print("the gap between the two score distributions is where your Unity")
        print("on/off thresholds should sit. If the gap is small, improve the prompts")
        print("in precompute_text_embeddings.py and re-run (no re-export needed).")


if __name__ == "__main__":
    main()
