"""Ability feature catalog SSOT used by gallery generators.

Player titles stay Chinese. Feature ids match Vignette file stems.
"""

from __future__ import annotations

PREFIX = "capability_standard_ability_feature_"
GALLERY_REL = "mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod"
ENTRY_ROOT_REL = "mods/showcases/capability_standard/ability_feature_entries"
WIKI_REL = "gitbook/reference/ability-feature-wiki"
COVERAGE_REL = "assets/GAS/ability_feature_coverage.registry.json"
ACCEPTANCE = "AbilityFeatureGalleryAcceptanceTests"
HANDBOOK = {
    "timeline": ("ab-02-exec-timeline.md", "执行时间轴 · ab-02"),
    "params": ("ab-03-caller-params.md", "CallerParams 参数池 · ab-03"),
    "gates": ("ab-05-activation-gates.md", "激活门 · ab-05"),
    "toggle": ("ab-08-toggle.md", "开关 · ab-08"),
    "dispatch": ("ab-02-exec-timeline.md", "执行时间轴 · ab-02"),
    "trigger": ("ab-01-definition.md", "技能定义骨架 · ab-01"),
    "form": ("ab-07-form-sets.md", "形态路由 · ab-07"),
    "progress": ("ab-05-activation-gates.md", "激活门 · ab-05"),
}

FAMILY_LABELS = {
    "timeline": "时间轴动词",
    "params": "同一招换数组",
    "gates": "放不放得出去",
    "toggle": "开关",
    "dispatch": "打在谁身上",
    "trigger": "技能自带的图",
    "form": "换姿态换栏",
    "progress": "解锁才能用",
}

STRIKE = "Effect.AbilityFeature.Strike"
WAVE = "Effect.AbilityFeature.Wave"
BURN = "Effect.AbilityFeature.Burn"
SELF = "Effect.AbilityFeature.SelfStrike"
CLOSE = "Effect.AbilityFeature.ToggleClose"
EXTRA = "Effect.AbilityFeature.TriggerExtra"
HAMMER = "Effect.AbilityFeature.Hammer"

CASTER_COMPONENTS = {
    "Name": {"Value": "施法者"},
    "Team": {"Id": 1},
    "PlayerOwner": {"PlayerId": 1},
    "WorldPositionCm": {"Value": {"X": 5000, "Y": 5000}},
    "AttributeBuffer": {"base": {"Health": 100}, "current": {"Health": 100}},
    "AbilityStateBuffer": {"abilityIds": []},
    "GameplayTagContainer": {},
    "TagCountContainer": {},
    "TimedTagBuffer": {},
    "OrderBuffer": {},
    "BlackboardSpatialBuffer": {},
    "BlackboardEntityBuffer": {},
    "BlackboardIntBuffer": {},
}

DUMMY_COMPONENTS = {
    "Name": {"Value": "木桩"},
    "Team": {"Id": 2},
    "WorldPositionCm": {"Value": {"X": 5600, "Y": 5000}},
    "AttributeBuffer": {"base": {"Health": 100}, "current": {"Health": 100}},
    "GameplayTagContainer": {},
    "TagCountContainer": {},
    "TimedTagBuffer": {},
}


def ability(aid: str, exec_items: list, **extra) -> dict:
    body = {"id": aid, "exec": {"clockId": "FixedFrame", "items": exec_items}, **extra}
    return body


def end(tick: int = 0) -> dict:
    return {"kind": "End", "tick": tick}


FEATURES: list[dict] = [
    {
        "feature": "EffectSignal",
        "family": "timeline",
        "title": "点一下就打中",
        "beat": "施法者对着木桩出手，木桩血条马上掉一截。",
        "detailTemplate": "木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["掉到"],
        "abilityId": "Ability.AbilityFeature.EffectSignal",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -25},
        "ability": ability(
            "Ability.AbilityFeature.EffectSignal",
            [
                {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                end(0),
            ],
            presentation={"displayName": "点一下就打中", "iconGlyph": "打", "hintText": "瞬发效果。"},
        ),
    },
    {
        "feature": "EffectClip",
        "family": "timeline",
        "title": "火还在烧",
        "beat": "出手之后火还挂在木桩身上，血条一格格往下掉。",
        "detailTemplate": "火还在烧；木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["还在烧"],
        "abilityId": "Ability.AbilityFeature.EffectClip",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 50, "op": "settle"},
        ],
        "expect": {"targetHealthMax": 99, "targetHasTag": "Status.AbilityFeature.Burning"},
        "ability": ability(
            "Ability.AbilityFeature.EffectClip",
            [
                {"kind": "EffectClip", "tick": 0, "durationTicks": 36, "template": BURN},
                end(36),
            ],
            presentation={"displayName": "火还在烧", "iconGlyph": "烧", "hintText": "持续效果。"},
        ),
    },
    {
        "feature": "TagClip",
        "family": "timeline",
        "title": "自己身上挂一阵印",
        "beat": "出手之后施法者头顶先亮一枚印，过一会儿印自己掉。",
        "detailTemplate": "施法者现在{casterTagState}。",
        "assertDetailContains": ["印"],
        "abilityId": "Ability.AbilityFeature.TagClip",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 18, "op": "assert", "casterHasTag": "Mark.AbilityFeature.SelfTimed"},
            {"atFrame": 48, "op": "settle"},
        ],
        "expect": {"casterLacksTag": "Mark.AbilityFeature.SelfTimed"},
        "ability": ability(
            "Ability.AbilityFeature.TagClip",
            [
                {"kind": "TagClip", "tick": 0, "duration": 24, "tag": "Mark.AbilityFeature.SelfTimed"},
                end(24),
            ],
            presentation={"displayName": "自己身上挂一阵印", "iconGlyph": "印", "hintText": "自己挂一阵标记。"},
        ),
    },
    {
        "feature": "TagClipTarget",
        "family": "timeline",
        "title": "给对面挂一阵印",
        "beat": "出手之后木桩头顶亮一枚印，过一会儿印自己掉。",
        "detailTemplate": "木桩现在{targetTagState}。",
        "assertDetailContains": ["印"],
        "abilityId": "Ability.AbilityFeature.TagClipTarget",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 18, "op": "assert", "targetHasTag": "Mark.AbilityFeature.TargetTimed"},
            {"atFrame": 48, "op": "settle"},
        ],
        "expect": {"targetLacksTag": "Mark.AbilityFeature.TargetTimed"},
        "ability": ability(
            "Ability.AbilityFeature.TagClipTarget",
            [
                {"kind": "TagClipTarget", "tick": 0, "duration": 24, "tag": "Mark.AbilityFeature.TargetTimed"},
                end(24),
            ],
            presentation={"displayName": "给对面挂一阵印", "iconGlyph": "印", "hintText": "给当前目标挂一阵标记。"},
        ),
    },
    {
        "feature": "TagSignal",
        "family": "timeline",
        "title": "瞬间给自己打上印",
        "beat": "出手那一下，施法者头顶立刻多一枚印，不会自己掉。",
        "detailTemplate": "施法者现在{casterTagState}。",
        "assertDetailContains": ["印"],
        "abilityId": "Ability.AbilityFeature.TagSignal",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"casterHasTag": "Mark.AbilityFeature.SelfInstant"},
        "ability": ability(
            "Ability.AbilityFeature.TagSignal",
            [
                {"kind": "TagSignal", "tick": 0, "tag": "Mark.AbilityFeature.SelfInstant", "payloadA": 0},
                end(0),
            ],
            presentation={"displayName": "瞬间给自己打上印", "iconGlyph": "印", "hintText": "瞬间给自己加标记。"},
        ),
    },
    {
        "feature": "TagSignalTarget",
        "family": "timeline",
        "title": "瞬间给对面打上印",
        "beat": "出手那一下，木桩头顶立刻多一枚印。",
        "detailTemplate": "木桩现在{targetTagState}。",
        "assertDetailContains": ["印"],
        "abilityId": "Ability.AbilityFeature.TagSignalTarget",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"targetHasTag": "Mark.AbilityFeature.TargetInstant"},
        "ability": ability(
            "Ability.AbilityFeature.TagSignalTarget",
            [
                {"kind": "TagSignalTarget", "tick": 0, "tag": "Mark.AbilityFeature.TargetInstant", "payloadA": 0},
                end(0),
            ],
            presentation={"displayName": "瞬间给对面打上印", "iconGlyph": "印", "hintText": "瞬间给目标加标记。"},
        ),
    },
    {
        "feature": "EventSignal",
        "family": "timeline",
        "title": "出手那一下敲响铃",
        "beat": "出手那一下场上的铃亮了，字幕报铃响。",
        "detailTemplate": "铃响了 {eventCount} 次。",
        "assertDetailContains": ["铃"],
        "abilityId": "Ability.AbilityFeature.EventSignal",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"eventTag": "Event.AbilityFeature.Bell", "eventCountMin": 1},
        "ability": ability(
            "Ability.AbilityFeature.EventSignal",
            [
                {"kind": "EventSignal", "tick": 0, "tag": "Event.AbilityFeature.Bell"},
                end(0),
            ],
            presentation={"displayName": "出手那一下敲响铃", "iconGlyph": "铃", "hintText": "到点发布事件。"},
        ),
    },
    {
        "feature": "InputGate",
        "family": "timeline",
        "title": "等你点头才打出去",
        "beat": "出手之后先停住等确认；确认一到，木桩才掉血。",
        "detailTemplate": "确认之后木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["确认"],
        "abilityId": "Ability.AbilityFeature.InputGate",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 18, "op": "confirm"},
            {"atFrame": 30, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -25, "waitedForGate": True},
        "ability": ability(
            "Ability.AbilityFeature.InputGate",
            [
                {"kind": "InputGate", "tick": 0, "tag": "Input.AbilityFeature.Confirm", "payloadA": 0},
                {"kind": "EffectSignal", "tick": 1, "template": STRIKE},
                end(1),
            ],
            presentation={"displayName": "等你点头才打出去", "iconGlyph": "等", "hintText": "等输入确认。"},
        ),
    },
    {
        "feature": "EventGate",
        "family": "timeline",
        "title": "铃响了才打出去",
        "beat": "出手之后先停住等铃；铃一响，木桩才掉血。",
        "detailTemplate": "铃响之后木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["铃"],
        "abilityId": "Ability.AbilityFeature.EventGate",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 18, "op": "publishEvent", "tag": "Event.AbilityFeature.Impact"},
            {"atFrame": 30, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -25, "waitedForGate": True},
        "ability": ability(
            "Ability.AbilityFeature.EventGate",
            [
                {"kind": "EventGate", "tick": 0, "tag": "Event.AbilityFeature.Impact", "payloadA": 60},
                {"kind": "EffectSignal", "tick": 1, "template": STRIKE},
                end(1),
            ],
            presentation={"displayName": "铃响了才打出去", "iconGlyph": "等", "hintText": "等事件再往下走。"},
        ),
    },
    {
        "feature": "TargetCollectionGate",
        "family": "timeline",
        "title": "名单齐了才打出去",
        "beat": "出手之后先停住等名单；名单一到，近处木桩才掉血。远处那根只是名单上的名字，这一场不挨打。",
        "detailTemplate": "名单齐了；近木桩 {targetAfter}，远木桩仍是 {target2After}。",
        "assertDetailContains": ["名单"],
        "abilityId": "Ability.AbilityFeature.TargetCollectionGate",
        "extraActors": ["target2"],
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 18, "op": "confirmCollection", "targets": ["target", "target2"]},
            {"atFrame": 30, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -25, "target2HealthDelta": 0, "waitedForGate": True},
        "ability": ability(
            "Ability.AbilityFeature.TargetCollectionGate",
            [
                {"kind": "TargetCollectionGate", "tick": 0, "tag": "Input.AbilityFeature.Collect", "payloadA": 0},
                {"kind": "EffectSignal", "tick": 1, "template": STRIKE},
                end(1),
            ],
            presentation={"displayName": "名单齐了才打出去", "iconGlyph": "单", "hintText": "等外部名单。"},
        ),
    },
    {
        "feature": "InterruptAny",
        "family": "timeline",
        "title": "被晕就停手",
        "beat": "出手之后还没打中，施法者被晕，这一招停在半路，木桩不掉血。",
        "detailTemplate": "停手时木桩血条仍是 {targetAfter}。",
        "assertDetailContains": ["停"],
        "abilityId": "Ability.AbilityFeature.InterruptAny",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 20, "op": "addTag", "entity": "caster", "tag": "Status.AbilityFeature.Stunned"},
            {"atFrame": 40, "op": "settle"},
        ],
        "expect": {"interrupted": True, "targetHealthDelta": 0},
        "ability": ability(
            "Ability.AbilityFeature.InterruptAny",
            [
                {"kind": "EffectSignal", "tick": 24, "template": STRIKE},
                end(24),
            ],
            extra_exec={"interruptAny": ["Status.AbilityFeature.Stunned"]},
            presentation={"displayName": "被晕就停手", "iconGlyph": "晕", "hintText": "身上有打断印就停。"},
        ),
    },
    {
        "feature": "CallerParams",
        "family": "params",
        "title": "同一招两波不同力道",
        "beat": "同一张效果票先轻轻一下，再重重一下，木桩掉两截不同的血。",
        "detailTemplate": "两波打完木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["两波"],
        "abilityId": "Ability.AbilityFeature.CallerParams",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 36, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -40},
        "ability": ability(
            "Ability.AbilityFeature.CallerParams",
            [
                {"kind": "EffectSignal", "tick": 0, "template": WAVE, "callerParamsIdx": 0},
                {"kind": "EffectSignal", "tick": 12, "template": WAVE, "callerParamsIdx": 1},
                end(12),
            ],
            extra_exec={
                "callerParams": [
                    {"entries": [{"key": "abilityfeature.wave.damage", "value": 10}]},
                    {"entries": [{"key": "abilityfeature.wave.damage", "value": 30}]},
                ]
            },
            presentation={"displayName": "同一招两波不同力道", "iconGlyph": "波", "hintText": "同一模板换两组数。"},
        ),
    },
    {
        "feature": "BlockTagsBlocked",
        "family": "gates",
        "title": "身上有禁招印就放不出",
        "beat": "第一下挂上禁招印；印还在时再出手，字幕说放不出。",
        "detailTemplate": "第二下{secondCast}。",
        "assertDetailContains": ["放不出"],
        "abilityId": "Ability.AbilityFeature.BlockTagsBlocked",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 20, "op": "cast", "slot": 0, "target": "target", "saveAs": "second"},
            {"atFrame": 30, "op": "settle"},
        ],
        "expect": {"secondCast": "rejected", "casterHasTag": "Cooldown.AbilityFeature.Lock"},
        "ability": ability(
            "Ability.AbilityFeature.BlockTagsBlocked",
            [
                {"kind": "TagClip", "tick": 0, "duration": 48, "tag": "Cooldown.AbilityFeature.Lock"},
                {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                end(0),
            ],
            blockTags={"blockedAny": ["Cooldown.AbilityFeature.Lock"]},
            presentation={"displayName": "身上有禁招印就放不出", "iconGlyph": "禁", "hintText": "禁招印在场就拒。"},
        ),
    },
    {
        "feature": "BlockTagsRequired",
        "family": "gates",
        "title": "没有姿态印就放不出",
        "beat": "没亮姿态印时出手放不出；印一挂上，木桩才掉血。",
        "detailTemplate": "挂上姿态印之后木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["姿态"],
        "abilityId": "Ability.AbilityFeature.BlockTagsRequired",
        "script": [
            {"atFrame": 8, "op": "cast", "slot": 0, "target": "target", "saveAs": "first"},
            {"atFrame": 14, "op": "addTag", "entity": "caster", "tag": "State.AbilityFeature.Stance"},
            {"atFrame": 20, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 32, "op": "settle"},
        ],
        "expect": {"firstCast": "rejected", "targetHealthDelta": -25},
        "ability": ability(
            "Ability.AbilityFeature.BlockTagsRequired",
            [
                {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                end(0),
            ],
            blockTags={"requiredAll": ["State.AbilityFeature.Stance"]},
            presentation={"displayName": "没有姿态印就放不出", "iconGlyph": "姿", "hintText": "缺姿态印就拒。"},
        ),
    },
    {
        "feature": "ActivationPrecondition",
        "family": "gates",
        "title": "残血才打得中",
        "beat": "对着满血木桩出手放不出；对着残血木桩出手，残血掉血，满血不动。",
        "detailTemplate": "残血木桩 {woundedAfter}，满血木桩仍是 {targetAfter}。",
        "assertDetailContains": ["残血"],
        "abilityId": "Ability.AbilityFeature.ActivationPrecondition",
        "extraActors": ["wounded"],
        "script": [
            {"atFrame": 8, "op": "cast", "slot": 0, "target": "target", "saveAs": "first"},
            {"atFrame": 16, "op": "cast", "slot": 0, "target": "wounded"},
            {"atFrame": 28, "op": "settle"},
        ],
        "expect": {"firstCast": "rejected", "woundedHealthDelta": -25, "targetHealthDelta": 0},
        "ability": ability(
            "Ability.AbilityFeature.ActivationPrecondition",
            [
                {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                end(0),
            ],
            activationPrecondition={"validationGraph": "Graph.AbilityFeature.WoundedOnly"},
            presentation={"displayName": "残血才打得中", "iconGlyph": "验", "hintText": "校验图不过就不放。"},
        ),
    },
    {
        "feature": "UseRequirement",
        "family": "progress",
        "title": "没解锁就放不出",
        "beat": "牌子还没点亮时出手放不出；牌子点亮之后，木桩才掉血。",
        "detailTemplate": "解锁之后木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["解锁"],
        "abilityId": "Ability.AbilityFeature.UseRequirement",
        "needsProgression": True,
        "script": [
            {"atFrame": 8, "op": "cast", "slot": 0, "target": "target", "saveAs": "first"},
            {"atFrame": 14, "op": "unlockProgression"},
            {"atFrame": 20, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 32, "op": "settle"},
        ],
        "expect": {"firstCast": "rejected", "targetHealthDelta": -25},
        "ability": ability(
            "Ability.AbilityFeature.UseRequirement",
            [
                {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                end(0),
            ],
            useRequirement="Req.AbilityFeature.Unlock",
            presentation={"displayName": "没解锁就放不出", "iconGlyph": "锁", "hintText": "进度不够就拒。"},
        ),
    },
    {
        "feature": "ShowRequirement",
        "family": "progress",
        "title": "没解锁栏上就没有",
        "beat": "牌子点亮之前栏上只有一招；点亮之后，隐藏的那招才出现。",
        "detailTemplate": "看得见的招：{visibleAbilities}。",
        "assertDetailContains": ["看得见"],
        "abilityId": "Ability.AbilityFeature.ShowRequirement",
        "needsProgression": True,
        "casterAbilities": ["Ability.AbilityFeature.ShowAlways", "Ability.AbilityFeature.ShowRequirement"],
        "script": [
            {"atFrame": 8, "op": "snapshotVisible"},
            {"atFrame": 14, "op": "unlockProgression"},
            {"atFrame": 20, "op": "snapshotVisible", "saveAs": "after"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"visibleBeforeCount": 1, "visibleAfterCount": 2},
        "ability": ability(
            "Ability.AbilityFeature.ShowRequirement",
            [end(0)],
            showRequirement="Req.AbilityFeature.Unlock",
            presentation={"displayName": "隐藏招", "iconGlyph": "藏", "hintText": "没解锁就看不见。"},
        ),
        "companionAbilities": [
            ability(
                "Ability.AbilityFeature.ShowAlways",
                [end(0)],
                presentation={"displayName": "常驻招", "iconGlyph": "常", "hintText": "一直看得见。"},
            )
        ],
    },
    {
        "feature": "ToggleSpec",
        "family": "toggle",
        "title": "再按一次关掉",
        "beat": "第一下打开姿态印；再按一次，印灭掉。",
        "detailTemplate": "关掉之后施法者{casterTagState}。",
        "assertDetailContains": ["关掉"],
        "abilityId": "Ability.AbilityFeature.ToggleSpec",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "caster"},
            {"atFrame": 20, "op": "assert", "casterHasTag": "State.AbilityFeature.ToggleOn"},
            {"atFrame": 26, "op": "cast", "slot": 0, "target": "caster"},
            {"atFrame": 38, "op": "settle"},
        ],
        "expect": {"casterLacksTag": "State.AbilityFeature.ToggleOn"},
        "ability": ability(
            "Ability.AbilityFeature.ToggleSpec",
            [end(0)],
            toggleSpec={"toggleTag": "State.AbilityFeature.ToggleOn", "activeEffects": []},
            presentation={"displayName": "再按一次关掉", "iconGlyph": "开", "hintText": "开关技能。"},
        ),
    },
    {
        "feature": "ToggleDeactivateExec",
        "family": "toggle",
        "title": "关掉时补一刀收尾",
        "beat": "打开时什么也不打；再按一次关掉，收尾那一刀打在木桩上。",
        "detailTemplate": "关掉之后木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["关掉"],
        "abilityId": "Ability.AbilityFeature.ToggleDeactivateExec",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 20, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 36, "op": "settle"},
        ],
        "expect": {"targetHealthDelta": -12},
        "ability": ability(
            "Ability.AbilityFeature.ToggleDeactivateExec",
            [end(0)],
            toggleSpec={
                "toggleTag": "State.AbilityFeature.ToggleArmed",
                "activeEffects": [],
                "deactivateExec": {
                    "clockId": "FixedFrame",
                    "items": [
                        {"kind": "EffectSignal", "tick": 0, "template": CLOSE},
                        end(0),
                    ],
                },
            },
            presentation={"displayName": "关掉时补一刀收尾", "iconGlyph": "收", "hintText": "关闭时跑收尾时间轴。"},
        ),
    },
    {
        "feature": "DispatchTarget",
        "family": "dispatch",
        "title": "这一刀打回自己",
        "beat": "对着木桩出手，血条掉的是施法者自己，木桩不动。",
        "detailTemplate": "对着木桩出手，掉血的是施法者自己：从 {casterBefore} 掉到 {casterAfter}；木桩仍是 {targetAfter}。",
        "assertDetailContains": ["自己"],
        "abilityId": "Ability.AbilityFeature.DispatchTarget",
        "script": [
            {"atFrame": 12, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 24, "op": "settle"},
        ],
        "expect": {"casterHealthDelta": -20, "targetHealthDelta": 0},
        "ability": ability(
            "Ability.AbilityFeature.DispatchTarget",
            [
                {"kind": "EffectSignal", "tick": 0, "template": SELF, "dispatchTarget": "Source"},
                end(0),
            ],
            presentation={"displayName": "这一刀打回自己", "iconGlyph": "己", "hintText": "效果打在施法者。"},
        ),
    },
    {
        "feature": "FormSet",
        "family": "form",
        "title": "换姿态换一栏招",
        "beat": "先按换姿态，栏上那一格从炮打换成锤砸；再出手，木桩挨的是锤。",
        "detailTemplate": "换栏之后这一格是 {slot0Name}；木桩血条从 {targetBefore} 掉到 {targetAfter}。",
        "assertDetailContains": ["换"],
        "abilityId": "Ability.AbilityFeature.FormSwitch",
        "casterAbilities": ["Ability.AbilityFeature.FormCannon", "Ability.AbilityFeature.FormSwitch"],
        "formSetId": "ability_feature_hammer_forms",
        "script": [
            {"atFrame": 8, "op": "snapshotSlot", "slot": 0, "saveAs": "before"},
            {"atFrame": 14, "op": "cast", "slot": 1, "target": "caster"},
            {"atFrame": 22, "op": "snapshotSlot", "slot": 0, "saveAs": "after"},
            {"atFrame": 28, "op": "cast", "slot": 0, "target": "target"},
            {"atFrame": 40, "op": "settle"},
        ],
        "expect": {"slot0After": "Ability.AbilityFeature.FormHammer", "targetHealthDelta": -35},
        "ability": ability(
            "Ability.AbilityFeature.FormSwitch",
            [end(0)],
            toggleSpec={"toggleTag": "State.AbilityFeature.Hammer", "activeEffects": []},
            presentation={"displayName": "换姿态", "iconGlyph": "换", "hintText": "挂上锤姿态印。"},
        ),
        "companionAbilities": [
            ability(
                "Ability.AbilityFeature.FormCannon",
                [
                    {"kind": "EffectSignal", "tick": 0, "template": STRIKE},
                    end(0),
                ],
                presentation={"displayName": "炮打", "iconGlyph": "炮", "hintText": "底座栏。"},
            ),
            ability(
                "Ability.AbilityFeature.FormHammer",
                [
                    {"kind": "EffectSignal", "tick": 0, "template": HAMMER},
                    end(0),
                ],
                presentation={"displayName": "锤砸", "iconGlyph": "锤", "hintText": "锤姿态覆盖栏。"},
            ),
        ],
    },
]


def finalize_ability(raw: dict) -> dict:
    exec_block = dict(raw["exec"])
    extra_exec = raw.pop("extra_exec", None) if "extra_exec" in raw else None
    # ability() may have put extra_exec via **extra incorrectly; handle both.
    if extra_exec:
        exec_block.update(extra_exec)
    if "extra_exec" in raw:
        exec_block.update(raw.pop("extra_exec"))
    out = {k: v for k, v in raw.items() if k != "extra_exec"}
    if "extra_exec" in out.get("exec", {}):
        raise AssertionError("extra_exec leaked into exec")
    # Re-read from original extra keys that ability() stuffed into top-level
    return out


def normalize_feature(feature: dict) -> dict:
    raw_ability = feature["ability"]
    extra_exec = raw_ability.pop("extra_exec", None)
    if extra_exec:
        raw_ability["exec"] = {**raw_ability["exec"], **extra_exec}
    for companion in feature.get("companionAbilities", []):
        extra = companion.pop("extra_exec", None)
        if extra:
            companion["exec"] = {**companion["exec"], **extra}
    return feature


FEATURES = [normalize_feature(f) for f in FEATURES]


def feature_ids() -> list[str]:
    return [f["feature"] for f in FEATURES]
