"""Real-bytes tests for the image pipeline — no mocks.

EXIF preservation is the whole point of the photo path (the backend extracts
GPS/timestamp/camera from it), so these tests build actual JPEGs with Pillow
and assert the metadata survives the resize round-trip.
"""

import io

import pytest
from PIL import Image

from services.image_processing import ImageDecodeError, resize_image_preserving_exif


def make_jpeg(width: int, height: int, exif: Image.Exif = None, icc: bytes = None) -> bytes:
    img = Image.new("RGB", (width, height), color=(20, 80, 160))
    buf = io.BytesIO()
    kwargs = {"format": "JPEG"}
    if exif is not None:
        kwargs["exif"] = exif
    if icc is not None:
        kwargs["icc_profile"] = icc
    img.save(buf, **kwargs)
    return buf.getvalue()


def test_downscales_longest_side_to_max_dim():
    out = resize_image_preserving_exif(make_jpeg(3200, 1600), max_dim=1600)
    img = Image.open(io.BytesIO(out))
    assert img.format == "JPEG"
    assert img.size == (1600, 800)


def test_does_not_upscale_small_images():
    out = resize_image_preserving_exif(make_jpeg(800, 400), max_dim=1600)
    img = Image.open(io.BytesIO(out))
    assert img.size == (800, 400)


def test_preserves_exif_through_resize():
    exif = Image.Exif()
    exif[271] = "TestCam"                  # Make
    exif[272] = "Model X"                  # Model
    exif[306] = "2026:06:01 12:00:00"      # DateTime
    exif[274] = 6                          # Orientation (rotate 90 CW)

    out = resize_image_preserving_exif(make_jpeg(3200, 1600, exif=exif), max_dim=1600)

    out_exif = Image.open(io.BytesIO(out)).getexif()
    assert out_exif[271] == "TestCam"
    assert out_exif[272] == "Model X"
    assert out_exif[306] == "2026:06:01 12:00:00"
    # Orientation must carry through untouched — the pipeline never rotates
    # pixels, viewers apply the tag themselves.
    assert out_exif[274] == 6


def test_preserves_icc_profile_through_resize():
    icc = b"fake-icc-profile-bytes-for-roundtrip-test"
    out = resize_image_preserving_exif(make_jpeg(100, 100, icc=icc), max_dim=1600)
    assert Image.open(io.BytesIO(out)).info.get("icc_profile") == icc


def test_converts_rgba_png_to_rgb_jpeg():
    img = Image.new("RGBA", (200, 100), color=(255, 0, 0, 128))
    buf = io.BytesIO()
    img.save(buf, format="PNG")

    out = resize_image_preserving_exif(buf.getvalue(), max_dim=1600)
    out_img = Image.open(io.BytesIO(out))
    assert out_img.format == "JPEG"
    assert out_img.mode == "RGB"


def test_grayscale_passes_through_as_jpeg():
    img = Image.new("L", (200, 100), color=128)
    buf = io.BytesIO()
    img.save(buf, format="JPEG")

    out = resize_image_preserving_exif(buf.getvalue(), max_dim=1600)
    assert Image.open(io.BytesIO(out)).format == "JPEG"


def test_corrupt_bytes_raise_decode_error():
    with pytest.raises(ImageDecodeError):
        resize_image_preserving_exif(b"definitely not an image")


def test_empty_bytes_raise_decode_error():
    with pytest.raises(ImageDecodeError):
        resize_image_preserving_exif(b"")


def test_truncated_jpeg_raises_decode_error():
    # A real JPEG header with the body chopped off — Pillow opens it but
    # fails on load(); the pipeline must still surface ImageDecodeError.
    data = make_jpeg(400, 400)[:60]
    with pytest.raises(ImageDecodeError):
        resize_image_preserving_exif(data)
