# 作者工具链

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

作者工具链把“声明地图 → 估算 → 烘焙 → 保存 → 启动 → 查询”压缩成一条可复验流程。CLI、Editor Bridge 和 Web 编辑器都是同一 Core 服务的操作面，不各自维护一套规则。

关联 issue：[#1348](https://github.com/MightyBubble/Ludots/issues/1348)。

## 2. 结构

地图声明 → 统一配置解析 → estimate/输入指纹 → Core bake → .ntil/Manifest → 冷加载/查询验证。CLI、Editor Bridge 和编辑器按钮调用同一套服务，页面只编排操作。

## 3. 详情

### 3.1 编辑器功能

- 四个明确面板：Map/Board、Navigation Config、Bake、Path Simulation。
- 面板显示真实 source、profile、layer、area、障碍、预算、hash 和失败原因。
- Estimate 和 Bake 使用同一份表单状态；Bake 不允许跳过 estimate 合同。
- 保存后提供 reload 检查，确认磁盘产物能被冷启动读取。

### 3.2 工具功能

目标入口是 `nav bake --map <id>`，负责：

1. 解析 map、board、source、profile、layer 和 semantic。
2. 计算目标 tile 和预算。
3. 生成 estimate hash 并执行 bake。
4. 写入 `.ntil` 和 manifest。
5. 输出成功、空 tile、失败和重试动作。

现有 CLI/Bridge 入口仍包含 `bake-react`、`bake-recast-react` 和 `estimate-recast-react` 等历史命令；在统一入口完成前，文档不能把它们写成最终产品形态。

### 3.3 Runtime 功能

- Runtime 只读取 manifest 和 `.ntil`，不理解编辑器私有 payload。
- 运行时加载校验 board、layer、profile、source revision 和 build hash。
- Runtime rebake 复用 Core bake service，但使用独立的 bounded tier 和 immutable snapshot。
- 工具链失败不会在 Runtime 里自动改算法或降级地形。

证据路径：`src/Tools/Ludots.Tool/Program.cs`、`src/Tools/Ludots.Tool/ToolMapConfigResolver.cs`、`src/Tools/Ludots.Editor.Bridge/Program.cs`、`src/Core/Navigation/NavMesh/Bake/NavBakeService.cs`。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

选地图、估算、烘焙，开图就能走。

面向地图作者和首次体验该能力的玩家。作者管线：修改声明后，看到估算、产物和查询按顺序更新。

### 主循环

0–15 秒：选地图和已声明 profile。15–35 秒：修改一处源区域，点击估算并确认影响范围。35–60 秒：启动烘焙，完成后加载并点选终点。长任务持续显示真实进度；一分钟内应看懂过程，不承诺大地图必在一分钟完成。

### 消融对照

并排比较上次发布结果与当前未烘焙工作副本。修改后旧 estimate 标过期，不能用旧估算批准新输入；发布后两侧指纹对齐。

### 解释层

阶段条显示校验/估算/烘焙/写入/验证；HUD 显示瓦片数、预算、输入指纹、产物路径和失败后的下一步。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 地图 | 声明过的样例 | 确定作者上下文 |
| 板 | 所选地图的 board | 限制操作范围 |
| profile | 地图已声明的 profile | 选择体型产物 |
| 烘焙范围 | 全量 / 变更范围 | 控制目标集合 |
| 结果视图 | 上次发布 / 当前工作副本 | 检查输入是否已发布 |

### 场景结构

主演示小地图从编辑到查询；子场景：hex、空瓦片、预算拒绝、冷启动。首屏：“选地图，点估算查看影响范围，再烘焙并进入地图。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 统一 map 命令 | Tools / Bridge | 待 nav bake --map 完整入口 |
| 估算指纹和 bake 共用输入 | NavBake Core | 已有基础，待全过程一致 |
| 冷加载验证 | 加载服务 | 依赖产物页，工具只调用 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

目标命令 nav bake --map <id> 尚未提供完整产品入口。源读取、烘焙算法、清单格式和诊断各由对应功能页定义；作者工作台不能再复制这些实现。

## 6. UAT

```gherkin
Feature: 作者用一条命令生成可查询 NavMesh

  Scenario: 四步完成烘焙
    Given 作者已选择 map、board、source、profile 和 layer
    When 作者执行 `nav bake --map <id>`
    Then 工具先报告预算再生成 `.ntil` 和 manifest
    And 启动地图后作者可以查询路径

  Scenario: 缺少声明时停止
    Given 作者没有声明有效 source 或 profile
    When 作者执行 bake
    Then 工具指出缺少的声明
    And 不生成默认或平地产物
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

CLI、Editor Bridge 和 Core bake 已有共用部分；统一 `nav bake --map`、manifest 写入、Artifact 版本收口、冷启动检查和旧入口退役仍未完成。`codex/nav-authoring-showcase` 与 `codex/nav-bake-visualization` 只能提取适配层，不能整体合入。

分支接收判断：nav-authoring-showcase、nav-bake-visualization 可提取体验/适配；旧 Cdt session、detourBase64 与内存 artifact 不整体接收。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 分阶段工作台与错误定位 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 统一 nav bake --map，合并现有 CLI/Bridge 编排 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 仅消费正式清单和 Core 服务 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 同图跨入口一致、冷启动可查询 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavBakeServiceContractTests.cs`
