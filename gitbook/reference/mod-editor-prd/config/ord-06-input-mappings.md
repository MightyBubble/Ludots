# ord-06 配置说明 · 输入映射

> 配置写法与行为。第一性需求见 [ord-06 PRD](../prd/ord-06-input-mappings.md)；编辑器需求见 [UXD](../uxd/ord-06-input-mappings.md)；现状见 [reference](../reference/ord-06-input-mappings.md)。

## 1. 示例配置

真实例（`mods/showcases/rts_demo/RtsDemoMod/assets/Input/input_order_mappings.json` 节选——直连与候选路由各一）：

```json
{ "interactionMode": "AimCast",
  "mappings": [
    { "actionId": "SkillQ", "trigger": "PressedThisFrame", "orderTypeKey": "castAbility",
      "argsTemplate": { "i0": 0 }, "requireTarget": false, "targetType": "Position",
      "isSkillMapping": true, "modifierBehavior": "QueueOnModifier" },
    { "actionId": "Command", "actorCollectionKey": "collection.command.source",
      "trigger": "PressedThisFrame", "requireTarget": true, "targetType": "Position",
      "isSkillMapping": false, "modifierBehavior": "QueueOnModifier",
      "actorOrderRouting": { "candidates": [
        { "orderTypeKey": "setSpawnTarget", "priority": 10, "targetType": "HoveredEntityOrPosition",
          "match": { "abilitySlotIndex": 2, "abilityIdKeySuffix": ".Train",
                     "blockedAnyTags": ["Progression.Rts.WarpGate"] } },
        { "orderTypeKey": "moveTo", "priority": 0, "match": {} } ] } } ],
  "userOverrides": { "enabled": true, "persistPath": "user://RtsDemoMod/input_preferences.json" } }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `interactionMode` | 全局交互模式：TargetFirst / SmartCast / AimCast / SmartCastWithIndicator / ContextScored / PressReleaseAimCast |
| `actionId` / `trigger` | 绑定的动作与触发沿（4 种 + 双击窗口） |
| `orderTypeKey` | 直连路由：全部命中演员提交同一类型（与 `actorOrderRouting` 二选一） |
| `actorOrderRouting.candidates[]` | 逐演员择单：按 `priority` 取首个 `match` 命中的候选 |
| `match` | 匹配条件：requiredAllTags / blockedAnyTags / abilitySlotIndex / abilityIdKey / abilityIdKeySuffix |
| `argsTemplate` | 参数槽 `i0`-`i3` / `f0`-`f3` 随单下发到黑板 |
| `requireTarget` / `targetType` | 是否必须取到目标；目标形态 None/Position/Entity/Entities/Direction/Vector/HoveredEntityOrPosition |
| `actorCollectionKey` / `targetCollectionKey` | 演员集合与目标集合的实体集合键 |
| `modifierBehavior` | 修饰键行为 4 值（如 QueueOnModifier=按修饰键转排队） |
| `isSkillMapping` | 技能映射：`i0` 必须为非负技能优先级 |
| `heldPolicy` / `castModeOverride` | 按住策略；单映射交互模式覆盖 |
| `autoTargetPolicy` / `cursorTargetPolicy`（+Range） | 自动/光标目标策略，二者互斥，范围须 >0 |
| `groupMoveTargetLayout` / `userOverrides` | 组移动落点（None 或 Grid：spacingCm+orderTypeKeys）；玩家覆写（enabled+persistPath，默认 `user://input_preferences.json`） |

## 3. 文件结构

目录条目 `Input/input_order_mappings.json`（根无文件，由 mod 贡献）（mod 携带；引擎根资产**无此文件**，全部由 mod 贡献）。

## 4. 运行时加载效果

加载校验后由输入映射系统消费：生效模式 = 映射覆盖 ?? 全局；模式分派经命令意图仲裁逐帧路由（input-01），施法派发按 input-02。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `actionId` 重复 / `orderTypeKey` 与 routing 并存 | 启动失败 |
| routing 携带 `isSkillMapping`/`Entities`、技能映射 `i0` 缺失或为负 | 启动失败 |
| auto/cursor 并存或范围 ≤0、Grid 参数非法 | 启动失败 |
| mod 缺此文件 | 当前仅日志跳过（治理中，见 O7） |

## 6. 实例

- 真实例：`mods/showcases/rts_demo/RtsDemoMod/assets/Input/input_order_mappings.json`

**相关文档**：[ord-06 PRD](../prd/ord-06-input-mappings.md) · [input-01 配置说明](input-01-command-intent.md) · [input-05 配置说明](input-05-filters-and-schemes.md)
