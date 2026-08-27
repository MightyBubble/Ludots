#!/usr/bin/env python3
"""Write Ability feature gallery authored assets from the catalog SSOT."""
from __future__ import annotations

import json
import sys
from copy import deepcopy
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from ability_feature_catalog import (  # noqa: E402
    CASTER_COMPONENTS,
    DUMMY_COMPONENTS,
    FEATURES,
    GALLERY_REL,
)


def dump(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_vignettes(gallery: Path) -> None:
    live = {feature["feature"] for feature in FEATURES}
    vignette_dir = gallery / "assets" / "Vignettes"
    if vignette_dir.is_dir():
        for stale in vignette_dir.glob("*.json"):
            if stale.stem not in live:
                stale.unlink()
    for feature in FEATURES:
        vignette = {
            "feature": feature["feature"],
            "family": feature["family"],
            "title": feature["title"],
            "beat": feature["beat"],
            "detailTemplate": feature["detailTemplate"],
            "assertDetailContains": feature["assertDetailContains"],
            "abilityId": feature["abilityId"],
            "script": feature["script"],
            "expect": feature["expect"],
        }
        for key in (
            "extraActors",
            "needsProgression",
            "casterAbilities",
            "formSetId",
            "companionAbilityIds",
        ):
            if key in feature:
                vignette[key] = feature[key]
        dump(gallery / "assets" / "Vignettes" / f"{feature['feature']}.json", vignette)


def write_abilities(gallery: Path) -> None:
    seen: dict[str, dict] = {}
    for feature in FEATURES:
        for raw in [feature["ability"], *feature.get("companionAbilities", [])]:
            aid = raw["id"]
            if aid in seen and seen[aid] != raw:
                raise SystemExit(f"Ability '{aid}' authored twice with different bodies.")
            seen[aid] = raw
            dump(gallery / "assets" / "GAS" / "abilities" / f"{aid.split('.')[-1]}.json", [raw])


def write_shared(gallery: Path) -> None:
    dump(
        gallery / "assets" / "GAS" / "effects.json",
        [
            {
                "id": "Effect.AbilityFeature.Strike",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -25}],
                "categories": ["Effect.AbilityFeature.Strike"],
            },
            {
                "id": "Effect.AbilityFeature.SelfStrike",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -20}],
                "categories": ["Effect.AbilityFeature.SelfStrike"],
            },
            {
                "id": "Effect.AbilityFeature.ToggleClose",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -12}],
                "categories": ["Effect.AbilityFeature.ToggleClose"],
            },
            {
                "id": "Effect.AbilityFeature.TriggerExtra",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -15}],
                "categories": ["Effect.AbilityFeature.TriggerExtra"],
            },
            {
                "id": "Effect.AbilityFeature.Hammer",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -35}],
                "categories": ["Effect.AbilityFeature.Hammer"],
            },
            {
                "id": "Effect.AbilityFeature.Wave",
                "presetType": "InstantDamage",
                "lifetime": "Instant",
                "participatesInResponse": False,
                "configParams": {
                    "abilityfeature.wave.damage": {"type": "Float", "value": 10}
                },
                "phaseGraphs": {"OnApply": {"main": "Graph.AbilityFeature.WaveHit"}},
                "categories": ["Effect.AbilityFeature.Wave"],
            },
            {
                "id": "Effect.AbilityFeature.Burn",
                "presetType": "Buff",
                "lifetime": "After",
                "participatesInResponse": False,
                "duration": {"durationTicks": 36, "periodTicks": 12, "clockId": "FixedFrame"},
                "modifiers": [{"attribute": "Health", "op": "Add", "value": -8}],
                "grantedTags": [
                    {"tag": "Status.AbilityFeature.Burning", "formula": "Fixed", "amount": 1}
                ],
                "categories": ["Effect.AbilityFeature.Burn"],
            },
        ],
    )
    dump(
        gallery / "assets" / "GAS" / "graphs.json",
        [
            {
                "id": "Graph.AbilityFeature.WaveHit",
                "kind": "Effect",
                "entry": "cfg",
                "nodes": [
                    {"id": "cfg", "op": "LoadConfigFloat", "configKey": "abilityfeature.wave.damage"},
                    {"id": "neg", "op": "NegFloat"},
                    {"id": "explicit", "op": "LoadExplicitTarget"},
                    {"id": "hit", "op": "ModifyAttributeAdd", "attribute": "Health"},
                ],
                "controlEdges": [
                    {"from": "cfg", "fromPort": "next", "to": "neg"},
                    {"from": "neg", "fromPort": "next", "to": "explicit"},
                    {"from": "explicit", "fromPort": "next", "to": "hit"},
                ],
                "valueEdges": [
                    {"from": "cfg", "fromPort": "value", "to": "neg", "toPort": "value"},
                    {"from": "neg", "fromPort": "value", "to": "hit", "toPort": "value"},
                    {"from": "explicit", "fromPort": "value", "to": "hit", "toPort": "target"},
                ],
            },
            {
                "id": "Graph.AbilityFeature.WoundedOnly",
                "kind": "Validation",
                "entry": "target",
                "nodes": [
                    {"id": "target", "op": "LoadContextTarget"},
                    {"id": "hp", "op": "LoadAttribute", "attribute": "Health"},
                    {"id": "line", "op": "ConstFloat", "floatValue": 50},
                    {"id": "wounded", "op": "CompareGtFloat"},
                ],
                "controlEdges": [
                    {"from": "target", "fromPort": "next", "to": "hp"},
                    {"from": "hp", "fromPort": "next", "to": "line"},
                    {"from": "line", "fromPort": "next", "to": "wounded"},
                ],
                "valueEdges": [
                    {"from": "target", "fromPort": "value", "to": "hp", "toPort": "source"},
                    {"from": "line", "fromPort": "value", "to": "wounded", "toPort": "a"},
                    {"from": "hp", "fromPort": "value", "to": "wounded", "toPort": "b"},
                ],
            },
            {
                "id": "Graph.AbilityFeature.OnCast",
                "kind": "TriggerGraph",
                "entries": [
                    {
                        "label": "on_cast",
                        "event": "Ability.CastStarted",
                        "start": "target",
                        "refire": "restart",
                    }
                ],
                "nodes": [
                    {"id": "target", "op": "LoadEntryPayloadEntity", "payloadKey": "MapTrigger.TargetEntity"},
                    {
                        "id": "fire",
                        "op": "DispatchMapEvent",
                        "event": "Event.AbilityFeature.GraphRan",
                        "scope": "map",
                    },
                ],
                "controlEdges": [
                    {"from": "target", "fromPort": "next", "to": "fire"},
                ],
                "valueEdges": [],
            },
        ],
    )
    dump(
        gallery / "assets" / "GAS" / "ability_form_sets.json",
        [
            {
                "id": "ability_feature_hammer_forms",
                "routes": [
                    {
                        "requiredAll": ["State.AbilityFeature.Hammer"],
                        "priority": 100,
                        "slotOverrides": [
                            {"slotIndex": 0, "abilityId": "Ability.AbilityFeature.FormHammer"}
                        ],
                    }
                ],
            }
        ],
    )
    dump(
        gallery / "assets" / "Events" / "custom_events.json",
        [
            {"id": "Event.AbilityFeature.GraphRan", "scope": "map", "params": []},
        ],
    )
    dump(gallery / "assets" / "Progression" / "scopes.json", [{"id": "abilityfeature.self"}])
    dump(
        gallery / "assets" / "Progression" / "progressions.json",
        [{"id": "Progression.AbilityFeature.Unlock", "scope": "abilityfeature.self"}],
    )
    dump(
        gallery / "assets" / "Progression" / "requirements.json",
        [
            {
                "id": "Req.AbilityFeature.Unlock",
                "root": {
                    "kind": "ProgressionCompleted",
                    "progression": "Progression.AbilityFeature.Unlock",
                    "scope": "abilityfeature.self",
                    "entitySource": "ScopeHost",
                },
            }
        ],
    )

    templates = [
        {
            "id": "AbilityFeature.Caster",
            "components": {
                **deepcopy(CASTER_COMPONENTS),
                "AbilityStateBuffer": {"abilityIds": ["Ability.AbilityFeature.EffectSignal"]},
            },
        },
        {
            "id": "AbilityFeature.Dummy",
            "components": deepcopy(DUMMY_COMPONENTS),
        },
        {
            "id": "AbilityFeature.Wounded",
            "components": {
                **deepcopy(DUMMY_COMPONENTS),
                "Name": {"Value": "残血木桩"},
                "AttributeBuffer": {"base": {"Health": 100}, "current": {"Health": 30}},
            },
        },
        {
            "id": "AbilityFeature.UnlockBoard",
            "components": {
                "Name": {"Value": "解锁牌"},
                "Team": {"Id": 1},
                "WorldPositionCm": {"Value": {"X": 4400, "Y": 5000}},
                "GameplayTagContainer": {},
                "TagCountContainer": {},
                "ProgressionStateBuffer": {},
                "ProgressionScopeHost": {"scope": "abilityfeature.self", "hostKey": "ability_feature_board"},
            },
        },
    ]
    dump(gallery / "assets" / "Entities" / "templates.json", templates)

    dump(
        gallery / "assets" / "config_catalog.json",
        [
            {"Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "Presentation/presenters.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "GAS/graphs.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "GAS/ability_form_sets.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "Events/custom_events.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "Progression/scopes.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "Progression/progressions.json", "Policy": "ArrayById", "IdField": "id"},
            {"Path": "Progression/requirements.json", "Policy": "ArrayById", "IdField": "id"},
        ],
    )
    dump(
        gallery / "assets" / "game.json",
        {
            "windowTitle": "Ludots - 技能词条画廊",
            "targetFps": 60,
            "windowWidth": 1600,
            "windowHeight": 900,
            "windowResizable": True,
            "startupLocalSeats": [{"seatId": "seat.0", "playerId": 1}],
        },
    )

    color_caster = [1, 0.82, 0.2, 1]
    color_dummy = [0.95, 0.28, 0.24, 1]
    color_wounded = [0.35, 0.9, 0.72, 1]
    presenters = [
        {
            "id": "abilityfeature.hud.health_bar",
            "extends": "entity_health_bar",
            "behaviors": [
                {
                    "slot": "body",
                    "kind": "AssetBinding",
                    "activeByDefault": True,
                    "assetBinding": {
                        "assetKind": "WorldHud",
                        "renderPath": "None",
                        "mobility": "Movable",
                        "localScale": [90, 12, 1],
                        "materialParamKey": "worldBar.fillRatio",
                    },
                    "style": {"color": [0.12, 0.92, 0.28, 1]},
                },
                {
                    "slot": "attachment",
                    "kind": "Attachment",
                    "activeByDefault": True,
                    "attachment": {
                        "target": "Parent",
                        "offset": [0, 2.35, 0],
                        "rotationOffset": [0, 0, 0, 1],
                        "inheritScale": False,
                    },
                },
            ],
            "anchor": {"offset": [0, 0, 0]},
        },
        {
            "id": "abilityfeature.hud.health_text",
            "extends": "entity_world_text",
            "behaviors": [
                {
                    "slot": "body",
                    "kind": "WorldText",
                    "activeByDefault": True,
                    "worldText": {
                        "textToken": "hud.attribute.current_over_base",
                        "mode": "AttributeCurrentOverBase",
                        "fontSize": 18,
                        "valueParamKey": "worldText.value0",
                        "secondaryValueParamKey": "worldText.value1",
                    },
                    "style": {"color": [1, 0.98, 0.94, 1]},
                }
            ],
            "anchor": {"offset": [0, 2.65, 0]},
        },
    ]
    for tid, color, scale in (
        ("AbilityFeature.Caster", color_caster, [1.05, 2.2, 1.05]),
        ("AbilityFeature.Dummy", color_dummy, [0.9, 1.95, 0.9]),
        ("AbilityFeature.Wounded", color_wounded, [0.85, 1.7, 0.85]),
        ("AbilityFeature.UnlockBoard", [0.6, 0.55, 0.2, 1], [0.7, 1.2, 0.7]),
    ):
        presenters.append(
            {
                "id": f"abilityfeature.visual.{tid.split('.')[-1].lower()}",
                "children": [
                    {"definitionId": "abilityfeature.hud.health_bar", "scopeTag": "hud"},
                    {"definitionId": "abilityfeature.hud.health_text", "scopeTag": "hud-text"},
                ],
                "behaviors": [
                    {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": True,
                        "assetBinding": {
                            "assetKind": "Mesh",
                            "assetId": "cube",
                            "materialId": "default_surface",
                            "renderPath": "InstancedStaticMesh",
                            "mobility": "Movable",
                            "localScale": scale,
                        },
                        "style": {"color": color},
                    }
                ],
                "rules": [
                    {
                        "event": {"kind": "EntitySpawned", "key": tid},
                        "condition": {"inline": "SourceHasVisualTransform"},
                        "command": {
                            "kind": "CreatePresenter",
                            "scopeSource": "EventPayloadA",
                            "definitionId": f"abilityfeature.visual.{tid.split('.')[-1].lower()}",
                        },
                    },
                    {
                        "event": {"kind": "EntityDestroyed", "key": tid},
                        "command": {"kind": "DestroyPresenterScope", "scopeSource": "EventPayloadA"},
                    },
                ],
            }
        )
    dump(gallery / "assets" / "Presentation" / "presenters.json", presenters)


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    gallery = repo / GALLERY_REL
    write_vignettes(gallery)
    write_abilities(gallery)
    write_shared(gallery)
    print(f"Wrote {len(FEATURES)} ability feature vignettes under {GALLERY_REL}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
