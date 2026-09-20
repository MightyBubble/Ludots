#!/usr/bin/env python3
"""Rasterize Natural Earth admin_0 polygons into Ludots Field schema v2 rects.

Projection SSOT: east_asia_terrain_profile.json (spherical Albers + WorldWidthCm scale).
World cm = Albers meters * 100 * (playableWorldWidthCm / sourceWorldWidthCm).
Cell = floor(worldCm / cellSizeCm), matching FieldGridSpec2D.WorldToCell.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

from shapely.geometry import Point, shape
from shapely.ops import unary_union
from shapely.prepared import prep

# East Asia board interest set (Natural Earth ADMIN names). First paint wins on ties.
COUNTRY_ORDER = [
    "China",
    "Mongolia",
    "Russia",
    "Japan",
    "South Korea",
    "North Korea",
    "Vietnam",
    "Laos",
    "Myanmar",
    "Thailand",
    "Cambodia",
    "Philippines",
    "Malaysia",
    "Indonesia",
    "India",
    "Nepal",
    "Bhutan",
    "Bangladesh",
    "Taiwan",
]


def load_profile(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def make_albers(projection: dict):
    r = float(projection["earthRadiusM"])
    lam0 = math.radians(float(projection["centralMeridianDeg"]))
    phi0 = math.radians(float(projection["latitudeOfOriginDeg"]))
    phi1 = math.radians(float(projection["standardParallel1Deg"]))
    phi2 = math.radians(float(projection["standardParallel2Deg"]))
    n = (math.sin(phi1) + math.sin(phi2)) / 2.0
    c = math.cos(phi1) ** 2 + 2.0 * n * math.sin(phi1)
    rho0 = r * math.sqrt(c - 2.0 * n * math.sin(phi0)) / n

    def project(lon_deg: float, lat_deg: float) -> tuple[float, float]:
        lam = math.radians(lon_deg)
        phi = math.radians(lat_deg)
        rho = r * math.sqrt(max(1e-12, c - 2.0 * n * math.sin(phi))) / n
        theta = n * (lam - lam0)
        return rho * math.sin(theta), rho0 - rho * math.cos(theta)

    return project


def project_ring(ring, project, scale: float) -> list[tuple[float, float]]:
    out = []
    for lon, lat, *rest in ring:
        x_m, y_m = project(float(lon), float(lat))
        out.append((x_m * 100.0 * scale, y_m * 100.0 * scale))
    return out


def project_geometry(geom, project, scale: float):
    """Rebuild shapely geometry in playable world cm."""
    gj = geom.__geo_interface__
    gtype = gj["type"]
    coords = gj["coordinates"]

    def map_polygon(poly_coords):
        return [project_ring(ring, project, scale) for ring in poly_coords]

    if gtype == "Polygon":
        return shape({"type": "Polygon", "coordinates": map_polygon(coords)})
    if gtype == "MultiPolygon":
        return shape(
            {
                "type": "MultiPolygon",
                "coordinates": [map_polygon(poly) for poly in coords],
            }
        )
    raise ValueError(f"Unsupported geometry type: {gtype}")


def coalesce_points(points: list[tuple[int, int, int]]) -> list[list[int]]:
    """Row-RLE then vertical merge — mirrors FieldRectCodec.CoalescePoints."""
    if not points:
        return []
    ordered = sorted(points, key=lambda p: (p[2], p[1], p[0]))
    runs: list[tuple[int, int, int, int]] = []
    i = 0
    while i < len(ordered):
        x0, y, rid = ordered[i]
        x1 = x0
        i += 1
        while i < len(ordered) and ordered[i][2] == rid and ordered[i][1] == y and ordered[i][0] == x1 + 1:
            x1 = ordered[i][0]
            i += 1
        runs.append((y, x0, x1, rid))

    runs.sort(key=lambda r: (r[3], r[1], r[0], r[2]))
    merged: list[list[int]] = []
    active: dict[tuple[int, int, int], list[int]] = {}
    for y, x0, x1, rid in runs:
        key = (x0, x1, rid)
        prev = active.get(key)
        if prev is not None and prev[3] + 1 == y:
            prev[3] = y
        else:
            if prev is not None:
                merged.append(prev)
            active[key] = [x0, y, x1, y, rid]
    merged.extend(active.values())
    merged.sort(key=lambda r: (r[4], r[1], r[0], r[2], r[3]))
    return merged


def region_key(admin_name: str) -> str:
    slug = (
        admin_name.strip()
        .lower()
        .replace(" ", "_")
        .replace(".", "")
        .replace("'", "")
    )
    return f"country.{slug}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--geojson", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--cell-size-cm", type=int, default=7142)
    parser.add_argument("--layer", type=str, default="ownership.east_asia.country")
    args = parser.parse_args()

    profile = load_profile(args.profile)
    projection = profile["projection"]
    vhtm = profile["continuousHeightmap"]
    source_w = float(vhtm["sourceWorldWidthCm"])
    playable_w = float(vhtm["playableWorldWidthOverrideCm"])
    playable_h = float(vhtm["worldHeightCm"])
    scale = playable_w / source_w
    half_w = playable_w / 2.0
    half_h = playable_h / 2.0
    project = make_albers(projection)

    board = shape(
        {
            "type": "Polygon",
            "coordinates": [
                [
                    (-half_w, -half_h),
                    (half_w, -half_h),
                    (half_w, half_h),
                    (-half_w, half_h),
                    (-half_w, -half_h),
                ]
            ],
        }
    )

    geo = json.loads(args.geojson.read_text(encoding="utf-8"))
    by_admin = {}
    for feature in geo["features"]:
        props = feature.get("properties") or {}
        admin = props.get("ADMIN") or props.get("NAME")
        if not admin:
            continue
        by_admin[admin] = feature

    selected = []
    for admin in COUNTRY_ORDER:
        feature = by_admin.get(admin)
        if feature is None:
            print(f"warn: missing ADMIN={admin}", file=sys.stderr)
            continue
        geom = shape(feature["geometry"])
        projected = project_geometry(geom, project, scale)
        clipped = projected.intersection(board)
        if clipped.is_empty:
            continue
        selected.append((admin, clipped))

    if not selected:
        raise SystemExit("No countries intersect the playable board.")

    # Paint smaller countries later so enclaves/coast fragments win over large neighbors when grids collide.
    selected.sort(key=lambda item: item[1].area, reverse=True)

    cell = args.cell_size_cm
    painted: dict[tuple[int, int], int] = {}
    region_names: list[str] = []
    region_areas: list[float] = []

    for admin, geom in selected:
        region_names.append(region_key(admin))
        region_id = len(region_names)
        region_areas.append(geom.area)
        prepared = prep(geom)
        minx, miny, maxx, maxy = geom.bounds
        x0 = math.floor(minx / cell)
        x1 = math.floor(maxx / cell)
        y0 = math.floor(miny / cell)
        y1 = math.floor(maxy / cell)
        for cy in range(y0, y1 + 1):
            for cx in range(x0, x1 + 1):
                center = Point((cx * cell) + (cell * 0.5), (cy * cell) + (cell * 0.5))
                if not prepared.contains(center):
                    continue
                key = (cx, cy)
                if key not in painted:
                    painted[key] = region_id

    # Rebuild region ids dense by ordinal name sort (FieldCellsConfigLoader contract).
    name_to_temp = {name: i + 1 for i, name in enumerate(region_names)}
    sorted_names = sorted(region_names)
    temp_to_final = {name_to_temp[name]: i + 1 for i, name in enumerate(sorted_names)}
    remapped = [(x, y, temp_to_final[rid]) for (x, y), rid in painted.items()]
    rects = coalesce_points(remapped)

    payload = {
        "schemaVersion": 2,
        "layer": args.layer,
        "regions": sorted_names,
        "rects": rects,
        "metadata": {
            "source": "Natural Earth 110m admin_0 countries",
            "projectionProfile": str(args.profile),
            "cellSizeCm": cell,
            "playableWorldWidthCm": int(playable_w),
            "nonDefaultCells": len(remapped),
            "countries": [
                {"admin": admin, "region": region_key(admin), "areaCm2": area}
                for (admin, _), area in zip(selected, region_areas)
            ],
        },
    }
    # metadata is authoring aid; Field loader forbids unknown top-level keys — strip before write for runtime file.
    runtime = {
        "schemaVersion": 2,
        "layer": args.layer,
        "regions": sorted_names,
        "rects": rects,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(runtime, indent=2) + "\n", encoding="utf-8")
    meta_path = args.out.with_suffix(args.out.suffix + ".meta.json")
    meta_path.write_text(json.dumps(payload["metadata"], indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "out": str(args.out),
                "regions": len(sorted_names),
                "rects": len(rects),
                "cells": len(remapped),
                "names": sorted_names,
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
