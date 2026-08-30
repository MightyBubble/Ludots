#!/usr/bin/env python3
"""Generate per-bag typed collection panel showcases from a catalog.

Host: mods/showcases/panel_collection_bags/PanelCollectionBagsShowcaseMod
Thin entries: .../entries/Panel{Kind}EntryMod/  (no Ludots.Core ProjectReference)

Writes/upserts:
  - host maps + open trigger graphs fragment
  - thin entry mods
  - launcher.config.json bindings + launcher.presets.json
  - showcase.registry.json entries + host csproj exemption
  - removes mega panel_collection_bags player entry
"""
from __future__ import annotations

import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
HOST_REL = "mods/showcases/panel_collection_bags/PanelCollectionBagsShowcaseMod"
ENTRY_ROOT_REL = "mods/showcases/panel_collection_bags/entries"
PREFIX = "panel_"

# One panel / one collection bag per showcase (no mega wall).
CATALOG = [
    {
        "slug": "effect_templates",
        "title": "效果图鉴",
        "summary": "翻开墙上的效果说明书：只有模板名，没有剩余时间。",
        "panelType": "panel.collection.effects",
        "collection": "templates",
        "assertNames": ["祝福", "迅捷", "护盾"],
        "wiki": "panel-effect-templates.md",
        "beat": "效果模板袋",
    },
    {
        "slug": "roster_nested",
        "title": "编队档案",
        "summary": "队员名下嵌着各自的技能格，不是全场技能大杂烩。",
        "panelType": "panel.collection.roster",
        "collection": "units",
        "assertNames": ["名册守望者", "名册学徒"],
        "wiki": "panel-roster-nested.md",
        "beat": "嵌套技能槽",
    },
    {
        "slug": "present_tags",
        "title": "身上的印记",
        "summary": "守望者身上现有的印记被点名列出。",
        "panelType": "panel.collection.tags",
        "collection": "tags",
        "assertNames": ["勇气印记", "洞察印记", "守望印记"],
        "wiki": "panel-present-tags.md",
        "beat": "标签袋",
    },
    {
        "slug": "inventory_aggregate",
        "title": "背包堆叠",
        "summary": "同类药剂只露出一枚图标和总数。",
        "panelType": "panel.collection.inventory",
        "collection": "items",
        "assertNames": ["试炼药剂"],
        "assertTotal": 3,
        "wiki": "panel-inventory-aggregate.md",
        "beat": "聚合展示",
    },
    {
        "slug": "item_definitions",
        "title": "物品图鉴",
        "summary": "已登记物品定义排成名册，没有堆叠实例。",
        "panelType": "panel.collection.itemDefinitions",
        "collection": "definitions",
        "assertNames": ["试炼药剂", "干粮"],
        "wiki": "panel-item-definitions.md",
        "beat": "物品定义袋",
    },
    {
        "slug": "active_tasks",
        "title": "进行中的差事",
        "summary": "挂在守望者名下的差事被点名。",
        "panelType": "panel.collection.tasks",
        "collection": "tasks",
        "assertNames": ["巡夜差事"],
        "wiki": "panel-active-tasks.md",
        "beat": "任务实例袋",
    },
    {
        "slug": "active_activities",
        "title": "进行中的活动",
        "summary": "挂在守望者名下的活动被点名。",
        "panelType": "panel.collection.activities",
        "collection": "activities",
        "assertNames": ["名册集会"],
        "wiki": "panel-active-activities.md",
        "beat": "活动实例袋",
    },
    {
        "slug": "ability_holders",
        "title": "谁会火球",
        "summary": "技能格旁挂着会这招的人（source=input 反查）。",
        "panelType": "panel.collection.holders",
        "collection": "slots",
        "assertNestedNames": ["名册守望者", "名册学徒"],
        "wiki": "panel-ability-holders.md",
        "beat": "反查持有者",
    },
    {
        "slug": "progression_nodes",
        "title": "修行进度",
        "summary": "守望者身上的进度节点排成一行。",
        "panelType": "panel.collection.progression",
        "collection": "progress",
        "assertNames": ["名册修行"],
        "wiki": "panel-progression-nodes.md",
        "beat": "进度节点袋",
    },
]


def dump(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def showcase_id(slug: str) -> str:
    return f"panel_{slug}"


def map_id(slug: str) -> str:
    return f"collection_bags_{slug}"


def open_graph_id(slug: str) -> str:
    return f"Graph.CollectionBags.Open.{slug}"


def entry_mod_name(slug: str) -> str:
    parts = "".join(p.title() for p in slug.split("_"))
    return f"Panel{parts}EntryMod"


def write_open_graphs(host: Path) -> None:
    graphs_path = host / "assets" / "GAS" / "graphs.json"
    graphs = load(graphs_path)
    # Drop mega open + keep query graphs; rewrite opens.
    kept = [g for g in graphs if not str(g.get("id", "")).startswith("Graph.CollectionBags.Open")
            and g.get("id") != "Graph.CollectionBags.Panels.Open"]
    for item in CATALOG:
        gid = open_graph_id(item["slug"])
        panel = item["panelType"]
        nodes = [{"id": "scope", "op": "LoadExplicitTarget"}]
        control_edges = []
        value_edges = []
        previous = "scope"
        if item["slug"] == "active_tasks":
            nodes.append({
                "id": "offer",
                "op": "OfferTask",
                "taskId": "Task.CollectionBags.NightWatch",
            })
            control_edges.append({"from": "scope", "fromPort": "next", "to": "offer"})
            value_edges.append({"from": "scope", "fromPort": "value", "to": "offer", "toPort": "source"})
            previous = "offer"
        elif item["slug"] == "active_activities":
            nodes.append({
                "id": "offer",
                "op": "OfferActivity",
                "activityId": "Activity.CollectionBags.Gathering",
            })
            control_edges.append({"from": "scope", "fromPort": "next", "to": "offer"})
            value_edges.append({"from": "scope", "fromPort": "value", "to": "offer", "toPort": "source"})
            previous = "offer"

        nodes.extend([
            {"id": "create", "op": "CreatePanel", "panelType": panel, "panelAnchor": "screen.topLeft"},
            {"id": "show", "op": "ShowPanel", "panelType": panel},
            {"id": "ok", "op": "ConstInt", "intValue": 1},
            {"id": "halt", "op": "HaltReturnInt"},
        ])
        control_edges.extend([
            {"from": previous, "fromPort": "next", "to": "create"},
            {"from": "create", "fromPort": "next", "to": "show"},
            {"from": "show", "fromPort": "next", "to": "ok"},
            {"from": "ok", "fromPort": "next", "to": "halt"},
        ])
        value_edges.extend([
            {"from": "scope", "fromPort": "value", "to": "create", "toPort": "source"},
            {"from": "ok", "fromPort": "value", "to": "halt", "toPort": "value"},
        ])
        kept.append({
            "id": gid,
            "kind": "TriggerGraph",
            "entries": [
                {"label": "on_map_loaded", "event": "MapLoaded", "start": "scope", "once": True}
            ],
            "nodes": nodes,
            "controlEdges": control_edges,
            "valueEdges": value_edges,
        })
    dump(graphs_path, kept)


def write_maps(host: Path) -> None:
    maps_dir = host / "assets" / "Maps"
    # Remove mega arena.
    mega = maps_dir / "collection_bags_arena.json"
    if mega.exists():
        mega.unlink()
    for item in CATALOG:
        mid = map_id(item["slug"])
        entities = [
            {"InstanceId": "collection-bags-hero", "Template": "collection_bags_hero"},
            {"InstanceId": "collection-bags-apprentice", "Template": "collection_bags_apprentice"},
        ]
        if item["slug"] == "inventory_aggregate":
            entities.extend([
                {"InstanceId": f"collection-bags-potion-{index}", "Template": "collection_bags_potion"}
                for index in range(1, 4)
            ])
        dump(maps_dir / f"{mid}.json", {
            "Id": mid,
            "Tags": ["showcase", "panel", "typed-collection-bag", item["slug"]],
            "TriggerGraphs": [
                {"graph": open_graph_id(item["slug"]), "scopeInstanceId": "collection-bags-hero"}
            ],
            "Entities": entities,
            "Teams": [
                {"TeamId": 1, "RepresentativeInstanceId": "collection-bags-hero"}
            ],
            "Players": [
                {"PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "collection-bags-hero"}
            ],
            "ParticipantRelationships": {
                "Teams": [
                    {"TeamA": 1, "TeamB": 1, "TypeId": "LudotsCore.Participant",
                     "Attitude": "Friendly", "Symmetric": True}
                ],
                "Players": [],
                "PlayerTeams": [],
            },
            "DefaultCamera": {
                "TargetXCm": 110, "TargetYCm": 0, "Yaw": 45, "Pitch": 35,
                "DistanceCm": 1100, "FovYDeg": 50
            },
        })


def write_host_game_json(host: Path) -> None:
    # Host is not a player entry; keep a default for accidental launches.
    dump(host / "assets" / "game.json", {
        "startupMapId": map_id(CATALOG[0]["slug"]),
        "windowTitle": "Ludots - Typed Collection Gallery Host",
        "windowWidth": 1600,
        "windowHeight": 900,
    })
    mod = load(host / "mod.json")
    mod["description"] = (
        "Shared host for per-bag typed collection panel showcases; "
        "player entries live under entries/."
    )
    mod.pop("main", None)
    dump(host / "mod.json", mod)


def write_single_purpose_panels(host: Path) -> None:
    """Ensure inventory/tags/tasks/activities/progression are single-collection panels."""
    panels_path = host / "assets" / "Panels" / "panel_templates.json"
    panels = load(panels_path)
    by_id = {p["id"]: p for p in panels}

    def chip(subject: str, cls: str) -> dict:
        return {
            "id": f"panel.collection.{cls}.chip",
            "subject": subject,
            "graph": "Graph.CollectionBags.Chip",
            "pins": [{"name": "ready", "key": "panel.collection.chip.ready", "mode": "realtime", "default": 1}],
            "layout": {"controls": [{"type": "label", "class": f"collection-{cls}", "bind": "displayName"}]},
        }

    # Replace combined panels with single-purpose ones.
    replacements = {
        "panel.collection.tags": {
            "id": "panel.collection.tags",
            "graph": "Graph.CollectionBags.PresentTags",
            "pins": [{"name": "ready", "key": "panel.collection.tags.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "tags", "source": "selfGraph",
                "collectionKey": "panel.collection.presentTags",
                "template": "panel.collection.tag.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "身上的印记"},
                {"type": "list", "class": "collection-present-tags", "bind": "tags",
                 "viewportHeight": 220, "itemExtent": 40},
            ]},
        },
        "panel.collection.inventory": {
            "id": "panel.collection.inventory",
            "graph": "Graph.CollectionBags.Inventory",
            "pins": [{"name": "ready", "key": "panel.collection.inventory.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "items", "source": "selfGraph",
                "collectionKey": "panel.collection.inventoryItems",
                "template": "panel.collection.item.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "背包堆叠"},
                {"type": "list", "class": "collection-inventory-items", "bind": "items",
                 "present": "aggregate", "viewportHeight": 80, "itemExtent": 48},
            ]},
        },
        "panel.collection.itemDefinitions": {
            "id": "panel.collection.itemDefinitions",
            "graph": "Graph.CollectionBags.ItemDefinitions",
            "pins": [{"name": "ready", "key": "panel.collection.itemDefinitions.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "definitions", "source": "selfGraph",
                "collectionKey": "panel.collection.itemDefinitions",
                "template": "panel.collection.itemDefinition.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "物品图鉴"},
                {"type": "list", "class": "collection-item-definitions", "bind": "definitions",
                 "viewportHeight": 220, "itemExtent": 40},
            ]},
        },
        "panel.collection.tasks": {
            "id": "panel.collection.tasks",
            "graph": "Graph.CollectionBags.ActiveTasks",
            "pins": [{"name": "ready", "key": "panel.collection.tasks.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "tasks", "source": "selfGraph",
                "collectionKey": "panel.collection.activeTasks",
                "template": "panel.collection.task.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "进行中的差事"},
                {"type": "list", "class": "collection-active-tasks", "bind": "tasks",
                 "viewportHeight": 220, "itemExtent": 40},
            ]},
        },
        "panel.collection.activities": {
            "id": "panel.collection.activities",
            "graph": "Graph.CollectionBags.ActiveActivities",
            "pins": [{"name": "ready", "key": "panel.collection.activities.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "activities", "source": "selfGraph",
                "collectionKey": "panel.collection.activeActivities",
                "template": "panel.collection.activity.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "进行中的活动"},
                {"type": "list", "class": "collection-active-activities", "bind": "activities",
                 "viewportHeight": 220, "itemExtent": 40},
            ]},
        },
        "panel.collection.progression": {
            "id": "panel.collection.progression",
            "graph": "Graph.CollectionBags.ProgressionNodes",
            "pins": [{"name": "ready", "key": "panel.collection.progression.ready", "mode": "realtime", "default": 1}],
            "collections": [{
                "name": "progress", "source": "selfGraph",
                "collectionKey": "panel.collection.progressionNodes",
                "template": "panel.collection.progression.chip",
            }],
            "layout": {"controls": [
                {"type": "label", "class": "collection-panel-title", "text": "修行进度"},
                {"type": "list", "class": "collection-progression-nodes", "bind": "progress",
                 "viewportHeight": 220, "itemExtent": 40},
            ]},
        },
    }

    # Drop obsolete combined panels.
    drop = {"panel.collection.supply", "panel.collection.questboard", "panel.collection.tags"}
    # Keep tags but replace; supply/questboard drop
    out = []
    seen = set()
    for p in panels:
        pid = p["id"]
        if pid in {"panel.collection.supply", "panel.collection.questboard"}:
            continue
        if pid in replacements:
            out.append(replacements[pid])
            seen.add(pid)
            continue
        out.append(p)
        seen.add(pid)
    for pid, body in replacements.items():
        if pid not in seen:
            out.append(body)
    dump(panels_path, out)


def write_query_graphs_split(host: Path) -> None:
    graphs_path = host / "assets" / "GAS" / "graphs.json"
    graphs = load(graphs_path)
    # Remove combined TagsAndActivities / Supply / QuestBoard; add singles.
    drop_ids = {
        "Graph.CollectionBags.TagsAndActivities",
        "Graph.CollectionBags.Supply",
        "Graph.CollectionBags.QuestBoard",
        "Graph.CollectionBags.PresentTags",
    }
    kept = [g for g in graphs if g.get("id") not in drop_ids]

    def query_entity_collect(gid: str, op: str, dest: str, key: str, title: str, summary: str) -> dict:
        return {
            "id": gid, "kind": "Query", "entry": "caster",
            "nodes": [
                {"id": "caster", "op": "LoadCaster"},
                {"id": "collect", "op": op},
            ],
            "controlEdges": [{"from": "caster", "fromPort": "next", "to": "collect"}],
            "valueEdges": [{"from": "caster", "fromPort": "value", "to": "collect", "toPort": "source"}],
            "outputs": [{
                "id": "collect", "destination": dest, "type": "TargetList",
                "collectionKey": key, "role": "Display", "title": title, "summary": summary,
            }],
        }

    def query_int_collect(gid: str, op: str, dest: str, key: str, title: str, summary: str, needs_caster: bool) -> dict:
        if not needs_caster:
            return {
                "id": gid, "kind": "Query", "entry": "collect",
                "nodes": [{"id": "collect", "op": op}],
                "controlEdges": [], "valueEdges": [],
                "outputs": [{
                    "id": "collect", "destination": dest, "type": "IntIdList",
                    "collectionKey": key, "role": "Display", "title": title, "summary": summary,
                }],
            }
        return {
            "id": gid, "kind": "Query", "entry": "caster",
            "nodes": [
                {"id": "caster", "op": "LoadCaster"},
                {"id": "collect", "op": op},
            ],
            "controlEdges": [{"from": "caster", "fromPort": "next", "to": "collect"}],
            "valueEdges": [{"from": "caster", "fromPort": "value", "to": "collect", "toPort": "source"}],
            "outputs": [{
                "id": "collect", "destination": dest, "type": "IntIdList",
                "collectionKey": key, "role": "Display", "title": title, "summary": summary,
            }],
        }

    extras = [
        query_int_collect("Graph.CollectionBags.PresentTags", "QueryCollectPresentTags",
                          "TagIdCollection", "panel.collection.presentTags", "身上的印记", "当前标签", True),
        query_entity_collect("Graph.CollectionBags.Inventory", "QueryCollectInventoryItems",
                             "ItemInstanceCollection", "panel.collection.inventoryItems", "背包", "物品实例"),
        query_int_collect("Graph.CollectionBags.ItemDefinitions", "QueryCollectItemDefinitions",
                          "ItemDefinitionCollection", "panel.collection.itemDefinitions", "物品图鉴", "物品定义", False),
        query_entity_collect("Graph.CollectionBags.ActiveTasks", "QueryCollectActiveTasks",
                             "TaskInstanceCollection", "panel.collection.activeTasks", "进行中的差事", "任务实例"),
        query_entity_collect("Graph.CollectionBags.ActiveActivities", "QueryCollectActiveActivities",
                             "ActivityInstanceCollection", "panel.collection.activeActivities", "进行中的活动", "活动实例"),
        query_int_collect("Graph.CollectionBags.ProgressionNodes", "QueryCollectProgressionNodes",
                          "ProgressionNodeCollection", "panel.collection.progressionNodes", "修行进度", "进度节点", True),
    ]
    # Avoid dupes if script re-run
    existing = {g["id"] for g in kept}
    for g in extras:
        if g["id"] in existing:
            kept = [x for x in kept if x["id"] != g["id"]]
        kept.append(g)
    dump(graphs_path, kept)


def write_entry(slug: str, title: str) -> None:
    name = entry_mod_name(slug)
    root = REPO / ENTRY_ROOT_REL / name
    root.mkdir(parents=True, exist_ok=True)
    (root / "GENERATED.txt").write_text(
        "GENERATED by scripts/generate-panel-typed-collection-galleries.py. Do not hand-edit.\n",
        encoding="utf-8",
    )
    dump(root / "mod.json", {
        "name": name,
        "version": "1.0.0",
        "description": f"Launcher entry for typed collection panel showcase: {title}",
        "priority": 0,
        "dependencies": {"PanelCollectionBagsShowcaseMod": "^1.0.0"},
        "tags": ["showcase", "panel", "typed-collection", slug],
    })
    dump(root / "assets" / "game.json", {
        "startupMapId": map_id(slug),
        "windowTitle": f"Ludots - {title}",
        "windowWidth": 1600,
        "windowHeight": 900,
    })
    (root / f"{name}.csproj").unlink(missing_ok=True)
    (root / f"{name}Entry.cs").unlink(missing_ok=True)


def upsert_launcher() -> None:
    config = load(REPO / "launcher.config.json")
    presets = load(REPO / "launcher.presets.json")
    bindings = config.setdefault("bindings", [])
    preset_list = presets.setdefault("presets", [])

    # Remove mega binding/preset
    config["bindings"] = [b for b in bindings if b.get("name") != "panel_collection_bags"]
    presets["presets"] = [p for p in preset_list if p.get("id") != "panel_collection_bags_raylib"]

    for item in CATALOG:
        sid = showcase_id(item["slug"])
        name = entry_mod_name(item["slug"])
        path = f"{ENTRY_ROOT_REL}/{name}"
        # binding
        config["bindings"] = [b for b in config["bindings"] if b.get("name") != sid]
        config["bindings"].append({
            "name": sid,
            "target": {
                "type": "path",
                "value": path,
            },
        })
        pid = f"{sid}_raylib"
        presets["presets"] = [p for p in presets["presets"] if p.get("id") != pid]
        presets["presets"].append({
            "id": pid,
            "name": f"Panel {item['title']} Raylib",
            "selectors": [f"${sid}"],
            "adapterId": "raylib",
            "buildMode": "auto",
        })
    dump(REPO / "launcher.config.json", config)
    dump(REPO / "launcher.presets.json", presets)


def upsert_registry() -> None:
    reg = load(REPO / "showcase.registry.json")
    exemptions = reg.setdefault("pathExemptions", reg.get("exemptions", []))
    # schema may use different key — inspect
    # Looking at earlier grep: top-level list under something
    # From showcase.registry read - it had path exemptions at start
    if "exemptions" in reg and isinstance(reg["exemptions"], list):
        ex_key = "exemptions"
    elif "pathExemptions" in reg:
        ex_key = "pathExemptions"
    else:
        # find from known structure
        ex_key = None
        for k, v in reg.items():
            if isinstance(v, list) and v and isinstance(v[0], dict) and v[0].get("kind") == "csproj":
                ex_key = k
                break
        if ex_key is None:
            reg["exemptions"] = []
            ex_key = "exemptions"

    ex_list = reg[ex_key]
    host_path = HOST_REL
    ex_list = [e for e in ex_list if e.get("value") != host_path]
    reg[ex_key] = ex_list

    shows = reg.setdefault("showcases", [])
    shows = [s for s in shows if s.get("id") != "panel_collection_bags"]
    for item in CATALOG:
        sid = showcase_id(item["slug"])
        name = entry_mod_name(item["slug"])
        shows = [s for s in shows if s.get("id") != sid]
        shows.append({
            "id": sid,
            "path": f"{ENTRY_ROOT_REL}/{name}",
            "projectPath": None,
            "title": item["title"],
            "summary": item["summary"],
            "tier": "T2",
            "category": "panel",
            "binding": sid,
            "preset": f"{sid}_raylib",
            "docsPath": f"gitbook/architecture/panel-cases/{item['wiki']}",
            "readmePath": None,
            "acceptanceTest": "Ludots.Tests.GAS.Production.PanelTypedCollectionShowcaseAcceptanceTests",
            "artifactDir": f"artifacts/acceptance/{sid}",
            "screenshot": f"artifacts/acceptance/{sid}/screens/001_{item['slug']}.png",
            "status": "active",
            "tags": ["panel", "ui", "typed-collection", "showcase", "g12", item["slug"]],
        })
    reg["showcases"] = shows
    dump(REPO / "showcase.registry.json", reg)


def write_catalog_json(host: Path) -> None:
    dump(host / "assets" / "typed_collection_catalog.json", CATALOG)


def main() -> None:
    host = REPO / HOST_REL
    write_single_purpose_panels(host)
    write_query_graphs_split(host)
    write_open_graphs(host)
    write_maps(host)
    write_host_game_json(host)
    write_catalog_json(host)
    for item in CATALOG:
        write_entry(item["slug"], item["title"])
    upsert_launcher()
    upsert_registry()
    print(f"Generated {len(CATALOG)} per-bag panel showcases under {ENTRY_ROOT_REL}")


if __name__ == "__main__":
    main()
