#!/usr/bin/env python3
"""Install Grok-generated raw frames into PanelThemes/*/images with transparent wells.

Expects raw files under /opt/cursor/artifacts/assets/:
  frame_{ember,sanguo,fantasy,acnh}_{panel,choice}_raw.png
  standing_warden_{ember,sanguo,fantasy,acnh}.png
"""
from __future__ import annotations

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

RAW = Path("/opt/cursor/artifacts/assets")
ROOT = Path(__file__).resolve().parents[2] / "mods/showcases/narrative/NarrativeShowcaseMod/assets/PanelThemes"

PANEL_MAP = {
    "story-ember": "frame_ember_panel_raw.png",
    "story-sanguo": "frame_sanguo_panel_raw.png",
    "story-fantasy": "frame_fantasy_panel_raw.png",
    "story-acnh": "frame_acnh_panel_raw.png",
}
CHOICE_MAP = {
    "story-ember": "frame_ember_choice_raw.png",
    "story-sanguo": "frame_sanguo_choice_raw.png",
    "story-fantasy": "frame_fantasy_choice_raw.png",
    "story-acnh": "frame_acnh_choice_raw.png",
}
STANDING_MAP = {
    "story-ember": "standing_warden_ember.png",
    "story-sanguo": "standing_warden_sanguo.png",
    "story-fantasy": "standing_warden_fantasy.png",
    "story-acnh": "standing_warden_acnh.png",
}


def punch_center(im: Image.Image, slice_px: int, soft: int = 6) -> Image.Image:
    w, h = im.size
    arr = np.array(im.convert("RGBA"))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [slice_px, slice_px, w - slice_px - 1, h - slice_px - 1],
        radius=max(8, slice_px // 4),
        fill=255,
    )
    mask = mask.filter(ImageFilter.GaussianBlur(soft))
    alpha = arr[:, :, 3].astype(np.float32)
    m = np.array(mask).astype(np.float32) / 255.0
    arr[:, :, 3] = np.clip(alpha * (1.0 - m), 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def install_panel(src: Path, dst: Path, size=(512, 288), slice_px=48) -> None:
    im = Image.open(src).convert("RGBA")
    if im.getbbox():
        im = im.crop(im.getbbox())
    im = punch_center(im.resize(size, Image.Resampling.LANCZOS), slice_px)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst)


def install_choice(src: Path, dst: Path, size=(440, 128), slice_px=36) -> None:
    im = Image.open(src).convert("RGBA")
    if im.getbbox():
        im = im.crop(im.getbbox())
    im = punch_center(im.resize(size, Image.Resampling.LANCZOS), slice_px, soft=4)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst)


def install_standing(src: Path, dst_standing: Path, dst_portrait: Path) -> None:
    im = Image.open(src).convert("RGBA")
    w = 720
    h = int(im.height * w / im.width)
    standing = im.resize((w, h), Image.Resampling.LANCZOS)
    standing.save(dst_standing)
    bust = standing.crop((0, 0, w, int(h * 0.42))).resize((256, 256), Image.Resampling.LANCZOS)
    bust.save(dst_portrait)


def main() -> None:
    for theme, fn in PANEL_MAP.items():
        install_panel(RAW / fn, ROOT / theme / "images" / "panel_frame.png")
        wash = Image.open(RAW / fn).convert("RGBA").resize((640, 360), Image.Resampling.LANCZOS)
        wash = ImageEnhance.Brightness(wash).enhance(0.45).filter(ImageFilter.GaussianBlur(8))
        wash.save(ROOT / theme / "images" / "panel_wash.png")
    for theme, fn in CHOICE_MAP.items():
        install_choice(RAW / fn, ROOT / theme / "images" / "choice_frame.png")
    for theme, fn in STANDING_MAP.items():
        out = ROOT / theme / "images"
        install_standing(RAW / fn, out / "standing_warden.png", out / "portrait_warden.png")
        print("installed", theme)


if __name__ == "__main__":
    main()
