#!/usr/bin/env python3
"""Record one play.mp4 per GraphNodeOp gallery, sequentially on one DISPLAY.

Do not parallelize: Raylib/Xvfb SIGSEGV when two captures share one display.
If capture cannot start, write visual.capture.blocked and continue the rest.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

PREFIX = "capability_standard_graph_op_"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument("--op", action="append", help="Record only these ops. Repeatable.")
    args = parser.parse_args()
    repo = Path(args.repo).resolve()
    vignette_dir = repo / "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes"
    ops = args.op or sorted(p.stem for p in vignette_dir.glob("*.json"))
    launcher = repo / "scripts" / "run-mod-launcher.cmd"
    failed = []
    for op in ops:
        sid = PREFIX + op
        out = repo / "artifacts" / "evidence" / sid
        out.mkdir(parents=True, exist_ok=True)
        env = os.environ.copy()
        env["LUDOTS_TAKE_SCREENSHOT_FRAMES"] = "30,90,150"
        env["LUDOTS_AUTO_EXIT_FRAME"] = "180"
        cmd = [
            "bash",
            str(repo / "scripts" / "run-mod-launcher.sh") if (repo / "scripts" / "run-mod-launcher.sh").exists() else str(launcher),
            "cli",
            "launch",
            f"${sid}",
            "--adapter",
            "raylib",
            "--record",
            str(out),
        ]
        if not (repo / "scripts" / "run-mod-launcher.sh").exists():
            cmd = ["cmd.exe", "/c", str(launcher), "cli", "launch", f"${sid}", "--adapter", "raylib", "--record", str(out)]
        print("Recording", sid, flush=True)
        proc = subprocess.run(cmd, cwd=repo, env=env)
        if proc.returncode != 0:
            blocked = {
                "kind": "visual.capture.blocked",
                "subject": sid,
                "blocker": f"launcher exit {proc.returncode}",
            }
            (out / "manifest.json").write_text(json.dumps(blocked, indent=2) + "\n", encoding="utf-8")
            failed.append(sid)
    print(f"Recorded {len(ops) - len(failed)}/{len(ops)}; blocked {len(failed)}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
