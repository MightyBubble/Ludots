#!/usr/bin/env python3
"""Paint Field v2 country rects into a board-aligned RGBA decal stamp.

Pixel (tx, ty) is one Field cell. ty=0 is north so OpenGL v=1 is north.
UV contract matches decal_project.fs: u = local.x+0.5, v = local.z+0.5
with the stamp centered on the playable board.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--field", type=Path, required=True)
    parser.add_argument("--palette", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--cell-size-cm", type=int, default=7142)
    args = parser.parse_args()

    field = load_json(args.field)
    palette_doc = load_json(args.palette)
    profile = load_json(args.profile)
    vhtm = profile["visualHeightmap"]
    playable_w = float(vhtm["playableWorldWidthOverrideCm"])
    playable_h = float(vhtm["worldHeightCm"])
    cell = args.cell_size_cm
    if abs(playable_w / cell - round(playable_w / cell)) > 1e-6:
        raise SystemExit(f"playable width {playable_w} is not an integer number of cells {cell}")
    if abs(playable_h / cell - round(playable_h / cell)) > 1e-6:
        raise SystemExit(f"playable height {playable_h} is not an integer number of cells {cell}")

    tex_w = int(round(playable_w / cell))
    tex_h = int(round(playable_h / cell))
    origin_cx = -tex_w // 2
    origin_cy = -tex_h // 2
    north_cy = origin_cy + tex_h - 1

    regions: list[str] = field["regions"]
    fill_a = int(palette_doc["fillAlpha"])
    border_a = int(palette_doc["borderAlpha"])
    raw_colors = palette_doc["regions"]
    missing = [name for name in regions if name not in raw_colors]
    extra = [name for name in raw_colors if name not in regions]
    if missing:
        raise SystemExit(f"palette missing regions: {missing}")
    if extra:
        raise SystemExit(f"palette has unknown regions: {extra}")

    colors = {name: tuple(int(c) for c in raw_colors[name][:3]) for name in regions}
    # rid 0 = ocean/default transparent; rid 1..n match Field 1-based ids
    ids = [[0] * tex_w for _ in range(tex_h)]

    def in_tex(cx: int, cy: int) -> tuple[int, int] | None:
        tx = cx - origin_cx
        ty = north_cy - cy
        if 0 <= tx < tex_w and 0 <= ty < tex_h:
            return tx, ty
        return None

    for x0, y0, x1, y1, rid in field["rects"]:
        if rid < 1 or rid > len(regions):
            raise SystemExit(f"rect references regionId {rid} outside 1..{len(regions)}")
        for cy in range(y0, y1 + 1):
            for cx in range(x0, x1 + 1):
                pix = in_tex(cx, cy)
                if pix is None:
                    continue
                tx, ty = pix
                ids[ty][tx] = rid

    img = Image.new("RGBA", (tex_w, tex_h), (0, 0, 0, 0))
    pix = img.load()
    painted = 0
    borders = 0
    for ty in range(tex_h):
        for tx in range(tex_w):
            rid = ids[ty][tx]
            if rid == 0:
                continue
            r, g, b = colors[regions[rid - 1]]
            edge = False
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                nx, ny = tx + dx, ty + dy
                neighbor = 0 if nx < 0 or ny < 0 or nx >= tex_w or ny >= tex_h else ids[ny][nx]
                if neighbor != rid:
                    edge = True
                    break
            if edge:
                pix[tx, ty] = (max(0, r * 55 // 100), max(0, g * 55 // 100), max(0, b * 55 // 100), border_a)
                borders += 1
            else:
                pix[tx, ty] = (r, g, b, fill_a)
            painted += 1

    args.out.parent.mkdir(parents=True, exist_ok=True)
    img.save(args.out, "PNG")
    meta = {
        "width": tex_w,
        "height": tex_h,
        "cellSizeCm": cell,
        "playableWorldWidthCm": int(playable_w),
        "playableWorldHeightCm": int(playable_h),
        "originCellX": origin_cx,
        "originCellY": origin_cy,
        "paintedCells": painted,
        "borderCells": borders,
        "stampWidthMeters": playable_w / 100.0,
        "stampDepthMeters": playable_h / 100.0,
    }
    args.out.with_suffix(args.out.suffix + ".meta.json").write_text(
        json.dumps(meta, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps({"out": str(args.out), **meta}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
