# Merge 状态与 ModLoader 开发即发布审计

**日期**: 2026-03-04
**范围**: Merge 状态核查、ModLoader 开发即发布方案、测试修复

---

## 一、Merge 状态

### 1.1 近期已合并 PR（origin/main）

| 提交 | 说明 |
|------|------|
| ce04ef9 | merge: cursor/prs-dbf7 → main — 相机修复 + 测试依赖 + 审计文档 |
| 70daad1 | merge: fix/moba-test-coreinputmod-dependency — MobaDemoMod 测试添加 CoreInputMod |
| 6a303b4 | merge: fix/camera-wasd-grid-and-overlay — 相机 WASD、网格锚定、跨平台一致性 |
| 8f46ecd | feat: cross-platform camera consistency |
| 906023b | docs: add PR integration audit report |

### 1.2 当前分支与 main 关系

- 当前工作分支：`cursor/-bc-af870afa-...`
- **落后于 origin/main**：main 已合并上述 PR，当前分支尚未 rebase/merge
- 建议：提交前先 `git fetch origin && git rebase origin/main` 或 merge main

---

## 二、ModLoader 开发即发布方案（已实现）

### 2.1 设计原则

- **开发即发布**：无 Debug/Release 路径区分，统一输出
- **无条件编译**：不引入 `#if DEBUG` 等分支
- **UGC 友好**：Mod 构建一次，开发与发布行为一致

### 2.2 实现

| 组件 | 变更 |
|------|------|
| `src/Mods/Directory.Build.props` | 新增，统一 `OutputPath=bin/net8.0/` |
| 所有 `mod.json` | `main` 从 `bin/Release/net8.0/` 改为 `bin/net8.0/` |
| `ModLoader.TryResolveMainAssemblyPath` | 仅使用 manifest.Main 路径，无 fallback |
| `ModLoader` candidates（无 main 时） | 仅 `bin/net8.0/`、`bin/net8.0-windows/` |

### 2.3 效果

- `dotnet build`、`dotnet build -c Release`、`dotnet test` 均输出到 `bin/net8.0/`
- ModLoader 不再依赖 Debug/Release 路径切换

---

## 三、测试状态

### 3.1 全量 GasTests

- **通过**: 635
- **失败**: 1

### 3.2 剩余失败

| 测试 | 失败原因 |
|------|----------|
| `GenerateGasProductionReport` | MOBA 场景：`TargetResolver creates fan-out commands - Count=0 Dropped=0`。GAS TargetResolver 锥形查询或配置问题，与 ModLoader 无关 |

### 3.3 已修复

- `RtsDemoLog`：RtsDemoMod 未纳入 GasTests 引用，构建后输出到 bin/net8.0/，已补 ProjectReference
- `Codebase_MustNotContainCompatibilityOrFallbackMarkers`：移除 `legacy one-shot` 模式，修正 PerformerRuntimeSystem 注释

---

## 四、本次改动清单（未提交）

- 删除 `OrderDispatchSystem.cs`
- MapManager 强制 ConfigPipeline，移除 VFS fallback
- ModLoader 移除 debug fallback，纯 manifest.Main 路径
- 清理 backward compat/legacy 注释
- 删除 Obsolete `Initialize` 重载，`WorldRuntime` 迁移至 `InitializeWithConfigPipeline`
- `Directory.Build.props` + 28 个 mod.json 更新
- `GasTests.csproj` 增加 `RtsDemoMod` 引用
- `docs/developer-guide/02_mod_architecture.md` 增加「开发即发布」说明
