---
文档类型: 总览入口
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: docs - 全局入口与阅读路径
状态: 草案
---

# Ludots 项目核心能力分析与文档目录重构方案

# 1 源代码核心能力总结（以 `src/` 为准）

通过分析 `src/` 目录，Ludots 项目包含以下核心能力域（按“依赖层级”分组）：

## 1.1 核心引擎层

| 能力域 | 代码入口 | 核心职责 |
|---|---|---|
| 游戏引擎 | `src/Core/Engine/GameEngine.cs` | 引擎生命周期、系统分组（Phase）、初始化流程 |
| 时钟与节拍器 | `src/Core/Engine/ClockFoundation.cs`、`src/Core/Engine/Pacemaker/` | 多域时钟、实时/回合节拍器、Time-Slice |
| 配置管线 | `src/Core/Config/` | ConfigPipeline、JsonMerger、EntityBuilder、模板与合并口径 |

## 1.2 ECS 基础设施

| 能力域 | 代码入口 | 核心职责 |
|---|---|---|
| Arch ECS | `src/Libraries/Arch/` | World、Entity、Query、System 核心 |
| Arch Extended | `src/Libraries/Arch.Extended/` | 扩展能力（事件/持久化/关系等） |

## 1.3 游戏逻辑层

| 能力域 | 代码入口 | 核心职责 |
|---|---|---|
| GAS 能力系统 | `src/Core/Gameplay/GAS/` | Effect、Attribute、Tag、Ability、Response、Order 等 |
| Graph 运行时 | `src/Core/GraphRuntime/` | GraphExecutor、GraphProgram、节点库 |
| 导航寻路 | `src/Core/Navigation/` | NodeGraph、A*、HPA、流式 Chunk、AOI 相关能力 |
| 空间服务 | `src/Core/Spatial/` | 空间分区、空间查询、坐标域与结果形态 |
| 地图系统 | `src/Core/Map/` | VertexMap、HexCoordinates、MapManager、MapDefinition |

## 1.4 表现与交互层

| 能力域 | 代码入口 | 核心职责 |
|---|---|---|
| 表现层 | `src/Core/Presentation/`、`src/Core/Systems/`、`src/Core/Gameplay/Camera/` | 相机、裁剪、HUD、坐标映射、逻辑到表现同步 |
| 输入系统 | `src/Core/Input/` | 输入配置、上下文栈、运行时处理、后端抽象 |
| UI 系统 | `src/Libraries/Ludots.UI/`（以及 Web 工程） | Widget、UIRoot、Web 侧 UI/编辑器形态 |

## 1.5 平台与扩展层

| 能力域 | 代码入口 | 核心职责 |
|---|---|---|
| Mod 系统 | `src/Core/Modding/` | ModLoader、依赖解析、VFS |
| 物理系统 | `src/Core/Physics/` | 2D 物理相关基建、宽相/查询形态（待统一到空间契约） |
| 多平台适配 | `src/Apps/`、`src/Adapters/`、`src/Platforms/` | Raylib 后端、Web 侧、工具链与适配层 |

# 2 文档问题诊断（重构动机）

本次采取“从头重写”的根因是：原 `docs/` 发生误删且无 `.git` 可直接恢复；同时历史结构存在以下通病（重建时一并修复）：

| 问题 | 现象 | 影响 |
|---|---|---|
| 能力缺失 | Mod/Input/Map/UI/物理缺少 SSOT | 重要系统无规范，协作成本高 |
| 职责混淆 | 空间服务/导航/烘焙/运行时多处平行叙事 | SSOT 不清晰，容易分叉 |
| 编号与入口不稳定 | 入口分散、读者需要猜“下一篇在哪” | 阅读路径不可控 |

# 3 新文档目录结构（按依赖顺序分层）

## 3.1 依赖顺序（阅读顺序）

Layer 0 元规则 → Layer 1 底层框架 → Layer 2 核心引擎 → Layer 3 基础服务 → Layer 4 游戏逻辑 → Layer 5 高级逻辑 → Layer 6 交互层 → Layer 7 附录与扩展。

物理系统放在“空间服务之后”：先统一坐标域/查询契约与预算口径，再描述物理如何作为后端或消费者接入。

## 3.2 目录结构

```
docs/
  00_文档总览/
  01_底层框架/
    01_ECS基础/
    02_Mod与VFS/
  02_核心引擎/
    03_核心引擎/
  03_基础服务/
    04_Graph运行时/
    05_脚本与事件/
    06_空间服务/
    07_物理系统/
  04_游戏逻辑/
    08_能力系统/
    09_地图与地形/
    10_导航寻路/
  05_高级逻辑/
    11_避障与Steering/
    12_效用AI系统/
  06_交互层/
    13_输入系统/
    14_表现层/
    15_UI系统/
  07_附录与扩展/
    external/
    工程指南/
```

# 4 目录设计原则（重建约束）

| 原则 | 说明 |
|---|---|
| 依赖顺序 | 目录编号按依赖关系排列；阅读路径不靠“猜” |
| SSOT 明确 | 每个能力域必须有唯一真源文档，其它文档只引用不复述 |
| fail-fast | 禁止静默 fallback；配置/加载/版本必须强校验且可观测 |
| 确定性与预算 | 明确稳定序/tie-break、固定容量、dropped 指标与退化策略 |
| 可扩展 | Mod/VFS 为内容与能力注入基石；核心引擎不硬编码业务内容 |

# 5 迁移映射表（旧路径兼容入口）

| 原路径 | 新路径 | 操作 |
|---|---|---|
| `docs/README.md` | `docs/00_文档总览/00_README.md` | 兼容入口 |
| `docs/01_核心规范/01_文档规范.md` | `docs/00_文档总览/01_文档规范.md` | 移动 |
| `docs/07_基础设施/` | `docs/02_核心引擎/` + `docs/03_基础服务/` + `docs/06_交互层/14_表现层/` | 拆分/合并 |
| `docs/09_空间服务运行时/` | `docs/03_基础服务/06_空间服务/` | 合并 |
| `docs/05_空间服务与导航烘焙/` | `docs/04_游戏逻辑/09_地图与地形/` + `docs/03_基础服务/06_空间服务/` | 拆分/合并 |
| `docs/04_导航寻路接口/` | `docs/04_游戏逻辑/10_导航寻路/` | 重编号 |
| `docs/06_避障与Steering/` | `docs/05_高级逻辑/11_避障与Steering/` | 重编号 |
| `docs/03_行为AI系统/` | `docs/05_高级逻辑/12_效用AI系统/` | 重编号 |
| `docs/08_能力系统/` | `docs/04_游戏逻辑/08_能力系统/` | 重编号 |

# 6 SSOT 一览（每个能力域的权威入口）

- ECS：`docs/01_底层框架/01_ECS基础/00_总览.md`
- Mod/VFS：`docs/01_底层框架/02_Mod与VFS/00_总览.md`
- 核心引擎：`docs/02_核心引擎/00_总览.md`
- Graph：`docs/03_基础服务/04_Graph运行时/00_总览.md`
- 脚本与事件：`docs/03_基础服务/05_脚本与事件/00_总览.md`
- 空间服务：`docs/03_基础服务/06_空间服务/00_总览.md`
- 物理系统：`docs/03_基础服务/07_物理系统/00_总览.md`
- GAS：`docs/04_游戏逻辑/08_能力系统/00_总览.md`
- 地图与地形：`docs/04_游戏逻辑/09_地图与地形/00_总览.md`
- 导航寻路：`docs/04_游戏逻辑/10_导航寻路/00_总览.md`
- 避障与 Steering：`docs/05_高级逻辑/11_避障与Steering/00_总览.md`
- 效用AI：`docs/05_高级逻辑/12_效用AI系统/00_总览.md`
- 输入：`docs/06_交互层/13_输入系统/00_总览.md`
- 表现层：`docs/06_交互层/14_表现层/00_总览.md`
- UI：`docs/06_交互层/15_UI系统/00_总览.md`
