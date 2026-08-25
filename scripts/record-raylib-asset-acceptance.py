#!/usr/bin/env python3
"""Record play.mp4 + poster.png for the raylib asset acceptance tool.

Mirrors scripts/record-engine-galleries.py: drive the acceptance app via the
host-env still contract (LUDOTS_TAKE_SCREENSHOT_PATH + LUDOTS_TAKE_SCREENSHOT_FRAMES),
stitch stills into play.mp4 with ffmpeg, pick poster.png. Do not invent videos.

Scenarios:
  demo  — mannequin GLB + --demo（视图拆通道 + 贴图/缺省标量消融的自动时间线）
  obj   — mass_navigation blocker_rock OBJ（#1050 崩溃资产经 Assimp 转 GLB 装载的回归证明）

Sequential only: two Raylib captures on one display SIGSEGV.

Output: artifacts/evidence/raylib_asset_acceptance_<scenario>/{play.mp4,poster.png}
(committed set only; screens/ and logs are working files, removed on success).
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

APP_PROJECT = "src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance/Ludots.App.RaylibAssetAcceptance.csproj"
APP_EXE = "src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance/bin/Debug/net8.0/Ludots.App.RaylibAssetAcceptance.exe"
MANNEQUIN = "src/Apps/Raylib/Ludots.App.RaylibEngineGallery/assets/Models/mannequin_large_walk.glb"
CRASH_OBJ = "mods/capabilities/navigation/MassNavigationMod/assets/Models/mass_navigation_blocker_rock.obj"
EVIDENCE_PREFIX = "raylib_asset_acceptance_"

# demo 时间线 720 帧 = 6 段 ×120（最终/Albedo/法线/粗糙度/缺省标量消融/回到最终）。
DEMO_PLAN: list[tuple[int, float]] = (
    [(6, 1.0), (60, 0.9)]
    + [(frame, 0.35) for frame in range(96, 721, 24)]
    + [(719, 1.2)]
)
OBJ_PLAN: list[tuple[int, float]] = (
    [(6, 1.0), (60, 0.9)]
    + [(frame, 0.4) for frame in range(96, 301, 24)]
    + [(299, 1.2)]
)

SCENARIOS: dict[str, dict] = {
    "demo": {
        "model": MANNEQUIN,
        "frames": 720,
        "plan": DEMO_PLAN,
        "extra_args": ["--demo"],
    },
    "obj": {
        "model": CRASH_OBJ,
        "frames": 300,
        "plan": OBJ_PLAN,
        "extra_args": [],
    },
}


def stitch_stills(pngs: list[Path], plan: list[tuple[int, float]], play: Path) -> None:
    list_file = play.parent / "frames.concat.txt"
    lines: list[str] = []
    for index, png in enumerate(pngs):
        duration = plan[index][1] if index < len(plan) else plan[-1][1]
        lines.append(f"file '{png.as_posix()}'")
        lines.append(f"duration {duration:g}")
    lines.append(f"file '{pngs[-1].as_posix()}'")
    lines.append(f"duration {plan[min(len(pngs) - 1, len(plan) - 1)][1]:g}")
    list_file.write_text("\n".join(lines) + "\n", encoding="utf-8")
    cmd = [
        "ffmpeg",
        "-y",
        "-f",
        "concat",
        "-safe",
        "0",
        "-i",
        str(list_file),
        "-vf",
        "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2",
        "-pix_fmt",
        "yuv420p",
        "-tune",
        "stillimage",
        "-an",
        str(play),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg failed: {proc.stderr[-2000:]}")


def build_app(repo: Path) -> None:
    proc = subprocess.run(
        ["dotnet", "build", str(repo / APP_PROJECT), "-v", "q", "--nologo"],
        cwd=repo,
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"acceptance app build failed: {proc.stderr[-2000:]}")


def record_one(repo: Path, scenario: str, exe: Path, out: Path) -> None:
    spec = SCENARIOS[scenario]
    if out.exists():
        shutil.rmtree(out)
    screens = out / "screens"
    screens.mkdir(parents=True)

    env = os.environ.copy()
    env["LUDOTS_TAKE_SCREENSHOT_PATH"] = str(screens / "still.png")
    env["LUDOTS_TAKE_SCREENSHOT_FRAMES"] = ",".join(str(frame) for frame, _ in spec["plan"])
    log_path = out / "launch.log"
    cmd = [str(exe), "--model", str(repo / spec["model"]), "--frames", str(spec["frames"])] + spec["extra_args"]
    with log_path.open("w", encoding="utf-8") as stream:
        stream.write(f"$ {' '.join(cmd)}\n")
        stream.flush()
        proc = subprocess.run(cmd, cwd=repo, env=env, stdout=stream, stderr=subprocess.STDOUT, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"acceptance app exit {proc.returncode}; see {log_path}")

    pngs = sorted(screens.glob("still_*.png"))
    if len(pngs) < 4:
        raise RuntimeError(f"app wrote {len(pngs)} stills (need >= 4); see {log_path}")

    play = out / "play.mp4"
    stitch_stills(pngs, spec["plan"], play)
    if not play.is_file() or play.stat().st_size < 20_000:
        raise RuntimeError(f"play.mp4 missing or empty: {play}")

    poster_src = pngs[2] if len(pngs) >= 3 else pngs[-1]
    shutil.copy2(poster_src, out / "poster.png")
    if (out / "poster.png").stat().st_size < 1_000:
        raise RuntimeError(f"poster.png missing or empty: {out / 'poster.png'}")

    shutil.rmtree(screens)
    log_path.unlink()
    print(f"  wrote {play} ({play.stat().st_size} bytes), poster.png", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument("--scenario", action="append", help="Record only these scenarios (demo/obj). Repeatable.")
    parser.add_argument("--build", default="auto", choices=("auto", "always", "never"))
    args = parser.parse_args()

    repo = Path(args.repo).resolve()
    scenarios = args.scenario or list(SCENARIOS)
    unknown = [s for s in scenarios if s not in SCENARIOS]
    if unknown:
        print(f"Unknown scenarios: {unknown}; available: {list(SCENARIOS)}", file=sys.stderr)
        return 1

    exe = repo / APP_EXE
    if args.build == "always" or (args.build == "auto" and not exe.is_file()):
        print("Building acceptance app...", flush=True)
        build_app(repo)

    evidence = repo / "artifacts" / "evidence"
    for scenario in scenarios:
        out = evidence / f"{EVIDENCE_PREFIX}{scenario}"
        print(f"[record] {scenario} → {out}", flush=True)
        record_one(repo, scenario, exe, out)

    return 0


if __name__ == "__main__":
    sys.exit(main())
