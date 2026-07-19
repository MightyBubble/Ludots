#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
diagram_forge — spec-driven architecture diagram rendering engine (Ludots docs).

Each diagram is described by one JSON spec in docs/diagrams/specs/<name>.json.
The engine renders a silver-white modern style diagram with matplotlib and
exports both SVG (real <text> elements, fonttype='none') and PNG (dpi=180).

Usage:
    python scripts/diagram_forge.py docs/diagrams/specs/<name>.json
    python scripts/diagram_forge.py --all

See docs/diagrams/specs/README.md for the full spec format reference.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import warnings
from dataclasses import dataclass, field

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
from matplotlib.patches import FancyArrowPatch, FancyBboxPatch

# ---------------------------------------------------------------------------
# Design tokens — silver-white modern system
# ---------------------------------------------------------------------------

THEME = {
    "canvas_bg": "#ffffff",
    "node_fill": "#f8f9fb",
    "node_border": "#dfe3e9",
    "node_border_w": 1.0,
    "corner_radius": 8.0,
    "title_color": "#181b20",
    "title_size": 20,
    "subtitle_color": "#5b6470",
    "subtitle_size": 12,
    "node_title_color": "#23272e",
    "node_title_size": 12.5,
    "node_item_color": "#5b6470",
    "node_item_size": 10.2,
    "chip_fill": "#ffffff",
    "chip_border": "#c9d1da",
    "chip_text": "#3a4149",
    "chip_size": 10.0,
    "edge_color": "#8a94a3",
    "edge_w": 1.2,
    "edge_label_color": "#4a525e",
    "edge_label_size": 9.5,
    "group_fill": "#fafbfc",
    "group_border": "#cfd6de",
    "group_title_color": "#5b6470",
    "group_title_size": 10.5,
    "legend_title_color": "#23272e",
    "legend_text_color": "#3a4149",
    "legend_text_size": 10.2,
    "note_color": "#8a94a3",
    "note_size": 9.5,
    "padding": 40.0,
}

# Muted slate / gray-scale accent tints (low saturation, no blue-purple pop).
PALETTE = {
    "slate":   {"fill": "#ebeef2", "border": "#7d8ba0"},
    "core":    {"fill": "#f3f5f8", "border": "#5b6470"},
    "sage":    {"fill": "#edf3ee", "border": "#7fa087"},
    "steel":   {"fill": "#e9eff5", "border": "#7490a8"},
    "stone":   {"fill": "#f0f0ea", "border": "#999a7e"},
    "sand":    {"fill": "#f5f0e8", "border": "#b39b7d"},
    "mauve":   {"fill": "#f1edf3", "border": "#9d8ba8"},
    "clay":    {"fill": "#f4ede9", "border": "#b08d7f"},
    "teal":    {"fill": "#eaf2f1", "border": "#76a09c"},
    "graphite": {"fill": "#eceef0", "border": "#828a92"},
}

WARN = "[diagram_forge WARNING]"


def warn(msg: str) -> None:
    print(f"{WARN} {msg}", file=sys.stderr)
    warnings.warn(msg, stacklevel=2)


# ---------------------------------------------------------------------------
# Fonts
# ---------------------------------------------------------------------------

def setup_fonts() -> str:
    """Register a CJK-capable font; warn explicitly when all candidates fail."""
    candidates = ["Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Noto Sans CJK"]
    available = {f.name for f in font_manager.fontManager.ttflist}
    chosen = next((c for c in candidates if c in available), None)

    if chosen is None:
        # Try registering directly from well-known font files.
        fallback_files = [
            r"C:\Windows\Fonts\msyh.ttc",
            r"C:\Windows\Fonts\simhei.ttf",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/System/Library/Fonts/PingFang.ttc",
        ]
        for path in fallback_files:
            if os.path.exists(path):
                try:
                    font_manager.fontManager.addfont(path)
                    name = font_manager.FontProperties(fname=path).get_name()
                    chosen = name
                    break
                except Exception as exc:  # pragma: no cover
                    warn(f"failed to register font file {path}: {exc}")
    if chosen is None:
        warn(
            "no CJK font found (tried Microsoft YaHei / SimHei / Noto Sans CJK); "
            "Chinese text may render as boxes. Falling back to DejaVu Sans."
        )
        chosen = "DejaVu Sans"

    plt.rcParams["font.family"] = [chosen, "DejaVu Sans"]
    plt.rcParams["axes.unicode_minus"] = False
    plt.rcParams["svg.fonttype"] = "none"  # real <text> in SVG, editable fonts
    return chosen


# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------

@dataclass
class Rect:
    x: float
    y: float
    w: float
    h: float

    @property
    def cx(self) -> float:
        return self.x + self.w / 2

    @property
    def cy(self) -> float:
        return self.y + self.h / 2

    @property
    def right(self) -> float:
        return self.x + self.w

    @property
    def bottom(self) -> float:
        return self.y + self.h

    def intersects(self, other: "Rect", gap: float = 0.0) -> bool:
        return not (
            self.right + gap <= other.x
            or other.right + gap <= self.x
            or self.bottom + gap <= other.y
            or other.bottom + gap <= self.y
        )

    def side_point(self, side: str) -> tuple[float, float]:
        return {
            "top": (self.cx, self.y),
            "bottom": (self.cx, self.bottom),
            "left": (self.x, self.cy),
            "right": (self.right, self.cy),
        }[side]

    def border_point_towards(self, tx: float, ty: float) -> tuple[float, float]:
        """Intersection of ray (center -> target) with the rect border."""
        dx, dy = tx - self.cx, ty - self.cy
        if dx == 0 and dy == 0:
            return self.cx, self.cy
        candidates = []
        if dx:
            candidates.append((self.w / 2) / abs(dx))
        if dy:
            candidates.append((self.h / 2) / abs(dy))
        s = min(candidates)
        return self.cx + dx * s, self.cy + dy * s


def est_text_width(text: str, size: float) -> float:
    """Rough text width estimate in canvas units (CJK ~1.0em, latin ~0.56em)."""
    units = sum(1.0 if ord(ch) > 0x2E7F else 0.56 for ch in text)
    return units * size


# ---------------------------------------------------------------------------
# Renderer
# ---------------------------------------------------------------------------

class DiagramRenderer:
    def __init__(self, spec: dict, out_dir: str):
        self.spec = spec
        self.name = spec.get("name", "diagram")
        self.theme = dict(THEME)
        self.theme.update(spec.get("theme", {}))
        canvas = spec.get("canvas", {})
        self.W = float(canvas.get("width", 1600))
        self.H = float(canvas.get("height", 1200))
        self.out_dir = out_dir

        self.nodes: dict[str, dict] = {}
        self.rects: dict[str, Rect] = {}
        self.groups: dict[str, Rect] = {}
        self.legend_rect: Rect | None = None

        scale = 100.0  # 100 canvas units per inch
        self.fig = plt.figure(figsize=(self.W / scale, self.H / scale))
        self.ax = self.fig.add_axes([0, 0, 1, 1])
        self.ax.set_xlim(0, self.W)
        self.ax.set_ylim(self.H, 0)  # y-down, CSS-style coordinates
        self.ax.axis("off")
        self.fig.patch.set_facecolor(self.theme["canvas_bg"])
        self.ax.set_facecolor(self.theme["canvas_bg"])

    # -- layout -----------------------------------------------------------

    def auto_size(self, node: dict) -> tuple[float, float]:
        t = self.theme
        if "w" in node and "h" in node:
            return float(node["w"]), float(node["h"])
        pad_x, pad_top, pad_bottom = 34.0, 16.0, 14.0
        title_h = 26.0
        item_h = 19.0
        title = node.get("title", "")
        items = node.get("items", [])
        chips = node.get("chips", [])
        chip_cols = int(node.get("chip_columns", 3)) or 1

        w_title = est_text_width(title, t["node_title_size"])
        w_items = max((est_text_width(i, t["node_item_size"]) for i in items), default=0.0)
        w = max(200.0, max(w_title, w_items) + 2 * pad_x)
        h = pad_top + title_h + len(items) * item_h + pad_bottom

        if chips:
            chip_h, chip_gap = 34.0, 10.0
            rows = math.ceil(len(chips) / chip_cols)
            chip_w = max(est_text_width(c, t["chip_size"]) for c in chips) + 30.0
            w = max(w, chip_cols * chip_w + (chip_cols - 1) * chip_gap + 2 * pad_x)
            h += rows * chip_h + (rows - 1) * chip_gap + 12.0
        return float(node.get("w", w)), float(node.get("h", h))

    def apply_layouts(self) -> None:
        """Assign positions to nodes referenced by layout helpers."""
        for helper in self.spec.get("layout", []):
            kind = helper.get("type")
            ids = helper.get("nodes", [])
            if kind == "grid":
                cols = int(helper.get("columns", 3))
                ox, oy = helper.get("origin", [self.theme["padding"], 150.0])
                sx = float(helper.get("spacing_x", 40.0))
                sy = float(helper.get("spacing_y", 40.0))
                cell_w = helper.get("cell_w")
                cell_h = helper.get("cell_h")
                if cell_w is None:
                    cell_w = max(self.auto_size(self.nodes[n])[0] for n in ids if n in self.nodes)
                if cell_h is None:
                    cell_h = max(self.auto_size(self.nodes[n])[1] for n in ids if n in self.nodes)
                for i, nid in enumerate(ids):
                    node = self.nodes.get(nid)
                    if node is None or ("x" in node and "y" in node):
                        continue
                    col, row = i % cols, i // cols
                    node["x"] = ox + col * (cell_w + sx)
                    node["y"] = oy + row * (cell_h + sy)
            elif kind == "radial":
                cx, cy = helper.get("center", self.default_content_center())
                rx = float(helper.get("radius_x", self.W * 0.32))
                ry = float(helper.get("radius_y", self.H * 0.30))
                angles = helper.get("angles")
                start = float(helper.get("start_angle", 90.0))
                direction = -1.0 if helper.get("direction", "cw") == "cw" else 1.0
                n = len(ids)
                for i, nid in enumerate(ids):
                    node = self.nodes.get(nid)
                    if node is None or ("x" in node and "y" in node):
                        continue
                    ang = math.radians(angles[i] if angles else start + direction * i * 360.0 / n)
                    w, h = self.auto_size(node)
                    node["x"] = cx + rx * math.cos(ang) - w / 2
                    node["y"] = cy - ry * math.sin(ang) - h / 2
            else:
                warn(f"unknown layout helper type: {kind!r}")

    def default_content_center(self) -> list[float]:
        """Content center; shifts right automatically when a legend is present."""
        pad = self.theme["padding"]
        x0, x1 = pad, self.W - pad
        y0, y1 = 130.0, self.H - pad
        if self.legend_rect is not None:
            x0 = max(x0, self.legend_rect.right + 40.0)
        return [(x0 + x1) / 2, (y0 + y1) / 2]

    # -- drawing ----------------------------------------------------------

    def rbox(self, rect: Rect, fill: str, border: str, lw: float,
             radius: float | None = None, linestyle: str = "solid", zorder: float = 2.0):
        r = radius if radius is not None else self.theme["corner_radius"]
        patch = FancyBboxPatch(
            (rect.x, rect.y), rect.w, rect.h,
            boxstyle=f"round,pad=0,rounding_size={r}",
            facecolor=fill, edgecolor=border, linewidth=lw,
            linestyle=linestyle, zorder=zorder, mutation_aspect=1.0,
        )
        self.ax.add_patch(patch)
        return patch

    def draw_title_block(self):
        t = self.theme
        if self.spec.get("title"):
            self.ax.text(self.W / 2, 34, self.spec["title"], ha="center", va="top",
                         fontsize=t["title_size"], fontweight="bold",
                         color=t["title_color"], zorder=5)
        if self.spec.get("subtitle"):
            self.ax.text(self.W / 2, 82, self.spec["subtitle"], ha="center", va="top",
                         fontsize=t["subtitle_size"], color=t["subtitle_color"], zorder=5)

    def resolve_colors(self, entry: dict) -> tuple[str, str]:
        tint = entry.get("tint")
        if tint:
            if tint not in PALETTE:
                warn(f"unknown tint {tint!r}; falling back to default node colors")
            else:
                p = PALETTE[tint]
                return entry.get("fill", p["fill"]), entry.get("border", p["border"])
        return (entry.get("fill", self.theme["node_fill"]),
                entry.get("border", self.theme["node_border"]))

    def draw_groups(self):
        t = self.theme
        for g in self.spec.get("groups", []):
            rect = Rect(float(g["x"]), float(g["y"]), float(g["w"]), float(g["h"]))
            fill, border = self.resolve_colors(g)
            if "fill" not in g and "tint" not in g:
                fill = t["group_fill"]
                border = t["group_border"]
            style = g.get("border_style", "dashed")
            ls = (0, (6, 4)) if style == "dashed" else "solid"
            self.rbox(rect, fill, border, 1.0, radius=g.get("radius", 14.0),
                      linestyle=ls, zorder=1.0)
            if g.get("title"):
                anchor = g.get("title_anchor", "top-left")
                if anchor == "top-left":
                    self.ax.text(rect.x + 16, rect.y + 10, g["title"], ha="left", va="top",
                                 fontsize=t["group_title_size"], color=t["group_title_color"],
                                 fontweight="bold", zorder=1.5)
                else:
                    self.ax.text(rect.cx, rect.y + 10, g["title"], ha="center", va="top",
                                 fontsize=t["group_title_size"], color=t["group_title_color"],
                                 fontweight="bold", zorder=1.5)

    def draw_node(self, node: dict):
        t = self.theme
        rect = self.rects[node["id"]]
        fill, border = self.resolve_colors(node)
        lw = float(node.get("border_w", t["node_border_w"]))
        self.rbox(rect, fill, border, lw, zorder=2.0)

        # Content block metrics; the whole block is vertically centered.
        title_h = 26.0
        item_h = 19.0
        chips = node.get("chips", [])
        items = node.get("items", [])
        chip_h, chip_gap = 34.0, 10.0
        chip_rows = 0
        chip_block = 0.0
        if chips:
            chip_cols = int(node.get("chip_columns", 3)) or 1
            chip_rows = math.ceil(len(chips) / chip_cols)
            chip_block = 6.0 + chip_rows * chip_h + max(0, chip_rows - 1) * chip_gap
            if items:
                chip_block += 10.0
        content_h = title_h + chip_block + len(items) * item_h
        align = node.get("valign", "center")
        if align == "top":
            y = rect.y + 16.0
        else:
            y = rect.y + max(14.0, (rect.h - content_h) / 2)

        self.ax.text(rect.cx, y, node.get("title", ""), ha="center", va="top",
                     fontsize=t["node_title_size"], fontweight="bold",
                     color=node.get("title_color", t["node_title_color"]), zorder=3.0)
        y += title_h

        if chips:
            chip_cols = int(node.get("chip_columns", 3)) or 1
            chip_w = (rect.w - 2 * 34.0 - (chip_cols - 1) * chip_gap) / chip_cols
            y += 6.0
            for i, label in enumerate(chips):
                r, c = divmod(i, chip_cols)
                cx0 = rect.x + 34.0 + c * (chip_w + chip_gap)
                cy0 = y + r * (chip_h + chip_gap)
                self.rbox(Rect(cx0, cy0, chip_w, chip_h), t["chip_fill"], t["chip_border"],
                          0.9, radius=6.0, zorder=2.5)
                self.ax.text(cx0 + chip_w / 2, cy0 + chip_h / 2, label,
                             ha="center", va="center", fontsize=t["chip_size"],
                             color=t["chip_text"], zorder=3.0)
            y += chip_rows * chip_h + max(0, chip_rows - 1) * chip_gap
            if items:
                y += 10.0

        for item in items:
            self.ax.text(rect.cx, y, item, ha="center", va="top",
                         fontsize=t["node_item_size"], color=t["node_item_color"],
                         zorder=3.0)
            y += item_h

    def draw_edges(self):
        t = self.theme
        for e in self.spec.get("edges", []):
            a = self.rects.get(e.get("from"))
            b = self.rects.get(e.get("to"))
            if a is None and e.get("from") in self.groups:
                a = self.groups[e["from"]]
            if b is None and e.get("to") in self.groups:
                b = self.groups[e["to"]]
            if a is None or b is None:
                warn(f"edge {e.get('from')!r} -> {e.get('to')!r} references unknown node/group; skipped")
                continue
            color = e.get("color", t["edge_color"])
            lw = float(e.get("width", t["edge_w"]))
            dashed = e.get("style") == "dashed"
            ls = (0, (5, 4)) if dashed else "solid"
            route = e.get("route", "straight")
            pts, already_drawn = self.route_edge(a, b, e, route)

            if not already_drawn:
                for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
                    self.ax.plot([x0, x1], [y0, y1], color=color, lw=lw,
                                 linestyle=ls, zorder=4.0, solid_capstyle="round")
                if e.get("arrow", True):
                    x0, y0 = pts[-2]
                    x1, y1 = pts[-1]
                    if x0 == x1 and y0 == y1 and len(pts) >= 3:
                        x0, y0 = pts[-3]
                    arr = FancyArrowPatch((x0, y0), (x1, y1), arrowstyle="-|>",
                                          mutation_scale=12, lw=0, color=color, zorder=4.5)
                    self.ax.add_patch(arr)

            if e.get("label"):
                if route == "elbow":
                    mx, my = self.longest_segment_midpoint(pts)
                else:
                    mx, my = self.path_midpoint(pts)
                self.ax.text(mx, my, e["label"], ha="center", va="center",
                             fontsize=t["edge_label_size"], color=t["edge_label_color"],
                             zorder=5.0,
                             bbox=dict(boxstyle="round,pad=0.28", fc="#ffffff",
                                       ec="none", alpha=0.92))

    def route_edge(self, a: Rect, b: Rect, e: dict,
                   route: str) -> tuple[list[tuple[float, float]], bool]:
        """Return (polyline points for label placement, drawn_already)."""
        if route == "elbow":
            a_side = e.get("from_side", "right")
            b_side = e.get("to_side", "left")
            stub = float(e.get("stub", 30.0))
            p0 = a.side_point(a_side)
            p1 = b.side_point(b_side)
            d0 = {"top": (0, -1), "bottom": (0, 1), "left": (-1, 0), "right": (1, 0)}[a_side]
            d1 = {"top": (0, -1), "bottom": (0, 1), "left": (-1, 0), "right": (1, 0)}[b_side]
            q0 = (p0[0] + d0[0] * stub, p0[1] + d0[1] * stub)
            q1 = (p1[0] + d1[0] * stub, p1[1] + d1[1] * stub)
            h0 = a_side in ("left", "right")
            h1 = b_side in ("left", "right")
            if h0 and h1:
                mx = (q0[0] + q1[0]) / 2
                return [p0, q0, (mx, q0[1]), (mx, q1[1]), q1, p1], False
            if not h0 and not h1:
                my = (q0[1] + q1[1]) / 2
                return [p0, q0, (q0[0], my), (q1[0], my), q1, p1], False
            if h0:
                return [p0, q0, (q1[0], q0[1]), q1, p1], False
            return [p0, q0, (q0[0], q1[1]), q1, p1], False
        if route == "curve":
            rad = float(e.get("rad", 0.18))
            p0 = a.border_point_towards(b.cx, b.cy)
            p1 = b.border_point_towards(a.cx, a.cy)
            color = e.get("color", self.theme["edge_color"])
            arr = FancyArrowPatch(p0, p1, arrowstyle="-|>" if e.get("arrow", True) else "-",
                                  mutation_scale=12, lw=float(e.get("width", self.theme["edge_w"])),
                                  color=color, zorder=4.0,
                                  linestyle=(0, (5, 4)) if e.get("style") == "dashed" else "solid",
                                  connectionstyle=f"arc3,rad={rad}")
            self.ax.add_patch(arr)
            mid = ((p0[0] + p1[0]) / 2 - rad * (p1[1] - p0[1]) / 2,
                   (p0[1] + p1[1]) / 2 + rad * (p1[0] - p0[0]) / 2)
            return [p0, mid, p1], True
        # straight, clipped at both rect borders
        p0 = a.border_point_towards(b.cx, b.cy)
        p1 = b.border_point_towards(a.cx, a.cy)
        return [p0, p1], False

    @staticmethod
    def longest_segment_midpoint(pts: list[tuple[float, float]]) -> tuple[float, float]:
        best, mid = -1.0, pts[len(pts) // 2]
        for p, q in zip(pts[:-1], pts[1:]):
            L = math.hypot(q[0] - p[0], q[1] - p[1])
            if L > best:
                best = L
                mid = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
        return mid

    @staticmethod
    def path_midpoint(pts: list[tuple[float, float]]) -> tuple[float, float]:
        total = 0.0
        segs = []
        for p, q in zip(pts[:-1], pts[1:]):
            L = math.hypot(q[0] - p[0], q[1] - p[1])
            segs.append(L)
            total += L
        target = total / 2
        acc = 0.0
        for (p, q), L in zip(zip(pts[:-1], pts[1:]), segs):
            if acc + L >= target and L > 0:
                f = (target - acc) / L
                return p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f
            acc += L
        return pts[len(pts) // 2]

    def compute_legend_rect(self) -> None:
        """Size the legend panel up-front so layout helpers can reserve its margin."""
        legend = self.spec.get("legend")
        if not legend or not legend.get("items"):
            return
        t = self.theme
        items = legend["items"]
        row_h = float(legend.get("row_height", 33.0))
        pad = 18.0
        title_h = 34.0 if legend.get("title") else 10.0
        text_w = max(est_text_width(i.get("label", ""), t["legend_text_size"]) for i in items)
        lw_ = float(legend.get("width", max(230.0, text_w + 20.0 + 3 * pad + 8)))
        lh = title_h + len(items) * row_h + pad
        self.legend_rect = Rect(float(legend.get("x", t["padding"])),
                                float(legend.get("y", 128.0)), lw_, lh)

    def draw_legend(self):
        if self.legend_rect is None:
            return
        legend = self.spec["legend"]
        t = self.theme
        items = legend.get("items", [])
        lx, ly = self.legend_rect.x, self.legend_rect.y
        row_h = float(legend.get("row_height", 33.0))
        swatch = 20.0
        pad = 18.0
        title_h = 34.0 if legend.get("title") else 10.0
        self.rbox(self.legend_rect, "#ffffff", t["node_border"], 1.0, radius=10.0, zorder=6.0)
        y = ly + 12.0
        if legend.get("title"):
            self.ax.text(lx + pad, y, legend["title"], ha="left", va="top",
                         fontsize=t["group_title_size"], fontweight="bold",
                         color=t["legend_title_color"], zorder=7.0)
            y += title_h - 8.0
        else:
            y += 2.0
        for item in items:
            fill, border = self.resolve_colors(item)
            style = item.get("style", "box")
            cy = y + row_h / 2 - 2.0
            if style == "line":
                self.ax.plot([lx + pad, lx + pad + swatch + 8], [cy, cy],
                             color=item.get("color", border), lw=1.6,
                             linestyle=(0, (5, 4)) if item.get("line") == "dashed" else "solid",
                             zorder=7.0)
            else:
                self.rbox(Rect(lx + pad, cy - swatch / 2, swatch + 8, swatch),
                          fill, border, 1.0, radius=5.0, zorder=7.0)
            self.ax.text(lx + pad + swatch + 16, cy, item.get("label", ""),
                         ha="left", va="center", fontsize=t["legend_text_size"],
                         color=t["legend_text_color"], zorder=7.0)
            y += row_h

    def draw_notes(self):
        t = self.theme
        for n in self.spec.get("notes", []):
            self.ax.text(float(n.get("x", t["padding"])), float(n.get("y", self.H - 24)),
                         n.get("text", ""), ha=n.get("ha", "left"), va="top",
                         fontsize=t["note_size"], color=t["note_color"], zorder=5.0)

    # -- validation -------------------------------------------------------

    def check_overlaps(self):
        rects = list(self.rects.items())
        for i in range(len(rects)):
            for j in range(i + 1, len(rects)):
                (id_a, ra), (id_b, rb) = rects[i], rects[j]
                if ra.intersects(rb, gap=4.0):
                    warn(f"nodes {id_a!r} and {id_b!r} overlap "
                         f"({ra.x:.0f},{ra.y:.0f},{ra.w:.0f}x{ra.h:.0f}) vs "
                         f"({rb.x:.0f},{rb.y:.0f},{rb.w:.0f}x{rb.h:.0f})")
        if self.legend_rect is not None:
            for nid, r in rects:
                if r.intersects(self.legend_rect, gap=8.0):
                    warn(f"legend overlaps node {nid!r}")
            for gid, r in self.groups.items():
                if r.intersects(self.legend_rect, gap=8.0):
                    warn(f"legend overlaps group {gid!r}")
        pad = self.theme["padding"]
        for nid, r in rects:
            if r.x < 0 or r.y < 0 or r.right > self.W or r.bottom > self.H:
                warn(f"node {nid!r} overflows canvas "
                     f"(x={r.x:.0f}, y={r.y:.0f}, right={r.right:.0f}, bottom={r.bottom:.0f}, "
                     f"canvas={self.W:.0f}x{self.H:.0f})")
            elif r.x < pad / 2 or r.right > self.W - pad / 2:
                warn(f"node {nid!r} is close to the horizontal canvas edge")

    # -- pipeline ---------------------------------------------------------

    def render(self) -> tuple[str, str]:
        for node in self.spec.get("nodes", []):
            self.nodes[node["id"]] = node
        self.compute_legend_rect()  # size legend first so layouts can reserve its margin
        self.apply_layouts()
        for node in self.nodes.values():
            w, h = self.auto_size(node)
            if "x" not in node or "y" not in node:
                warn(f"node {node['id']!r} has no position (no x/y and no layout helper); "
                     "placed at top-left content origin")
                node.setdefault("x", self.theme["padding"])
                node.setdefault("y", 150.0)
            self.rects[node["id"]] = Rect(float(node["x"]), float(node["y"]), w, h)
        for g in self.spec.get("groups", []):
            self.groups[g.get("id", g.get("title", ""))] = Rect(
                float(g["x"]), float(g["y"]), float(g["w"]), float(g["h"]))
        self.check_overlaps()

        self.draw_title_block()
        self.draw_groups()
        for node in self.nodes.values():
            self.draw_node(node)
        self.draw_edges()
        if self.legend_rect is not None:
            self.draw_legend()  # redraw above everything
        self.draw_notes()

        os.makedirs(self.out_dir, exist_ok=True)
        svg_path = os.path.join(self.out_dir, f"{self.name}.svg")
        png_path = os.path.join(self.out_dir, f"{self.name}.png")
        self.fig.savefig(svg_path, facecolor=self.theme["canvas_bg"])
        self.fig.savefig(png_path, dpi=180, facecolor=self.theme["canvas_bg"])
        plt.close(self.fig)

        n_nodes = len(self.nodes)
        n_edges = len(self.spec.get("edges", []))
        n_groups = len(self.spec.get("groups", []))
        print(f"[diagram_forge] {self.name}: canvas={self.W:.0f}x{self.H:.0f}, "
              f"nodes={n_nodes}, edges={n_edges}, groups={n_groups}")
        print(f"[diagram_forge] wrote {svg_path}")
        print(f"[diagram_forge] wrote {png_path}")
        return svg_path, png_path


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def load_spec(path: str) -> dict:
    with open(path, "r", encoding="utf-8") as fh:
        spec = json.load(fh)
    if "name" not in spec:
        spec["name"] = os.path.splitext(os.path.basename(path))[0]
    return spec


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Render Ludots architecture diagrams from JSON specs.")
    parser.add_argument("spec", nargs="?", help="path to a spec JSON file")
    parser.add_argument("--all", action="store_true",
                        help="render every spec in docs/diagrams/specs/")
    parser.add_argument("--specs-dir", default=None,
                        help="spec directory (default: <repo>/docs/diagrams/specs)")
    parser.add_argument("--out-dir", default=None,
                        help="output directory (default: <repo>/docs/diagrams)")
    args = parser.parse_args(argv)

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    specs_dir = args.specs_dir or os.path.join(repo_root, "docs", "diagrams", "specs")
    out_dir = args.out_dir or os.path.join(repo_root, "docs", "diagrams")

    font = setup_fonts()
    print(f"[diagram_forge] font: {font}")

    if args.all:
        if not os.path.isdir(specs_dir):
            print(f"[diagram_forge] specs dir not found: {specs_dir}", file=sys.stderr)
            return 1
        files = sorted(f for f in os.listdir(specs_dir) if f.endswith(".json"))
        if not files:
            print(f"[diagram_forge] no specs in {specs_dir}")
            return 0
        rc = 0
        for f in files:
            try:
                DiagramRenderer(load_spec(os.path.join(specs_dir, f)), out_dir).render()
            except Exception as exc:
                rc = 1
                print(f"[diagram_forge ERROR] {f}: {exc}", file=sys.stderr)
        return rc

    if not args.spec:
        parser.error("provide a spec path or --all")
    DiagramRenderer(load_spec(args.spec), out_dir).render()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
