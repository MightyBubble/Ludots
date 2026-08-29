# 属性写入权威

本页是 Attribute current / cap 写入合同的正式入口。实现必须服从本页；运行时状态（有没有夹上限、此刻有没有聚合修正）不能改变同一个 API 的语义。

## 权威路径

选择 **(a) 直写永远存活**。

- 正式写入面是 `AttributeMutationOps.SetCurrent` / `SetBase` / `AddCurrent`。写入的 current 就是之后 `GetCurrent` 读到的值。
- `AttributeAggregatorSystem` 只重算有效上限，写入 `CapValues`，由 `AttributeBuffer.GetCap` 读取。聚合器不把 current 改回「基础值 + 修正」。
- 需要把 current 对齐到有效上限时，调用显式操作 `AttributeMutationOps.ReplaceCurrentFromCap`。禁止依赖「无约束属性被聚合器悄悄覆盖」。
- `SetCurrent` / `SetBase` 必须排队 `AttributeAggregateDirty`（实体已有 `ActiveEffectContainer` 时），因此「写完之后要过多久才重算上限」是确定的。

不选择 (b) 的理由：把「直接改当前值」从公开 API 收回、只让聚合器写 current，会让正式写入在重算后消失。那正好是本页要消灭的裂缝。

夹上限约束只约束「current 不得超过有效上限」，不决定「这次写入能不能活下来」。禁止给所有属性都配上夹上限来假装语义统一。

## 读写合同

| API | 语义 |
|-----|------|
| `AttributeMutationOps.SetCurrent` | 结算写入 current；值存活；标脏并排队聚合 |
| `AttributeMutationOps.SetBase` | 结算写入基础值，并把 current 重置到新基础值后走同一套约束 |
| `AttributeMutationOps.ReplaceCurrentFromCap` | 显式把 current 设成当前有效上限 |
| `AttributeBuffer.GetCurrent` | 读取权威 current |
| `AttributeBuffer.GetCap` | 读取聚合后的有效上限 |
| `AttributeBuffer.GetBase` | 夹上限属性返回有效上限；其余返回基础值 |

派生属性仍走已有围栏：`BeginDerivedAttributeWrites` / `EndDerivedAttributeWrites` / `RejectDerivedAttributeSideEffect`。该围栏是对的，本页不改它的语义。派生图对 current 的显式写入会保留；聚合修正本身不会覆盖 current。

## TriggerGraph 属性写边界

`ModifyAttributeSet` 在 TriggerGraph 中只作为权威属性写入口使用：图可以决定目标和值，但最终仍由 `GasGraphRuntimeApi` 调用 `AttributeMutationOps`，不会绕过 GAS 结算或另建一套存储。TriggerGraph 放行是为了让地图事件、面板按钮等运行时触发器能执行确定的属性写入；Script 图继续保持 Pure-only，不开放这个有副作用的操作。Effect 图的既有放行不变。

## 强制手段

`AttributeBuffer.SetBase` / `SetCurrent` / `SetAggregatedCurrent` 不是玩法写入面。Core 与展厅程序集里只有白名单类型可以调用它们：

- 结算：`AttributeMutationOps`、`EffectModifierOps`
- 聚合与派生：`AttributeAggregatorSystem`、`GasGraphRuntimeApi`（仅派生暂存 buffer）
- 装载物化：`ComponentRegistry`、`TemplateEntityBatchSpawner`、`QuestDefinitions`
- 每帧脉冲消费：`ForceInput2DSink`、`CameraBehaviorInputSink`
- 基准装配：`GasBenchmark`

展厅运行时（含运行期补金、种子属性、基准装配）必须走 `AttributeMutationOps`，禁止裸写缓冲。`EffectPhaseSideEffectTransaction.Commit` 按变更的 attributeId 调用 `AttributeMutationOps` 写 current/base，不再整块赋值 `AttributeBuffer`，因此不在白名单里；暂存副本通过 `EffectModifierOps` 写入。`ArchitectureGuardTests.AttributeBufferWrites_MustComeFromWhitelistedCallers` 用 IL 扫描 Core 与展厅程序集强制这条名单；不在名单里的调用方必须让 CI 失败并点名。

## 非法编号失败关闭

属性编号的合法区间是 `[0, AttributeRegistry.MaxAttributes)`。`InvalidId` 是 `-1`。

- `GetId(name)` 找不到时返回 `InvalidId`。
- `RequireId(name)` 找不到时抛异常并点名该属性名。
- `GetCurrent` / `SetCurrent` / `SetBase` / `GetCap` 遇到非法或越界 id 抛 `ArgumentOutOfRangeException`。禁止静默返回 `0` 或丢掉写入。

容量常量只在 `AttributeRegistry.MaxAttributes` 定义一处；`AttributeBuffer` 与 `DirtyFlags` 引用它。

## 注册表约定与 Freeze

属性表与标签表的哨兵不同，调用方必须用各自的 `InvalidId` / `IsValidId`，禁止套用 `id > 0`：

| 表 | 首个合法 id | `InvalidId` |
|----|-------------|-------------|
| `AttributeRegistry` | `0` | `-1` |
| `TagRegistry` | `1` | `0` |

生产装载在统一注册点 `Register`，并在 GameEngine 装载结束后 `AttributeRegistry.Freeze()` / `AttributeSinkRegistry.Freeze()`。冻结后禁止新增属性身份；已登记名称可以继续解析。`Clear()` 只用于测试隔离，会解除冻结。

## UAT

```gherkin
Feature: 我写进属性的数字不会自己变回去

  Scenario: 直接写入的当前值语义唯一
    Given 一个基础值 100 的属性，挂着一个永久 +18 的修正
    And 我通过正式接口把当前值写成 50
    When 属性重算发生
    Then 当前值仍是 50
    And 这个结果与该属性是否配了夹上限约束无关
    And 有效上限是 118

Feature: 写错属性名不应该悄无声息

  Scenario: 非法属性编号必须失败关闭
    Given 我引用了一个没有注册的属性名
    When 系统按这个名字取编号，然后拿这个编号去写值
    Then 写入必须失败并点名这个属性名

Feature: 属性的编号不该看 mod 装载顺序

  Scenario: 第一个注册的属性也必须可用
    Given 某个属性恰好是本次运行中第一个被注册的
    When 内置的施加冲力之类的路径去读它
    Then 它应当和其他属性一样正常工作

Feature: 绕过结算改属性必须被拦住

  Scenario: 裸写属性缓冲要么编译不过，要么 CI 红
    Given 一段代码直接调用属性缓冲的写方法，而它不在允许名单里
    When CI 跑架构守卫
    Then 守卫必须失败并点名这个调用方
```

相关入口：

- [GAS 分层架构](gas-layered-architecture.md)
- [GAS、订单与输入运行时合同](gas-order-input-runtime-contract.md)
