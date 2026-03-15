# Feature 开发工作流规范

本篇定义 Ludots 仓库中新功能开发的完整流程，从需求发现到合并验收。核心目标：杜绝重复造轮子，消除幻觉代码（引用不存在的 API），确保每一行新代码都挂靠在已有架构管线上。

编码标准（ECS 约束、命名、Commit 格式）见 [00_coding_standards.md](00_coding_standards.md)。
AI Agent 专项规则见 [02_ai_assisted_development.md](02_ai_assisted_development.md)。

## 1 适用范围

本规范适用于以下场景：

*   新增 Core 层 System、Component、Registry
*   新增或修改 Mod（包括 gameplay 逻辑、配置、资源）
*   新增 Adapter 层实现（Raylib、Editor 等）
*   AI Agent 辅助开发的全部场景

简单的 bugfix、文档修订、配置调整不强制执行全部流程，但仍需遵守第 4 节的验证清单。

## 2 发现阶段——动手之前必须做的事

发现阶段的目标是 **确认要做的事不是已有能力的重复**。跳过这一步是重复造轮子的首要原因。

### 2.1 已有能力检索清单

在写任何代码之前，按以下清单逐项检索：

| 检索对象 | 检索方法 | 典型位置 |
|---------|---------|---------|
| 已有 System | 搜索 `BaseSystem<World` 和 `SystemGroup` | `src/Core/` 各子目录 |
| 已有 Registry | 搜索 `Registry` 类名 | 见 `02_ai_assisted_development.md` §4 速查表 |
| 已有 Component | 搜索目标语义的 `Cm`/`Tag`/`Event` 后缀组件 | `src/Core/Components/`、各子模块 |
| 已有 Mod | 检查 `mods/` 目录下所有 `mod.json` | `mods/*/mod.json` |
| 已有配置管线 | 搜索 `ConfigCatalogEntry`、`MergeGameConfig` | `src/Core/Config/` |
| 已有 Sink | 搜索 `AttributeSinkRegistry` 注册点 | `src/Core/Gameplay/GAS/Bindings/` |
| 已有 Trigger/Event | 搜索 `EventKey`、`OnEvent` | `src/Core/Scripting/` |
| 已有文档 | 浏览 `docs/architecture/README.md` 目录 | `docs/architecture/` |

### 2.2 发现结论记录

检索完成后，记录发现结论：

```
发现：[已有/无] 可复用能力。
- 可复用：<列出具体类名、Registry、System>
- 需新增：<列出确认不存在、需要从零实现的部分>
- 需扩展：<列出已有但需修改接口的部分>
```

如果发现结论为"全部需新增"，需要额外确认：是否遗漏了检索项？是否搜索了正确的关键词？

## 3 设计阶段——写代码之前先写方案

### 3.1 设计方案内容

对于非 trivial 的功能（涉及 2 个以上文件的修改），在编码前产出简要设计方案：

| 项目 | 内容 | 目的 |
|-----|------|-----|
| 目标 | 一句话说明这个功能做什么 | 对齐理解 |
| 挂靠点 | 新代码将注册到哪个 Registry、属于哪个 SystemGroup phase、挂靠哪条管线 | 防止游离代码 |
| 复用清单 | 列出将使用的已有类、接口、Registry（附源码路径） | 防止重复造轮子 |
| 新增清单 | 列出需要新建的类、组件、配置（附计划路径） | 明确增量 |
| 数据流 | 新功能的输入从哪来、输出到哪去，经过哪些 phase | 确保架构一致 |
| 测试策略 | 如何验证功能正确性（单元测试 / 集成测试 / 手动验证） | 防止提交未验证代码 |

### 3.2 API 引用验证（防幻觉条款）

设计方案中引用的每一个 API 必须通过以下验证：

*   **类存在性**：搜索确认类/接口定义存在于代码库中
*   **方法签名**：确认方法名、参数类型、返回类型与实际代码一致
*   **注册模式**：确认 Registry 的 `Register` 方法签名和调用时机（schema phase / runtime）
*   **组件布局**：确认 ECS 组件是 blittable struct，字段类型正确

违反此条的代码一律不得提交。AI Agent 生成的代码尤其需要逐项验证。

### 3.3 设计方案的 Review

*   人类开发者：在 Issue 或 PR description 中记录设计方案，获得至少一位成员确认
*   AI Agent：在开始编码前将方案输出给用户确认（除非用户明确授权自主执行）

## 4 验证阶段——提交前必须完成

### 4.1 验证清单

每次提交前逐项确认：

*   [ ] **编译通过**：`dotnet build` 无 error
*   [ ] **测试通过**：相关测试项目全量通过
*   [ ] **无重复实现**：确认没有与已有 Registry/System 功能重叠的新增代码
*   [ ] **API 引用正确**：所有引用的类、方法、接口在代码库中确实存在且签名匹配
*   [ ] **ECS 约束满足**：新组件 blittable、无 GC、QueryDescription 已缓存
*   [ ] **文档同步**：如果行为变更影响了已有文档，同一 commit 更新文档
*   [ ] **命名合规**：组件后缀（Cm/Tag/Event）、系统后缀（System）、命名不耦合业务

### 4.2 测试要求

| 变更类型 | 最低测试要求 |
|---------|------------|
| Core 新增 System | 单元测试覆盖核心逻辑 + 边界条件 |
| Core 新增 Registry | 注册/查询/冲突/容量边界测试 |
| GAS 相关变更 | GasTests 全量通过 |
| Mod 新增/修改 | Launcher CLI `resolve`/`launch` 冒烟通过 + 功能冒烟测试 |
| UI / Showcase / Presentation 变更 | 编译与相关测试通过 + adapter 可见冒烟 + 首帧可读性与接管/恢复验证 |
| 架构边界变更 | ArchitectureTests 通过 |
| 导航相关变更 | Navigation2DTests 全量通过 |

测试风格遵循 `src/Tests/GasTests/TESTING_STYLE.md`。
测试命令见 [03_environment_setup.md](03_environment_setup.md)。

### 4.3 UI、Showcase 与表现层附加验收

当变更涉及 `UiScene`、`ReactivePage`、HUD、overlay、showcase takeover 或玩家可见表现层时，除 4.1 与 4.2 外，还必须补齐以下验收：

*   **Surface ownership 明确**：说明当前改动占用的是哪一类 surface（如 retained UI、`ScreenOverlayBuffer`、world HUD），谁是 owner，是否存在 takeover。
*   **接管/恢复链路完整**：如果 showcase 或 mod 会临时接管已有 UI，必须验证 `MapLoaded`、`MapResumed`、`MapUnloaded` 上的 acquire / restore / release 行为，而不是只验证“能显示出来”。
*   **首帧可读**：首个可见帧不得出现闪烁标题、反复 remount、占位文本或明显越界布局。
*   **交互安全**：面板显示时，实体选择、世界点击、相机等无关交互仍然正常；若设计上需要屏蔽，必须显式记录边界。
*   **双证据闭环**：不仅要有 engine-side 正确性的日志或测试，还要有 adapter 可见证据，证明玩家实际看到的内容正确。
*   **性能边界可解释**：多实例或多实体显示必须说明 SoA、dirty-refresh、零分配边界，避免把展示成本扩散到 ECS 热路径。

## 5 分支与 PR 工作流

### 5.1 分支命名

| 类型 | 格式 | 示例 |
|-----|------|------|
| 功能 | `feat/<scope>-<short-desc>` | `feat/gas-shield-system` |
| 修复 | `fix/<scope>-<short-desc>` | `fix/physics2d-collision-leak` |
| 重构 | `refactor/<scope>-<short-desc>` | `refactor/config-pipeline-merge` |
| 文档 | `docs/<short-desc>` | `docs/<topic>` |

### 5.2 PR 要求

每个 PR 必须包含：

1. **标题**：与 commit 格式一致（`<type>(<scope>): <description>`）
2. **描述**：包含第 3.1 节设计方案的简要版本，至少包含"挂靠点"和"复用/新增清单"
3. **测试证据**：测试通过的截图或日志
4. **文档同步**：如有架构文档需要更新，包含在同一 PR 中

### 5.3 Review 要点

Review 时优先检查以下项目：

*   是否存在与已有 Registry/System 重复的实现
*   是否引用了不存在的 API（幻觉代码）
*   是否遵循 ECS blittable/zero-GC 约束
*   是否正确归属 SystemGroup phase
*   UI / showcase 改动是否明确 surface owner，并覆盖 takeover / restore / first-frame readability
*   命名是否耦合了具体业务

## 6 相关文档

*   编码标准：见 [00_coding_standards.md](00_coding_standards.md)
*   AI 辅助开发规范：见 [02_ai_assisted_development.md](02_ai_assisted_development.md)
*   开发环境与构建：见 [03_environment_setup.md](03_environment_setup.md)
*   文档编写规范：见 [04_documentation_governance.md](04_documentation_governance.md)
*   ECS 开发实践：见 [../architecture/ecs_soa.md](../architecture/ecs_soa.md)
*   Mod 架构与配置系统：见 [../architecture/mod_architecture.md](../architecture/mod_architecture.md)
*   GAS 分层架构：见 [../architecture/gas_layered_architecture.md](../architecture/gas_layered_architecture.md)

