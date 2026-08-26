# ab-01 配置说明 · 技能定义骨架

> 配置写法与行为。第一性需求见 [ab-01 PRD](../prd/ab-01-definition.md)；编辑器需求见 [UXD](../uxd/ab-01-definition.md)；现状见 [reference](../reference/ab-01-definition.md)。

## 1. 示例配置

演示场景真实技能（`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json`，节选）：

```json
{ "id": "Ability.Rts.RedAlert.BuildPowerPlant",
  "exec": { "clockId": "FixedFrame", "items": [ { "kind": "End", "tick": 120 } ] },
  "input": { "castModeOverride": "TargetFirst" },
  "blockTags": { "blockedAny": [ "State.Rts.RedAlert.Constructing" ] },
  "presentation": { "displayName": "Build Power Plant", "iconGlyph": "PP", "accentColor": "#F97316", "hintText": "Queue construction; place after the build item is ready." } }
```

顶层全块骨架（教学骨架；各块展开见 ab-02…ab-10）：

```json
[ { "id": "Ability.Example.Full",
    "exec": { "clockId": "FixedFrame", "interruptAny": [], "callerParams": [], "items": [] },
    "cooldown": { "valueAttribute": "CooldownSeconds", "tag": "Cooldown.Example.Q" },
    "blockTags": { "requiredAll": [], "blockedAny": [] }, "categories": [ "Catalog.Hero.Damage" ],
    "interactionContextProfile": "ctxProfileId",
    "activationPrecondition": { "validationGraph": "Graph.Example.CanCast" },
    "toggleSpec": { "toggleTag": "State.Example.On", "activeEffects": [], "deactivateExec": {} },
    "targeting": { "castRangeCm": 400, "impactEffect": "Effect.Example.Impact" },
    "presentation": { "displayName": "示例", "displayNameToken": "UI.ABILITY.EXAMPLE.NAME", "modeHints": { "SmartCastWithIndicator": "按住预览" } },
    "input": { "trigger": "Press", "heldPolicy": "Cancel", "castModeOverride": "TargetFirst", "autoTargetPolicy": "None", "autoTargetRangeCm": 0 },
    "useRequirement": "Progression.Example.Unlocked", "showRequirement": "Progression.Example.Visible" } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 技能身份；同名后加载覆盖先加载 |
| `exec` | **必填**执行时间轴：clockId 必填、items ≤16（详表见 ab-02）；`exec.callerParams` 参数池 ≤4 组（ab-03） |
| `cooldown` / `blockTags` | 冷却数据契约（valueAttribute 须已注册 + tag 至少其一，ab-04）/ tag 激活门（ab-05） |
| `categories` / `interactionContextProfile` | 纯分类（进 AbilityCategoryRegistry，运行时零玩法判定）/ 交互上下文档案 id（非空 Trim）；前置校验图 `activationPrecondition.validationGraph` 必填已注册 |
| `toggleSpec` / `targeting` | 开关声明（toggleTag 必填、activeEffects ≤4，ab-08）/ 射程与命中（必填非负 + 已注册，ab-09） |
| `presentation` / `input` | 表现九字段（全空=不声明，mode 键须已知）/ 输入覆盖五字段至少一项 |
| `useRequirement` / `showRequirement` | 进度需求 id，须已注册；分别管"可用"与"可见" |

## 3. 文件结构

目录条目 `GAS/abilities.json`（根数据为空，由 mod 贡献）（数组按 id 深合并；已启用分片目录 `GAS/abilities` 且允许为空，见 [facts](../facts.md)）。跨 mod 合并：后加载的 mod 只赢它写到的字段。

## 4. 运行时加载效果

加载器按 id 排序后逐条编译注册进技能注册表；编译即全量校验（item 结构、引用解析、token 校验）。技能本身不落实体：单位通过槽位持有技能（ab-06）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 任一条目编译失败 | 启动失败，聚合全部条目错误（含 id、文件、字段路径） |
| 已删除字段（indicator/onActivateEffects/瞄准表现族）或旧字段名（四项改名、clockId "Turn"） | 启动失败，报错指明替代写法 |
| cooldown 引用未注册/两项皆空；文案 token 未注册；进度需求未知名 | 启动失败 |

## 6. 实例

- 演示底座 `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json`（9 技能）；toggle/targeting 全块 `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`

**相关文档**：[ab-01 PRD](../prd/ab-01-definition.md) · [ab-02 配置说明](ab-02-exec-timeline.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
