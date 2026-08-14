#!/usr/bin/env python3
"""Generate StarCraft Full RTS mod content: 100 unit templates + GAS/items/graphs."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "mods/showcases/rts_starcraft_full/RtsStarCraftFullShowcaseMod/assets"

# --- Unit roster (100 templates) -------------------------------------------------

TERRAN: list[dict[str, Any]] = [
    {"id": "scf_terran_scv", "name": "SCV", "kind": "worker", "health": 220, "minerals": 1200, "gas": 0, "speed": 600},
    {"id": "scf_terran_command_center", "name": "Command Center", "kind": "hq", "health": 1500, "minerals": 4000, "gas": 0, "trains": ["scf_terran_scv", "scf_terran_marine"]},
    {"id": "scf_terran_supply_depot", "name": "Supply Depot", "kind": "structure", "health": 400, "minerals": 100},
    {"id": "scf_terran_barracks", "name": "Barracks", "kind": "production", "health": 1000, "minerals": 1500, "trains": ["scf_terran_marine", "scf_terran_marauder", "scf_terran_reaper"]},
    {"id": "scf_terran_factory", "name": "Factory", "kind": "production", "health": 1250, "minerals": 1500, "gas": 100, "trains": ["scf_terran_hellion", "scf_terran_siege_tank", "scf_terran_hellbat"]},
    {"id": "scf_terran_starport", "name": "Starport", "kind": "production", "health": 1300, "minerals": 1500, "gas": 100, "trains": ["scf_terran_viking", "scf_terran_medivac", "scf_terran_banshee"]},
    {"id": "scf_terran_refinery", "name": "Refinery", "kind": "structure", "health": 750, "minerals": 75},
    {"id": "scf_terran_engineering_bay", "name": "Engineering Bay", "kind": "upgrade", "health": 850, "minerals": 125},
    {"id": "scf_terran_armory", "name": "Armory", "kind": "upgrade", "health": 750, "minerals": 150, "gas": 100},
    {"id": "scf_terran_bunker", "name": "Bunker", "kind": "defense", "health": 350, "minerals": 100, "attack": 6, "range": 500},
    {"id": "scf_terran_missile_turret", "name": "Missile Turret", "kind": "defense", "health": 250, "minerals": 100, "attack": 12, "range": 700},
    {"id": "scf_terran_fusion_core", "name": "Fusion Core", "kind": "tech", "health": 750, "minerals": 150, "gas": 150},
    {"id": "scf_terran_orbital_command", "name": "Orbital Command", "kind": "hq", "health": 1500, "minerals": 550, "gas": 0, "trains": ["scf_terran_scv", "scf_terran_marine", "scf_terran_marauder"]},
    {"id": "scf_terran_marine", "name": "Marine", "kind": "combat", "health": 450, "speed": 600, "attack": 6, "range": 500},
    {"id": "scf_terran_marauder", "name": "Marauder", "kind": "combat", "health": 625, "speed": 550, "attack": 10, "range": 450},
    {"id": "scf_terran_reaper", "name": "Reaper", "kind": "combat", "health": 360, "speed": 750, "attack": 4, "range": 450},
    {"id": "scf_terran_ghost", "name": "Ghost", "kind": "combat", "health": 300, "speed": 600, "attack": 10, "range": 600, "gas": 50},
    {"id": "scf_terran_firebat", "name": "Firebat", "kind": "combat", "health": 500, "speed": 550, "attack": 8, "range": 350},
    {"id": "scf_terran_medic", "name": "Medic", "kind": "support", "health": 350, "speed": 600, "attack": 0, "range": 400},
    {"id": "scf_terran_vulture", "name": "Vulture", "kind": "combat", "health": 400, "speed": 800, "attack": 6, "range": 500},
    {"id": "scf_terran_goliath", "name": "Goliath", "kind": "combat", "health": 700, "speed": 500, "attack": 12, "range": 550},
    {"id": "scf_terran_siege_tank", "name": "Siege Tank", "kind": "combat", "health": 950, "speed": 450, "attack": 35, "range": 700},
    {"id": "scf_terran_hellion", "name": "Hellion", "kind": "combat", "health": 450, "speed": 850, "attack": 8, "range": 450},
    {"id": "scf_terran_hellbat", "name": "Hellbat", "kind": "combat", "health": 550, "speed": 550, "attack": 12, "range": 350},
    {"id": "scf_terran_thor", "name": "Thor", "kind": "combat", "health": 1600, "speed": 400, "attack": 30, "range": 600},
    {"id": "scf_terran_viking", "name": "Viking", "kind": "air", "health": 500, "speed": 700, "attack": 10, "range": 550},
    {"id": "scf_terran_medivac", "name": "Medivac", "kind": "air", "health": 450, "speed": 650, "attack": 0, "range": 400},
    {"id": "scf_terran_banshee", "name": "Banshee", "kind": "air", "health": 550, "speed": 700, "attack": 12, "range": 600},
    {"id": "scf_terran_battlecruiser", "name": "Battlecruiser", "kind": "air", "health": 2500, "speed": 450, "attack": 25, "range": 650},
    {"id": "scf_terran_raven", "name": "Raven", "kind": "air", "health": 400, "speed": 650, "attack": 0, "range": 500},
    {"id": "scf_terran_cyclone", "name": "Cyclone", "kind": "combat", "health": 550, "speed": 600, "attack": 11, "range": 550},
    {"id": "scf_terran_widow_mine", "name": "Widow Mine", "kind": "combat", "health": 300, "speed": 650, "attack": 40, "range": 500},
    {"id": "scf_terran_liberator", "name": "Liberator", "kind": "air", "health": 750, "speed": 550, "attack": 18, "range": 700},
    {"id": "scf_terran_diamondback", "name": "Diamondback", "kind": "combat", "health": 600, "speed": 650, "attack": 14, "range": 550},
]

ZERG: list[dict[str, Any]] = [
    {"id": "scf_zerg_drone", "name": "Drone", "kind": "worker", "health": 240, "minerals": 900, "speed": 600},
    {"id": "scf_zerg_overlord", "name": "Overlord", "kind": "support", "health": 600, "speed": 450, "supply": 8},
    {"id": "scf_zerg_hatchery", "name": "Hatchery", "kind": "hq", "health": 1500, "minerals": 300, "trains": ["scf_zerg_drone", "scf_zerg_zergling"]},
    {"id": "scf_zerg_spawning_pool", "name": "Spawning Pool", "kind": "tech", "health": 1000, "minerals": 200, "trains": ["scf_zerg_zergling", "scf_zerg_baneling"]},
    {"id": "scf_zerg_roach_warren", "name": "Roach Warren", "kind": "tech", "health": 850, "minerals": 150, "gas": 100, "trains": ["scf_zerg_roach", "scf_zerg_ravager"]},
    {"id": "scf_zerg_baneling_nest", "name": "Baneling Nest", "kind": "tech", "health": 850, "minerals": 100, "gas": 50},
    {"id": "scf_zerg_hydra_den", "name": "Hydralisk Den", "kind": "tech", "health": 850, "minerals": 150, "gas": 100, "trains": ["scf_zerg_hydralisk", "scf_zerg_lurker"]},
    {"id": "scf_zerg_spire", "name": "Spire", "kind": "tech", "health": 850, "minerals": 200, "gas": 200, "trains": ["scf_zerg_mutalisk", "scf_zerg_corruptor"]},
    {"id": "scf_zerg_infestation_pit", "name": "Infestation Pit", "kind": "tech", "health": 850, "minerals": 100, "gas": 150, "trains": ["scf_zerg_infestor", "scf_zerg_viper"]},
    {"id": "scf_zerg_ultra_cavern", "name": "Ultralisk Cavern", "kind": "tech", "health": 850, "minerals": 150, "gas": 200, "trains": ["scf_zerg_ultralisk"]},
    {"id": "scf_zerg_evolution_chamber", "name": "Evolution Chamber", "kind": "upgrade", "health": 600, "minerals": 75},
    {"id": "scf_zerg_extractor", "name": "Extractor", "kind": "structure", "health": 500, "minerals": 50},
    {"id": "scf_zerg_nydus", "name": "Nydus Network", "kind": "tech", "health": 1000, "minerals": 150, "gas": 200},
    {"id": "scf_zerg_hive", "name": "Hive", "kind": "hq", "health": 2000, "minerals": 400, "gas": 200, "trains": ["scf_zerg_drone", "scf_zerg_queen"]},
    {"id": "scf_zerg_creep_tumor", "name": "Creep Tumor", "kind": "structure", "health": 200, "minerals": 0},
    {"id": "scf_zerg_zergling", "name": "Zergling", "kind": "combat", "health": 350, "speed": 900, "attack": 5, "range": 350},
    {"id": "scf_zerg_baneling", "name": "Baneling", "kind": "combat", "health": 300, "speed": 700, "attack": 20, "range": 300},
    {"id": "scf_zerg_roach", "name": "Roach", "kind": "combat", "health": 750, "speed": 550, "attack": 16, "range": 450},
    {"id": "scf_zerg_ravager", "name": "Ravager", "kind": "combat", "health": 800, "speed": 500, "attack": 18, "range": 550},
    {"id": "scf_zerg_hydralisk", "name": "Hydralisk", "kind": "combat", "health": 550, "speed": 600, "attack": 12, "range": 550},
    {"id": "scf_zerg_lurker", "name": "Lurker", "kind": "combat", "health": 650, "speed": 450, "attack": 20, "range": 650},
    {"id": "scf_zerg_mutalisk", "name": "Mutalisk", "kind": "air", "health": 400, "speed": 850, "attack": 9, "range": 450},
    {"id": "scf_zerg_corruptor", "name": "Corruptor", "kind": "air", "health": 550, "speed": 700, "attack": 14, "range": 500},
    {"id": "scf_zerg_broodlord", "name": "Brood Lord", "kind": "air", "health": 1100, "speed": 450, "attack": 25, "range": 700},
    {"id": "scf_zerg_swarm_host", "name": "Swarm Host", "kind": "combat", "health": 900, "speed": 400, "attack": 15, "range": 600},
    {"id": "scf_zerg_viper", "name": "Viper", "kind": "air", "health": 500, "speed": 650, "attack": 0, "range": 500},
    {"id": "scf_zerg_infestor", "name": "Infestor", "kind": "combat", "health": 400, "speed": 550, "attack": 0, "range": 450},
    {"id": "scf_zerg_ultralisk", "name": "Ultralisk", "kind": "combat", "health": 2000, "speed": 450, "attack": 35, "range": 400},
    {"id": "scf_zerg_queen", "name": "Queen", "kind": "support", "health": 700, "speed": 550, "attack": 8, "range": 500},
    {"id": "scf_zerg_spine_crawler", "name": "Spine Crawler", "kind": "defense", "health": 300, "minerals": 100, "attack": 15, "range": 550},
    {"id": "scf_zerg_spore_crawler", "name": "Spore Crawler", "kind": "defense", "health": 300, "minerals": 75, "attack": 15, "range": 650},
    {"id": "scf_zerg_nydus_worm", "name": "Nydus Worm", "kind": "structure", "health": 500, "minerals": 100, "gas": 100},
    {"id": "scf_zerg_brutalisk", "name": "Brutalisk", "kind": "combat", "health": 1800, "speed": 500, "attack": 28, "range": 450},
]

PROTOSS: list[dict[str, Any]] = [
    {"id": "scf_protoss_probe", "name": "Probe", "kind": "worker", "health": 200, "shield": 200, "minerals": 500, "speed": 600},
    {"id": "scf_protoss_nexus", "name": "Nexus", "kind": "hq", "health": 1000, "shield": 1000, "minerals": 400, "trains": ["scf_protoss_probe", "scf_protoss_zealot"]},
    {"id": "scf_protoss_pylon", "name": "Pylon", "kind": "structure", "health": 200, "shield": 200, "minerals": 100, "supply": 8},
    {"id": "scf_protoss_gateway", "name": "Gateway", "kind": "production", "health": 500, "shield": 500, "minerals": 150, "trains": ["scf_protoss_zealot", "scf_protoss_stalker", "scf_protoss_sentry"]},
    {"id": "scf_protoss_cybernetics", "name": "Cybernetics Core", "kind": "tech", "health": 550, "shield": 550, "minerals": 150},
    {"id": "scf_protoss_robotics", "name": "Robotics Facility", "kind": "production", "health": 500, "shield": 500, "minerals": 200, "gas": 100, "trains": ["scf_protoss_immortal", "scf_protoss_colossus", "scf_protoss_observer"]},
    {"id": "scf_protoss_stargate", "name": "Stargate", "kind": "production", "health": 600, "shield": 600, "minerals": 150, "gas": 150, "trains": ["scf_protoss_phoenix", "scf_protoss_voidray", "scf_protoss_carrier"]},
    {"id": "scf_protoss_forge", "name": "Forge", "kind": "upgrade", "health": 400, "shield": 400, "minerals": 150},
    {"id": "scf_protoss_assimilator", "name": "Assimilator", "kind": "structure", "health": 450, "shield": 450, "minerals": 75},
    {"id": "scf_protoss_twilight", "name": "Twilight Council", "kind": "tech", "health": 650, "shield": 650, "minerals": 150, "gas": 100},
    {"id": "scf_protoss_templar_archives", "name": "Templar Archives", "kind": "tech", "health": 650, "shield": 650, "minerals": 200, "gas": 150, "trains": ["scf_protoss_hightemplar", "scf_protoss_darktemplar"]},
    {"id": "scf_protoss_robo_bay", "name": "Robotics Bay", "kind": "tech", "health": 550, "shield": 550, "minerals": 200, "gas": 150, "trains": ["scf_protoss_disruptor", "scf_protoss_warp_prism"]},
    {"id": "scf_protoss_fleet_beacon", "name": "Fleet Beacon", "kind": "tech", "health": 550, "shield": 550, "minerals": 300, "gas": 200, "trains": ["scf_protoss_tempest", "scf_protoss_mothership"]},
    {"id": "scf_protoss_photon_cannon", "name": "Photon Cannon", "kind": "defense", "health": 300, "shield": 300, "minerals": 150, "attack": 20, "range": 600},
    {"id": "scf_protoss_zealot", "name": "Zealot", "kind": "combat", "health": 500, "shield": 500, "speed": 600, "attack": 8, "range": 350},
    {"id": "scf_protoss_stalker", "name": "Stalker", "kind": "combat", "health": 400, "shield": 400, "speed": 650, "attack": 10, "range": 550},
    {"id": "scf_protoss_sentry", "name": "Sentry", "kind": "support", "health": 300, "shield": 300, "speed": 600, "attack": 6, "range": 500},
    {"id": "scf_protoss_adept", "name": "Adept", "kind": "combat", "health": 350, "shield": 350, "speed": 700, "attack": 7, "range": 450},
    {"id": "scf_protoss_hightemplar", "name": "High Templar", "kind": "combat", "health": 300, "shield": 300, "speed": 550, "attack": 0, "range": 600, "gas": 100},
    {"id": "scf_protoss_darktemplar", "name": "Dark Templar", "kind": "combat", "health": 400, "shield": 400, "speed": 700, "attack": 15, "range": 350},
    {"id": "scf_protoss_archon", "name": "Archon", "kind": "combat", "health": 200, "shield": 800, "speed": 550, "attack": 25, "range": 450},
    {"id": "scf_protoss_immortal", "name": "Immortal", "kind": "combat", "health": 400, "shield": 400, "speed": 450, "attack": 20, "range": 550},
    {"id": "scf_protoss_colossus", "name": "Colossus", "kind": "combat", "health": 600, "shield": 600, "speed": 450, "attack": 15, "range": 650},
    {"id": "scf_protoss_disruptor", "name": "Disruptor", "kind": "combat", "health": 300, "shield": 300, "speed": 500, "attack": 30, "range": 500},
    {"id": "scf_protoss_phoenix", "name": "Phoenix", "kind": "air", "health": 350, "shield": 350, "speed": 800, "attack": 8, "range": 500},
    {"id": "scf_protoss_voidray", "name": "Void Ray", "kind": "air", "health": 500, "shield": 500, "speed": 650, "attack": 12, "range": 550},
    {"id": "scf_protoss_oracle", "name": "Oracle", "kind": "air", "health": 300, "shield": 300, "speed": 750, "attack": 0, "range": 500},
    {"id": "scf_protoss_carrier", "name": "Carrier", "kind": "air", "health": 900, "shield": 900, "speed": 450, "attack": 8, "range": 650},
    {"id": "scf_protoss_tempest", "name": "Tempest", "kind": "air", "health": 600, "shield": 600, "speed": 500, "attack": 22, "range": 800},
    {"id": "scf_protoss_mothership", "name": "Mothership", "kind": "air", "health": 1000, "shield": 1000, "speed": 450, "attack": 6, "range": 600},
    {"id": "scf_protoss_observer", "name": "Observer", "kind": "support", "health": 200, "shield": 200, "speed": 700, "attack": 0, "range": 400},
    {"id": "scf_protoss_warp_prism", "name": "Warp Prism", "kind": "air", "health": 400, "shield": 400, "speed": 650, "attack": 0, "range": 400},
    {"id": "scf_protoss_corsair", "name": "Corsair", "kind": "air", "health": 350, "shield": 350, "speed": 750, "attack": 5, "range": 500},
]

ALL_UNITS = [(1, TERRAN), (2, ZERG), (3, PROTOSS)]
assert sum(len(r) for _, r in ALL_UNITS) == 100, "Expected exactly 100 unit templates"

RACE_COLORS = {1: "#3B82F6", 2: "#A855F7", 3: "#F59E0B"}
RACE_NAMES = {1: "Terran", 2: "Zerg", 3: "Protoss"}
RACE_GRAPH_PROGRAMS = {
    1: "Graph.Scf.Terran.ArmorShred",
    2: "Graph.Scf.Zerg.RegenOnKill",
    3: "Graph.Scf.Protoss.ShieldRecharge",
}
CONSTRUCTION_EFFECT = {
    1: "Effect.Scf.Shared.Construction.Terran",
    2: "Effect.Scf.Shared.Construction.Zerg",
    3: "Effect.Scf.Shared.Construction.Protoss",
}


def slug(unit_id: str) -> str:
    return unit_id.replace("scf_", "").replace("_", ".")


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def base_components(unit: dict, team: int) -> dict:
    attrs: dict[str, float] = {"Health": unit["health"]}
    current_attrs: dict[str, float] = {}
    if "shield" in unit:
        attrs["Shield"] = unit["shield"]
    if "minerals" in unit:
        attrs["Minerals"] = unit["minerals"] + 5000
        current_attrs["Minerals"] = unit["minerals"]
    if "gas" in unit:
        attrs["Gas"] = unit["gas"] + 5000
        current_attrs["Gas"] = unit["gas"]
    if "speed" in unit:
        attrs["MoveSpeed"] = unit["speed"]
    if unit.get("attack", 0) > 0:
        attrs["AttackDamage"] = unit["attack"]
        attrs["AttackRange"] = unit.get("range", 450)
    if unit.get("supply"):
        attrs["Supply"] = unit["supply"]

    attribute_buffer: dict[str, Any] = {"base": attrs}
    if current_attrs:
        attribute_buffer["current"] = current_attrs

    comps: dict = {
        "Name": {"Value": unit["name"]},
        "Team": {"Id": team},
        "CommandSourceSelectableTag": {},
        "CommandSourceSelectableState": {"IsEnabled": True},
        "WorldPositionCm": {"Value": {"X": 0, "Y": 0}},
        "AttributeBuffer": attribute_buffer,
        "GameplayTagContainer": {},
        "TagCountContainer": {},
        "TimedTagBuffer": {},
        "OrderBuffer": {},
        "BlackboardSpatialBuffer": {},
        "BlackboardEntityBuffer": {},
        "BlackboardIntBuffer": {},
    }

    ability_ids = ["Ability.Rts.Strategy.Shared.Hold"] * 4
    trains = unit.get("trains") or []
    for i, train_id in enumerate(trains[:3]):
        ability_ids[i] = f"Ability.Scf.Train.{slug(train_id)}"

    kind = unit["kind"]
    if kind in ("combat", "air", "defense") and unit.get("attack", 0) > 0:
        ability_ids[0] = f"Ability.Scf.Attack.{slug(unit['id'])}"
        ability_ids[1] = "Ability.Scf.Assault.ZergBase" if team == 1 else "Ability.Scf.Combat.AttackMove"
    elif kind == "worker":
        ability_ids[0] = "Ability.Scf.Harvest.Minerals"
        ability_ids[1] = "Ability.Scf.Combat.AttackMove"

    if kind in ("hq", "production", "tech") and trains:
        comps["AbilityFormSetRef"] = {"formSetId": f"{unit['id']}_forms"}

    comps["AbilityStateBuffer"] = {"abilityIds": ability_ids}
    return comps


def generate_templates() -> list:
    templates = []
    for team, roster in ALL_UNITS:
        for unit in roster:
            entry: dict[str, Any] = {"id": unit["id"], "components": base_components(unit, team)}
            if unit.get("attack", 0) > 0:
                entry["onSpawnEffect"] = f"Effect.Scf.AutoAttack.{slug(unit['id'])}"
            templates.append(entry)
    return templates


def generate_effects() -> list:
    effects: list = [
        {
            "id": "Effect.Scf.Shared.Construction.Terran",
            "tags": ["Effect.Scf.Construction"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 60, "periodTicks": 0, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "State.Rts.Constructing", "formula": "Fixed", "amount": 1}],
        },
        {
            "id": "Effect.Scf.Shared.Construction.Zerg",
            "tags": ["Effect.Scf.Construction"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 90, "periodTicks": 0, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "State.Rts.Constructing", "formula": "Fixed", "amount": 1}],
        },
        {
            "id": "Effect.Scf.Shared.Construction.Protoss",
            "tags": ["Effect.Scf.Construction"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "State.Rts.Constructing", "formula": "Fixed", "amount": 1}],
        },
        {
            "id": "Effect.Scf.Shared.SpawnBootstrap",
            "tags": ["Effect.Scf.Spawn"],
            "presetType": "Buff",
            "lifetime": "Infinite",
            "participatesInResponse": False,
            "grantedTags": [{"tag": "Unit.Scf.Bootstrapped", "formula": "Fixed", "amount": 1}],
        },
        {
            "id": "Effect.Scf.Mining.Tick",
            "tags": ["Effect.Scf.Mining"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "Minerals", "op": "Add", "value": 50}],
        },
        {
            "id": "Effect.Scf.Mining.Minerals",
            "tags": ["Effect.Scf.Mining"],
            "presetType": "Buff",
            "lifetime": "Infinite",
            "participatesInResponse": False,
            "duration": {"durationTicks": 0, "periodTicks": 60, "clockId": "FixedFrame"},
            "grantedTags": [{"tag": "Status.Scf.Mining", "formula": "Fixed", "amount": 1}],
            "phaseGraphs": {
                "OnApply": {"post": "Graph.Scf.Mining.Minerals"},
                "OnPeriod": {"post": "Graph.Scf.Mining.Minerals"},
            },
        },
        {
            "id": "Effect.Scf.Assault.Signal",
            "tags": ["Effect.Scf.Assault"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "AssaultSignal", "op": "Add", "value": 1}],
        },
    ]

    units_by_id = {unit["id"]: unit for _, roster in ALL_UNITS for unit in roster}
    emitted_train_effects: set[str] = set()

    for team, roster in ALL_UNITS:
        for unit in roster:
            uid = unit["id"]
            s = slug(uid)

            # Train effect
            if unit.get("trains"):
                for train_id in unit["trains"]:
                    ts = slug(train_id)
                    cost_id = f"Effect.Scf.Cost.Train.{ts}"
                    train_effect_id = f"Effect.Scf.Train.{ts}"
                    if train_effect_id in emitted_train_effects:
                        continue

                    emitted_train_effects.add(train_effect_id)
                    train_unit = units_by_id[train_id]
                    mineral_cost = max(25, int(train_unit.get("minerals", 50) * 0.05))
                    gas_cost = train_unit.get("gas", 0)
                    effects.append({
                        "id": cost_id,
                        "tags": ["Effect.Scf.Cost"],
                        "presetType": "InstantDamage",
                        "lifetime": "Instant",
                        "participatesInResponse": False,
                        "modifiers": [
                            {"attribute": "Minerals", "op": "Add", "value": -mineral_cost},
                            *([{"attribute": "Gas", "op": "Add", "value": -gas_cost}] if gas_cost else []),
                        ],
                    })
                    effects.append({
                        "id": train_effect_id,
                        "tags": ["Effect.Scf.Train"],
                        "presetType": "CreateUnit",
                        "lifetime": "Instant",
                        "participatesInResponse": False,
                        "unitCreation": {
                            "templateId": train_id,
                            "placementPattern": "Scatter",
                            "count": 1,
                            "offsetRadius": 280,
                            "copySourcePlayerOwner": True,
                        },
                    })

            # Combat damage + auto attack aura
            attack = unit.get("attack", 0)
            if attack > 0:
                hit_id = f"Effect.Scf.Damage.{s}"
                modifiers = [{"attribute": "Health", "op": "Add", "value": -attack}]
                if team == 3:
                    modifiers.insert(0, {"attribute": "Shield", "op": "Add", "value": -attack})
                effects.append({
                    "id": hit_id,
                    "tags": ["Effect.Scf.Damage"],
                    "presetType": "InstantDamage",
                    "lifetime": "Instant",
                    "participatesInResponse": True,
                    "modifiers": modifiers,
                    "phaseGraphs": {"OnApply": {"post": RACE_GRAPH_PROGRAMS[team]}},
                })
                radius = unit.get("range", 450)
                effects.append({
                    "id": f"Effect.Scf.AutoAttack.{s}",
                    "tags": ["Effect.Scf.AutoAttack"],
                    "presetType": "PeriodicSearch",
                    "lifetime": "Infinite",
                    "participatesInResponse": False,
                    "duration": {"durationTicks": 999999, "periodTicks": 20, "clockId": "FixedFrame"},
                    "targetQuery": {"kind": "BuiltinSpatial", "shape": "Circle", "radius": radius},
                    "targetFilter": {"relationFilter": "Hostile", "excludeSource": True, "maxTargets": 1},
                    "targetDispatch": {"payloadEffect": hit_id},
                })

    # Graph helper effects
    effects.extend([
        {
            "id": "Effect.Scf.Graph.ZergRegen",
            "tags": ["Effect.Scf.Graph"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "Health", "op": "Add", "value": 15}],
        },
        {
            "id": "Effect.Scf.Graph.ArmorShred",
            "tags": ["Effect.Scf.Graph"],
            "presetType": "Buff",
            "lifetime": "After",
            "participatesInResponse": False,
            "duration": {"durationTicks": 120, "periodTicks": 0, "clockId": "FixedFrame"},
            "modifiers": [{"attribute": "AttackDamage", "op": "Add", "value": -2}],
        },
        {
            "id": "Effect.Scf.Graph.ShieldRecharge",
            "tags": ["Effect.Scf.Graph"],
            "presetType": "InstantDamage",
            "lifetime": "Instant",
            "participatesInResponse": False,
            "modifiers": [{"attribute": "Shield", "op": "Add", "value": 25}],
        },
    ])

    # Item passive effects
    for race, items in [
        ("Terran", ["MarineRange", "TankArmor", "MedivacSpeed", "StimPack"]),
        ("Zerg", ["ZerglingSpeed", "RoachArmor", "HydraRange", "MetabolicBoost"]),
        ("Protoss", ["ZealotCharge", "StalkerBlink", "ShieldBoost", "GraviticDrive"]),
    ]:
        for item in items:
            effects.append({
                "id": f"Effect.Scf.Item.{race}.{item}",
                "tags": ["Effect.Scf.ItemPassive"],
                "presetType": "Buff",
                "lifetime": "Infinite",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "AttackDamage", "op": "Add", "value": 3}],
            })

    return effects


def generate_abilities() -> list:
    abilities = [
        {
            "id": "Ability.Scf.Combat.AttackMove",
            "exec": {"clockId": "FixedFrame", "items": [{"kind": "End", "tick": 0}]},
            "presentation": {
                "displayName": "Attack Move",
                "iconGlyph": "A",
                "accentColor": "#EF4444",
                "hintText": "Move while engaging hostiles.",
            },
        },
        {
            "id": "Ability.Scf.Harvest.Minerals",
            "exec": {
                "clockId": "FixedFrame",
                "items": [
                    {"kind": "EffectSignal", "tick": 0, "template": "Effect.Scf.Mining.Minerals", "dispatchTarget": "Source"},
                    {"kind": "End", "tick": 0},
                ],
            },
            "blockTags": {"blockedAny": ["Status.Scf.Mining"]},
            "presentation": {
                "displayName": "Harvest Minerals",
                "iconGlyph": "MIN",
                "accentColor": "#22C55E",
                "hintText": "Start a mineral mining loop through a periodic GAS effect.",
            },
        },
        {
            "id": "Ability.Scf.Assault.ZergBase",
            "exec": {
                "clockId": "FixedFrame",
                "items": [
                    {"kind": "EffectSignal", "tick": 0, "template": "Effect.Scf.Assault.Signal", "dispatchTarget": "Source"},
                    {"kind": "End", "tick": 0},
                ],
            },
            "presentation": {
                "displayName": "Assault Hatchery",
                "iconGlyph": "ATK",
                "accentColor": "#EF4444",
                "hintText": "Commit the Terran force to attack the Zerg Hatchery.",
            },
        },
    ]

    units_by_id = {unit["id"]: unit for _, roster in ALL_UNITS for unit in roster}
    emitted_train_abilities: set[str] = set()

    for team, roster in ALL_UNITS:
        for unit in roster:
            uid = unit["id"]
            s = slug(uid)
            attack = unit.get("attack", 0)

            if attack > 0:
                abilities.append({
                    "id": f"Ability.Scf.Attack.{s}",
                    "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                            {"kind": "EffectSignal", "tick": 0, "template": f"Effect.Scf.Damage.{s}"},
                            {"kind": "End", "tick": 0},
                        ],
                    },
                    "presentation": {
                        "displayName": f"Attack ({unit['name']})",
                        "iconGlyph": "ATK",
                        "accentColor": RACE_COLORS[team],
                        "hintText": f"Deal {attack} damage to target.",
                    },
                    "targeting": {"castRangeCm": unit.get("range", 450), "impactEffect": f"Effect.Scf.Damage.{s}"},
                })

            if unit.get("trains"):
                for train_id in unit["trains"]:
                    ts = slug(train_id)
                    ability_id = f"Ability.Scf.Train.{ts}"
                    if ability_id in emitted_train_abilities:
                        continue

                    emitted_train_abilities.add(ability_id)
                    train_name = units_by_id[train_id]["name"]
                    abilities.append({
                        "id": ability_id,
                        "exec": {
                            "clockId": "FixedFrame",
                            "items": [
                                {"kind": "TagClip", "tick": 0, "duration": 90, "tag": "Status.Rts.Training"},
                                {"kind": "EffectSignal", "tick": 0, "template": f"Effect.Scf.Cost.Train.{ts}"},
                                {"kind": "EffectSignal", "tick": 90, "template": f"Effect.Scf.Train.{ts}", "dispatchTarget": "Source"},
                                {"kind": "End", "tick": 90},
                            ],
                        },
                        "blockTags": {"blockedAny": ["Status.Rts.Training", "State.Rts.Constructing"]},
                        "presentation": {
                            "displayName": f"Train {train_name}",
                            "iconGlyph": "TR",
                            "accentColor": RACE_COLORS[team],
                            "hintText": f"Produce {train_name} from this structure.",
                        },
                    })

    return abilities


def generate_form_sets() -> list:
    form_sets = []
    for team, roster in ALL_UNITS:
        for unit in roster:
            trains = unit.get("trains")
            if not trains:
                continue
            uid = unit["id"]
            overrides = [{"slotIndex": i, "abilityId": f"Ability.Scf.Train.{slug(trains[i])}"} for i in range(min(3, len(trains)))]
            routes = [{"priority": 10, "slotOverrides": overrides}]
            if len(trains) > 3:
                routes.append({
                    "priority": 20,
                    "requiredAll": ["Progression.Scf.ExpandedProduction"],
                    "slotOverrides": [{"slotIndex": 3, "abilityId": f"Ability.Scf.Train.{slug(trains[3])}"}],
                })
            form_sets.append({"id": f"{uid}_forms", "routes": routes})
    return form_sets


def generate_graphs() -> list:
    return [
        {
            "id": "Graph.Scf.Mining.Minerals",
            "kind": "Effect",
            "entry": "target",
            "nodes": [
                {"id": "target", "op": "LoadContextTarget", "next": "income"},
                {"id": "income", "op": "ApplyEffectTemplate", "effectTemplate": "Effect.Scf.Mining.Tick", "inputs": ["target"]},
            ],
        },
        {
            "id": "Graph.Scf.Zerg.RegenOnKill",
            "kind": "Effect",
            "entry": "source",
            "nodes": [
                {"id": "source", "op": "LoadContextSource", "next": "heal"},
                {"id": "heal", "op": "ApplyEffectTemplate", "effectTemplate": "Effect.Scf.Graph.ZergRegen", "inputs": ["source"]},
            ],
        },
        {
            "id": "Graph.Scf.Terran.ArmorShred",
            "kind": "Effect",
            "entry": "target",
            "nodes": [
                {"id": "target", "op": "LoadContextTarget", "next": "shred"},
                {"id": "shred", "op": "ApplyEffectTemplate", "effectTemplate": "Effect.Scf.Graph.ArmorShred", "inputs": ["target"]},
            ],
        },
        {
            "id": "Graph.Scf.Protoss.ShieldRecharge",
            "kind": "Effect",
            "entry": "target",
            "nodes": [
                {"id": "target", "op": "LoadContextTarget", "next": "recharge"},
                {"id": "recharge", "op": "ApplyEffectTemplate", "effectTemplate": "Effect.Scf.Graph.ShieldRecharge", "inputs": ["target"]},
            ],
        },
    ]


def generate_items() -> tuple[list, list, list]:
    shapes = [
        {"id": "shape_scf_1x1", "rows": ["X"], "rotatable": False},
        {"id": "shape_scf_1x2", "rows": ["X", "X"], "rotatable": True},
    ]
    layouts = [
        {
            "id": "layout_scf_armory",
            "purpose": "Equipment",
            "width": 4,
            "height": 4,
            "grantsEquipmentBonuses": True,
            "namedSlots": [
                {"id": "upgrade", "label": "Upgrade", "requiredAll": ["Item.Scf.Upgrade"]},
            ],
        },
    ]
    definitions = []
    for race, items in [
        ("Terran", ["MarineRange", "TankArmor", "MedivacSpeed", "StimPack"]),
        ("Zerg", ["ZerglingSpeed", "RoachArmor", "HydraRange", "MetabolicBoost"]),
        ("Protoss", ["ZealotCharge", "StalkerBlink", "ShieldBoost", "GraviticDrive"]),
    ]:
        for item in items:
            definitions.append({
                "id": f"itm_scf_{race.lower()}_{item.lower()}",
                "displayName": f"{race} {item}",
                "shape": "shape_scf_1x1",
                "tags": [f"Race.{race}", "Item.Scf.Upgrade"],
                "allowedNamedSlots": ["upgrade"],
                "equipEffects": [f"Effect.Scf.Item.{race}.{item}"],
            })
    return shapes, layouts, definitions


def hex_to_rgba(hex_color: str, alpha: float = 1.0) -> list[float]:
    value = hex_color.lstrip("#")
    r = int(value[0:2], 16) / 255.0
    g = int(value[2:4], 16) / 255.0
    b = int(value[4:6], 16) / 255.0
    return [round(r, 4), round(g, 4), round(b, 4), alpha]


def generate_presenters() -> list:
    presenters = []
    for team, roster in ALL_UNITS:
        color = RACE_COLORS[team]
        rgba = hex_to_rgba(color)
        for unit in roster:
            uid = unit["id"]
            kind = unit["kind"]
            size = 1.2 if kind in ("combat", "air", "worker", "support") else 2.2
            height = 0.8 if kind in ("combat", "air", "worker", "support") else 1.8
            mesh = f"scf.prim.{uid}"
            visual_id = f"scf.visual.{uid}"

            presenters.append({
                "id": visual_id,
                "defaultColor": rgba,
                "behaviors": [
                    {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": True,
                        "assetBinding": {
                            "assetKind": "Mesh",
                            "assetId": mesh,
                            "materialId": "default_surface",
                            "renderPath": "InstancedStaticMesh",
                            "mobility": "Movable",
                            "localScale": [size, height, size],
                        },
                    },
                ],
                "rules": [
                    {
                        "event": {"kind": "EntitySpawned", "key": uid},
                        "command": {
                            "kind": "CreatePresenter",
                            "scopeSource": "EventPayloadA",
                            "definitionId": visual_id,
                        },
                        "condition": {"inline": "SourceHasVisualTransform"},
                    },
                    {
                        "event": {"kind": "EntityDestroyed", "key": uid},
                        "command": {
                            "kind": "DestroyPresenterScope",
                            "scopeSource": "EventPayloadA",
                        },
                    },
                ],
            })
    return presenters


def generate_mesh_assets() -> list:
    assets = []
    for _, roster in ALL_UNITS:
        for unit in roster:
            primitive_kind = "Sphere" if unit["kind"] == "air" else "Cube"
            assets.append({
                "id": f"scf.prim.{unit['id']}",
                "type": "Primitive",
                "primitiveKind": primitive_kind,
            })
    return assets


def generate_map() -> dict:
    entities = []
    positions = {
        1: (5200, 5200),
        2: (5200, 9800),
        3: (9800, 5200),
    }
    idx = 0
    for team, roster in ALL_UNITS:
        bx, by = positions[team]
        for i, unit in enumerate(roster):
            ring = i // 12
            radius = 650 + ring * 520
            angle = (i % 12) * 30 + ring * 8
            rad = math.radians(angle)
            x = int(bx + math.cos(rad) * radius)
            y = int(by + math.sin(rad) * radius)
            entities.append({
                "Template": unit["id"],
                "InstanceId": f"map_{unit['id']}_{idx}",
                "Overrides": {
                    "WorldPositionCm": {"Value": {"X": x, "Y": y}},
                    "Team": {"Id": team},
                    "Name": {"Value": unit["name"]},
                },
            })
            idx += 1

    instance_by_template = {entity["Template"]: entity["InstanceId"] for entity in entities}

    return {
        "Id": "rts_starcraft_full",
        "Tags": ["rts", "rts_showcase", "rts_production", "starcraft_full", "sc2", "scf"],
        "DefaultCamera": {
            "VirtualCameraId": "Rts",
            "TargetXCm": 7200,
            "TargetYCm": 7200,
            "Yaw": 180,
            "Pitch": 48,
            "DistanceCm": 12000,
            "FovYDeg": 60,
        },
        "Boards": [{"Name": "default", "SpatialType": "HexGrid", "DataFile": "sc2_highlands.vtxm"}],
        "Entities": entities,
        "Teams": [
            {"TeamId": 1, "RepresentativeInstanceId": instance_by_template["scf_terran_command_center"]},
            {"TeamId": 2, "RepresentativeInstanceId": instance_by_template["scf_zerg_hatchery"]},
            {"TeamId": 3, "RepresentativeInstanceId": instance_by_template["scf_protoss_nexus"]},
        ],
        "Players": [
            {"PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": instance_by_template["scf_terran_scv"]},
            {"PlayerId": 2, "TeamId": 2, "RepresentativeInstanceId": instance_by_template["scf_zerg_drone"]},
            {"PlayerId": 3, "TeamId": 3, "RepresentativeInstanceId": instance_by_template["scf_protoss_probe"]},
        ],
    }


def main() -> None:
    write_json(ASSETS / "Entities/templates.json", generate_templates())
    write_json(ASSETS / "GAS/effects.json", generate_effects())
    write_json(ASSETS / "GAS/abilities.json", generate_abilities())
    write_json(ASSETS / "GAS/ability_form_sets.json", generate_form_sets())
    write_json(ASSETS / "GAS/graphs.json", generate_graphs())
    shapes, layouts, definitions = generate_items()
    write_json(ASSETS / "Items/shapes.json", shapes)
    write_json(ASSETS / "Items/layouts.json", layouts)
    write_json(ASSETS / "Items/definitions.json", definitions)
    write_json(ASSETS / "Presentation/presenters.json", generate_presenters())
    write_json(ASSETS / "Presentation/mesh_assets.json", generate_mesh_assets())
    write_json(ASSETS / "Presentation/host_assets.json", [])
    write_json(ASSETS / "Maps/rts_starcraft_full.json", generate_map())
    print(f"Generated content for 100 units into {ASSETS}")


if __name__ == "__main__":
    main()
