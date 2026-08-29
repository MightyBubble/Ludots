## 合同订正（来自架构方向裁定）：内容层条件与结算统一走图，Provider 合同退出活动内容路径

### 裁定

活动定义里的条件求值与选项结算**不再引用 Provider 合同键**（`condition_key` / `effect_key`），统一改为引用图：

| 现合同字段（§3.2/§3.5） | 订正为 | 执行入口 |
|---|---|---|
| `trigger_condition` / `show_condition` / `execute_condition`（condition_key 引用） | `trigger_graph` / `show_graph` / `execute_graph`（Validation 图 id） | `GraphExecutor.ExecuteValidation`（caster = scope host） |
| `options[].effects[]` / `automatic_effects[]`（effect_key 引用） | `settle_graph`（Effect 图 id，选项结算） / automatic 的 `settle_graph` | `GraphExecutor.ExecuteRegistered`（caster = scope host） |

理由：引擎已有 TriggerGraph / Script / Query / Effect / Validation / Score 六种图与 150 个 op，内容层另立一套 Condition/Effect 注册表属于重复造轮子。图的执行门面（`GraphExecutor`）已支撑 AI 行为树/HFSM，活动复用同一扇门。

### 保留与删除

- **保留**：`source_key` 事实源声明（#775 Source 侧是摄取合同，不是求值轮子）；`OfferActivity` 图 op（派发轨本来就是图）；呈现/生命周期缓冲与排水（对齐 GasPresentationEventBuffer 模式）。
- **删除**：活动内容路径的 provider Condition/Effect 求值（`ActivityRuntimeService.ExecuteEffects` / `EvaluateCondition` 的 provider 分支）；`world.subject_attribute` 条件与 `activity.offer` 效果（历史增量，属轮子）。
- **新增（唯一）**：图 op `CreateTask`（参数 `task_id`，caster 为 scope）——结算图接棒任务的缺口节点。新增 graph 节点是组合门的正路（A 类）。

### 一致性影响

- 加载期 fail-fast 语义不变：未知图 id 在加载/编译期拒绝，错误带图 id；
- 无隐藏骰子纪律不变：结算图里只允许确定性 op，随机仍限池抽/对象选取；
- 本订正由 #1296 交付线执行（含 showcase/gallery 内容迁移、测试迁移、`narrative-runtime-wiki/activity.md` 手册改写）。
