"""
Precompute CLIP TEXT embeddings for zero-shot 'sea vs not-sea' detection
and save them to a JSON file that Unity can parse with JsonUtility.

Run this every time you tweak the prompt lists (no need to re-export the ONNX).
The text encoder NEVER ships in the app - only this JSON does.

Usage:
    pip install torch open_clip_torch
    python precompute_text_embeddings.py
Output:
    sea_embeddings.json   ->  drop into Unity (e.g. Assets/StreamingAssets or as TextAsset)

How it's used at runtime:
    For each camera frame, Unity computes cosine similarity between the image
    embedding and every class embedding below, applies softmax (scaled by
    logitScale), and sums the probabilities of classes marked "isSea": true.
"""

import json

import torch
import open_clip

# ----------------------------------------------------------------------------- config
MODEL_ID = "MobileCLIP2-S0"                 # MUST match export_mobileclip_onnx.py
PRETRAINED = "dfndr2b"                      # (see note there about hf-hub: 404)
OUTPUT_PATH = "sea_embeddings.json"

# Classes marked is_sea=True count toward the "sea" probability.
# Negatives act as competitors in the softmax - the more realistic your
# negatives (things your camera will actually see), the better it works.
# TIP: 'beach_sand' is a negative here; move it to is_sea=True if dry sand
#      in the frame should count as "pointing at the sea" for your app.
CLASSES = {
    "sea": dict(
        is_sea=True,
        prompts=[
            "a photo of the sea",
            "a photo of the ocean",
            "ocean waves near the shore",
            "the sea seen from the beach",
            "open ocean water stretching to the horizon",
            "waves crashing on the coast",
        ],
    ),
    "lake": dict(
        is_sea=False,
        prompts=[
            "a photo of a lake",
            "a calm lake surrounded by trees",
        ],
    ),
    "river": dict(
        is_sea=False,
        prompts=[
            "a photo of a river",
            "a river flowing through a landscape",
        ],
    ),
    "pool": dict(
        is_sea=False,
        prompts=[
            "a swimming pool",
            "a photo of a pool with clear blue water",
        ],
    ),
    "sky": dict(
        is_sea=False,
        prompts=[
            "a photo of the sky",
            "clouds in a blue sky",
        ],
    ),
    "beach_sand": dict(
        is_sea=False,
        prompts=[
            "dry sand on a beach with no water visible",
        ],
    ),
    "street": dict(
        is_sea=False,
        prompts=[
            "a city street with buildings",
            "cars on a road",
        ],
    ),
    "indoor": dict(
        is_sea=False,
        prompts=[
            "a photo of a room indoors",
            "furniture inside a house",
            "a blank wall",
        ],
    ),
    "nature_land": dict(
        is_sea=False,
        prompts=[
            "a grassy field",
            "a forest with trees",
            "mountains without water",
        ],
    ),
    "people": dict(
        is_sea=False,
        prompts=[
            "a photo of a person",
            "a group of people",
        ],
    ),
}
# -----------------------------------------------------------------------------


def main():
    print(f"Loading {MODEL_ID} (pretrained={PRETRAINED}) ...")
    model, _, _ = open_clip.create_model_and_transforms(MODEL_ID, pretrained=PRETRAINED)
    model.eval()
    tokenizer = open_clip.get_tokenizer(MODEL_ID)

    entries = []
    with torch.no_grad():
        for name, spec in CLASSES.items():
            tokens = tokenizer(spec["prompts"])
            emb = model.encode_text(tokens)
            emb = emb / emb.norm(dim=-1, keepdim=True)   # normalize each prompt
            emb = emb.mean(dim=0)                        # prompt ensembling
            emb = emb / emb.norm()                       # re-normalize the mean
            entries.append(
                dict(
                    name=name,
                    isSea=bool(spec["is_sea"]),
                    embedding=[round(float(v), 6) for v in emb.tolist()],
                )
            )
            print(f"  {name:<12} ({len(spec['prompts'])} prompts) "
                  f"{'[SEA]' if spec['is_sea'] else ''}")

        logit_scale = float(model.logit_scale.exp().item())

    dim = len(entries[0]["embedding"])
    payload = dict(dim=dim, logitScale=logit_scale, classes=entries)

    with open(OUTPUT_PATH, "w") as f:
        json.dump(payload, f)

    print(f"\nSaved {len(entries)} class embeddings (dim={dim}, "
          f"logitScale={logit_scale:.2f}) -> {OUTPUT_PATH}")
    print("Copy this file into your Unity project and assign it to SeaDetector.")


if __name__ == "__main__":
    main()
