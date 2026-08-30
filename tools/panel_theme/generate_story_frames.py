#!/usr/bin/env python3
"""Generate Narrative showcase nine-slice frame PNGs under PanelThemes/*/images/.

Slice contract (must match theme.css):
  .story-frame        → image-slice: 48 48 48 48
  .story-choice-frame → image-slice: 36 36 36 36

Center well is transparent so the frame overlays dialogue content.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = Path(__file__).resolve().parents[2] / "mods/showcases/narrative/NarrativeShowcaseMod/assets/PanelThemes"
SOURCE_ROOT = Path(__file__).resolve().parent / "sources"

PANEL_W, PANEL_H, PANEL_SLICE = 256, 192, 48
CHOICE_W, CHOICE_H, CHOICE_SLICE = 220, 96, 36


def _lerp(a: int, b: int, t: float) -> int:
    return int(a + (b - a) * t)


def _mix(c0: tuple[int, int, int], c1: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return (_lerp(c0[0], c1[0], t), _lerp(c0[1], c1[1], t), _lerp(c0[2], c1[2], t))


def _draw_beveled_ring(
    d: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    radius: int,
    outer: tuple[int, int, int],
    inner: tuple[int, int, int],
    width: int,
) -> None:
    x0, y0, x1, y1 = box
    d.rounded_rectangle([x0, y0, x1, y1], radius=radius, outline=(*outer, 255), width=width)
    d.rounded_rectangle(
        [x0 + width, y0 + width, x1 - width, y1 - width],
        radius=max(2, radius - width),
        outline=(*inner, 200),
        width=max(1, width - 1),
    )


def _corner_ornament(
    d: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    sx: int,
    sy: int,
    edge: tuple[int, int, int],
    accent: tuple[int, int, int],
    style: str,
) -> None:
    if style == "metal":
        d.ellipse([cx - 14, cy - 14, cx + 14, cy + 14], outline=(*edge, 240), width=3)
        d.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=(*accent, 230), outline=(*edge, 255), width=1)
        d.line([(cx - 18 * sx, cy), (cx - 8 * sx, cy)], fill=(*edge, 220), width=2)
        d.line([(cx, cy - 18 * sy), (cx, cy - 8 * sy)], fill=(*edge, 220), width=2)
    elif style == "leaf":
        d.ellipse([cx - 16, cy - 10, cx + 16, cy + 10], outline=(*edge, 210), width=2)
        d.arc([cx - 20, cy - 20, cx + 20, cy + 20], start=20, end=250, fill=(*accent, 200), width=3)
        d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=(*accent, 220))
    else:  # gem
        d.polygon(
            [(cx, cy - 14 * sy), (cx + 11 * sx, cy), (cx, cy + 14 * sy), (cx - 11 * sx, cy)],
            fill=(*accent, 235),
            outline=(*edge, 255),
        )
        d.line([(cx - 6 * sx, cy - 4 * sy), (cx + 4 * sx, cy + 2 * sy)], fill=(255, 255, 255, 120), width=1)


def paint_panel(
    base: tuple[int, int, int],
    edge: tuple[int, int, int],
    accent: tuple[int, int, int],
    style: str,
) -> Image.Image:
    w, h, s = PANEL_W, PANEL_H, PANEL_SLICE
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    plate = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(plate)

    # Outer plate (opaque chrome only in border band)
    d.rounded_rectangle([1, 1, w - 2, h - 2], radius=22, fill=(*base, 245))
    # Soft highlight along top edge band
    for i in range(s - 8):
        alpha = max(0, 70 - i * 2)
        d.line([(12, 8 + i), (w - 13, 8 + i)], fill=(255, 255, 255, alpha))
    # Bottom edge band — leaf themes stay soft; others get a darker bevel
    for i in range(s - 10):
        alpha = max(0, 55 - i * 2)
        y = h - 10 - i
        shade = (40, 70, 40, alpha) if style == "leaf" else (0, 0, 0, alpha)
        d.line([(12, y), (w - 13, y)], fill=shade)

    _draw_beveled_ring(d, (4, 4, w - 5, h - 5), 20, edge, _mix(edge, (255, 255, 255), 0.35), 3)
    _draw_beveled_ring(d, (14, 14, w - 15, h - 15), 14, _mix(base, edge, 0.55), _mix(base, (0, 0, 0), 0.35), 2)

    # Punch transparent content well exactly outside the slice so nine-slice center stays clear
    well = Image.new("L", (w, h), 0)
    ImageDraw.Draw(well).rounded_rectangle([s, s, w - s - 1, h - s - 1], radius=10, fill=255)
    plate.putalpha(
        Image.composite(
            Image.new("L", (w, h), 0),
            plate.split()[-1],
            well,
        )
    )

    # Inner lip around the transparent well (still inside border band)
    lip = ImageDraw.Draw(plate)
    lip.rounded_rectangle(
        [s - 6, s - 6, w - s + 5, h - s + 5],
        radius=12,
        outline=(*edge, 180),
        width=2,
    )

    for cx, cy, sx, sy in [
        (s // 2 + 4, s // 2 + 4, 1, 1),
        (w - s // 2 - 5, s // 2 + 4, -1, 1),
        (s // 2 + 4, h - s // 2 - 5, 1, -1),
        (w - s // 2 - 5, h - s // 2 - 5, -1, -1),
    ]:
        _corner_ornament(lip, cx, cy, sx, sy, edge, accent, style)

    plate = ImageEnhance.Contrast(plate).enhance(1.08)
    plate = ImageEnhance.Sharpness(plate).enhance(1.2)
    # Soft outer glow for production chrome presence
    glow = plate.filter(ImageFilter.GaussianBlur(1.2))
    return Image.alpha_composite(glow, plate)


def paint_choice(
    base: tuple[int, int, int],
    edge: tuple[int, int, int],
    accent: tuple[int, int, int],
) -> Image.Image:
    w, h, s = CHOICE_W, CHOICE_H, CHOICE_SLICE
    plate = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(plate)
    d.rounded_rectangle([1, 1, w - 2, h - 2], radius=14, fill=(*base, 240))
    for i in range(s - 6):
        d.line([(8, 5 + i), (w - 9, 5 + i)], fill=(255, 255, 255, max(0, 55 - i * 3)))
    _draw_beveled_ring(d, (3, 3, w - 4, h - 4), 12, edge, _mix(edge, (255, 255, 255), 0.3), 2)
    # Left accent rail inside border band
    d.rounded_rectangle([8, 10, 14, h - 11], radius=3, fill=(*accent, 230))

    well = Image.new("L", (w, h), 0)
    ImageDraw.Draw(well).rounded_rectangle([s, s, w - s - 1, h - s - 1], radius=8, fill=255)
    plate.putalpha(Image.composite(Image.new("L", (w, h), 0), plate.split()[-1], well))

    lip = ImageDraw.Draw(plate)
    lip.rounded_rectangle([s - 4, s - 4, w - s + 3, h - s + 3], radius=8, outline=(*edge, 160), width=1)
    return ImageEnhance.Sharpness(plate).enhance(1.15)


SPECS = {
    "story-ember": dict(base=(92, 42, 28), edge=(212, 140, 72), accent=(255, 176, 72), style="gem"),
    "story-sanguo": dict(base=(92, 28, 28), edge=(212, 168, 72), accent=(232, 196, 96), style="metal"),
    "story-fantasy": dict(base=(42, 36, 72), edge=(148, 120, 212), accent=(196, 168, 255), style="gem"),
    "story-acnh": dict(base=(72, 128, 72), edge=(232, 196, 96), accent=(120, 176, 88), style="leaf"),
}

def install_ember_v2() -> None:
    source_path = SOURCE_ROOT / "story_ember_frame_v2.png"
    if not source_path.is_file():
        raise FileNotFoundError(f"Missing authored frame source: {source_path}")

    out = ROOT / "story-ember" / "images"
    source = Image.open(source_path).convert("RGBA")
    scaled = source.resize((500, 310), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", (512, 322), (0, 0, 0, 0))
    frame.alpha_composite(scaled, (6, 6))
    frame.save(out / "panel_frame.png")
    ember = SPECS["story-ember"]
    paint_choice(ember["base"], ember["edge"], ember["accent"]).save(out / "choice_frame.png")

    random.seed(1389)
    width, height = 640, 360
    wash = Image.new("RGBA", (width, height))
    pixels = wash.load()
    for y in range(height):
        for x in range(width):
            nx = (x - width / 2) / (width / 2)
            ny = (y - height / 2) / (height / 2)
            radial = max(0.0, 1.0 - math.sqrt(nx * nx + ny * ny))
            top = 1.0 - y / (height - 1)
            grain = random.randint(-5, 5)
            pixels[x, y] = (
                max(0, int(15 + 17 * radial + 6 * top + grain)),
                max(0, int(17 + 12 * radial + 4 * top + grain * 0.6)),
                max(0, int(19 + 8 * radial + 3 * top + grain * 0.4)),
                255,
            )

    overlay = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    draw.rounded_rectangle(
        (10, 10, width - 11, height - 11),
        radius=20,
        outline=(120, 82, 45, 45),
        width=2,
    )
    draw.rectangle((0, height - 90, width, height), fill=(70, 30, 10, 18))
    wash = Image.alpha_composite(wash, overlay.filter(ImageFilter.GaussianBlur(2)))
    wash.save(out / "panel_wash.png")


def main() -> None:
    for theme, s in SPECS.items():
        out = ROOT / theme / "images"
        out.mkdir(parents=True, exist_ok=True)
        if theme == "story-ember":
            install_ember_v2()
            print(
                f"wrote story-ember panel=512x322 slice={PANEL_SLICE} "
                f"choice={CHOICE_W}x{CHOICE_H} slice={CHOICE_SLICE}"
            )
            continue
        paint_panel(s["base"], s["edge"], s["accent"], s["style"]).save(out / "panel_frame.png")
        paint_choice(s["base"], s["edge"], s["accent"]).save(out / "choice_frame.png")
        print(f"wrote {theme} panel={PANEL_W}x{PANEL_H} slice={PANEL_SLICE} choice={CHOICE_W}x{CHOICE_H} slice={CHOICE_SLICE}")


if __name__ == "__main__":
    main()
