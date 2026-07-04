#!/usr/bin/env python3
"""Generate AoE Empire mod JSON assets: 5 nations × 20 unit types = 100 templates."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "mods/aoe_empire/AoeEmpireMod/assets"

NATIONS = [
    {"id": "frankia", "team": 1, "name": "Frankia", "color": "#2563EB", "elite_name": "Paladin", "prefix": "Frank"},
    {"id": "andalus", "team": 2, "name": "Al-Andalus", "color": "#16A34A", "elite_name": "Mameluke", "prefix": "Saracen"},
    {"id": "khanate", "team": 3, "name": "Khanate", "color": "#DC2626", "elite_name": "Keshik", "prefix": "Mongol"},
    {"id": "dynasty", "team": 4, "name": "Dynasty", "color": "#CA8A04", "elite_name": "Chu Ko Nu", "prefix": "Chinese"},
    {"id": "nordheim", "team": 5, "name": "Nordheim", "color": "#7C3AED", "elite_name": "Berserker", "prefix": "Viking"},
]

ARCHETYPES: list[tuple[str, str, dict[str, float], list[str], str | None]] = [
    ("team_anchor", "Team Anchor", {"Food": 0, "Wood": 0, "Gold": 0, "Stone": 0}, [], None),
    ("player_anchor", "Player Anchor", {"Food": 0, "Wood": 0, "Gold": 0, "Stone": 0}, [], None),
    ("town_center", "Town Center", {"Health": 2400, "Food": 200, "Wood": 200, "Gold": 100, "Stone": 150}, [
        "Ability.Aoe.Build.House",
        "Ability.Aoe.Build.Barracks",
        "Ability.Aoe.Build.LumberCamp",
        "Ability.Aoe.Age.Feudal",
    ], "TOWN_CENTER_FORMS"),
    ("house", "House", {"Health": 900, "Food": 5}, [], None),
    ("barracks", "Barracks", {"Health": 1200}, [
        "Ability.Aoe.Train.Militia",
        "Ability.Aoe.Train.Spearman",
        "Ability.Aoe.Train.Archer",
    ], None),
    ("archery_range", "Archery Range", {"Health": 1100}, [
        "Ability.Aoe.Train.Archer",
        "Ability.Aoe.Train.Crossbowman",
    ], None),
    ("stable", "Stable", {"Health": 1200}, [
        "Ability.Aoe.Train.Scout",
        "Ability.Aoe.Train.Knight",
    ], None),
    ("siege_workshop", "Siege Workshop", {"Health": 1300}, [
        "Ability.Aoe.Train.Ram",
        "Ability.Aoe.Train.Catapult",
    ], None),
    ("lumber_camp", "Lumber Camp", {"Health": 800, "Wood": 100}, [], None),
    ("mining_camp", "Mining Camp", {"Health": 850, "Stone": 100, "Gold": 50}, [], None),
    ("farm", "Farm", {"Health": 600, "Food": 50}, [], None),
    ("watch_tower", "Watch Tower", {"Health": 1000}, [
        "Ability.Aoe.Attack.Tower",
    ], None),
    ("villager", "Villager", {"Health": 250, "MoveSpeed": 550}, [
        "Ability.Aoe.Gather.Wood",
        "Ability.Aoe.Gather.Gold",
        "Ability.Aoe.Build.House",
        "Ability.Aoe.Attack.Melee",
    ], "VILLAGER_FORMS"),
    ("militia", "Militia", {"Health": 400, "MoveSpeed": 600, "Attack": 6}, [
        "Ability.Aoe.Attack.Melee",
    ], None),
    ("spearman", "Spearman", {"Health": 450, "MoveSpeed": 580, "Attack": 8}, [
        "Ability.Aoe.Attack.Melee",
    ], None),
    ("archer", "Archer", {"Health": 300, "MoveSpeed": 560, "Attack": 5, "Range": 500}, [
        "Ability.Aoe.Attack.Ranged",
    ], None),
    ("cavalry", "Scout Cavalry", {"Health": 500, "MoveSpeed": 900, "Attack": 7}, [
        "Ability.Aoe.Attack.Melee",
    ], None),
    ("knight", "Knight", {"Health": 700, "MoveSpeed": 750, "Attack": 12}, [
        "Ability.Aoe.Attack.Melee",
    ], None),
    ("ram", "Battering Ram", {"Health": 800, "MoveSpeed": 400, "Attack": 20}, [
        "Ability.Aoe.Attack.Siege",
    ], None),
    ("elite", "Elite Unit", {"Health": 850, "MoveSpeed": 700, "Attack": 14}, [
        "Ability.Aoe.Attack.Melee",
        "Ability.Aoe.Attack.Special",
    ], None),
]

DISPLAY_NAMES = {
    "team_anchor": "Team Anchor",
    "player_anchor": "Player Anchor",
    "town_center": "Town Center",
    "house": "House",
    "barracks": "Barracks",
    "archery_range": "Archery Range",
    "stable": "Stable",
    "siege_workshop": "Siege Workshop",
    "lumber_camp": "Lumber Camp",
    "mining_camp": "Mining Camp",
    "farm": "Farm",
    "watch_tower": "Watch Tower",
    "villager": "Villager",
    "militia": "Militia",
    "spearman": "Spearman",
    "archer": "Archer",
    "cavalry": "Scout Cavalry",
    "knight": "Knight",
    "ram": "Battering Ram",
}

TRAIN_MAP = {
    "militia": "Ability.Aoe.Train.Militia",
    "spearman": "Ability.Aoe.Train.Spearman",
    "archer": "Ability.Aoe.Train.Archer",
    "cavalry": "Ability.Aoe.Train.Scout",
    "knight": "Ability.Aoe.Train.Knight",
    "ram": "Ability.Aoe.Train.Ram",
}

BUILD_MAP = {
    "house": "Ability.Aoe.Build.House",
    "barracks": "Ability.Aoe.Build.Barracks",
    "lumber_camp": "Ability.Aoe.Build.LumberCamp",
    "mining_camp": "Ability.Aoe.Build.MiningCamp",
    "farm": "Ability.Aoe.Build.Farm",
    "archery_range": "Ability.Aoe.Build.ArcheryRange",
    "stable": "Ability.Aoe.Build.Stable",
    "siege_workshop": "Ability.Aoe.Build.SiegeWorkshop",
    "watch_tower": "Ability.Aoe.Build.WatchTower",
}


def base_components(
    name: str,
    team: int,
    attrs: dict[str, float],
    abilities: list[str],
    form_set: str | None,
    selectable: bool,
    player_owned: bool,
) -> dict[str, Any]:
    comps: dict[str, Any] = {
        "Name": {"Value": name},
        "Team": {"Id": team},
        "WorldPositionCm": {"Value": {"X": 0, "Y": 0}},
        "AttributeBuffer": {"base": attrs, "current": dict(attrs)},
        "GameplayTagContainer": {},
        "TagCountContainer": {},
        "TimedTagBuffer": {},
        "OrderBuffer": {},
        "BlackboardSpatialBuffer": {},
        "BlackboardEntityBuffer": {},
        "BlackboardIntBuffer": {},
    }
    if selectable:
        comps["SelectionSelectableTag"] = {}
        comps["SelectionSelectableState"] = {"IsEnabled": True}
        comps["FacingDirection"] = {"AngleRad": 0.0}
        comps["PresentationStaticTransform"] = {}
    if player_owned:
        comps["PlayerOwner"] = {"PlayerId": 1}
    if abilities:
        padded = abilities + ["Ability.Aoe.Shared.Hold"] * max(0, 4 - len(abilities))
        comps["AbilityStateBuffer"] = {"abilityIds": padded[:4]}
    if form_set:
        comps["AbilityFormSetRef"] = {"formSetId": form_set}
    return comps


def generate_templates() -> list[dict[str, Any]]:
    templates: list[dict[str, Any]] = []
    for nation in NATIONS:
        for archetype, _label, attrs, abilities, form_set in ARCHETYPES:
            tid = f"aoe_{nation['id']}_{archetype}"
            if archetype == "elite":
                display = nation["elite_name"]
            else:
                display = DISPLAY_NAMES.get(archetype, archetype.replace("_", " ").title())
            name = f"{nation['prefix']} {display}" if archetype not in ("team_anchor", "player_anchor") else display
            selectable = archetype not in ("team_anchor", "player_anchor")
            player_owned = archetype not in ("team_anchor",)
            nation_abilities = list(abilities)
            if archetype == "barracks":
                nation_abilities = [
                    f"Ability.Aoe.{nation['id'].title()}.Train.Militia",
                    f"Ability.Aoe.{nation['id'].title()}.Train.Spearman",
                    f"Ability.Aoe.{nation['id'].title()}.Train.Archer",
                ]
            elif archetype == "town_center":
                nation_abilities = [
                    f"Ability.Aoe.{nation['id'].title()}.Build.House",
                    f"Ability.Aoe.{nation['id'].title()}.Build.Barracks",
                    f"Ability.Aoe.{nation['id'].title()}.Build.LumberCamp",
                    f"Ability.Aoe.Age.Feudal",
                ]
            elif archetype == "villager":
                nation_abilities = [
                    "Ability.Aoe.Gather.Wood",
                    "Ability.Aoe.Gather.Gold",
                    f"Ability.Aoe.{nation['id'].title()}.Build.House",
                    "Ability.Aoe.Attack.Melee",
                ]
            nation_form_set = None
            if form_set == "TOWN_CENTER_FORMS":
                nation_form_set = f"aoe_{nation['id']}_town_center_forms"
            elif form_set == "VILLAGER_FORMS":
                nation_form_set = f"aoe_{nation['id']}_villager_forms"
            elif form_set:
                nation_form_set = form_set
            templates.append({
                "id": tid,
                "components": base_components(
                    name,
                    nation["team"],
                    attrs,
                    nation_abilities,
                    nation_form_set,
                    selectable,
                    player_owned and nation["team"] == 1,
                ),
            })
    return templates


def hold_ability() -> dict[str, Any]:
    return {
        "id": "Ability.Aoe.Shared.Hold",
        "exec": {"clockId": "FixedFrame", "items": [{"kind": "End", "tick": 0}]},
        "presentation": {
            "displayName": "Hold",
            "iconGlyph": "H",
            "accentColor": "#6B7280",
            "hintText": "Reserved command slot.",
        },
    }


def build_ability(nation_id: str, nation_name: str, build_key: str, display: str, glyph: str, color: str, ticks: int, step_cost: dict[str, float], ready_tag: str, place_ability: str) -> dict[str, Any]:
    cap = nation_id.title()
    build_id = f"Ability.Aoe.{cap}.Build.{build_key}"
    items: list[dict[str, Any]] = [
        {"kind": "TagClip", "tick": 0, "duration": ticks, "tag": f"Status.Aoe.{cap}.Building.{build_key}"},
    ]
    for tick in range(0, ticks, 15):
        items.append({"kind": "EffectSignal", "tick": tick, "template": f"Effect.Aoe.{cap}.Cost.{build_key}Step"})
    items.append({"kind": "TagSignal", "tick": ticks, "tag": ready_tag})
    items.append({"kind": "End", "tick": ticks})
    return {
        "id": build_id,
        "exec": {"clockId": "FixedFrame", "items": items},
        "input": {"castModeOverride": "TargetFirst"},
        "blockTags": {"blockedAny": ["State.Aoe.Constructing"]},
        "presentation": {
            "displayName": f"Build {display}",
            "iconGlyph": glyph,
            "accentColor": color,
            "hintText": f"{nation_name}: queue {display.lower()} construction.",
        },
    }


def place_ability(nation_id: str, build_key: str, display: str, glyph: str, color: str, ready_tag: str, effect_id: str) -> dict[str, Any]:
    cap = nation_id.title()
    return {
        "id": f"Ability.Aoe.{cap}.Place.{build_key}",
        "exec": {
            "clockId": "FixedFrame",
            "items": [
                {"kind": "TagSignal", "tick": 0, "tag": ready_tag, "payloadA": 1},
                {"kind": "EffectSignal", "tick": 0, "template": effect_id},
                {"kind": "End", "tick": 0},
            ],
        },
        "blockTags": {"requiredAll": [ready_tag]},
        "presentation": {
            "displayName": f"Place {display}",
            "iconGlyph": glyph,
            "accentColor": color,
            "hintText": f"Place completed {display.lower()}.",
        },
    }


def train_ability(nation_id: str, nation_name: str, unit_key: str, display: str, glyph: str, color: str, ticks: int, ready_tag: str) -> dict[str, Any]:
    cap = nation_id.title()
    items: list[dict[str, Any]] = [
        {"kind": "TagClip", "tick": 0, "duration": ticks, "tag": f"Status.Aoe.{cap}.Training.{unit_key}"},
    ]
    for tick in range(0, ticks, 10):
        items.append({"kind": "EffectSignal", "tick": tick, "template": f"Effect.Aoe.{cap}.Cost.Train{unit_key.title()}Step"})
    items.append({"kind": "TagSignal", "tick": ticks, "tag": ready_tag})
    items.append({"kind": "End", "tick": ticks})
    return {
        "id": f"Ability.Aoe.{cap}.Train.{unit_key.title()}",
        "exec": {"clockId": "FixedFrame", "items": items},
        "input": {"castModeOverride": "TargetFirst"},
        "presentation": {
            "displayName": f"Train {display}",
            "iconGlyph": glyph,
            "accentColor": color,
            "hintText": f"{nation_name}: queue {display.lower()} training.",
        },
    }


def generate_abilities() -> list[dict[str, Any]]:
    abilities = [hold_ability()]
    build_defs = [
        ("House", "House", "HS", "#A3A3A3", 90),
        ("Barracks", "Barracks", "BA", "#EF4444", 120),
        ("LumberCamp", "Lumber Camp", "LC", "#22C55E", 60),
        ("MiningCamp", "Mining Camp", "MC", "#78716C", 75),
        ("Farm", "Farm", "FM", "#FACC15", 45),
        ("ArcheryRange", "Archery Range", "AR", "#F97316", 100),
        ("Stable", "Stable", "ST", "#8B5CF6", 110),
        ("SiegeWorkshop", "Siege Workshop", "SW", "#64748B", 130),
        ("WatchTower", "Watch Tower", "WT", "#0EA5E9", 80),
    ]
    train_defs = [
        ("Militia", "Militia", "MI", "#DC2626", 60),
        ("Spearman", "Spearman", "SP", "#B91C1C", 70),
        ("Archer", "Archer", "AC", "#EA580C", 65),
        ("Scout", "Scout Cavalry", "SC", "#7C3AED", 80),
        ("Knight", "Knight", "KN", "#4338CA", 100),
        ("Ram", "Battering Ram", "RM", "#525252", 120),
        ("Catapult", "Catapult", "CP", "#44403C", 140),
        ("Crossbowman", "Crossbowman", "CB", "#C2410C", 75),
    ]
    for nation in NATIONS:
        nid = nation["id"]
        for key, display, glyph, color, ticks in build_defs:
            ready = f"State.Aoe.{nid.title()}.Ready.{key}"
            abilities.append(build_ability(nid, nation["name"], key, display, glyph, color, ticks, {}, ready, f"Ability.Aoe.{nid.title()}.Place.{key}"))
            abilities.append(place_ability(nid, key, display, glyph, color, ready, f"Effect.Aoe.{nid.title()}.Place.{key}"))
        for key, display, glyph, color, ticks in train_defs:
            ready = f"State.Aoe.{nid.title()}.Ready.Train{key}"
            abilities.append(train_ability(nid, nation["name"], key.lower(), display, glyph, color, ticks, ready))

    # Shared combat / gather / age
    abilities.extend([
        {
            "id": "Ability.Aoe.Gather.Wood",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Gather.Wood"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Gather Wood", "iconGlyph": "W", "accentColor": "#22C55E", "hintText": "Harvest wood from nearby trees."},
        },
        {
            "id": "Ability.Aoe.Gather.Gold",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Gather.Gold"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Gather Gold", "iconGlyph": "G", "accentColor": "#EAB308", "hintText": "Mine gold from deposits."},
        },
        {
            "id": "Ability.Aoe.Age.Feudal",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Age.Feudal.Cost"},
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Age.Feudal.Grant"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Feudal Age", "iconGlyph": "II", "accentColor": "#2563EB", "hintText": "Advance to Feudal Age."},
        },
        {
            "id": "Ability.Aoe.Attack.Melee",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Attack.Melee"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Melee Attack", "iconGlyph": "AT", "accentColor": "#DC2626", "hintText": "Strike nearby enemies."},
            "targeting": {"castRangeCm": 120, "impactEffect": "Effect.Aoe.Attack.Melee"},
        },
        {
            "id": "Ability.Aoe.Attack.Ranged",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Attack.Ranged"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Ranged Attack", "iconGlyph": "RG", "accentColor": "#F97316", "hintText": "Fire at distant enemies."},
            "targeting": {"castRangeCm": 800, "impactEffect": "Effect.Aoe.Attack.Ranged"},
        },
        {
            "id": "Ability.Aoe.Attack.Siege",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Attack.Siege"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Siege Attack", "iconGlyph": "SG", "accentColor": "#525252", "hintText": "Crush structures."},
            "targeting": {"castRangeCm": 200, "impactEffect": "Effect.Aoe.Attack.Siege"},
        },
        {
            "id": "Ability.Aoe.Attack.Tower",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Attack.Tower"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Tower Fire", "iconGlyph": "TW", "accentColor": "#0EA5E9", "hintText": "Defensive tower attack."},
        },
        {
            "id": "Ability.Aoe.Attack.Special",
            "exec": {"clockId": "FixedFrame", "items": [
                {"kind": "EffectSignal", "tick": 0, "template": "Effect.Aoe.Attack.Special"},
                {"kind": "End", "tick": 0},
            ]},
            "presentation": {"displayName": "Special Attack", "iconGlyph": "SP", "accentColor": "#7C3AED", "hintText": "Nation elite special ability."},
            "targeting": {"castRangeCm": 600, "impactEffect": "Effect.Aoe.Attack.Special"},
        },
    ])
    return abilities


def cost_effect(eid: str, modifiers: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "id": eid,
        "tags": ["Effect.Aoe.Cost"],
        "presetType": "InstantDamage",
        "lifetime": "Instant",
        "participatesInResponse": False,
        "modifiers": modifiers,
    }


def place_effect(eid: str, template_id: str) -> dict[str, Any]:
    return {
        "id": eid,
        "tags": ["Effect.Aoe.Build"],
        "presetType": "CreateUnit",
        "lifetime": "Instant",
        "participatesInResponse": False,
        "unitCreation": {
            "templateId": template_id,
            "placementPattern": "Scatter",
            "count": 1,
            "onSpawnEffect": "Effect.Aoe.Construction",
            "copySourcePlayerOwner": True,
        },
    }


def train_effect(eid: str, template_id: str) -> dict[str, Any]:
    return {
        "id": eid,
        "tags": ["Effect.Aoe.Train"],
        "presetType": "CreateUnit",
        "lifetime": "Instant",
        "participatesInResponse": False,
        "unitCreation": {
            "templateId": template_id,
            "placementPattern": "Scatter",
            "count": 1,
            "offsetRadius": 320,
            "copySourcePlayerOwner": True,
        },
    }


def generate_effects() -> list[dict[str, Any]]:
    effects: list[dict[str, Any]] = [
        {
            "id": "Effect.Aoe.Construction",
            "tags": ["Effect.Aoe.Construction"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "State.Aoe.Constructing", "formula": "Fixed", "amount": 1}],
        },
        cost_effect("Effect.Aoe.Age.Feudal.Cost", [
            {"attribute": "Food", "op": "Add", "value": -500},
            {"attribute": "Gold", "op": "Add", "value": -200},
        ]),
        {
            "id": "Effect.Aoe.Age.Feudal.Grant",
            "tags": ["Effect.Aoe.Progression"],
            "presetType": "CompleteProgression",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "progression": {
                "id": "Progression.Aoe.FeudalAge",
                "scope": "explicit",
                "level": 1,
            },
        },
        cost_effect("Effect.Aoe.Gather.Wood", [{"attribute": "Wood", "op": "Add", "value": 10}]),
        cost_effect("Effect.Aoe.Gather.Gold", [{"attribute": "Gold", "op": "Add", "value": 8}]),
        {
            "id": "Effect.Aoe.Attack.Melee",
            "tags": ["Effect.Aoe.Combat"],
            "presetType": "Search",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "targetQuery": {"kind": "BuiltinSpatial", "shape": "Circle", "radius": 140},
            "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 4},
            "targetDispatch": {"payloadEffect": "Effect.Aoe.Damage.Melee"},
        },
        {
            "id": "Effect.Aoe.Damage.Melee",
            "tags": ["Effect.Aoe.Damage"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "modifiers": [{"attribute": "Health", "op": "Add", "value": -18}],
        },
        {
            "id": "Effect.Aoe.Attack.Ranged",
            "tags": ["Effect.Aoe.Combat"],
            "presetType": "Search",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "targetQuery": {"kind": "BuiltinSpatial", "shape": "Circle", "radius": 700},
            "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 1},
            "targetDispatch": {"payloadEffect": "Effect.Aoe.Damage.Ranged"},
        },
        {
            "id": "Effect.Aoe.Damage.Ranged",
            "tags": ["Effect.Aoe.Damage"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "modifiers": [{"attribute": "Health", "op": "Add", "value": -14}],
        },
        {
            "id": "Effect.Aoe.Attack.Siege",
            "tags": ["Effect.Aoe.Combat"],
            "presetType": "Search",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "targetQuery": {"kind": "BuiltinSpatial", "shape": "Circle", "radius": 180},
            "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 2},
            "targetDispatch": {"payloadEffect": "Effect.Aoe.Damage.Siege"},
        },
        {
            "id": "Effect.Aoe.Damage.Siege",
            "tags": ["Effect.Aoe.Damage"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "modifiers": [{"attribute": "Health", "op": "Add", "value": -45}],
        },
        {
            "id": "Effect.Aoe.Attack.Tower",
            "tags": ["Effect.Aoe.Combat"],
            "presetType": "PeriodicSearch",
            "lifetime": "After",
            "participatesInResponse": True,
            "duration": {"durationTicks": 60, "periodTicks": 30, "clockId": "FixedFrame"},
            "targetQuery": {"kind": "BuiltinSpatial", "shape": "Circle", "radius": 900},
            "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 1},
            "targetDispatch": {"payloadEffect": "Effect.Aoe.Damage.Ranged"},
        },
        {
            "id": "Effect.Aoe.Attack.Special",
            "tags": ["Effect.Aoe.Combat"],
            "presetType": "Search",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "targetQuery": {"kind": "BuiltinSpatial", "shape": "Cone", "radius": 600, "halfAngle": 35},
            "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 6},
            "targetDispatch": {"payloadEffect": "Effect.Aoe.Damage.Special"},
        },
        {
            "id": "Effect.Aoe.Damage.Special",
            "tags": ["Effect.Aoe.Damage"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": True,
            "modifiers": [{"attribute": "Health", "op": "Add", "value": -28}],
        },
    ]

    build_costs = {
        "House": [{"attribute": "Wood", "op": "Add", "value": -6.25}],
        "Barracks": [{"attribute": "Wood", "op": "Add", "value": -12.5}],
        "LumberCamp": [{"attribute": "Wood", "op": "Add", "value": -4.17}],
        "MiningCamp": [{"attribute": "Wood", "op": "Add", "value": -5.0}],
        "Farm": [{"attribute": "Wood", "op": "Add", "value": -3.33}],
        "ArcheryRange": [{"attribute": "Wood", "op": "Add", "value": -10.0}],
        "Stable": [{"attribute": "Wood", "op": "Add", "value": -11.0}, {"attribute": "Gold", "op": "Add", "value": -2.0}],
        "SiegeWorkshop": [{"attribute": "Wood", "op": "Add", "value": -13.0}, {"attribute": "Gold", "op": "Add", "value": -3.0}],
        "WatchTower": [{"attribute": "Stone", "op": "Add", "value": -8.0}],
    }
    train_costs = {
        "Militia": [{"attribute": "Food", "op": "Add", "value": -6.67}],
        "Spearman": [{"attribute": "Food", "op": "Add", "value": -3.57}, {"attribute": "Wood", "op": "Add", "value": -3.57}],
        "Archer": [{"attribute": "Food", "op": "Add", "value": -4.0}, {"attribute": "Wood", "op": "Add", "value": -3.0}],
        "Scout": [{"attribute": "Food", "op": "Add", "value": -5.0}, {"attribute": "Gold", "op": "Add", "value": -2.0}],
        "Knight": [{"attribute": "Food", "op": "Add", "value": -6.0}, {"attribute": "Gold", "op": "Add", "value": -4.0}],
        "Ram": [{"attribute": "Wood", "op": "Add", "value": -8.0}, {"attribute": "Gold", "op": "Add", "value": -2.0}],
        "Catapult": [{"attribute": "Wood", "op": "Add", "value": -10.0}, {"attribute": "Gold", "op": "Add", "value": -4.0}],
        "Crossbowman": [{"attribute": "Food", "op": "Add", "value": -4.5}, {"attribute": "Gold", "op": "Add", "value": -2.5}],
    }
    build_template_map = {
        "House": "house",
        "Barracks": "barracks",
        "LumberCamp": "lumber_camp",
        "MiningCamp": "mining_camp",
        "Farm": "farm",
        "ArcheryRange": "archery_range",
        "Stable": "stable",
        "SiegeWorkshop": "siege_workshop",
        "WatchTower": "watch_tower",
    }
    train_template_map = {
        "Militia": "militia",
        "Spearman": "spearman",
        "Archer": "archer",
        "Scout": "cavalry",
        "Knight": "knight",
        "Ram": "ram",
        "Catapult": "ram",
        "Crossbowman": "archer",
    }

    for nation in NATIONS:
        cap = nation["id"].title()
        nid = nation["id"]
        for key, mods in build_costs.items():
            effects.append(cost_effect(f"Effect.Aoe.{cap}.Cost.{key}Step", mods))
            arch = build_template_map[key]
            effects.append(place_effect(f"Effect.Aoe.{cap}.Place.{key}", f"aoe_{nid}_{arch}"))
        for key, mods in train_costs.items():
            effects.append(cost_effect(f"Effect.Aoe.{cap}.Cost.Train{key}Step", mods))
            arch = train_template_map[key]
            if key == "Catapult":
                arch = "ram"
            elif key == "Crossbowman":
                arch = "archer"
            elif key == "Scout":
                arch = "cavalry"
            effects.append(train_effect(f"Effect.Aoe.{cap}.Train.{key}", f"aoe_{nid}_{arch}"))

    return effects


def generate_form_sets() -> list[dict[str, Any]]:
    form_sets: list[dict[str, Any]] = []
    build_defs = [
        ("House", 0),
        ("Barracks", 1),
        ("LumberCamp", 2),
    ]
    for nation in NATIONS:
        cap = nation["id"].title()
        routes = []
        for key, slot in build_defs:
            ready = f"State.Aoe.{cap}.Ready.{key}"
            routes.append({
                "priority": 100,
                "requiredAll": [ready],
                "slotOverrides": [{"slotIndex": slot, "abilityId": f"Ability.Aoe.{cap}.Place.{key}"}],
            })
        form_sets.append({"id": f"aoe_{nation['id']}_town_center_forms", "routes": routes})
        form_sets.append({"id": f"aoe_{nation['id']}_villager_forms", "routes": [{
            "priority": 100,
            "requiredAll": [f"State.Aoe.{cap}.Ready.House"],
            "slotOverrides": [{"slotIndex": 2, "abilityId": f"Ability.Aoe.{cap}.Place.House"}],
        }]})
    return form_sets


def generate_progression() -> tuple[list, list, list]:
    scopes = [{"id": "faction"}, {"id": "city"}]
    progressions = [
        {"id": "Progression.Aoe.FeudalAge", "scope": "faction"},
        {"id": "Progression.Aoe.CastleAge", "scope": "faction"},
        {"id": "Progression.Aoe.ImperialAge", "scope": "faction"},
    ]
    requirements = [
        {
            "id": "Req.Aoe.FeudalAge",
            "root": {"kind": "ProgressionCompleted", "progression": "Progression.Aoe.FeudalAge", "scope": "faction", "entitySource": "ScopeHost"},
        },
        {
            "id": "Req.Aoe.CastleAge",
            "root": {"kind": "ProgressionCompleted", "progression": "Progression.Aoe.CastleAge", "scope": "faction", "entitySource": "ScopeHost"},
        },
    ]
    return scopes, progressions, requirements


def generate_items() -> tuple[list, list, list]:
    shapes = [
        {"id": "shape_relic_1x1", "rows": ["X"], "rotatable": False},
    ]
    layouts = [
        {
            "id": "layout_relic_socket",
            "purpose": "Equipment",
            "grantsEquipmentBonuses": True,
            "namedSlots": [
                {"id": "relic", "label": "Relic", "requiredAll": ["Equip.Slot.Relic"]},
            ],
        },
    ]
    definitions = []
    for nation in NATIONS:
        definitions.append({
            "id": f"itm_aoe_relic_{nation['id']}",
            "displayName": f"{nation['name']} Relic",
            "shape": "shape_relic_1x1",
            "tags": ["Equip.Slot.Relic", f"Item.Aoe.{nation['id']}"],
            "allowedNamedSlots": ["relic"],
            "equipEffects": [f"Effect.Aoe.Item.Relic.{nation['id'].title()}"],
            "abilityGrants": [{"slotIndex": 3, "ability": "Ability.Aoe.Attack.Special"}],
        })
    return shapes, layouts, definitions


def generate_item_effects() -> list[dict[str, Any]]:
    effects = []
    for nation in NATIONS:
        cap = nation["id"].title()
        effects.append({
            "id": f"Effect.Aoe.Item.Relic.{cap}",
            "tags": ["Effect.Aoe.Item"],
            "presetType": "Buff",
            "lifetime": "Infinite",
            "participatesInResponse": False,
            "modifiers": [
                {"attribute": "Attack", "op": "Add", "value": 3},
                {"attribute": "Health", "op": "Add", "value": 50},
            ],
        })
    return effects


def generate_graphs() -> list[dict[str, Any]]:
    return [
        {
            "id": "Graph.Aoe.Autocast.TargetHealth",
            "kind": "Score",
            "entry": "target",
            "nodes": [
                {"id": "target", "op": "LoadExplicitTarget", "next": "health"},
                {"id": "health", "op": "LoadAttribute", "attribute": "Health", "inputs": ["target"]},
            ],
        },
    ]


def generate_map() -> dict[str, Any]:
    entities: list[dict[str, Any]] = []
    positions = [
        (8000, 14000),
        (20000, 14000),
        (8000, 4000),
        (20000, 4000),
        (14000, 9000),
    ]
    for nation, (x, y) in zip(NATIONS, positions):
        nid = nation["id"]
        team = nation["team"]
        pid = 1 if team == 1 else team
        entities.extend([
            {"Template": f"aoe_{nid}_team_anchor", "InstanceId": f"{nid}_team_anchor", "Overrides": {
                "Team": {"Id": team},
                "WorldPositionCm": {"Value": {"X": x, "Y": y + 800}},
                "AttributeBuffer": {"base": {"Food": 200, "Wood": 200, "Gold": 100, "Stone": 100}, "current": {"Food": 200, "Wood": 200, "Gold": 100, "Stone": 100}},
            }},
            {"Template": f"aoe_{nid}_player_anchor", "InstanceId": f"{nid}_player_anchor", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid},
                "WorldPositionCm": {"Value": {"X": x + 200, "Y": y + 1000}},
            }},
            {"Template": f"aoe_{nid}_town_center", "InstanceId": f"{nid}_town_center", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid} if team == 1 else {},
                "WorldPositionCm": {"Value": {"X": x, "Y": y}},
                "Name": {"Value": f"{nation['prefix']} Town Center"},
                "FacingDirection": {"AngleRad": 0.0},
                "AbilityFormSetRef": {"formSetId": f"aoe_{nid}_town_center_forms"},
            }},
            {"Template": f"aoe_{nid}_villager", "InstanceId": f"{nid}_villager_a", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid} if team == 1 else {},
                "WorldPositionCm": {"Value": {"X": x - 600, "Y": y - 400}},
                "Name": {"Value": f"{nation['prefix']} Villager"},
            }},
            {"Template": f"aoe_{nid}_villager", "InstanceId": f"{nid}_villager_b", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid} if team == 1 else {},
                "WorldPositionCm": {"Value": {"X": x + 600, "Y": y - 400}},
                "Name": {"Value": f"{nation['prefix']} Villager"},
            }},
            {"Template": f"aoe_{nid}_militia", "InstanceId": f"{nid}_militia", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid} if team == 1 else {},
                "WorldPositionCm": {"Value": {"X": x - 400, "Y": y + 600}},
                "Name": {"Value": f"{nation['prefix']} Militia"},
            }},
            {"Template": f"aoe_{nid}_archer", "InstanceId": f"{nid}_archer", "Overrides": {
                "Team": {"Id": team},
                "PlayerOwner": {"PlayerId": pid} if team == 1 else {},
                "WorldPositionCm": {"Value": {"X": x + 400, "Y": y + 600}},
                "Name": {"Value": f"{nation['prefix']} Archer"},
            }},
        ])
    return {
        "Id": "rts_empire_like",
        "Tags": ["rts", "rts_showcase", "rts_production", "empire_like", "aoe"],
        "DefaultCamera": {
            "TargetXCm": 14000,
            "TargetYCm": 9000,
            "Yaw": 180,
            "Pitch": 54,
            "DistanceCm": 16000,
            "FovYDeg": 60,
        },
        "Boards": [{
            "Name": "default",
            "SpatialType": "Grid",
            "widthInMacroTiles": 80,
            "heightInMacroTiles": 60,
            "GridCellSizeCm": 400,
            "ChunkSizeCells": 32,
            "NavigationEnabled": False,
        }],
        "Entities": entities,
    }


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> None:
    templates = generate_templates()
    assert len(templates) == 100, f"Expected 100 templates, got {len(templates)}"

    abilities = generate_abilities()
    effects = generate_effects() + generate_item_effects()
    form_sets = generate_form_sets()
    scopes, progressions, requirements = generate_progression()
    shapes, layouts, items = generate_items()
    graphs = generate_graphs()
    game_map = generate_map()

    write_json(ASSETS / "Entities/templates.json", templates)
    write_json(ASSETS / "GAS/abilities.json", abilities)
    write_json(ASSETS / "GAS/effects.json", effects)
    write_json(ASSETS / "GAS/ability_form_sets.json", form_sets)
    write_json(ASSETS / "GAS/graphs.json", graphs)
    write_json(ASSETS / "Progression/scopes.json", scopes)
    write_json(ASSETS / "Progression/progressions.json", progressions)
    write_json(ASSETS / "Progression/requirements.json", requirements)
    write_json(ASSETS / "Items/shapes.json", shapes)
    write_json(ASSETS / "Items/layouts.json", layouts)
    write_json(ASSETS / "Items/definitions.json", items)
    write_json(ASSETS / "Maps/rts_empire_like.json", game_map)

    write_json(ASSETS / "game.json", {
        "startupMapId": "rts_empire_like",
        "startupSelectedPlayerId": 1,
        "windowTitle": "Ludots Engine - AoE Empire Mod",
        "windowWidth": 1600,
        "windowHeight": 900,
        "windowResizable": True,
        "targetFps": 60,
    })

    write_json(ASSETS / "Input/default_input.json", [{"id": "Default_Gameplay", "priority": 0}])
    write_json(ASSETS / "Input/input_order_mappings.json", {
        "actorOrderRouting": {
            "defaultOrderTypeKey": "moveTo",
            "rules": [],
        },
    })

    print(f"Generated {len(templates)} templates, {len(abilities)} abilities, {len(effects)} effects")
    print(f"Output: {ASSETS}")


if __name__ == "__main__":
    main()
