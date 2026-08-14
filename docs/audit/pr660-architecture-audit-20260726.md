# PR #660 架构规范审计报告（远端最新 head）

- 审计日期：2026-07-26
- 审计工作区：`C:\001_AI\_codex_audit\Ludots-pr660-72364d9-reaudit-20260726`（detached @ PR head）
- PR head：`948640f6aad264ef8b0d5a115dfe749d71526a7d`
- 合并基：`origin/main` @ `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`
- 规模：118 commits / 475 files / +54,175 / −27,285
- 交付 SSOT：#689（OPEN）；历史票 #644–#688 已关闭
- 审计依据：`gitbook/contributing/ai-assisted-development.md`、四个禁止、ECS 硬性约束、六边形架构、一切皆 Mod
- 可视化摘要：Cursor canvas `pr660-architecture-audit.canvas.tsx`（项目 canvases 目录）

## 一、独立验证结果

| 验证项 | PR/#689 声称 | 独立复跑 |
|---|---|---|
| ArchitectureTests 全量 | 历史门禁曾宣称 | **188/188 PASS（4m29s，Release）** |
| 聚焦 GasTests（Response/Planner/Road/Capacity 等） | 37/37 + 160/160（本地） | **103/103 PASS（1m19s，Release，SDK 9.0.312）** |
| 本轮 7 门禁精确用例名 | 待远端勾选 | **9/9 PASS** |
| 远端 CI `solution-verify` | 等待 | **SUCCESS @ 948640f6** |
| `artifacts/ci-audit/pr660` | 像最终收口 | **过期**：叙事仍绑旧工作树 / “final closeout”，未刷新到本 head |
| Collection `MemberScratchCapacity` | 本轮门禁 | 生产已 fail-fast；**无锁定测试** |

结论：架构守卫与本轮所有权补丁的聚焦回归可复现为绿。不能把仓库内旧 CI 产物当作本 head 的完成证明。

## 二、Issue 演进与卫生

- #689 是唯一存活交付 SSOT，正文明确：**未收口、不可合并**；7 项新门禁留给远端审计勾选——口径诚实。
- 2026-07-19 批量关闭 #644–#688，且声明“迁移历史 ≠ 验收完成”，但 GitHub `state_reason=completed`，PR 正文仍列大量 `Closes`。
- #669 曾重开纠偏后并入 #689，说明早期 completed 关闭过早。
- Issue 卫生分：**5/10**。建议：`Closes` → `Refs`，完成证明只留在 #689 勾选。

## 三、本轮 14 文件所有权补丁（相对 `72364d91`）

方向正确：连锁响应唯一结局与路径释放、续接抛错取消预约、EntityIntake 拒绝发 Failed 终态、Road 真实提交结果、`commandIntentScratchCapacity` 进配置 SSOT、Collection scratch 满硬失败。

| 门禁 | 裁定 |
|---|---|
| 连锁溢出类型化失败 | PASS（代码+测） |
| 路径槽位恰好释放 | PASS（代码+测） |
| 续接预检抛错取消预约 | PASS（代码+测） |
| Queued→EntityIntake 拒绝→Failed | PASS（代码+测） |
| Road 单条真实失败结果 | PASS（代码+测） |
| scratch 容量配置注入 | PASS（代码+测） |
| Collection member scratch fail-fast | WEAK（代码有，测无） |

## 四、架构合规

### 4.1 六边形 / 分层 — PASS
Core 未见平台 API 泄漏；AbilityExec 热路径 `World.Add/Remove` 由 ArchitectureTests IL 守卫强制。

### 4.2 四个禁止 — 大体 PASS，有残留
- 主链缺服务/容量不足走类型化失败，非静默跳过。
- 残留：输入/命令路径 `Array.Resize`；瞄准表现仍可读 `VisualTransform`（订单规划层已禁，合规）；`InputOrderMappingSystem` 默认 4096 软兜底。

### 4.3 ECS 硬约束 — 大体 PASS
`EffectPhaseSideEffectTransaction` 等 staging 事务边界成立；热路径直接结构变更主犯已压住。扩容残留见上。

### 4.4 文档 / 自审 — 部分 PASS
`gas-order-input-runtime-contract.md` 在册；`gas-composition-gate.md` 头部乱码且未覆盖本轮容量字段；CI 审计产物措辞与 #689“未完成”冲突。

### 4.5 变更规模 — FAIL（可审查性）
单 PR 混合 ordering / spawn / graph / road / showcase / 治理测试，违背“沿现有管线做增量”的可审查精神。

## 五、评分

**83 / 100（B）——架构主线正确，流程与证据未收口，不建议合并**

| 维度 | 得分 |
|---|---|
| 六边形架构 / 分层 | 20/20 |
| 四个禁止 | 17/20 |
| ECS 硬性约束 | 17/20 |
| 测试与证据可复现性 | 17/20 |
| 文档与自审流程 | 7/10 |
| 变更规模与提交卫生 | 5/10 |

相对 2026-07-25 审计（head `832481ec`，88 分）：本轮所有权补丁加分，但 Issue `Closes` 污染、证据过期、残留合同缝与超大体量把分数拉到 83，且合并建议从“修小项后可合”改为“明确未收口”。

## 六、合并前最少动作

1. PR 正文 `Closes` 改为 `Refs`；完成证明只写 #689 勾选。
2. 刷新 `artifacts/ci-audit/pr660` 到 `948640f6`，去掉 “final closeout” 措辞。
3. 补 Collection `MemberScratchCapacity` 锁定测试。
4. 对残留 `Array.Resize` / 瞄准 `VisualTransform` 显式开后续票或本轮修完。
5. 在 #689 评论绑定本 head 的 Features|Integration 复跑结果。
