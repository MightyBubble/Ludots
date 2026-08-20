#!/usr/bin/env python3
"""Generate per-op GraphNodeOp gallery SSOT from vignettes.

Reads:
  mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/{Op}.json
Writes/upserts (do not hand-edit these outputs):
  - gallery maps
  - thin entry mods under mods/showcases/capability_standard/graph_op_entries/
  - launcher.config.json bindings
  - launcher.presets.json raylib presets
  - showcase.registry.json entries + gallery csproj exemption
  - assets/GAS/graph_node_op_coverage.registry.json showcaseId + gallery unitTestRefs
  - family capability_standard_graph_ops_* registry rows, launcher bindings, and presets removed

Subagents own vignettes and FrontDoor graphs only.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))
from graph_op_coverage_index import coverage_refs_for_op, index_gas_tests

PREFIX = "capability_standard_graph_op_"
GALLERY_REL = "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod"
ENTRY_ROOT_REL = "mods/showcases/capability_standard/graph_op_entries"
COVERAGE_REL = "assets/GAS/graph_node_op_coverage.registry.json"
DEFAULT_CAMERA_PROFILE = "Camera.Profile.GraphOpsGallery"
ACCEPTANCE = "GraphOpsNodeGalleryAcceptanceTests"
WIKI_DOCS = "gitbook/reference/graph-node-op-wiki"
FAMILY_PREFIX = "capability_standard_graph_ops_"
FAMILY_SHOWCASE_IDS = (
    "capability_standard_graph_ops_attr",
    "capability_standard_graph_ops_float",
    "capability_standard_graph_ops_script",
    "capability_standard_graph_ops_spatial",
    "capability_standard_graph_ops_query",
    "capability_standard_graph_ops_rel",
    "capability_standard_graph_ops_blackboard",
    "capability_standard_graph_ops_event",
)

MAP_TAGS = [
    "showcase",
    "capability-standard",
    "gas",
    "graph-ops",
    "per-op",
]


def merge_field(vignette_dir: Path, vignette: dict) -> dict:
    field = vignette.get("field")
    if not field:
        return vignette
    path = vignette_dir / "_fields" / f"{field}.json"
    if not path.is_file():
        raise SystemExit(f"Vignette field '{field}' missing: {path}")
    scene = load(path)
    merged = dict(vignette)
    if vignette.get("actors"):
        raise SystemExit(
            f"Vignette {vignette.get('op')} sets field '{field}' and also actors; field owns the scene."
        )
    for key in ("actors", "collections", "links", "camera"):
        if key in scene:
            merged[key] = scene[key]
    return merged


def json_number(value):
    number = float(value)
    return int(number) if number.is_integer() else number


def actor_to_entity(actor: dict) -> dict:
    x_cm = int(round(float(actor.get("x", 0)) * 100))
    y_cm = int(round(float(actor.get("y", 0)) * 100))
    health = actor.get("health", 100)
    entity = {
        "InstanceId": actor["id"],
        "Template": actor["template"],
        "Overrides": {
            "Name": {"Value": actor["name"]},
            "WorldPositionCm": {"Value": {"X": x_cm, "Y": y_cm}},
            "AttributeBuffer": {"base": {"Health": health}},
        },
    }
    team = actor.get("team")
    if team:
        entity["Overrides"]["Team"] = {"Id": team}
    return entity


def load_template_teams(templates_path: Path) -> dict[str, int]:
    data = load(templates_path)
    templates = data if isinstance(data, list) else data.get("templates") or data.get("Templates") or []
    result: dict[str, int] = {}
    for template in templates:
        template_id = template.get("id")
        team = (template.get("components") or {}).get("Team") or {}
        team_id = team.get("Id", team.get("id"))
        if template_id and team_id:
            result[str(template_id)] = int(team_id)
    return result


def collect_team_bindings(actors: list, template_teams: dict[str, int]) -> list[dict]:
    representatives: dict[int, str] = {}
    for actor in actors:
        team = actor.get("team")
        if team is None:
            team = template_teams.get(actor["template"])
        if team is None:
            continue
        team_id = int(team)
        if team_id <= 0:
            continue
        representatives.setdefault(team_id, actor["id"])
    return [
        {"TeamId": team_id, "RepresentativeInstanceId": instance_id}
        for team_id, instance_id in sorted(representatives.items())
    ]


def write_map(
    path: Path,
    map_id: str,
    actors: list,
    camera: dict | None = None,
    template_teams: dict[str, int] | None = None,
    variables: list | None = None,
) -> None:
    if not actors:
        raise SystemExit(f"Map {map_id} has no actors; per-op galleries must spawn people through MapLoader.")
    cam = camera or {}
    payload = {
        "Id": map_id,
        "Tags": MAP_TAGS,
        "DefaultCamera": {
            "VirtualCameraId": cam.get("virtualCameraId", DEFAULT_CAMERA_PROFILE),
            "TargetXCm": int(cam.get("targetXCm", 0)),
            "TargetYCm": int(cam.get("targetYCm", 0)),
            "DistanceCm": int(cam.get("distanceCm", 2600)),
            "Pitch": json_number(cam.get("pitch", 75)),
            "Yaw": json_number(cam.get("yaw", 180)),
            "FovYDeg": json_number(cam.get("fovYDeg", 50)),
        },
        "Entities": [actor_to_entity(actor) for actor in actors],
    }
    teams = collect_team_bindings(actors, template_teams or {})
    if teams:
        payload["Teams"] = teams
    if variables:
        payload["Variables"] = variables
    dump(path, payload)

ENTRY_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>{ns}</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\CapabilityStandardGraphOpsNodeGalleryMod\\CapabilityStandardGraphOpsNodeGalleryMod.csproj" />
  </ItemGroup>
</Project>
"""

ENTRY_CS = """using Ludots.Core.Modding;

namespace {ns};

public sealed class {ns}Entry : IMod
{{
    public void OnLoad(IModContext context) {{ }}
    public void OnUnload() {{ }}
}}
"""

ENTRY_MOD_JSON = """{{
  "name": "{ns}",
  "version": "1.0.0",
  "description": "Launcher entry for GraphNodeOp {op} player gallery.",
  "main": "bin/net8.0/{ns}.dll",
  "priority": 0,
  "dependencies": {{
    "CapabilityStandardGraphOpsNodeGalleryMod": "^1.0.0"
  }},
  "tags": [
    "showcase",
    "capability-standard",
    "gas",
    "graph-ops",
    "per-op"
  ]
}}
"""

ENTRY_GAME_JSON = """{{
  "startupMapId": "{map_id}",
  "windowTitle": "Ludots - {title}",
  "targetFps": 60,
  "windowWidth": 1600,
  "windowHeight": 900,
  "windowResizable": true,
  "presentation": {{
    "cameraCulling": {{
      "highLodDistanceCm": 12000,
      "mediumLodDistanceCm": 36000,
      "lowLodDistanceCm": 72000
    }}
  }}
}}
"""

STAMP = "GENERATED by scripts/generate-graph-op-node-galleries.py. Do not hand-edit.\n"


def windows_extended_path(path: Path) -> Path:
    if sys.platform != "win32":
        return path
    text = str(path)
    if text.startswith("\\\\?\\"):
        return path
    if text.startswith("\\\\"):
        return Path("\\\\?\\UNC\\" + text[2:])
    return Path("\\\\?\\" + text)


def write_text_lf(path: Path, text: str) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as file:
        file.write(text)


def dump(path: Path, data) -> None:
    write_text_lf(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n")


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def upsert_by_key(items: list, key: str, value: str, entry: dict) -> None:
    for i, item in enumerate(items):
        if isinstance(item, dict) and item.get(key) == value:
            items[i] = entry
            return
    items.append(entry)


def is_per_op_showcase_id(sid: str) -> bool:
    return sid.startswith(PREFIX) and not sid.startswith(FAMILY_PREFIX)


def remove_orphan_per_op_artifacts(
    repo: Path,
    live_ops: set[str],
    maps_dir: Path,
    bindings: list,
    preset_list: list,
    showcases: list,
) -> list[str]:
    live_ids = {PREFIX + op for op in live_ops}
    removed: list[str] = []

    for path in list(maps_dir.glob("*.json")):
        sid = path.stem
        if is_per_op_showcase_id(sid) and sid not in live_ids:
            path.unlink()
            removed.append(str(path.relative_to(repo)))

    entry_root = repo / ENTRY_ROOT_REL
    if entry_root.is_dir():
        for folder in list(entry_root.iterdir()):
            name = folder.name
            if not name.startswith("CapabilityStandardGraphOp") or not name.endswith("EntryMod"):
                continue
            op = name[len("CapabilityStandardGraphOp") : -len("EntryMod")]
            if op not in live_ops:
                shutil.rmtree(folder)
                removed.append(str(folder.relative_to(repo)))

    kept_bindings = []
    for binding in bindings:
        name = binding.get("name", "") if isinstance(binding, dict) else ""
        if is_per_op_showcase_id(name) and name not in live_ids:
            removed.append(f"launcher.binding:{name}")
            continue
        kept_bindings.append(binding)
    bindings[:] = kept_bindings

    kept_presets = []
    for preset in preset_list:
        preset_id = preset.get("id", "") if isinstance(preset, dict) else ""
        sid = preset_id[: -len("_raylib")] if preset_id.endswith("_raylib") else ""
        if is_per_op_showcase_id(sid) and sid not in live_ids:
            removed.append(f"launcher.preset:{preset_id}")
            continue
        kept_presets.append(preset)
    preset_list[:] = kept_presets

    kept_showcases = []
    for showcase in showcases:
        sid = showcase.get("id", "") if isinstance(showcase, dict) else ""
        if is_per_op_showcase_id(sid) and sid not in live_ids:
            removed.append(f"showcase:{sid}")
            continue
        kept_showcases.append(showcase)
    showcases[:] = kept_showcases
    return removed


def write_entry_mod(repo: Path, op: str, title: str) -> None:
    ns = f"CapabilityStandardGraphOp{op}EntryMod"
    folder = repo / ENTRY_ROOT_REL / ns
    folder.mkdir(parents=True, exist_ok=True)
    map_id = PREFIX + op
    write_text_lf(folder / "GENERATED.txt", STAMP)
    write_text_lf(folder / f"{ns}.csproj", ENTRY_CSPROJ.format(ns=ns))
    write_text_lf(folder / f"{ns}Entry.cs", ENTRY_CS.format(ns=ns))
    write_text_lf(folder / "mod.json", ENTRY_MOD_JSON.format(ns=ns, op=op))
    assets = folder / "assets"
    assets.mkdir(exist_ok=True)
    write_text_lf(
        assets / "game.json",
        ENTRY_GAME_JSON.format(map_id=map_id, title=title.replace('"', '\\"')),
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument("--strict", action="store_true", help="Fail if any coverage op lacks a vignette.")
    args = parser.parse_args()
    repo = windows_extended_path(Path(args.repo).resolve())
    gallery = repo / GALLERY_REL
    vignette_dir = gallery / "assets" / "Vignettes"
    maps_dir = gallery / "assets" / "Maps"
    maps_dir.mkdir(parents=True, exist_ok=True)

    coverage_path = repo / COVERAGE_REL
    coverage = load(coverage_path)
    ops = [e["op"] for e in coverage["entries"]]
    vignettes = {}
    for path in sorted(vignette_dir.glob("*.json")):
        data = load(path)
        op = data["op"]
        if path.stem != op:
            raise SystemExit(f"Vignette filename {path.name} must match op {op}.")
        vignettes[op] = data

    missing = [op for op in ops if op not in vignettes]
    extra = [op for op in vignettes if op not in ops]
    if extra:
        raise SystemExit(f"Vignettes for unknown ops: {extra}")
    if args.strict and missing:
        raise SystemExit("Missing vignettes:\n" + "\n".join(missing))

    test_index = index_gas_tests(repo, ops)
    coverage["description"] = (
        "GraphNodeOp coverage SSOT. Each executable opcode must have status=covered, "
        "a per-op showcaseId, and unitTestRefs: Class.Method pairs measured to execute that op."
    )
    for entry in coverage["entries"]:
        op = entry["op"]
        entry["showcaseId"] = PREFIX + op
        vignette = vignettes.get(op)
        driver = vignette["driver"] if vignette else None
        if not driver:
            raise SystemExit(f"Coverage op '{op}' has no vignette driver; cannot write gallery unitTestRefs.")
        existing = entry.get("unitTestRefs")
        if existing is None:
            existing = entry.get("unitTestFilter", "")
        entry.pop("unitTestFilter", None)
        entry["unitTestRefs"] = coverage_refs_for_op(op, existing, test_index)
        if entry.get("status") != "covered":
            raise SystemExit(f"Coverage op '{op}' status is {entry.get('status')!r}; generator only emits covered per-op galleries.")
    dump(coverage_path, coverage)

    launcher_path = repo / "launcher.config.json"
    presets_path = repo / "launcher.presets.json"
    registry_path = repo / "showcase.registry.json"
    launcher = load(launcher_path)
    presets = load(presets_path)
    registry = load(registry_path)

    exemptions = registry.setdefault("exemptions", [])
    gallery_exempt = {
        "kind": "csproj",
        "value": GALLERY_REL,
        "reason": "shared per-op GraphNodeOp gallery host; player entries are thin mods under graph_op_entries/",
    }
    if not any(
        isinstance(e, dict) and e.get("kind") == "csproj" and e.get("value") == GALLERY_REL
        for e in exemptions
    ):
        exemptions.append(gallery_exempt)

    showcases = registry.setdefault("showcases", [])
    bindings = launcher.setdefault("bindings", [])
    preset_list = presets.setdefault("presets", [])
    template_teams = load_template_teams(gallery / "assets" / "Entities" / "templates.json")

    for op, vignette in vignettes.items():
        sid = PREFIX + op
        scene = merge_field(vignette_dir, vignette)
        title = scene["title"]
        beat = scene["beat"]
        ns = f"CapabilityStandardGraphOp{op}EntryMod"
        entry_path = f"{ENTRY_ROOT_REL}/{ns}"
        map_path = maps_dir / f"{sid}.json"
        write_map(
            map_path,
            sid,
            scene.get("actors") or [],
            scene.get("camera"),
            template_teams,
            scene.get("variables"),
        )
        write_entry_mod(repo, op, title)

        upsert_by_key(
            bindings,
            "name",
            sid,
            {
                "name": sid,
                "target": {
                    "type": "path",
                    "value": entry_path,
                    "projectPath": f"{ns}.csproj",
                },
            },
        )
        upsert_by_key(
            preset_list,
            "id",
            f"{sid}_raylib",
            {
                "id": f"{sid}_raylib",
                "name": f"Graph Op {op} Raylib",
                "selectors": [f"${sid}"],
                "adapterId": "raylib",
                "buildMode": "auto",
            },
        )
        evidence = f"artifacts/evidence/{sid}"
        upsert_by_key(
            showcases,
            "id",
            sid,
            {
                "id": sid,
                "path": entry_path,
                "projectPath": f"{ns}.csproj",
                "title": title,
                "summary": beat,
                "tier": "T2",
                "category": "capability",
                "tags": [
                    "graph",
                    "capability-standard",
                    "gas",
                    "graph-ops",
                    "per-op",
                    op,
                ],
                "binding": sid,
                "preset": f"{sid}_raylib",
                "docsPath": f"{WIKI_DOCS}/{op}.md",
                "readmePath": None,
                "acceptanceTest": ACCEPTANCE,
                "artifactDir": evidence,
                "screenshot": f"{evidence}/poster.png",
                "video": f"{evidence}/play.mp4",
                "status": "active",
                "notes": "Per-op player gallery. play.mp4 is Git LFS; wiki under graph-node-op-wiki.",
            },
        )

    family_ids = set(FAMILY_SHOWCASE_IDS)
    family_presets = {f"{sid}_raylib" for sid in FAMILY_SHOWCASE_IDS}
    showcases[:] = [showcase for showcase in showcases if showcase.get("id") not in family_ids]
    presets["presets"] = [preset for preset in preset_list if preset.get("id") not in family_presets]
    launcher["bindings"] = [binding for binding in bindings if binding.get("name") not in family_ids]

    orphans = remove_orphan_per_op_artifacts(
        repo,
        set(vignettes),
        maps_dir,
        bindings,
        presets["presets"],
        showcases,
    )

    dump(launcher_path, launcher)
    dump(presets_path, presets)
    dump(registry_path, registry)

    print(
        f"Generated {len(vignettes)} per-op galleries; "
        f"coverage showcaseIds+unitTestRefs updated for {len(ops)} ops; "
        f"removed {len(FAMILY_SHOWCASE_IDS)} family aggregate entries; "
        f"removed {len(orphans)} per-op orphans; "
        f"missing vignettes: {len(missing)}"
    )
    if missing:
        print("Still missing:")
        for op in missing:
            print(f"  {op}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
