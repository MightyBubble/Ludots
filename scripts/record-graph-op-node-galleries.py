#!/usr/bin/env python3
"""Record one real play.mp4 per GraphNodeOp gallery from the Raylib window.

Launch the production gallery binding. Dump Raylib framebuffer stills.
Stitch those stills into play.mp4. Do not invent videos. Do not use launcher
--record: that path has no GraphOps scenario and would write a fake bundle.

Sequential only: two Raylib captures on one DISPLAY SIGSEGV.
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

PREFIX = "capability_standard_graph_op_"
CLI_PROJECT = "src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj"
CLI_DLL = "src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll"
AUTO_EXIT_FRAME = 120
# One still every 8 frames from first caption beat through auto-exit.
STILL_FRAMES = list(range(24, AUTO_EXIT_FRAME + 1, 8))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument("--op", action="append", help="Record only these ops. Repeatable.")
    parser.add_argument("--build", default="auto", choices=("auto", "always", "never"))
    parser.add_argument(
        "--publish-dir",
        default=None,
        help="Copy finished play.mp4 here for the cloud artifact viewer. Skipped when omitted.",
    )
    parser.add_argument(
        "--poster-frame",
        default="first-settlement",
        choices=("first-settlement", "last"),
        help=(
            "Which still becomes poster.png. first-settlement = second still "
            "(frame 32: first think beat settled, launch animation done); "
            "last = legacy loop-tail frame."
        ),
    )
    args = parser.parse_args()
    repo = Path(args.repo).resolve()
    vignette_dir = (
        repo
        / "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes"
    )
    ops = args.op or sorted(
        p.stem for p in vignette_dir.glob("*.json") if not p.name.startswith("_")
    )
    if not ops:
        print("No vignettes to record.", file=sys.stderr)
        return 1

    publish = Path(args.publish_dir).resolve() if args.publish_dir else None
    if publish is not None:
        publish.mkdir(parents=True, exist_ok=True)

    failed: list[str] = []
    build_mode = args.build
    for index, op in enumerate(ops):
        sid = PREFIX + op
        out = repo / "artifacts" / "evidence" / sid
        print(f"[{index + 1}/{len(ops)}] Recording {sid}", flush=True)
        try:
            record_one(repo, sid, op, out, publish, build_mode, args.poster_frame)
        except Exception as exc:
            print(f"FAILED {sid}: {exc}", file=sys.stderr, flush=True)
            failed.append(sid)
        else:
            if build_mode == "auto":
                build_mode = "never"

    print(f"Recorded {len(ops) - len(failed)}/{len(ops)}; failed {len(failed)}")
    if failed:
        print("Failed:", ", ".join(failed), file=sys.stderr)
        return 1
    return 0


def record_one(
    repo: Path,
    sid: str,
    op: str,
    out: Path,
    publish: Path,
    build_mode: str,
    poster_frame: str = "first-settlement",
) -> None:
    if out.exists():
        shutil.rmtree(out)
    screens = out / "screens"
    screens.mkdir(parents=True)
    still_path = screens / "still.png"
    env = os.environ.copy()
    if os.name == "posix":
        env["DISPLAY"] = ":99"
        env["LIBGL_ALWAYS_SOFTWARE"] = "1"
        env["GALLIUM_DRIVER"] = "llvmpipe"
    env["LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY"] = "1"
    env["LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY"] = "1"
    env["LUDOTS_RAYLIB_PRIMITIVE_RENDER_MODE"] = "immediate"
    env["LUDOTS_RAYLIB_MAX_MODEL_INSTANCES_PER_DRAW"] = "1"
    env["LUDOTS_AUTO_EXIT_FRAME"] = str(AUTO_EXIT_FRAME)
    env["LUDOTS_TAKE_SCREENSHOT_PATH"] = str(still_path)
    env["LUDOTS_TAKE_SCREENSHOT_FRAMES"] = ",".join(str(frame) for frame in STILL_FRAMES)
    env["LUDOTS_RAYLIB_DIAGNOSTIC_PATH"] = str(out / "raylib-diagnostic.log")
    env["LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT"] = "0"

    cli_dll = repo / CLI_DLL
    if not cli_dll.is_file():
        raise RuntimeError(f"Launcher CLI is not built: {cli_dll}")
    cmd = [
        "dotnet",
        "exec",
        "--roll-forward",
        "Major",
        str(cli_dll),
        "launch",
        sid,
        "--adapter",
        "raylib",
        "--build",
        build_mode,
    ]
    log = out / "launch.log"
    with log.open("w", encoding="utf-8") as stream:
        stream.write(f"$ {' '.join(cmd)}\n")
        stream.flush()
        proc = subprocess.Popen(
            cmd,
            cwd=repo,
            env=env,
            stdout=stream,
            stderr=subprocess.STDOUT,
            text=True,
        )
        try:
            cli_exit = proc.wait(timeout=180)
        except subprocess.TimeoutExpired as exc:
            proc.kill()
            raise RuntimeError(f"launcher CLI hung; see {log}") from exc

    text = log.read_text(encoding="utf-8")
    if cli_exit != 0:
        raise RuntimeError(f"launcher exit {cli_exit}; see {log}")

    pid_match = re.search(r"^pid=(\d+)\s*$", text, re.MULTILINE)
    if pid_match is None:
        raise RuntimeError(f"launcher did not print pid=; see {log}")
    pid = int(pid_match.group(1))
    wait_for_pid(pid, timeout_s=120)

    pngs = sorted(screens.glob("still_*.png"))
    if len(pngs) < 4:
        raise RuntimeError(
            f"Raylib wrote {len(pngs)} stills (need >= 4 real frames). See {log} and {out / 'raylib-diagnostic.log'}"
        )

    play = out / "play.mp4"
    stitch_stills(pngs, play)
    if not play.is_file() or play.stat().st_size < 20_000:
        raise RuntimeError(f"play.mp4 missing or empty: {play}")

    poster_src = (
        pngs[1] if (poster_frame == "first-settlement" and len(pngs) >= 2) else pngs[-1]
    )
    poster = out / "poster.png"
    shutil.copy2(poster_src, poster)
    if not poster.is_file() or poster.stat().st_size < 1_000:
        raise RuntimeError(f"poster.png missing or empty: {poster}")

    if publish is not None:
        dest = publish / f"{op}.mp4"
        shutil.copy2(play, dest)
        print(
            f"  wrote {play} ({play.stat().st_size} bytes), {poster.name}, and {dest}",
            flush=True,
        )
    else:
        print(
            f"  wrote {play} ({play.stat().st_size} bytes), {poster.name}",
            flush=True,
        )


def _pid_alive_windows(pid: int) -> bool:
    import ctypes

    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    STILL_ACTIVE = 259
    kernel32 = ctypes.windll.kernel32
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid)
    if not handle:
        # ERROR_ACCESS_DENIED (5): process exists but owned elsewhere.
        return ctypes.get_last_error() == 5
    try:
        exit_code = ctypes.c_ulong()
        if not kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
            return True
        return exit_code.value == STILL_ACTIVE
    finally:
        kernel32.CloseHandle(handle)


def pid_alive(pid: int) -> bool:
    if os.name == "nt":
        return _pid_alive_windows(pid)
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def wait_for_pid(pid: int, timeout_s: float) -> None:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        if not pid_alive(pid):
            return
        time.sleep(0.2)
    raise RuntimeError(f"gallery process {pid} did not exit within {timeout_s}s")


def stitch_stills(pngs: list[Path], play: Path) -> None:
    list_file = play.parent / "frames.concat.txt"
    lines: list[str] = []
    for png in pngs:
        lines.append(f"file '{png.as_posix()}'")
        lines.append("duration 0.12")
    lines.append(f"file '{pngs[-1].as_posix()}'")
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
        "-an",
        str(play),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg failed: {proc.stderr[-2000:]}")


if __name__ == "__main__":
    sys.exit(main())
