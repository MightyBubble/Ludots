# Activity 活动：把一次拍板摆到玩家面前

前线补给超限，撤还是推？城池刚易主，要不要通报？Activity 是"现在就得拍板"的内容容器：一段正文、若干选项、当场结算、进历史。它相当于 CK3 的事件弹窗，但永远只有一层——选完即结束，需要跨周期的东西由结算效果去创建 Task。

完整合同在 issue #773（SSOT）。本页讲玩家看到什么、作者怎么配、从哪跑起来。

## 玩家看到什么

一个活动怎么到达玩家面前，由 `dispatch_policy` 决定，三条路径：

| 路径 | 玩家体验 | 典型内容 |
|---|---|---|
| `forced` | 弹层挡在面前，必须选 | 补给超限、俘虏处置 |
| `pooled` | 周期结算点从候选池抽一个弹给玩家 | 过境商队、随机机遇 |
| `automatic` | 不弹层，直接归档为一条通报 | 归属切换、天象记录 |

弹层里的选项有四种形态，一次说清：

1. **基础选项**：没有任何条件，永远可选——玩家永远不会被卡死在一个全部点不动的弹层里；
2. **普通可执行**：点得动；
3. **可见但锁定**：显示出来并写明原因（"目标当前不处于可接管状态"）——为什么点不动，必须说；
4. **Gate 未通过**：整个不出现——它存在，但这一局不满足显示条件。

选完之后：效果当场执行（走已登记的 Effect，活动正文里不允许发明新规则），实例进入只读历史，历史里查得到这次选了哪个选项。

同一活动能不能反复出现，由 `repeat_policy` 决定：挂起去重（默认）、可重复、唯一、冷却（按时间域）、互斥组。

## 作者写法（0 编码）

新增一条活动只需要 JSON，不需要写 C#。

**第一步：声明路径。** mod 的 `assets/config_catalog.json` 加一行：

```json
{ "Path": "Activities/activities.json", "Policy": "ArrayById", "IdField": "id" }
```

**第二步：写定义。** `assets/Activities/activities.json`，必填块见下表：

| 块 | 写什么 |
|---|---|
| `id` / `display_name` / `summary` | 稳定 id 与玩家可见文案 |
| `source_key` | 声明属于哪个事实域（须是已登记的 fact source，当前生产可用 `task.state_changed`） |
| `dispatch_policy` | `forced` / `pooled` / `automatic` 三选一 |
| `repeat_policy`（+ `repeat_cooldown` / `mutex_group`） | 出现资格 |
| `options` | 需玩家处理的活动必填；每个选项 `id`/`title`/`body`，效果引用 `effect_key`；**必须有一个 `is_baseline` 选项** |
| `automatic_effects` | 自动结算的活动必填 |

条件分三层，写错层玩家体验就错：`trigger_condition` 管活动出不出现；选项的 `show_condition` 管显不显示（Gate）；`execute_condition` 管点不点得动。当前生产可用的条件键：`world.subject_attribute`（读主体 GAS 属性比较）。

**第三步：接触发。** 地图事件 → TriggerGraph → `OfferActivity` 节点：

```json
{"id": "forced_offer", "op": "OfferActivity", "activityId": "showcase.forced_supply"}
```

（词条详解见[图节点词典 · OfferActivity](../graph-node-op-wiki/OfferActivity.md)；也可以用 provider effect `activity.offer`。）池抽活动另配 `assets/Rng/distributions.json`：候选池权重表 + 命名流种子，同流状态必得同一候选。

**第四步：接面板。** PanelKit panelType `activity`，manifest 绑定照抄 showcase 里的 `panel_manifest.json`，确认走命名命令 `activity.confirm`。

完整可抄的真实用例：`mods/showcases/activity_dispatch/ActivityDispatchShowcaseMod/Assets/`——forced/pooled/automatic 三条路径各一份定义、触发图、RNG 分布和面板绑定。

## 运行时合同速查

- 实例只有三个状态：`pending → active → resolved`，不增设；
- **单层**：选项不得就地打开另一个活动；要后续就由结算效果创建 Task 或发出新事实信号；
- **无隐藏骰子**：随机只允许候选池抽取与对象选取，一律走命名流（见[确定性随机](../../architecture/deterministic-rng.md)）；禁止对选项结果掷骰；
- **未知键硬失败**：未登记的 `effect_key` / `condition_key` / `source_key` 在加载期整包拒装，错误信息带键名；
- 运行时真相只有实例实体一处，面板与叙事都不得另存一份进度；呈现缓冲每帧排空，历史经只读投影查询。

## 入口与验收

| 项 | 值 |
|---|---|
| 可玩 showcase | `activity_dispatch`（registry），启动 preset `activity_dispatch_cef_raylib` |
| headless 验收 | `ActivityDispatchShowcaseAcceptanceTests`（三路径端到端 / 池抽确定性 / 呈现排水） |
| 单元与桥接测试 | `src/Tests/GasTests/Integration/Activity*.cs`（61 项） |
| 证据目录 | `artifacts/acceptance/activity_dispatch/` |
| 数据面投影 | `src/Libraries/Ludots.WebUI.DataPlane/ActivityWebUiTopicProducer.cs` |

## 边界与已知缺口

- **forced 路径结算时拿不到触发信号的对象**（定义级 `context_bindings` 未落地）：选项效果的 target 限定 `context.subject`（scope host）。"撤回刚才那支部队"这类引用触发对象的写法，等合同 A 线补齐后再用；
- 信号订阅轨（`IntakeSignal`）已实现但生产尚无事实源泵——当前触发走 graph 派发轨；共享 Source 合同见 issue #775；
- 生命周期引擎键（`activity.started` 等）目前只进呈现缓冲，事件总线订阅面是 issue #818；
- `presentation_cue` 字段暂为死配置（写了不生效）。
