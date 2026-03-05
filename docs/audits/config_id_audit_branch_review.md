# 分支审计：feat/config-id-audit

**审计日期**：2026-03-04  
**分支**：`feat/config-id-audit`（基于 `origin/main`）  
**提交**：2 个（99c66c1 feature showcase demos + cc4a13f Config ID 审计）

---

## 一、改动概览

### 1.1 Config ID 审计提交（cc4a13f）

| 类别 | 改动 |
|------|------|
| **硬性约束** | IdField 统一为 `id`；ID 值必须为 JSON 字符串；无 fallback、无向后兼容 |
| **ConfigMerger** | `TryReadId` 仅精确匹配 idField，校验 `JsonValueKind.String` |
| **config_catalog** | 全部 IdField 改为 `id` |
| **数据文件** | 所有 ArrayById 配置的 id 字段改为字符串 |
| **performers.json** | 整数 id 改为字符串（如 `"9010"`, `"5001"`） |
| **ModManifestJson** | ModId 禁止包含 `:` 和 `/` |
| **文档** | 新增 `18_config_id_standards.md` |
| **测试** | ConfigCatalogTests、ConfigMergeE2ETests、ConfigMergerEntriesTests、AiConfigLoaderTests 已更新 |

### 1.2 文档规范

- `18_config_id_standards.md` 符合 `00_documentation_standards.md` 规范
- 编号 18、README.md 目录已同步

---

## 二、测试结果

| 测试组 | 结果 |
|--------|------|
| Config 相关（ConfigMerge、ConfigCatalog、ConfigMerger、AiConfigLoader） | 33 个通过 |
| PerformerDefinition 合并（Scenario_PerformerDefinition_NumericId） | 通过 |
| 全量 GasTests | 635 通过，1 失败 |

### 2.1 失败测试：MobaDemoLog

**错误**：`Index is out of bounds, component WorldPositionCm with id -1 does not exist in this chunk`

**调用链**：`SpatialQueryService.QueryRadius` → `_positionProvider(buffer[i])` → `World.Get<WorldPositionCm>(entity)`，其中 entity 为无效 id -1。

**根因**：该失败在 **feature showcase demos 提交（99c66c1）** 上已存在，与 Config ID 审计无关。Config ID 审计本身未引入该回归。

---

## 三、合并冲突

- 与 `origin/main` 的 `git merge --no-commit --no-ff` 自动合并成功，无冲突。

---

## 四、审计发现与修复

### 4.1 遗漏的整数 id（已修复）

`MobaDemoMod/assets/Presentation/performers.json` 中 `id: 5002` 仍为整数，ConfigMerger 会跳过该条目（因 `TryReadId` 仅接受字符串）。已修复为 `"id": "5002"` 并提交。

### 4.2 代码质量

- 核心逻辑：`ConfigMerger.TryReadId` 实现简洁，符合「唯一链路、无 fallback」设计
- `ModManifestJson.ParseStrict` 校验逻辑清晰
- 文档与代码一致

---

## 五、合并建议

| 项目 | 结论 |
|------|------|
| Config ID 审计改动 | 可合并，逻辑正确、文档完整 |
| MobaDemoLog 失败 | 由 feature showcase 提交引入，建议单独排查并修复 |
| 合并冲突 | 无 |

**建议**：

1. 若 PR 仅包含 Config ID 审计，可合并；MobaDemoLog 失败需在 feature showcase 分支修复。
2. 若 PR 包含两个提交，建议先修复 MobaDemoLog 再合并，或拆分为两个 PR：先合并 feature showcase（并修复 MobaDemoLog），再合并 Config ID 审计。

---

## 六、相关文档

- [Config ID 格式规范](../developer-guide/18_config_id_standards.md)
- [数据配置类与通用合并策略最佳实践](../developer-guide/12_config_data_merge_best_practices.md)
- [Mod 架构与配置系统](../developer-guide/02_mod_architecture.md)
