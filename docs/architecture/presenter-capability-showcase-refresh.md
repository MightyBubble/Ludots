# Presenter 能力演示集体翻新

## 1. 概述

玩家与作者要能**逐条**学会 Presenter：每一种内建 BehaviorKind、每一种内建 PresenterCommandKind，都有「只讲这一件事」的可跑入口；能力目录是唯一指路牌。铁匠铺等大场景回归「故事集成巡演」职责；引擎画廊专责渲染器验收；作者路径以 `presenters.json` 的 L1 为准。

本轮翻新目标：

1. **每条能力都有演示**：作者面 `presenters.json` 可写、可跑、可读说明。
2. **集体翻新目录**：`gitbook/reference/presenter-capability-catalog/` 与枚举、schema、真实 preset 对齐。
3. **清理过期与大杂烩**：过期登记降级/退役；大杂烩改挂 L3 职责标签；逐条勾选认 L1。

## 2. 结构

```text
学习入口（SSOT）
  └─ presenter-capability-catalog/
       ├─ behaviors.md / commands.md / asset-kinds.md …
       └─ acceptance-map.md（路线：单能力 → 指令全息 → 集成巡演）

演示分层
  ├─ L1 单能力演示（必选，一能力一入口）
  │    · 独立 Mod，或全息馆内「单站单职责」站点
  │    · 关键行为/指令写在 presenters.json
  ├─ L2 扩展模板（必选）
  │    · Behavior Extension / Command Extension
  ├─ L3 集成巡演（可选）
  │    · 铁匠铺等：故事串联；逐条勾选认 L1
  └─ L4 引擎画廊（渲染层）
       · 可证渲染器；目录标注分层职责
```

挂靠点：

- 登记：`showcase.registry.json`（`status`、`docsPath`、`preset`、`tags`）
- 启动：`launcher.presets.json`
- 说明：能力目录 + 可选 `mod-extensible-runtime-showcases/*`

复用：现有 `CapabilityStandard*ShowcaseMod` 骨架、指令全息四站点、Sound 单能力模版。

新增：TrailMesh / Material 行为 / activationCondition 的 L1 作者演示；目录 TrailMesh 与 activationCondition 条目；L3/L4 职责标签。

## 3. 详情

### 3.1 单能力演示合同

每条内建能力的演示必须同时满足：

| 要求 | 说明 |
|------|------|
| 单一职责 | 标题/摘要只声明一种 BehaviorKind 或一种 CommandKind（可含支撑用的 AssetBinding/SetParam） |
| 作者路径 | 关键行为/指令写在 `presenters.json`（或分片）；事件经规则编译为指令 |
| 可跑 | `launcher.presets.json` 有 preset；registry 有 active 条目 |
| 可读 | 能力目录该条目「跑/证据」指向该 preset；摘要用人话 |

### 3.2 大杂烩处理

| 入口 | 新职责 |
|------|--------|
| `presenter_blacksmith` 及 large_world / scatter 变体 | **L3 集成巡演 / 性能压测**；title/summary 写明故事巡演 |
| `capability_standard_presenter_command_showcase` | **L1 指令全息馆**（多站，站内单职责）；SinkParamToAsset 由规则编译 |
| `engine_raylib_slash_trail` | **L4 渲染画廊**；TrailMesh 作者路径另有 L1 |
| `engine_raylib_material_binding` | **L4 材质实例链**；Material BehaviorKind 另有 L1 |
| `blacksmith_test` / `presenter_schema_reference` | 夹具（registry 标注 fixture） |

### 3.3 过期清理规则

满足任一即 `status: deprecated` 或从学习路线摘除：

- 文档宣称的作者路径与装载/运行时合同不符（例如宣称数据规则，实现却是直发指令）
- schema 字段装载即拒绝，却仍当作者能力宣传
- 重复入口且无独立验收（多个铁匠入口只留巡演 + 压测必要项）

### 3.4 本轮必做清单（相对审计）

1. 目录：Behavior 十四种（补 TrailMesh）；修正 Timer/Destroy/Initialize/InstancedBatch 过期口径；SUMMARY 改「十四种」。✅
2. L1 补齐：TrailMesh 作者演示；Material BehaviorKind 演示；activationCondition 演示。✅
3. 指令全息：SinkParamToAsset 改为 presenters 规则，删 C# `PublishRefresh` 直发。✅
4. 铁匠铺 registry/acceptance-map：降级为 L3。✅
5. schema：删除或修正死字段（定义级 `visibility`、`assetBinding.grounding*`）；`activationCondition` 类型与 loader 对齐。✅

L1 preset：`capability_standard_presenter_trailmesh_showcase_raylib` / `capability_standard_presenter_material_behavior_showcase_raylib` / `capability_standard_presenter_activation_condition_showcase_raylib`。

## 4. 场景

- 新作者打开能力目录，点 TrailMesh，按 L1 preset 启动，看到 `kind: TrailMesh` 行为槽驱动的刀光。
- 新作者学 Material 行为：同一 mesh 只换材质表，入口是 Material BehaviorKind 的 L1 preset。
- 新作者学 activationCondition：条件满足时行为自行点亮，配置写在行为槽的 `activationCondition` 对象上。
- 玩家进指令全息 B 站点「强制刷新」，链路是事件 → 规则 → SinkParamToAsset。
- 玩家进铁匠铺：登记摘要写明 L3 故事巡演；逐条学习回能力目录 L1。

## 5. 边界

- 本翻新范围不含 EntityRuntime/ConfigLoader 瘦身（后续优化债）。
- Deferred DestroyMode 仍属设计稿，本轮只保证文档口径与实现一致。
- 铁匠铺内容保留，改职责与指路；压测入口可保留。
- 扩展（Extension=255）继续以现有黄金模板为准，不改扩展协议。
- 一能力一入口优先独立 Mod；指令全息允许多站共模，站内指令由规则编译。

## 6. UAT

```gherkin
Feature: 每条 Presenter 能力都能单独学会

  Scenario: 从目录学会 TrailMesh 作者写法
    Given 我打开能力目录的 TrailMesh 条目
    When 我按条目里的 L1 preset 启动
    Then 画面出现由 presenters.json 中 TrailMesh 行为驱动的刀光
    And 「跑/证据」指向该 L1 preset

  Scenario: Material 行为作者路径可跑
    Given 我启动 Material BehaviorKind 的 L1 单能力演示
    When 我切换区域参数
    Then 同一 mesh 只换材质表结果
    And 「跑/证据」指向 Material 的 L1 preset

  Scenario: activationCondition 可读可跑
    Given 我启动 activationCondition 单能力演示
    When 满足条件的实体进入范围
    Then 对应行为自行点亮
    And 配置里能看到 activationCondition 对象

  Scenario: 指令全息的 sink 刷新是数据规则
    Given 我启动 capability_standard_presenter_command_showcase_raylib
    When 我点击 B 站「强制刷新对照柱」
    Then 刷新由 presenters 规则编译为 SinkParamToAsset

  Scenario: 铁匠铺职责是集成巡演
    Given 我查看 presenter_blacksmith 的登记摘要或验收路线
    Then 它被标成集成巡演或性能压测
    And 逐条能力的「跑/证据」指向 L1 单能力入口
```
