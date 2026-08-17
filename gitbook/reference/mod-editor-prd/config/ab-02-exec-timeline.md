# ab-02 配置说明 · 执行时间轴

> 配置写法与行为。第一性需求见 [ab-02 PRD](../prd/ab-02-exec-timeline.md)；编辑器需求见 [UXD](../uxd/ab-02-exec-timeline.md)；现状见 [reference](../reference/ab-02-exec-timeline.md)。

## 1. 示例配置

演示场景真实时间轴（rts 底座电站建造，节选 tick 0/15/120）：

```json
"exec": { "clockId": "FixedFrame", "items": [
    { "kind": "TagClip",     "tick": 0,   "duration": 120, "tag": "Status.Rts.RedAlert.Building.PowerPlant" },
    { "kind": "EffectSignal","tick": 0,   "template": "Effect.Rts.RedAlert.CostPowerPlantStep" },
    { "kind": "TagSignal",   "tick": 120, "tag": "State.Rts.RedAlert.Ready.PowerPlant" },
    { "kind": "End",         "tick": 120 } ] }
```

Gate 与派发骨架（教学骨架）：

```json
"exec": { "clockId": "FixedFrame", "interruptAny": [ "Status.Stunned" ], "items": [
  { "kind": "EffectClip", "tick": 0, "duration": 60, "template": "Effect.Ex.Buff", "callerParamsIdx": 0, "dispatchTarget": "Target" },
  { "kind": "InputGate", "tick": 0, "tag": "Input.Confirm", "payloadA": 0 },
  { "kind": "EventGate", "tick": 0, "tag": "Event.Impact", "payloadA": 30 },
  { "kind": "End", "tick": 0 } ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `clockId` / `interruptAny` | 必填基准时钟（FixedFrame / Step / EntityLocal）/ 施法者带任一列出 tag 即打断 |
| `callerParams` / `items` | 参数池（ab-03）/ 条目数组 ≤16，按 tick 升序消费（数组序即消费序） |

item 公共字段：`kind`、`tick` 必填；`duration`（Clip 时长）；`clock` 单条目换时钟；`tag`；`template`（效果 id 须已注册）；`callerParamsIdx`；`payloadA`；`dispatchTarget`（效果条目四值 Default/Source/Target/TargetContext）。

| kind | 必填/可变字段 | 行为 |
|---|---|---|
| `EffectClip` / `EffectSignal` | template；Clip 另有 duration | 持续效果（起点生效）/ 瞬发效果（到点即发） |
| `TagClip` / `TagClipTarget` | tag + duration | 起点加 tag（自身/当前目标实体），到期自动移除（定时预约） |
| `EventSignal` | tag=事件名 | 到点发布 GameplayEvent |
| `TagSignal` / `TagSignalTarget` | tag；payloadA=0 加 / 1 删 | 到点瞬发加/删 tag（自身/当前目标实体；现状无枚举名，见 reference） |
| `InputGate` / `TargetCollectionGate` | tag=请求 tag；payloadA=请求 id（0=用订单 id） | 挂起等玩家输入/外部目标收集；响应可回填目标 |
| `EventGate` | tag=事件 tag；payloadA=超时 tick（0=无限等） | 挂起等事件；超时放行 |
| `End` | tick | 收束：时间轴完成 |

## 3. 文件结构

位于 `abilities.json` 单条的 `exec` 块（ab-01）；无独立文件。

## 4. 运行时加载效果

编译为定长 SoA 结构（每字段一列定长数组）：tag 首现注册、template 解析为效果 id、dispatchTarget 编进 payloadA。运行期零分配推进。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| items 超 16 / kind 或 tick 缺失 / 未知 kind / template 未注册 / InputGate 缺 payloadA | 启动失败 |
| 起播黑板无槽位键 / 效果发布时队列容量不足（上限见事实页） | 技能失败（MissingBlackboardSlot / SubmissionQueueFull） |

## 6. 实例

- rts 底座 `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json`（TagClip+EffectSignal+TagSignal+End）；champion 沙盒 `.../champion_skill_sandbox/.../abilities.json`（TagClip 冷却 + dispatchTarget:Source）

**相关文档**：[ab-02 PRD](../prd/ab-02-exec-timeline.md) · [ab-01 配置说明](ab-01-definition.md) · [fx-02 配置说明](fx-02-template.md)
