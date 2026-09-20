#!/usr/bin/env python3
"""Record one real play.mp4 per Raylib engine gallery scene.

Mirrors scripts/record-graph-op-node-galleries.py: drive the gallery app,
dump framebuffer stills via the host-env contract
(LUDOTS_TAKE_SCREENSHOT_PATH + LUDOTS_TAKE_SCREENSHOT_FRAMES), stitch stills
into play.mp4 with ffmpeg, pick poster.png. Do not invent videos.

Sequential only: two Raylib captures on one display SIGSEGV.

Output: artifacts/evidence/engine_raylib_<scene>/{play.mp4,poster.png}
(committed set only; screens/ and logs are working files, removed on success).
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

GALLERY_PROJECT = "src/Apps/Raylib/Ludots.App.RaylibPlayer/Ludots.App.RaylibPlayer.csproj"
GALLERY_ENGINE_PROJECT = "projects/engine_gallery"
GALLERY_EXE = "src/Apps/Raylib/Ludots.App.RaylibPlayer/bin/Debug/net9.0/Ludots.App.RaylibPlayer.exe"
EVIDENCE_PREFIX = "engine_raylib_"

# 通用节拍：起手稳 2 张 -> 动作段密集采样 -> 收尾定格。帧号 1 起。
DEFAULT_PLAN: list[tuple[int, float]] = (
    [(8, 0.9), (24, 0.9)]
    + [(frame, 0.35) for frame in range(48, 313, 24)]
    + [(360, 1.1), (400, 1.2), (419, 1.2)]
)
DEFAULT_FRAMES = 420

# 昼夜天空以 48s 为周期，拉长录制覆盖整圈相位（缩时录像）。
SPECIAL_PLANS: dict[str, tuple[int, list[tuple[int, float]]]] = {
    "sky_daynight": (
        2940,
        [(frame, 1.5) for frame in range(12, 2941, 168)],
    ),
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


def build_gallery(repo: Path) -> None:
    proc = subprocess.run(
        ["dotnet", "build", str(repo / GALLERY_PROJECT), "-v", "q", "--nologo"],
        cwd=repo,
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"gallery build failed: {proc.stderr[-2000:]}")


def record_one(repo: Path, scene: str, exe: Path, out: Path) -> None:
    frames, plan = SPECIAL_PLANS.get(scene, (DEFAULT_FRAMES, DEFAULT_PLAN))
    if out.exists():
        shutil.rmtree(out)
    screens = out / "screens"
    screens.mkdir(parents=True)

    env = os.environ.copy()
    env["LUDOTS_TAKE_SCREENSHOT_PATH"] = str(screens / "still.png")
    env["LUDOTS_TAKE_SCREENSHOT_FRAMES"] = ",".join(str(frame) for frame, _ in plan)
    log_path = out / "launch.log"
    cmd = [str(exe), "--project", GALLERY_ENGINE_PROJECT, "--scene", scene, "--frames", str(frames)]
    with log_path.open("w", encoding="utf-8") as stream:
        stream.write(f"$ {' '.join(cmd)}\n")
        stream.flush()
        proc = subprocess.run(cmd, cwd=repo, env=env, stdout=stream, stderr=subprocess.STDOUT, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"gallery exit {proc.returncode}; see {log_path}")

    pngs = sorted(screens.glob("still_*.png"))
    if len(pngs) < 4:
        raise RuntimeError(f"gallery wrote {len(pngs)} stills (need >= 4); see {log_path}")

    play = out / "play.mp4"
    stitch_stills(pngs, plan, play)
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
    parser.add_argument("--scene", action="append", help="Record only these scenes. Repeatable.")
    parser.add_argument("--build", default="auto", choices=("auto", "always", "never"))
    args = parser.parse_args()

    repo = Path(args.repo).resolve()
    sys.path.insert(0, str(repo / "scripts"))
    # 场景清单单一事实源：引擎工程运行时目录 catalog.json。
    import json

    catalog_path = repo / "projects/engine_gallery/catalog.json"
    scenes = args.scene or [
        entry["id"]
        for entry in json.loads(catalog_path.read_text(encoding="utf-8"))["scenes"]
    ]
    if not scenes:
        print(f"No scenes found in {catalog_path}.", file=sys.stderr)
        return 1

    exe = repo / GALLERY_EXE
    if args.build == "always" or (args.build == "auto" and not exe.is_file()):
        print("Building gallery app...", flush=True)
        build_gallery(repo)

    evidence = repo / "artifacts" / "evidence"
    failed: list[str] = []
    for index, scene in enumerate(scenes):
        sid = EVIDENCE_PREFIX + scene
        print(f"[{index + 1}/{len(scenes)}] Recording {sid}", flush=True)
        try:
            record_one(repo, scene, exe, evidence / sid)
        except Exception as exc:
            print(f"FAILED {sid}: {exc}", file=sys.stderr, flush=True)
            failed.append(sid)

    print(f"Recorded {len(scenes) - len(failed)}/{len(scenes)}; failed {len(failed)}")
    if failed:
        print("Failed:", ", ".join(failed), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
