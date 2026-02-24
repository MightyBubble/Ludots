---
文档类型: 对齐报告
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 核心引擎 - 总架构 - Core层解耦对齐
状态: 已实现
依赖文档:
  - docs/02_核心引擎/01_总架构/02_CoreMod_架构设计.md
  - docs/02_核心引擎/01_总架构/05_Core层解耦_裁决条款.md
---

# Core层解耦 对齐报告

# 1 摘要

## 1.1 结论

Core 层解耦重构已完成。所有设计口径与代码现状完全对齐，无差异。

核心变更：
- Core 层已纯基建化，不再持有任何游戏内容
- LudotsCoreMod 已创建并提供所有默认配置
- ConfigPipeline 已实现配置分层合并
- 所有 Mods 已迁移到数据驱动常量
- 118/118 单元测试全部通过

## 1.2 风险等级与影响面

| 风险等级 | 描述 |
|---|---|
| 低 | 重构已完成，所有测试通过 |

影响面：
- Core 层：4 个常量文件删除，3 个系统文件修改
- Mods：6 个 Mod 的多个文件修改
- Tests：5 个测试文件修改，1 个新增

## 1.3 建议动作

1. ✅ 已完成：所有代码变更
2. ✅ 已完成：测试验证
3. 建议：后续新增常量时，遵循 `game.json` 配置结构规范

# 2 审计范围与方法

## 2.1 审计范围

审计对象：
- `src/Core/` 目录下所有 `.cs` 文件
- `src/Mods/` 目录下所有 `.cs` 文件
- `src/Tests/GasTests/` 目录下所有测试文件
- `assets/Configs/` 目录下所有配置文件

审计目标：
- 确认硬编码常量已删除
- 确认 ConfigPipeline 正确合并配置
- 确认所有代码使用数据驱动常量
- 确认无向后兼容代码

## 2.2 审计方法

1. **静态分析**：使用 `grep` 检查关键模式
2. **编译验证**：`dotnet build` 确认无编译错误
3. **测试验证**：`dotnet test` 确认所有测试通过
4. **人工复核**：检查关键文件的代码变更

## 2.3 证据口径

所有证据路径使用仓库相对路径（`src/...` 或 `docs/...`）。

# 3 差异表

## 3.1 差异表

| 设计口径 | 代码现状 | 差异等级 | 风险 | 证据 |
|---|---|---|---|---|
| Core 层禁止硬编码常量 | ✅ 已删除 OrderTags.cs、GasOrderTags.cs、GameAttributes.cs、MapIds.cs | 无差异 | 无 | 文件不存在 |
| Core 层禁止注册游戏系统 | ✅ InitializeCoreSystems() 从 config.Constants 读取 | 无差异 | 无 | `src/Core/Engine/GameEngine.cs` |
| Core 层禁止持有游戏资产 | ✅ entry.json、templates.json、hud.json 已迁移 | 无差异 | 无 | `src/Mods/LudotsCoreMod/assets/` |
| ConfigPipeline 分层合并 | ✅ MergeGameConfig() 已实现 | 无差异 | 无 | `src/Core/Config/ConfigPipeline.cs` |
| 字典深度合并 | ✅ Constants 字典按键合并 | 无差异 | 无 | `src/Core/Config/ConfigPipeline.cs` |
| 禁止向后兼容代码 | ✅ 无 fallback、backward compatibility 代码 | 无差异 | 无 | grep 无结果 |
| LudotsCoreMod 提供默认配置 | ✅ game.json 包含 constants 与 startupMapId | 无差异 | 无 | `src/Mods/LudotsCoreMod/assets/game.json` |
| MobaDemoMod 使用数据驱动常量 | ✅ 从 config.Constants 读取 | 无差异 | 无 | `src/Mods/MobaDemoMod/` |
| 其他 Mods 使用 MergedConfig | ✅ MapIds.Entry 改为 engine.MergedConfig.StartupMapId | 无差异 | 无 | 各 Mod 文件 |
| 测试使用 TestGasOrderTags | ✅ 已创建并使用 | 无差异 | 无 | `src/Tests/GasTests/TestGasOrderTags.cs` |
| 所有测试通过 | ✅ 118/118 passed | 无差异 | 无 | `dotnet test` 输出 |

# 4 行动项

## 4.1 行动项清单

| ID | 行动项 | 负责人 | 优先级 | 状态 | 验收条件 |
|---|---|---|---:|---|---|
| ACT-001 | 删除硬编码常量文件 | X28技术团队 | P0 | ✅ 完成 | 文件不存在 |
| ACT-002 | 创建 LudotsCoreMod | X28技术团队 | P0 | ✅ 完成 | Mod 存在且可加载 |
| ACT-003 | 实现 ConfigPipeline | X28技术团队 | P0 | ✅ 完成 | 配置正确合并 |
| ACT-004 | 迁移 Core 系统到数据驱动 | X28技术团队 | P0 | ✅ 完成 | 编译通过 |
| ACT-005 | 迁移所有 Mods | X28技术团队 | P0 | ✅ 完成 | 编译通过 |
| ACT-006 | 修复测试代码 | X28技术团队 | P0 | ✅ 完成 | 118/118 测试通过 |
| ACT-007 | 修复 Physics2D CleanupSystem | X28技术团队 | P1 | ✅ 完成 | CollisionPair 测试通过 |
| ACT-008 | 修复 NavMesh 浮点精度问题 | X28技术团队 | P1 | ✅ 完成 | CrossTilePath 测试通过 |
| ACT-009 | 创建架构文档 | X28技术团队 | P1 | ✅ 完成 | 文档符合规范 |

# 5 变更历史

| 日期 | 版本 | 变更内容 |
|---|---|---|
| 2026-02-05 | v1.0 | 初始版本，Core 层解耦重构完成 |

# 6 回归用例链接

相关测试文件：
- `src/Tests/GasTests/MudSc2AndYgoDemoTests.cs`
- `src/Tests/GasTests/AllocationTests.cs`
- `src/Tests/GasTests/ApplyForceEndToEndTests.cs`
- `src/Tests/GasTests/InteractiveWindowStressTests.cs`
- `src/Tests/GasTests/ResponseChainPresenterPipelineTests.cs`
- `src/Tests/GasTests/NavMeshTests.cs`
- `src/Tests/GasTests/Physics2DIntegrationTests.cs`

运行命令：
```bash
dotnet test src/Tests/GasTests/GasTests.csproj
```

预期结果：
```
已通过! - 失败: 0，通过: 118，已跳过: 0，总计: 118
```
