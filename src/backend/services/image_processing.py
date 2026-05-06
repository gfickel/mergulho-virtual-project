import io
from typing import Optional

from PIL import Image, UnidentifiedImageError

# Optional HEIC/HEIF support — iOS gallery often returns HEIC. If the wheel
# isn't installed, HEIC inputs surface as UnidentifiedImageError to the caller.
try:
    import pillow_heif  # type: ignore
    pillow_heif.register_heif_opener()
except ImportError:
    pass


class ImageDecodeError(Exception):
    """Raised when the input bytes can't be decoded by Pillow."""


def resize_image_preserving_exif(
    data: bytes,
    max_dim: int = 1600,
    quality: int = 85,
) -> bytes:
    """
    Decode an image, downscale so the longest side is at most ``max_dim`` pixels,
    and re-encode as JPEG with the original EXIF + ICC profile carried through.

    The EXIF block is passed back to ``Image.save`` as raw bytes — Pillow doesn't
    re-interpret it, so GPS, DateTimeOriginal, camera model, and the orientation
    tag survive unchanged. The orientation tag's pixel dimensions become slightly
    stale after resize, but no viewer reads those — they read the actual JPEG
    SOF marker. Same story for the embedded thumbnail.

    Only downscales: if the image is already within ``max_dim`` it's still
    re-encoded (so the output is consistent JPEG).

    :raises ImageDecodeError: Pillow couldn't open the bytes (HEIC without
        pillow-heif, corrupt file, unsupported format).
    """
    try:
        img = Image.open(io.BytesIO(data))
        img.load()
    except (UnidentifiedImageError, OSError) as e:
        raise ImageDecodeError(str(e)) from e

    exif: Optional[bytes] = img.info.get("exif")
    icc: Optional[bytes] = img.info.get("icc_profile")

    # JPEG can't store alpha or palette modes; flatten to RGB.
    if img.mode not in ("RGB", "L"):
        img = img.convert("RGB")

    img.thumbnail((max_dim, max_dim), Image.Resampling.LANCZOS)

    out = io.BytesIO()
    save_kwargs = {"format": "JPEG", "quality": quality, "optimize": True}
    if exif:
        save_kwargs["exif"] = exif
    if icc:
        save_kwargs["icc_profile"] = icc
    img.save(out, **save_kwargs)
    return out.getvalue()
