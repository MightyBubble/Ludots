# Showcase 完成度硬门槛

本页是可玩 Showcase（含 capability-standard 与门户画廊条目）的 SSOT 完成度合同。`AGENTS.md` / `CLAUDE.md` 只作入口引用；改门槛只改本页。

## 1. 概述

Showcase 是给**新玩家上手看懂一个功能**的短剧，不是技术探针、不是 Debug 覆盖层、不是验收夹具截图册。

引擎已经具备 Presenter、网格、材质、World HUD、字幕缓冲、Raylib 适配。交付 Showcase 时必须**用这些正式呈现管线**，不得用 DebugDraw 色圈/轴线冒充产品画面。

## 2. 结构

完成度按四层验收，缺一层即未完成：

| 层 | 合同 |
|----|------|
| 剧本 | 一场短剧只讲一个功能；玩家看得懂因果 |
| 画面 | 主角色走 Presenter + 网格/材质（或已登记的正式 host 资产） |
| 反馈 | 字幕/World HUD/面板至少一种，读出当前阶段与结果 |
| 证据 | registry + 验收测试 + 真机截图/录像；截图必须能看出「东西」而不只是网格 |

## 3. 详情（硬禁令与硬要求）

### 3.1 硬禁令

- **禁止**把 `DebugDraw` / `GraphShowcaseStagePresenter.DrawActor` 色圈、线框、坐标轴当作主画面或唯一可见角色。
- **禁止**空场景 + 仅屏幕字幕冒充「可玩」。
- **禁止**用 headless 验收夹具（仅断言 ECS 状态、无 Presenter）顶替门户 Showcase。
- **禁止**一功能多杂烩：一个 root Mod 塞多层无关故事。
- **禁止**静默失败：Presenter 规则 key 写错、资产缺失、车道不匹配必须装载期失败，不得「画面空着也算过」。

### 3.2 硬要求（默认最低配）

每个可玩 Showcase root Mod 必须具备：

1. **`assets/Presentation/presenters.json`**：按模板 id 的 `EntitySpawned` → `CreatePresenter` / `EntityDestroyed` → `DestroyPresenterScope`。
2. **`assets/config_catalog.json`**：登记 `Presentation/presenters.json`（及本 Mod 实际用到的 mesh/host 目录）。
3. **实体具备可视变换合同**：模板带 `WorldPositionCm`，并带 `VisualHeightmapSampleState` 或 `PresentationStaticTransform`（与 `SourceHasVisualTransform` 条件一致）。
4. **主角色网格**：优先复用 Core 内置 `cube`/`sphere` + `default_surface`，或仓库已有正式 mesh/host；颜色与尺度能让人在默认相机下分清角色身份。
5. **阶段可读**：自动剧本或可操作路径下，字幕/HUD 说出「现在发生什么 / 结束得到什么」。
6. **一功能一 Mod**；launcher binding + preset + `showcase.registry.json` 齐全。

更高保真（士兵 GLTF、建筑、粒子）欢迎，但不得用「还没做模型」当借口退回 DebugDraw 主画面。没有定制模型时用内置图元仍必须走 Presenter。

### 3.3 DebugDraw 的合法用途

仅允许作为**叠加诊断**（碰撞盒、寻路折线、选中高亮），且：

- 主角色已经由 Presenter 画出来；
- 关掉 DebugDraw 后，短剧仍可被新玩家看懂。

### 3.4 参考正例 / 反例

- 正例（Presenter 主画面）：`CapabilityStandardAbilityGraphSandboxMod`、`MapTriggerNightRaidMod`、`CapabilityStandardStaticPresenter30kMod`、`CapabilityStandardCrowdPhysicsArenaMod`。
- 反例（禁止再交付）：仅 `DebugDraw` 色圈的「可读剧本」、无 `presenters.json` 的 Attachment 探针皮。

上手路径：`gitbook/architecture/presenter-quickstart.md`。能力标准目录：`gitbook/architecture/capability-standard-showcases.md`。

## 4. 场景

- **新开 Showcase**：设计剧本 → 列 Presenter/资产清单 → 再写 Demo 系统；没有画面合同不得开 PR 称「可玩」。
- **抬升旧短剧**：先补 Presenter 与截图，再谈验收绿。
- **纯架构守卫 / headless 合同测试**：可以没有 Presenter，但不得登记为门户可玩 Showcase，也不得写进「玩家短剧」文档行。

## 5. 边界

- 压力基准、CI 夹具、架构棘轮测试：不在本页「可玩」门槛内。
- 图节点单节点画廊：仍须可见角色或明确舞台物；细则见 capability-standard 文档的 Graph Op 节。
- 适配器差异：逻辑与 Presenter 配置在 Mod；平台只绑 host 资产，不写回 Core 私货。

## 6. UAT（玩家视角）

```gherkin
Feature: 可玩 Showcase 必须让新玩家看见正式画面上的角色与故事
  Scenario: 打开一场宣称可玩的短剧
    Given 我用 raylib 预设启动该 Showcase
    When 短剧开场后若干秒
    Then 我能在场景中看到网格/材质构成的角色或道具，而不是只有色圈或空地
    And 我能从字幕或世界 HUD 读出当前阶段
    And 故事结束时我能说出「这个功能演示了什么」

  Scenario: 关掉调试线框后短剧仍成立
    Given 主角色已由 Presenter 创建
    When 调试绘制被关闭或忽略
    Then 我仍能分清谁是谁、发生了什么
```
