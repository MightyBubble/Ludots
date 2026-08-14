#!/usr/bin/env python3
"""Generate C&C tri-nation mod content: 102 unit templates, GAS, presenters."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "mods/showcases/cnc_tri_nation/CncTriNationFullGameMod/assets"

NATIONS = [
    {
        "key": "allied",
        "team_id": 1,
        "display": "Allied",
        "color": "#59A7FF",
        "prefix": "cnc_allied",
        "infantry": [
            "GI", "Engineer", "Medic", "Rocketeer", "Sniper", "Spy", "Seal", "Chrono Legionnaire"
        ],
        "vehicles": [
            "Grizzly Tank", "IFV", "Mirage Tank", "Prism Tank", "Battle Fortress",
            "Destroyer", "Aircraft Carrier", "Chrono Tank"
        ],
        "aircraft": ["Harrier", "Black Eagle", "Nighthawk", "Chrono Copter"],
        "special": ["Tanya", "Weather Controller", "Grand Cannon"],
    },
    {
        "key": "soviet",
        "team_id": 2,
        "display": "Soviet",
        "color": "#F06449",
        "prefix": "cnc_soviet",
        "infantry": [
            "Conscript", "Flak Trooper", "Tesla Trooper", "Engineer", "Crazy Ivan",
            "Desolator", "Boris", "Terrorist"
        ],
        "vehicles": [
            "Rhino Tank", "Flak Track", "V3 Launcher", "Apocalypse Tank", "Tesla Tank",
            "Demolisher", "Dreadnought", "Mammoth Tank"
        ],
        "aircraft": ["MiG", "Kirov", "Siege Chopper", "Vortex"],
        "special": ["Iron Curtain", "Nuclear Silo", "Tesla Coil"],
    },
    {
        "key": "yuri",
        "team_id": 3,
        "display": "Yuri",
        "color": "#B692FF",
        "prefix": "cnc_yuri",
        "infantry": [
            "Initiate", "Brute", "Virus", "Yuri Clone", "Yuri Prime", "Genetic Mutator",
            "Slave Miner", "Guardian GI"
        ],
        "vehicles": [
            "Gattling Tank", "Magnetron", "Mastermind", "Floating Disk", "Lasher Tank",
            "Virus Tank", "Chaos Tank", "Battle Fortress Yuri"
        ],
        "aircraft": ["Floating Disk Scout", "Harrier Yuri", "Kirov Yuri", "Vortex Yuri"],
        "special": ["Psychic Dominator", "Cloning Vats", "Genetic Mutator Device"],
    },
]

STRUCTURES = [
    ("conyard", "Construction Yard", 1600, "structure", 6000, 120),
    ("power", "Power Plant", 900, "structure", 800, 160),
    ("refinery", "Ore Refinery", 1200, "structure", 2000, 0),
    ("barracks", "Barracks", 1000, "structure", 500, 0),
    ("war_factory", "War Factory", 1400, "structure", 2800, 0),
    ("airfield", "Airfield", 1100, "structure", 1800, 0),
    ("battle_lab", "Battle Lab", 1300, "structure", 3200, 0),
    ("pillbox", "Pillbox", 600, "defense", 600, 0),
    ("super_weapon", "Super Weapon", 2000, "super", 5000, 200),
    ("service_depot", "Service Depot", 900, "structure", 1200, 0),
]

WORKER = ("harvester", "Ore Harvester", 500, "worker", 1400, 0)


def slug(name: str) -> str:
    return name.lower().replace(" ", "_").replace("-", "_")


def template_id(nation_prefix: str, category: str, name: str) -> str:
    return f"{nation_prefix}_{category}_{slug(name)}"


def ability_id(kind: str, nation_key: str, name: str) -> str:
    return f"Ability.Cnc.{nation_key.title()}.{kind}.{slug(name)}"


def effect_id(kind: str, nation_key: str, name: str) -> str:
    return f"Effect.Cnc.{nation_key.title()}.{kind}.{slug(name)}"


def base_components(
    display_name: str,
    kind: str,
    health: float,
    credits: float = 0,
    power: float = 0,
    ore: float = 0,
    selectable: bool = True,
) -> dict[str, Any]:
    half = 80 if kind in ("infantry", "worker") else 120 if kind == "aircraft" else 160
    height = 60 if kind == "infantry" else 90 if kind in ("worker", "vehicle") else 140 if kind == "aircraft" else 170
    comps: dict[str, Any] = {
        "Name": {"Value": display_name},
        "Team": {"Id": 1},
        "PlayerOwner": {"PlayerId": 1},
        "WorldPositionCm": {"Value": {"X": 0, "Y": 0}},
        "FacingDirection": {"AngleRad": 0.0},
        "AttributeBuffer": {
            "base": {"Health": health, "Credits": credits, "Power": power, "Ore": ore},
            "current": {"Health": health, "Credits": credits, "Power": power, "Ore": ore},
        },
        "GameplayTagContainer": {},
        "TagCountContainer": {},
        "TimedTagBuffer": {},
        "OrderBuffer": {},
        "BlackboardSpatialBuffer": {},
        "BlackboardEntityBuffer": {},
        "BlackboardIntBuffer": {},
    }
    if selectable:
        comps["CommandSourceSelectableTag"] = {}
        comps["CommandSourceSelectableState"] = {"IsEnabled": True}
    if kind in ("structure", "defense", "super"):
        comps["PresentationStaticTransform"] = {}
        comps["SpatialBounds"] = {
            "kind": "Box3D",
            "localCenterXCm": 0,
            "localCenterYCm": height,
            "localCenterZCm": 0,
        }
        comps["SpatialBox3D"] = {
            "halfSizeXCm": half + 40,
            "halfSizeYCm": height,
            "halfSizeZCm": half + 40,
        }
    else:
        comps["SpatialBounds"] = {
            "kind": "Box3D",
            "localCenterXCm": 0,
            "localCenterYCm": height // 2,
            "localCenterZCm": 0,
        }
        comps["SpatialBox3D"] = {
            "halfSizeXCm": half,
            "halfSizeYCm": height // 2,
            "halfSizeZCm": half,
        }
    return comps


def train_ability(nation: dict, unit_name: str, tpl: str, cost: float, ticks: int) -> tuple[dict, dict, dict]:
    nk = nation["key"]
    aid = ability_id("Train", nk, unit_name)
    cost_effect = effect_id("CostTrainStep", nk, unit_name)
    spawn_effect = effect_id("Train", nk, unit_name)
    step_count = max(ticks // 12, 1)
    step_cost = -cost / step_count
    exec_items: list[dict] = [
        {"kind": "TagClip", "tick": 0, "duration": ticks, "tag": "Status.Cnc.Training"},
    ]
    for i in range(step_count):
        exec_items.append({"kind": "EffectSignal", "tick": i * (ticks // step_count), "template": cost_effect})
    exec_items.append({"kind": "EffectSignal", "tick": ticks, "template": spawn_effect, "dispatchTarget": "Source"})
    exec_items.append({"kind": "End", "tick": ticks})
    ability = {
        "id": aid,
        "exec": {"clockId": "FixedFrame", "items": exec_items},
        "input": {"castModeOverride": "TargetFirst"},
        "blockTags": {"blockedAny": ["State.Cnc.Constructing"]},
        "presentation": {
            "displayName": f"Train {unit_name}",
            "iconGlyph": unit_name[:3].upper(),
            "accentColor": nation["color"],
            "hintText": f"Train {unit_name} via formal GAS queue.",
        },
    }
    effects = [
        {
            "id": cost_effect,
            "tags": ["Effect.Cnc.Cost"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "Credits", "op": "Add", "value": step_cost}],
        },
        {
            "id": spawn_effect,
            "tags": ["Effect.Cnc.Train"],
            "presetType": "CreateUnit",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "unitCreation": {
                "templateId": tpl,
                "placementPattern": "Scatter",
                "count": 1,
                "offsetRadius": 340,
                "copySourcePlayerOwner": True,
            },
        },
    ]
    return ability, effects, {"ability": aid, "name": unit_name}


def build_ability(nation: dict, struct_name: str, tpl: str, cost: float, ready_tag: str, ticks: int) -> tuple[dict, dict, dict, dict]:
    nk = nation["key"]
    build_aid = ability_id("Build", nk, struct_name)
    place_aid = ability_id("Place", nk, struct_name)
    cost_effect = effect_id("CostBuildStep", nk, struct_name)
    place_effect = effect_id("Place", nk, struct_name)
    step_count = max(ticks // 15, 1)
    step_cost = -cost / step_count
    exec_items: list[dict] = [
        {"kind": "TagClip", "tick": 0, "duration": ticks, "tag": f"Status.Cnc.Building.{slug(struct_name)}"},
    ]
    for i in range(step_count):
        exec_items.append({"kind": "EffectSignal", "tick": i * (ticks // step_count), "template": cost_effect})
    exec_items.append({"kind": "TagSignal", "tick": ticks, "tag": ready_tag})
    exec_items.append({"kind": "End", "tick": ticks})
    build = {
        "id": build_aid,
        "exec": {"clockId": "FixedFrame", "items": exec_items},
        "input": {"castModeOverride": "TargetFirst"},
        "blockTags": {"blockedAny": ["State.Cnc.Constructing"]},
        "presentation": {
            "displayName": f"Build {struct_name}",
            "iconGlyph": struct_name[:2].upper(),
            "accentColor": nation["color"],
            "hintText": f"Queue {struct_name} construction.",
        },
    }
    place = {
        "id": place_aid,
        "exec": {
            "clockId": "FixedFrame",
            "items": [
                {"kind": "TagSignal", "tick": 0, "tag": ready_tag, "payloadA": 1},
                {"kind": "EffectSignal", "tick": 0, "template": place_effect},
                {"kind": "End", "tick": 0},
            ],
        },
        "blockTags": {"requiredAll": [ready_tag]},
        "presentation": {
            "displayName": f"Place {struct_name}",
            "iconGlyph": struct_name[:2].upper(),
            "accentColor": nation["color"],
            "hintText": f"Place completed {struct_name}.",
        },
    }
    effects = [
        {
            "id": cost_effect,
            "tags": ["Effect.Cnc.Cost"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "Credits", "op": "Add", "value": step_cost}],
        },
        {
            "id": place_effect,
            "tags": ["Effect.Cnc.Build"],
            "presetType": "CreateUnit",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "unitCreation": {
                "templateId": tpl,
                "placementPattern": "Scatter",
                "count": 1,
                "onSpawnEffect": "Effect.Cnc.Construction",
                "copySourcePlayerOwner": True,
            },
        },
    ]
    return build, place, effects, {"build": build_aid, "place": place_aid, "ready_tag": ready_tag}


def hold_ability() -> dict:
    return {
        "id": "Ability.Cnc.Shared.Hold",
        "exec": {"clockId": "FixedFrame", "items": [{"kind": "End", "tick": 0}]},
        "input": {"castModeOverride": "TargetFirst"},
        "presentation": {
            "displayName": "Hold",
            "iconGlyph": "H",
            "accentColor": "#6B7280",
            "hintText": "Empty command slot.",
        },
    }


def presenter_visual(tpl_id: str, kind: str, color_hex: str) -> list[dict]:
    """Minimal primitive presenter for visibility."""
    scale = [1.2, 1.2, 1.2]
    if kind == "structure":
        scale = [3.5, 2.8, 3.5]
    elif kind == "vehicle":
        scale = [2.0, 1.2, 2.8]
    elif kind == "infantry":
        scale = [0.8, 1.4, 0.8]
    elif kind == "aircraft":
        scale = [1.8, 0.6, 2.4]
    elif kind == "worker":
        scale = [1.6, 1.0, 2.2]
    mesh = "sphere" if kind in ("infantry", "aircraft") else "cube"
    def_id = f"cnc.visual.{tpl_id}"
    child_id = f"cnc.visual.{tpl_id}.body"
    return [
        {
            "id": def_id,
            "children": [{"definitionId": child_id, "scopeTag": "body"}],
            "rules": [
                {
                    "event": {"kind": "EntitySpawned", "key": tpl_id},
                    "command": {"kind": "CreatePresenter", "scopeSource": "EventPayloadA", "definitionId": def_id},
                    "condition": {"inline": "SourceHasVisualTransform"},
                },
                {
                    "event": {"kind": "EntityDestroyed", "key": tpl_id},
                    "command": {"kind": "DestroyPresenterScope", "scopeSource": "EventPayloadA"},
                },
            ],
        },
        {
            "id": child_id,
            "behaviors": [
                {
                    "slot": "AssetBinding",
                    "assetBinding": {
                        "assetKind": "Mesh",
                        "assetId": mesh,
                        "materialId": "default_surface",
                        "renderPath": "StaticMesh",
                        "mobility": "Movable" if kind not in ("structure", "defense", "super") else "Static",
                        "localScale": scale,
                        "tintColor": color_hex,
                    },
                }
            ],
        },
    ]


def generate() -> None:
    templates: list[dict] = []
    abilities: list[dict] = [hold_ability()]
    effects: list[dict] = [
        {
            "id": "Effect.Cnc.Construction",
            "tags": ["Effect.Cnc.Construction"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "State.Cnc.Constructing", "formula": "Fixed", "amount": 1}],
        }
    ]
    presenters: list[dict] = []
    form_sets: list[dict] = []
    roster: list[dict] = []
    unit_count = 0

    # anchors
    for nation in NATIONS:
        templates.append({
            "id": f"{nation['prefix']}_team_anchor",
            "components": {
                "Team": {"Id": nation["team_id"]},
                "WorldPositionCm": {"Value": {"X": 0, "Y": 0}},
                "AttributeBuffer": {
                    "base": {"Credits": 0, "Power": 0, "Ore": 0},
                    "current": {"Credits": 0, "Power": 0, "Ore": 0},
                },
            },
        })
        templates.append({
            "id": f"{nation['prefix']}_player_anchor",
            "components": {
                "Team": {"Id": nation["team_id"]},
                "PlayerOwner": {"PlayerId": nation["team_id"]},
                "WorldPositionCm": {"Value": {"X": 0, "Y": 0}},
                "AttributeBuffer": {
                    "base": {"Credits": 0, "Power": 0, "Ore": 0},
                    "current": {"Credits": 0, "Power": 0, "Ore": 0},
                },
            },
        })

    for nation in NATIONS:
        np = nation["prefix"]
        nk = nation["key"]
        color = nation["color"]

        # structures
        conyard_abilities: list[str] = []
        conyard_placements: list[dict] = []
        for struct_key, struct_name, health, kind, credits, power in STRUCTURES:
            if struct_key == "conyard":
                continue
            tid = template_id(np, "structure", struct_key)
            comps = base_components(f"{nation['display']} {struct_name}", kind, health, credits, power)
            ready_tag = f"State.Cnc.Ready.{nk}.{struct_key}"
            build, place, fx, meta = build_ability(nation, struct_name, tid, float(credits or 800), ready_tag, 120)
            abilities.extend([build, place])
            effects.extend(fx)
            conyard_abilities.append(meta["build"])
            conyard_placements.append({"slotIndex": len(conyard_placements), "abilityId": meta["place"]})
            prod_abilities: list[str] = ["Ability.Cnc.Shared.Hold"]
            if struct_key == "barracks":
                for inf in nation["infantry"]:
                    a, e, _ = train_ability(nation, inf, template_id(np, "infantry", inf), 200 + len(prod_abilities) * 50, 96)
                    abilities.append(a)
                    effects.extend(e)
                    tid_inf = template_id(np, "infantry", inf)
                    comps_inf = base_components(f"{nation['display']} {inf}", "infantry", 180 + len(prod_abilities) * 20)
                    comps_inf["AbilityStateBuffer"] = {"abilityIds": ["Ability.Cnc.Shared.Hold"]}
                    templates.append({"id": tid_inf, "components": comps_inf})
                    presenters.extend(presenter_visual(tid_inf, "infantry", color))
                    roster.append({"nation": nk, "id": tid_inf, "name": inf, "category": "infantry"})
                    unit_count += 1
                    if len(prod_abilities) < 8:
                        prod_abilities.append(a["id"])
                while len(prod_abilities) < 8:
                    prod_abilities.append("Ability.Cnc.Shared.Hold")
            elif struct_key == "war_factory":
                for veh in nation["vehicles"]:
                    a, e, _ = train_ability(nation, veh, template_id(np, "vehicle", veh), 400 + len(prod_abilities) * 80, 120)
                    abilities.append(a)
                    effects.extend(e)
                    tid_v = template_id(np, "vehicle", veh)
                    comps_v = base_components(f"{nation['display']} {veh}", "vehicle", 400 + len(prod_abilities) * 30)
                    comps_v["AbilityStateBuffer"] = {"abilityIds": ["Ability.Cnc.Shared.Hold"]}
                    templates.append({"id": tid_v, "components": comps_v})
                    presenters.extend(presenter_visual(tid_v, "vehicle", color))
                    roster.append({"nation": nk, "id": tid_v, "name": veh, "category": "vehicle"})
                    unit_count += 1
                    if len(prod_abilities) < 8:
                        prod_abilities.append(a["id"])
                while len(prod_abilities) < 8:
                    prod_abilities.append("Ability.Cnc.Shared.Hold")
            elif struct_key == "airfield":
                for air in nation["aircraft"]:
                    a, e, _ = train_ability(nation, air, template_id(np, "aircraft", air), 600 + len(prod_abilities) * 100, 144)
                    abilities.append(a)
                    effects.extend(e)
                    tid_a = template_id(np, "aircraft", air)
                    comps_a = base_components(f"{nation['display']} {air}", "aircraft", 250)
                    comps_a["AbilityStateBuffer"] = {"abilityIds": ["Ability.Cnc.Shared.Hold"]}
                    templates.append({"id": tid_a, "components": comps_a})
                    presenters.extend(presenter_visual(tid_a, "aircraft", color))
                    roster.append({"nation": nk, "id": tid_a, "name": air, "category": "aircraft"})
                    unit_count += 1
                    if len(prod_abilities) < 8:
                        prod_abilities.append(a["id"])
                while len(prod_abilities) < 8:
                    prod_abilities.append("Ability.Cnc.Shared.Hold")
            comps["AbilityStateBuffer"] = {"abilityIds": prod_abilities[:8]}
            templates.append({"id": tid, "components": comps})
            presenters.extend(presenter_visual(tid, kind, color))
            roster.append({"nation": nk, "id": tid, "name": struct_name, "category": kind})
            unit_count += 1

        # conyard
        conyard_tid = template_id(np, "structure", "conyard")
        conyard_comps = base_components(f"{nation['display']} Construction Yard", "structure", 1600, 6000, 120)
        conyard_slots = (conyard_abilities + ["Ability.Cnc.Shared.Hold"] * 8)[:8]
        conyard_comps["AbilityStateBuffer"] = {"abilityIds": conyard_slots}
        conyard_comps["AbilityFormSetRef"] = {"formSetId": f"cnc_{nk}_conyard_forms"}
        templates.append({"id": conyard_tid, "components": conyard_comps})
        presenters.extend(presenter_visual(conyard_tid, "structure", color))
        roster.append({"nation": nk, "id": conyard_tid, "name": "Construction Yard", "category": "structure"})
        unit_count += 1
        form_sets.append({
            "id": f"cnc_{nk}_conyard_forms",
            "forms": [{"formId": "default", "slotOverrides": conyard_placements}],
        })

        # worker
        wkey, wname, whealth, wkind, wcredits, _ = WORKER
        wtid = template_id(np, wkind, wkey)
        wcomps = base_components(f"{nation['display']} {wname}", wkind, whealth, wcredits)
        wcomps["AbilityStateBuffer"] = {"abilityIds": ["Ability.Cnc.Shared.Hold"]}
        templates.append({"id": wtid, "components": wcomps})
        presenters.extend(presenter_visual(wtid, wkind, color))
        roster.append({"nation": nk, "id": wtid, "name": wname, "category": wkind})
        unit_count += 1

        # special
        for spec in nation["special"]:
            stid = template_id(np, "special", spec)
            scomps = base_components(f"{nation['display']} {spec}", "super" if "Silo" in spec or "Controller" in spec or "Dominator" in spec else "defense", 800, 3000, 100)
            scomps["AbilityStateBuffer"] = {"abilityIds": ["Ability.Cnc.Shared.Hold"]}
            templates.append({"id": stid, "components": scomps})
            presenters.extend(presenter_visual(stid, "super", color))
            roster.append({"nation": nk, "id": stid, "name": spec, "category": "special"})
            unit_count += 1

    graphs = [
        {
            "id": "cnc.graph.armyComposition",
            "kind": "Query",
            "entry": "allMapEntities",
            "nodes": [
                {"id": "allMapEntities", "op": "QueryAllMapEntities", "next": "filterTeam"},
                {"id": "filterTeam", "op": "QueryFilterTeam", "teamId": 1, "next": "hasHealth"},
                {"id": "hasHealth", "op": "QueryFilterAttributeRange", "attribute": "Health", "inputs": ["minHealth", "maxHealth"], "next": "unitCount"},
                {"id": "minHealth", "op": "ConstFloat", "floatValue": 1, "next": "maxHealth"},
                {"id": "maxHealth", "op": "ConstFloat", "floatValue": 10000, "next": "filterTeam"},
                {"id": "unitCount", "op": "AggCount", "next": "totalHealth"},
                {"id": "totalHealth", "op": "AggSumAttribute", "attribute": "Health"},
            ],
            "outputs": [
                {
                    "id": "army",
                    "destination": "EntityCollection",
                    "type": "TargetList",
                    "collectionKey": "cnc.collection.team1.army",
                    "role": "Display",
                    "title": "Allied Army",
                    "summary": "Team 1 combat entities with health.",
                },
                {"id": "unitCount", "destination": "Summary", "type": "Int", "source": "unitCount", "key": "cnc.summary.unitCount"},
                {"id": "totalHealth", "destination": "Summary", "type": "Float", "source": "totalHealth", "key": "cnc.summary.totalHealth"},
            ],
        },
        {
            "id": "cnc.graph.sovietArmy",
            "kind": "Query",
            "entry": "allMapEntities",
            "nodes": [
                {"id": "allMapEntities", "op": "QueryAllMapEntities", "next": "filterTeam"},
                {"id": "filterTeam", "op": "QueryFilterTeam", "teamId": 2, "next": "unitCount"},
                {"id": "unitCount", "op": "AggCount"},
            ],
            "outputs": [
                {
                    "id": "army",
                    "destination": "EntityCollection",
                    "type": "TargetList",
                    "collectionKey": "cnc.collection.team2.army",
                    "role": "Display",
                    "title": "Soviet Army",
                    "summary": "Team 2 entities.",
                },
                {"id": "unitCount", "destination": "Summary", "type": "Int", "source": "unitCount", "key": "cnc.summary.sovietCount"},
            ],
        },
        {
            "id": "cnc.graph.yuriArmy",
            "kind": "Query",
            "entry": "allMapEntities",
            "nodes": [
                {"id": "allMapEntities", "op": "QueryAllMapEntities", "next": "filterTeam"},
                {"id": "filterTeam", "op": "QueryFilterTeam", "teamId": 3, "next": "unitCount"},
                {"id": "unitCount", "op": "AggCount"},
            ],
            "outputs": [
                {
                    "id": "army",
                    "destination": "EntityCollection",
                    "type": "TargetList",
                    "collectionKey": "cnc.collection.team3.army",
                    "role": "Display",
                    "title": "Yuri Army",
                    "summary": "Team 3 entities.",
                },
                {"id": "unitCount", "destination": "Summary", "type": "Int", "source": "unitCount", "key": "cnc.summary.yuriCount"},
            ],
        },
    ]

    items = {
        "shapes": [{"id": "shape_cnc_mod_1x1", "width": 1, "height": 1, "rotations": [0]}],
        "layouts": [{
            "id": "layout_cnc_depot",
            "purpose": "Equipment",
            "namedSlots": [{"id": "upgrade_module", "requiredAll": ["Equip.Slot.CncUpgrade"]}],
        }],
        "definitions": [],
    }
    exchange_ops: list[dict] = []
    for nation in NATIONS:
        for idx, spec in enumerate(["Armor Plating", "Reactor Boost", "Targeting AI"]):
            iid = f"itm_{nation['key']}_upgrade_{idx}"
            eid = f"Effect.Cnc.Item.{nation['key'].title()}.Upgrade{idx}"
            items["definitions"].append({
                "id": iid,
                "displayName": f"{nation['display']} {spec}",
                "shape": "shape_cnc_mod_1x1",
                "tags": ["Equip.Slot.CncUpgrade", f"Item.Cnc.{nation['key']}"],
                "allowedNamedSlots": ["upgrade_module"],
                "equipEffects": [eid],
            })
            effects.append({
                "id": eid,
                "tags": ["Effect.Cnc.ItemPassive"],
                "presetType": "Buff",
                "lifetime": "Infinite",
                "participatesInResponse": False,
                "modifiers": [
                    {"attribute": "Health", "op": "Add", "value": 50 + idx * 25},
                    {"attribute": "Credits", "op": "Add", "value": 0},
                ],
            })
            exchange_ops.append({
                "id": f"exchange_buy_{iid}",
                "kind": "Buy",
                "costAttributes": [{"attribute": "Credits", "amount": 500 + idx * 200}],
                "outputs": [{"itemId": iid, "quantity": 1}],
            })

    map_entities: list[dict] = []
    positions = [(8000, 14000), (16000, 8000), (12000, 4000)]
    for nation, (x, y) in zip(NATIONS, positions):
        np = nation["prefix"]
        nk = nation["key"]
        tid = nation["team_id"]
        pid = 1 if tid == 1 else tid
        map_entities.extend([
            {"Template": f"{np}_team_anchor", "InstanceId": f"{nk}_team", "Overrides": {
                "Team": {"Id": tid}, "WorldPositionCm": {"Value": {"X": x, "Y": y}},
            }},
            {"Template": f"{np}_player_anchor", "InstanceId": f"{nk}_player", "Overrides": {
                "Team": {"Id": tid}, "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x + 200, "Y": y + 200}},
            }},
            {"Template": template_id(np, "structure", "conyard"), "InstanceId": f"{nk}_conyard", "Overrides": {
                "Team": {"Id": tid}, "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x, "Y": y - 400}},
                "Name": {"Value": f"{nation['display']} Construction Yard"},
            }},
            {"Template": template_id(np, "structure", "barracks"), "InstanceId": f"{nk}_barracks", "Overrides": {
                "Team": {"Id": tid}, "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x + 1200, "Y": y}},
                "Name": {"Value": f"{nation['display']} Barracks"},
            }},
            {"Template": template_id(np, "structure", "war_factory"), "InstanceId": f"{nk}_factory", "Overrides": {
                "Team": {"Id": tid}, "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x - 1200, "Y": y}},
                "Name": {"Value": f"{nation['display']} War Factory"},
            }},
            {"Template": template_id(np, "worker", "harvester"), "InstanceId": f"{nk}_harvester", "Overrides": {
                "Team": {"Id": tid}, "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x + 600, "Y": y + 800}},
                "Name": {"Value": f"{nation['display']} Ore Harvester"},
            }},
        ])

    game_map = {
        "Id": "cnc_tri_nation_war",
        "Tags": ["rts", "cnc", "tri_nation", "production"],
        "DefaultCamera": {
            "TargetXCm": 12000, "TargetYCm": 9000, "Yaw": 180, "Pitch": 54,
            "DistanceCm": 14000, "FovYDeg": 60,
        },
        "Boards": [{
            "Name": "default", "SpatialType": "Grid", "WidthInTiles": 80, "HeightInTiles": 80,
            "GridCellSizeCm": 400, "ChunkSizeCells": 32, "NavigationEnabled": False,
        }],
        "Entities": map_entities,
        "Teams": [{"TeamId": n["team_id"], "RepresentativeInstanceId": f"{n['key']}_team"} for n in NATIONS],
        "Players": [{"PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "allied_player"}],
    }

    OUT.mkdir(parents=True, exist_ok=True)
    for sub in ("Entities", "GAS", "Presentation", "Items", "Exchange", "Maps"):
        (OUT / sub).mkdir(parents=True, exist_ok=True)

    (OUT / "Entities/templates.json").write_text(json.dumps(templates, indent=2), encoding="utf-8")
    (OUT / "GAS/abilities.json").write_text(json.dumps(abilities, indent=2), encoding="utf-8")
    (OUT / "GAS/effects.json").write_text(json.dumps(effects, indent=2), encoding="utf-8")
    (OUT / "GAS/graphs.json").write_text(json.dumps(graphs, indent=2), encoding="utf-8")
    (OUT / "GAS/ability_form_sets.json").write_text(json.dumps(form_sets, indent=2), encoding="utf-8")
    (OUT / "Presentation/presenters.json").write_text(json.dumps(presenters, indent=2), encoding="utf-8")
    (OUT / "Presentation/mesh_assets.json").write_text(json.dumps([
        {"id": "cube", "type": "Primitive", "primitiveKind": "Cube"},
        {"id": "sphere", "type": "Primitive", "primitiveKind": "Sphere"},
    ], indent=2), encoding="utf-8")
    (OUT / "Presentation/host_assets.json").write_text("[]", encoding="utf-8")
    (OUT / "Items/shapes.json").write_text(json.dumps(items["shapes"], indent=2), encoding="utf-8")
    (OUT / "Items/layouts.json").write_text(json.dumps(items["layouts"], indent=2), encoding="utf-8")
    (OUT / "Items/definitions.json").write_text(json.dumps(items["definitions"], indent=2), encoding="utf-8")
    (OUT / "Exchange/operations.json").write_text(json.dumps(exchange_ops, indent=2), encoding="utf-8")
    (OUT / "Maps/cnc_tri_nation_war.json").write_text(json.dumps(game_map, indent=2), encoding="utf-8")
    (OUT / "game.json").write_text(json.dumps({
        "startupMapId": "cnc_tri_nation_war",
        "startupLocalPlayerId": 1,
        "windowTitle": "Ludots - C&C Tri-Nation Full Game",
        "windowWidth": 1600, "windowHeight": 900, "windowResizable": True, "targetFps": 60,
        "browserRuntime": {"enabled": True, "required": True, "provider": "cef"},
    }, indent=2), encoding="utf-8")
    (OUT / "roster_catalog.json").write_text(json.dumps({"unitCount": unit_count, "roster": roster}, indent=2), encoding="utf-8")

    print(f"Generated {unit_count} unit templates, {len(abilities)} abilities, {len(effects)} effects")
    assert unit_count >= 100, f"Expected >=100 units, got {unit_count}"


if __name__ == "__main__":
    generate()
