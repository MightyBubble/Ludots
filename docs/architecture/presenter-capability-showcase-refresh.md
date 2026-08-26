# Presenter 能力演示集体翻新

## 1. 概述

玩家与作者要能**逐条**学会 Presenter：每一种内建 BehaviorKind、每一种内建 PresenterCommandKind，都有「只讲这一件事」的可跑入口；能力目录是唯一指路牌；铁匠铺之类大杂烩退回「故事集成巡演」，不再冒充逐条 SSOT；引擎画廊直接写 buffer 的场景不得冒充作者路径。

本轮翻新目标：

1. **每条能力都有演示**：作者面 `presenters.json` 可写、可跑、可读说明。
2. **集体翻新目录**：`gitbook/reference/presenter-capability-catalog/` 与枚举、schema、真实 preset 对齐。
3. **清理过期与大杂烩**：过期登记降级/退役；大杂烩改职责标签，禁止用它勾选「该能力已演示」。

## 2. 结构

```text
学习入口（SSOT）
  └─ presenter-capability-catalog/
       ├─ behaviors.md / commands.md / asset-kinds.md …
       └─ acceptance-map.md（路线：单能力 → 指令全息 → 集成巡演）

演示分层
  ├─ L1 单能力演示（必选，一能力一入口）
  │    · 独立 Mod，或全息馆内「单站单职责」站点
  │    · 必须走 presenters.json 作者路径
  ├─ L2 扩展模板（必选）
  │    · Behavior Extension / Command Extension
  ├─ L3 集成巡演（可选）
  │    · 铁匠铺等：故事串联，不作逐条勾选依据
  └─ L4 引擎画廊（渲染层）
       · 可证渲染器；若非 presenters.json，目录必须写明「非作者路径」
```

挂靠点：

- 登记：`showcase.registry.json`（`status`、`docsPath`、`preset`、`tags`）
- 启动：`launcher.presets.json`
- 说明：能力目录 + 可选 `mod-extensible-runtime-showcases/*`

复用：现有 `CapabilityStandard*ShowcaseMod` 骨架、指令全息四站点、Sound 单能力模版。

新增：缺作者路径的能力演示（至少 TrailMesh / Material 行为 / activationCondition）；目录 TrailMesh 条目；黑名单「不得用大杂烩勾选」。

## 3. 详情

### 3.1 单能力演示合同

每条内建能力的演示必须同时满足：

| 要求 | 说明 |
|------|------|
| 单一职责 | 标题/摘要只声明一种 BehaviorKind 或一种 CommandKind（可含支撑用的 AssetBinding/SetParam） |
| 作者路径 | 关键行为/指令写在 `presenters.json`（或分片），禁止 C# 直塞 `PresenterCommandBuffer` 冒充数据规则 |
| 可跑 | `launcher.presets.json` 有 preset；registry 有 active 条目 |
| 可读 | 能力目录该条目「跑/证据」指向该 preset；摘要用人话 |

### 3.2 大杂烩处理

| 入口 | 新职责 |
|------|--------|
| `presenter_blacksmith` 及 large_world / scatter 变体 | **L3 集成巡演 / 性能压测**；title/summary 去掉「覆盖全部行为」暗示 |
| `capability_standard_presenter_command_showcase` | 保留为 **L1 指令全息馆**（多站，但站内单职责）；修正 SinkParamToAsset 必须由规则编译 |
| `engine_raylib_slash_trail` | 保留为 **L4 渲染画廊**；另建 L1 TrailMesh 作者演示 |
| `engine_raylib_material_binding` | L4/材质实例链；另建 L1 Material **BehaviorKind** 演示 |
| `blacksmith_test` / `presenter_schema_reference` | 夹具，不进玩家学习主路径（registry 标注 fixture） |

### 3.3 过期清理规则

满足任一即 `status: deprecated` 或从学习路线摘除：

- 文档宣称的作者路径与代码不符（直发指令、画廊冒充 Behavior）
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

- 新作者打开能力目录，点 TrailMesh，按 preset 跑起来，看到刀光来自 `kind: TrailMesh` 的行为槽，而不是画廊场景硬编码。
- 新作者学 Material 行为：换一块砖只换材质表，不误进「画廊材质绑定」另一套语义。
- 新作者学 activationCondition：靠近才亮灯，配置写在行为上，不用手写 Activate 规则凑。
- 玩家进指令全息 B 站点「强制刷新」，链路仍是事件 → 规则 → SinkParamToAsset，模组代码不碰命令缓冲。
- 玩家进铁匠铺：被明确告知这是故事巡演，逐条学习请回目录。

## 5. 边界

- 不在本翻新中拆上帝类运行时（EntityRuntime/ConfigLoader 瘦身属后续优化债）。
- 不把 Deferred DestroyMode 设计稿假装已实现。
- 不删铁匠铺内容，只改职责与指路；压测入口可保留。
- 扩展（Extension=255）继续以现有黄金模板为准，不改扩展协议。
- 一能力一入口优先独立 Mod；指令全息允许多站共模，但禁止站内「程序化直发」冒充数据。

## 6. UAT

```gherkin
Feature: 每条 Presenter 能力都能单独学会

  Scenario: 从目录学会 TrailMesh 作者写法
    Given 我打开能力目录的 TrailMesh 条目
    When 我按条目里的 preset 启动
    Then 画面出现由 presenters.json 中 TrailMesh 行为驱动的刀光
    And 说明文字没有把引擎画廊 slash_trail 当作唯一作者路径

  Scenario: Material 行为不是画廊材质课
    Given 我启动 Material BehaviorKind 的单能力演示
    When 我切换区域参数
    Then 同一 mesh 只换材质表结果
    And 我不会被指去 engine_raylib_material_binding 当作本行为的作者证明

  Scenario: activationCondition 可读可跑
    Given 我启动 activationCondition 单能力演示
    When 满足条件的实体进入范围
    Then 对应行为自行点亮
    And 配置里能看到 activationCondition 对象而不是靠手写 ActivateBehavior 凑

  Scenario: 指令全息的 sink 刷新是数据规则
    Given 我启动 capability_standard_presenter_command_showcase_raylib
    When 我点击 B 站「强制刷新对照柱」
    Then 刷新由 presenters 规则编译为 SinkParamToAsset
    And 模组代码没有直接往 PresenterCommandBuffer 塞该指令

  Scenario: 铁匠铺不再冒充实全覆盖
    Given 我查看 presenter_blacksmith 的登记摘要或验收路线
    Then 它被标成集成巡演或性能压测
    And 逐条能力的「跑/证据」指向 L1 单能力入口而不是铁匠铺冒充
```
