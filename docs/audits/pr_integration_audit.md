# PR 整合审计报告

**审计日期**：2026-03-04  
**基准分支**：`cursor/prs-dbf7`（含 GAS mod 架构质量 #8）  
**目标**：评估各 PR 分支的整合策略与合并顺序

---

## 一、分支与 PR 概览

| 分支 | 相对 main 提交数 | 主要内容 | 与 prs-dbf7 关系 |
|:--|:--|:--|:--|
| `cursor/prs-dbf7` | 1 | GAS mod 架构质量 (#8) | 当前基准 |
| `fix/camera-wasd-grid-and-overlay` | 2 | 相机 WASD 相对移动、A/D 方向、网格锚定、HUD 边界防护 | 基于 #8，+1 提交 |
| `fix/moba-test-coreinputmod-dependency` | 2 | MobaDemoMod 测试添加 CoreInputMod 依赖 | 基于 #8，+1 提交 |
| `cursor/gas-input-order-933a` | 5+ | 全链路生产化、Ability→JSON、InjectAction API、HUD 迁移、删除 RtsShowcaseMod | 基于更早 main，分叉严重 |
| `cursor/gas-mod-dcf5` | 5+ | Ability JSON 迁移、11 presets 测试、MOBA demo log | 与 #8 内容高度重叠 |
| `cursor/performer-skill-demo-mod-5a21` | 5+ | MobaSkillDemo 演示、实体 body pipeline、HUD overlay、fireball 目标 | 基于 #8 或相近 |

---

## 二、依赖关系与冲突分析

### 2.1 共同祖先

- `fix/camera-wasd-grid-and-overlay`、`fix/moba-test-coreinputmod-dependency` 均以 `13b1f3a`（#8）为共同祖先
- 二者与 `cursor/prs-dbf7` 无冲突，可**直接合并**

### 2.2 试合并结果

| 合并目标 | 结果 |
|:--|:--|
| `prs-dbf7` + `fix/camera-wasd-grid-and-overlay` | ✅ 无冲突 |
| `prs-dbf7` + `fix/moba-test-coreinputmod-dependency` | ✅ 无冲突 |

### 2.3 重叠与分叉

- **cursor/gas-mod-dcf5**：Ability JSON 迁移、ARPG/RTS/4X/TCG 注册、测试扩展等已并入 #8，该分支**大部分内容已覆盖**
- **cursor/gas-input-order-933a**：基于 `f85936d`，与当前 main 分叉较深；且**删除了 RtsShowcaseMod**（含 vtxm、实体、地图等），需业务决策是否采纳
- **cursor/performer-skill-demo-mod-5a21**：新增 `MobaSkillDemoPresentationSystem` 等，与 #8 的 Moba 改动有重叠，需逐文件比对

---

## 三、整合策略建议

### 3.1 优先级 1：立即合并（低风险）

```bash
# 1. 合并相机修复
git merge origin/fix/camera-wasd-grid-and-overlay -m "fix: 相机 WASD 相对移动、A/D 方向、网格锚定与 HUD 边界防护"

# 2. 合并 Moba 测试依赖修复
git merge origin/fix/moba-test-coreinputmod-dependency -m "fix: add CoreInputMod to MobaDemoMod test mod paths"
```

**理由**：改动小、无冲突、基于 #8，可直接提升主分支质量。

### 3.2 优先级 2：选择性采纳（需人工评审）

**cursor/performer-skill-demo-mod-5a21**

- 新增：`MobaSkillDemoPresentationSystem`、实体 body pipeline 修复、HUD overlay 堆叠、fireball 目标选择
- 建议：以 **cherry-pick 或逐文件对比** 方式提取有用修复，避免整分支合并带来的冗余

### 3.3 优先级 3：暂缓或废弃

**cursor/gas-mod-dcf5**

- 与 #8 高度重叠，**建议关闭/废弃**，不再合并

**cursor/gas-input-order-933a**

- 改动大（71 文件、+2255/-1549 行）、删除 RtsShowcaseMod、分叉深
- 建议：
  1. 若 RtsShowcaseMod 删除为有意决策，需在 main 上单独讨论
  2. 若需保留，则从该分支 **选择性提取**：InjectAction API、HUD 迁移、AutoDemo 框架等，以独立 PR 形式合并

---

## 四、推荐合并顺序

```
cursor/prs-dbf7 (当前)
    │
    ├─ merge fix/camera-wasd-grid-and-overlay
    │
    ├─ merge fix/moba-test-coreinputmod-dependency
    │
    └─ (可选) cherry-pick performer-skill-demo-mod 中的修复提交
```

---

## 五、执行检查清单

合并后建议执行：

- [ ] `dotnet test src/Tests/GasTests/GasTests.csproj`
- [ ] `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj`
- [ ] `dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj`
- [ ] ModLauncher 全 Mod 构建与运行验证

---

## 六、相关文档

- [Phase 1 + Phase 2a 架构审计](phase1_phase2a_audit_report.md)
- [端到端验收用例](e2e_acceptance_tests.md)
- [Camera 标准规范](../developer-guide/16_camera_standards.md)
