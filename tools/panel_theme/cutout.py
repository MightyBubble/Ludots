#!/usr/bin/env python3
"""Chroma-key cutout for AI-generated theme art.

Removes a solid background from PNG/JPG images (AI art is generated on a flat key
color, e.g. pure white or magenta). Programmatic matte extraction only — no semantic
segmentation, no network calls.

Method:
  1. Sample the key color from the image border (or take an explicit --key).
  2. Alpha = color distance vs key, two thresholds: similarity (fully transparent)
     and blend (partial, hard ramp between them).
  3. Despill: pull leftover key tint out of near-edge pixels.
  4. Feather the matte (box blur on alpha) and trim fully-transparent padding.

Usage:
  python cutout.py --input in.png --output out.png [--key "#ffffff"|"auto"]
                   [--similarity 0.18] [--blend 0.08] [--feather 1]
"""
import argparse
import sys
from pathlib import Path

try:
    from PIL import Image, ImageFilter
except ImportError:
    sys.exit("Pillow is required: pip install pillow")


def parse_key(value: str):
    value = value.strip()
    if value.startswith("#"):
        value = value[1:]
    if len(value) != 6:
        sys.exit(f"Bad key color '{value}' — expected #rrggbb")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def sample_border_key(image: Image.Image, samples_per_side: int = 64):
    width, height = image.size
    pixels = image.load()
    candidates = []
    for i in range(samples_per_side):
        t = i / (samples_per_side - 1)
        candidates.append(pixels[int(t * (width - 1)), 0])
        candidates.append(pixels[int(t * (width - 1)), height - 1])
        candidates.append(pixels[0, int(t * (height - 1))])
        candidates.append(pixels[width - 1, int(t * (height - 1))])
    # Border majority color: quantize to 16-level buckets and pick the mode.
    buckets = {}
    for rgb in candidates:
        key = tuple(channel >> 4 for channel in rgb[:3])
        buckets.setdefault(key, []).append(rgb)
    majority = max(buckets.values(), key=len)
    return tuple(sum(channel) // len(majority) for channel in zip(*majority))


def distance(a, b):
    return max(abs(a[i] - b[i]) for i in range(3)) / 255.0


def cutout(source: Image.Image, key, similarity: float, blend: float, feather: int):
    rgba = source.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    transparent = similarity
    opaque = similarity + blend
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            d = distance((r, g, b), key)
            if d <= transparent:
                alpha = 0
            elif d >= opaque:
                alpha = 255
            else:
                alpha = int(255 * (d - transparent) / max(1e-6, opaque - transparent))
            # Despill: neutralize residual key tint on semi/opaque edge pixels.
            if alpha > 0 and d < opaque:
                key_max = max(key)
                if key_max > 128:
                    spill = min(r, g, b)
                    r, g, b = max(r, spill), max(g, spill), max(b, spill)
                else:
                    spill = max(r, g, b)
                    r, g, b = min(r, spill), min(g, spill), min(b, spill)
            pixels[x, y] = (r, g, b, alpha)
    if feather > 0:
        alpha_band = rgba.getchannel("A").filter(ImageFilter.BoxBlur(feather))
        rgba.putalpha(alpha_band)
    return rgba


def trim(image: Image.Image):
    bbox = image.getchannel("A").getbbox()
    return image.crop(bbox) if bbox else image


def main():
    parser = argparse.ArgumentParser(description="Chroma-key cutout for theme art")
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--key", default="auto", help="#rrggbb or 'auto' (border sampling)")
    parser.add_argument("--similarity", type=float, default=0.18)
    parser.add_argument("--blend", type=float, default=0.08)
    parser.add_argument("--feather", type=int, default=1)
    parser.add_argument("--no-trim", action="store_true")
    args = parser.parse_args()

    source = Image.open(args.input)
    key = sample_border_key(source) if args.key == "auto" else parse_key(args.key)
    result = cutout(source, key, args.similarity, args.blend, args.feather)
    if not args.no_trim:
        result = trim(result)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    result.save(output)
    print(f"key=#{key[0]:02x}{key[1]:02x}{key[2]:02x} -> {output} ({result.width}x{result.height})")


if __name__ == "__main__":
    main()
