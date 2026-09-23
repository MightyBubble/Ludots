from __future__ import annotations

import argparse
import copy
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
APP_PROJECT = ROOT / "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj"
SOURCE_RUNTIME = APP_PROJECT.parent / "launcher.mass-navigation-10k-hud.runtime.json"
SOURCE_GRAPH = APP_PROJECT.parent / "raylib.mass-navigation-10k-hud.launch.graph.json"
EXPECTED_MODS = [
    "LudotsCoreMod", "CoreInputMod", "SelectionInteractionMod", "MassNavigationMod",
    "CapabilityStandardMassNavigationLargeWorld10kMod", "AgentBridgeMod",
]
PORT = 47931


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def stamp():
    return dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")


def within_root(path):
    path = path.resolve()
    require(path.is_relative_to(ROOT), f"Path escapes independent worktree: {path}")
    return path


def fingerprint(path):
    path = within_root(path)
    require(path.is_file(), f"Required file is missing: {path}")
    return {"path": str(path), "bytes": path.stat().st_size,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def app_output():
    tfm = ET.parse(APP_PROJECT).getroot().findtext(".//TargetFramework")
    require(tfm is not None, "Application must have one explicit target framework.")
    return within_root(APP_PROJECT.parent / "bin" / "Release" / tfm)


def configuration():
    require((ROOT / ".git").exists(), f"Not a worktree: {ROOT}")
    graph = read_json(SOURCE_GRAPH)
    require(graph["orderedModIds"] == EXPECTED_MODS, "Unexpected 10K Mod order.")
    require([m["id"] for m in graph["plannedMods"]] == EXPECTED_MODS,
            "plannedMods and orderedModIds differ.")
    mods = []
    for mod in graph["plannedMods"]:
        raw = mod["rootPath"]
        root = within_root((app_output() / raw) if not Path(raw).is_absolute() else Path(raw))
        manifest = read_json(root / "mod.json")
        require(manifest["name"] == mod["id"], f"Wrong Mod manifest: {root}")
        binary = within_root(root / manifest["main"]) if manifest.get("main") else None
        project = root / (mod["id"] + ".csproj") if binary else None
        if project:
            require(project.is_file(), f"Mod project missing: {project}")
        else:
            require(mod["id"] == "SelectionInteractionMod", f"Unexpected data-only Mod: {mod['id']}")
        mods.append({"id": mod["id"], "root": str(root),
                     "project": str(project) if project else None,
                     "binary": str(binary) if binary else None})
    return graph, mods


def mode_evidence(mods):
    roots = {m["id"]: Path(m["root"]) for m in mods}
    nav = roots["MassNavigationMod"] / "assets"
    showcase = roots["CapabilityStandardMassNavigationLargeWorld10kMod"]
    config = read_json(nav / "MassNavigationConfig.json")
    scenario = config["scenario"]
    count = scenario["agentsPerTeam"] * len(scenario["teams"])
    require(count == 10000, f"Expected 10000 configured agents, got {count}.")
    require(config["scenarioRuntime"]["autoSpawnConfiguredScenario"], "Auto-spawn is off.")
    templates = read_json(nav / "Entities/templates.json")
    template_by_id = {x["id"]: x for x in templates}
    used = []
    for team in config["presentation"]["teams"]:
        for kind in ("lightTemplateId", "heavyTemplateId"):
            template = template_by_id[team[kind]]
            require(template["onSpawnEffect"] == "Effect.MassNavigation.Agent.HealthDrift",
                    f"Health effect missing from {team[kind]}.")
            used.append(team[kind])
    effects = read_json(nav / "GAS/effects.json")
    effect = next(x for x in effects if x["id"] == "Effect.MassNavigation.Agent.HealthDrift")
    require(effect["lifetime"] == "Infinite" and effect["duration"]["periodTicks"] > 0,
            "HealthDrift is not a periodic persistent effect.")
    presenters = (nav / "Presentation/presenters.json").read_text(encoding="utf-8")
    for token in ('"WorldHud"', '"WorldText"', '"Animator"', '"MinimapMarker"', '"GpuSkinnedInstance"'):
        require(token in presenters, f"Presentation asset contract missing: {token}")
    entry = showcase / "CapabilityStandardMassNavigationLargeWorld10kModEntry.cs"
    require("runtime.Visible = true" in entry.read_text(encoding="utf-8"), "Minimap startup visibility missing.")
    game = read_json(showcase / "assets/game.json")
    require(game["startupMapId"] == "mass_navigation", "Wrong startup map.")
    paths = [nav / "MassNavigationConfig.json", nav / "Entities/templates.json",
             nav / "GAS/effects.json", nav / "GAS/graphs.json",
             nav / "Presentation/presenters.json", nav / "Presentation/animator_controllers.json",
             nav / "Presentation/animation_clips.json", nav / "Presentation/host_assets.json",
             nav / "Presentation/animation_profiles.json", nav / "Models/mannequin_large_idle_walk.glb",
             showcase / "assets/game.json", entry]
    return {"configuredAgents": count, "agentsPerTeam": scenario["agentsPerTeam"],
            "teams": len(scenario["teams"]), "hud": "bar and text",
            "persistentEffect": effect, "minimap": "visible at startup",
            "animation": "GpuSkinnedInstance with locomotion controller; explicit move orders required",
            "agentTemplates": used, "sourceEvidence": [fingerprint(p) for p in paths]}


def prepare(require_binaries=False):
    graph, mods = configuration()
    prepared = copy.deepcopy(graph)
    for row, mod in zip(prepared["plannedMods"], mods):
        row["rootPath"] = mod["root"]
    graph_path = HERE / SOURCE_GRAPH.name
    runtime = read_json(SOURCE_RUNTIME)
    runtime["LaunchGraphPath"] = graph_path.name
    write_json(graph_path, prepared)
    write_json(HERE / SOURCE_RUNTIME.name, runtime)
    manifest = {"preparedAtUtc": stamp(), "root": str(ROOT),
                "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                "bridgePort": PORT, "appOutput": str(app_output()), "mods": mods,
                "mode": mode_evidence(mods),
                "bootstrap": fingerprint(HERE / SOURCE_RUNTIME.name),
                "graph": fingerprint(graph_path),
                "sourceRuntime": fingerprint(SOURCE_RUNTIME), "sourceGraph": fingerprint(SOURCE_GRAPH)}
    if require_binaries:
        manifest["modBinaries"] = [fingerprint(Path(m["binary"])) for m in mods if m["binary"]]
        manifest["appBinary"] = fingerprint(app_output() / "Ludots.App.Raylib.dll")
        manifest["renderBinary"] = fingerprint(app_output() / "Ludots.Raylib.Render.dll")
        arch_path = ROOT / "src/Libraries/Arch/src/Arch/bin/Release/net8.0/Arch.dll"
        arch = fingerprint(arch_path)
        app_arch = fingerprint(app_output() / "Arch.dll")
        require(arch["sha256"] == app_arch["sha256"], "App Arch.dll differs from net8.0 project output.")
        manifest["archNet8"] = app_arch
        for name in ("skinning_instanced_pose_texture.vs", "shadow_depth_skinning_pose_texture.vs"):
            expected = fingerprint(ROOT / "src/Platforms/Desktop" / name)
            actual = fingerprint(app_output() / name)
            require(expected["sha256"] == actual["sha256"], f"App has a stale shader: {name}")
    write_json(HERE / "prepared-manifest.json", manifest)
    write_json(HERE / "preparations" / (manifest["preparedAtUtc"] + ".json"), manifest)
    return manifest


def build_project(project, label, logs):
    command = ["dotnet", "build", str(project), "-c", "Release", "--nologo", "-m:1", "-v:minimal"]
    print(f"Building {label}", flush=True)
    log = logs / (label + ".log")
    with log.open("w", encoding="utf-8") as output:
        result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
    write_json(logs / (label + ".result.json"), {"command": command, "exitCode": result.returncode,
                                               "log": str(log), "completedAtUtc": stamp()})
    require(result.returncode == 0, f"Build failed ({result.returncode}); inspect {log}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "build-mods", "build-app", "verify", "start"))
    args = parser.parse_args()
    manifest = prepare(require_binaries=args.action in ("verify", "start"))
    if args.action == "build-mods":
        logs = HERE / "builds" / stamp()
        logs.mkdir(parents=True)
        for mod in manifest["mods"]:
            if mod["project"] is None:
                print(f"Validated data-only Mod: {mod['id']}", flush=True)
                continue
            build_project(Path(mod["project"]), mod["id"], logs)
            fingerprint(Path(mod["binary"]))
        write_json(logs / "mod-binaries.json", [fingerprint(Path(m["binary"])) for m in manifest["mods"] if m["binary"]])
        print(f"Five code Mod builds passed; one data-only Mod validated. Evidence: {logs}")
    elif args.action == "build-app":
        logs = HERE / "builds" / stamp()
        logs.mkdir(parents=True)
        build_project(APP_PROJECT, "Ludots.App.Raylib", logs)
        prepare(require_binaries=True)
        print(f"Application build and binary verification passed. Evidence: {logs}")
    elif args.action == "start":
        overrides = {"LUDOTS_AGENT_BRIDGE": "1", "LUDOTS_AGENT_BRIDGE_PORT": str(PORT),
                     "LUDOTS_RAYLIB_DRAW_SHADOWS": "true",
                     "LUDOTS_RAYLIB_SHARED_POSE_UNIFORMS": "1",
                     "LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES": "60",
                     "LUDOTS_RAYLIB_TIMING_SYSTEM_BREAKDOWN": "true"}
        inherited = sorted(k for k in os.environ if k.startswith("LUDOTS_") and k not in overrides)
        require(not inherited, f"Unexpected inherited Ludots overrides; review before launch: {inherited}")
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", PORT))
        run = HERE / "runs" / stamp()
        run.mkdir(parents=True)
        overrides["LUDOTS_RAYLIB_DIAGNOSTIC_PATH"] = str(run / "timing.log")
        command = ["dotnet", "exec", str(app_output() / "Ludots.App.Raylib.dll"),
                   str(HERE / SOURCE_RUNTIME.name)]
        child_env = os.environ.copy()
        child_env.update(overrides)
        with (run / "stdout.log").open("w", encoding="utf-8") as stdout, (run / "stderr.log").open("w", encoding="utf-8") as stderr:
            process = subprocess.Popen(command, cwd=app_output(), env=child_env, stdout=stdout, stderr=stderr,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        write_json(run / "process.json", {"pid": process.pid, "command": command, "cwd": str(app_output()),
                                         "environment": overrides, "bridgePort": PORT,
                                         "startedAtUtc": stamp(), "preparedManifest": manifest})
        print(f"Started PID {process.pid}; verify two advancing health samples at port {PORT}. Logs: {run}")
    else:
        print(f"{args.action} passed: {HERE / 'prepared-manifest.json'}")


if __name__ == "__main__":
    main()
