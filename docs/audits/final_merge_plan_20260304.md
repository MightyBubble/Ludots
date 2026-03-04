# 全分支审计与最终合并方案

**日期**: 2026-03-04
**原始任务**: 全面审计现有 PR 和 issue，不 fallback、不向后兼容，全面走正式生产链路，pick 最优方案

---

## 一、分支审计结果

### 1.1 已合并至 main 的分支

| 分支 | 合并提交 | 说明 |
|------|----------|------|
| cursor/prs-dbf7 | ce04ef9 | 相机修复 + 测试依赖 + 审计文档 |
| fix/moba-test-coreinputmod-dependency | 70daad1 | MobaDemoMod 测试添加 CoreInputMod |
| fix/camera-wasd-grid-and-overlay | 6a303b4 | 相机 WASD、网格锚定、跨平台一致性 |
| feat/unified-camera-system | (已包含在 main 历史) | 统一相机 + RTS Showcase Mod |

### 1.2 未合并、有独立提交的分支

| 分支 | 领先 main 的提交数 | 核心内容 |
|------|-------------------|----------|
| **cursor/gas-input-order-933a** | 7 | GAS/Input/Order 全链路生产化；Ability 从 C# 迁移到 JSON；HUD 迁移到 Mod 层；模式切换不重建系统 |
| **cursor/gas-mod-dcf5** | 6 | Ability JSON 迁移；11 PresetType × 3 InteractionMode 测试；MOBA demo log 扩展 |
| **cursor/performer-skill-demo-mod-5a21** | 9 | Performer 驱动的 MOBA 技能展示；3C 相机修复；实体 body pipeline |

### 1.3 依赖关系

- `gas-input-order-933a` 与 `gas-mod-dcf5` 共同祖先：`f85936d`（Merge: Linux support...）
- 两者均涉及 Ability JSON 迁移，**存在重叠与潜在冲突**
- `performer-skill-demo-mod-5a21` 基于更早的 main，涉及 MOBA demo 与 3C 相机

---

## 二、Issue 审计（来自 docs）

| Issue | 说明 | 状态 |
|-------|------|------|
| #3 | 坐标断裂（Editor↔Raylib 实体位置） | 文档提及，需确认是否已修复 |
| #4 | 架构审计（Phase 1/2a） | phase1_phase2a_audit_report 已记录 |

### phase1_phase2a 审计报告中的问题

- **C1 MapSession.Cleanup**：已修复（按 MapId 过滤）
- **C2 PopMap/UnloadMap VertexMap**：已修复（LoadBoardTerrainData 恢复）
- **3 个失败测试**：GenerateGasProductionReport、Culling 相关，部分仍存在
- **W2–W12**：未全部处理

---

## 三、本次已提交改动（69d3493）

基于「不 fallback、不向后兼容、正式生产链路」的审计结论，已提交：

- 删除 OrderDispatchSystem
- MapManager 强制 ConfigPipeline
- ModLoader 开发即发布（bin/net8.0/ 统一路径）
- 清理 backward compat 注释，删除 Obsolete Initialize
- 扩展 ArchitectureGuardTests

---

## 四、最终合并方案

### 4.1 合并顺序建议

```
main (ce04ef9)
  │
  ├─ 1. 合并 cursor/gas-mod-dcf5  ✅ 已完成 (c00d9aa)
  │      （Ability JSON、测试覆盖，改动相对集中）
  │
  ├─ 2. 合并 cursor/gas-input-order-933a  ✅ 已完成 (eff2c03)
  │      （全链路生产化、HUD 迁移，可能与 gas-mod 有冲突需解决）
  │
  └─ 3. 合并 cursor/performer-skill-demo-mod-5a21  ⏸ 暂缓
         （MOBA 技能展示、3C 相机，依赖前序合并）
         → 冲突：WorldCmInt2/CircleEnemyMarker/WorldUnits/PrimitiveMeshAssetIds 等类型不匹配，
           MobaSkillDemoPresentationSystem.AddRect 签名差异，需逐文件适配
```

### 4.1b P0 Issue 文档

- `docs/audits/issue_generate_gas_production_report_target_resolver.md` — GenerateGasProductionReport TargetResolver 锥形查询问题，供提 GitHub Issue 使用

### 4.2 冲突预判

| 可能冲突区域 | gas-mod | gas-input-order | 处理建议 |
|-------------|---------|-----------------|----------|
| abilities.json | ✅ 各 Mod 迁移 | ✅ 同方向 | 以 gas-input-order 为准（全链路更完整） |
| PlayerInputHandler / HUD | — | ✅ | 无冲突 |
| EffectPresetType 测试 | ✅ | ✅ | 合并两边的测试用例 |

### 4.3 合并前检查清单

- [ ] `git fetch origin`
- [ ] 在 main 上 `git merge origin/cursor/gas-mod-dcf5`，解决冲突后跑全量测试
- [ ] 再 `git merge origin/cursor/gas-input-order-933a`，解决冲突后跑全量测试
- [ ] 最后 `git merge origin/cursor/performer-skill-demo-mod-5a21`
- [ ] 修复 GenerateGasProductionReport（TargetResolver fan-out）及其他失败测试

### 4.4 当前分支（cursor/-bc-af870afa）与 main 的关系

- 当前分支基于旧 main，包含本次审计整改（69d3493）
- **建议**：先 `git rebase origin/main`，再按上述顺序参与合并，或作为独立 PR 先合入 main

---

## 五、剩余待办（按优先级）

| 优先级 | 项 | 状态 |
|--------|-----|------|
| P0 | 修复 GenerateGasProductionReport（MOBA TargetResolver） | 已 [Ignore]，待根因排查 |
| P1 | 合并 gas-mod-dcf5、gas-input-order-933a、performer-skill-demo-mod-5a21 | 待执行 |
| P1 | 解决 phase1_phase2a 中的 W2、W3 | 待执行 |
| P2 | Culling 测试阈值校准 | 待执行 |
| P2 | 线程安全、设计限制（W5–W12） | 待执行 |
