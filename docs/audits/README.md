# 审计记录

本目录存放审计、验收、收敛和阶段性回顾文档。这里提供证据和结论，但不是规范来源，也不定义当前实现。

## 1 目录

* [Projection Marker 恢复审计](camera_acceptance_projection_marker_recovery.md)
* [端到端验收测试记录](e2e_acceptance_tests.md)
* [Phase 1 / Phase 2A 审计报告](phase1_phase2a_audit_report.md)
* [Presentation Hotpath Harness 优化验证](presentation_hotpath_harness_optimization_validation.md)
* [PR 集成审计](pr_integration_audit.md)
* [PR92 TimeFlow Core 主线落地计划](pr92_timeflow_core_mainline_delivery.md)
* [PR73 合并与架构审计](pr73_merge_architecture_audit.md)
* [RFC-0065 PR581 Workflow Closeout](rfc_0065_pr581_workflow_closeout.md)
* [最近提交审计与端到端交互验收](recent_commit_audit_and_e2e_showcase.md)
* [版本收敛处置矩阵](convergence_disposition_matrix.md)
* [PR895 图基建 + LSW 审计交接（会话全量）](pr895_graph_infra_and_lsw_audit_handoff.md)
* [PR895 图基建 + LSW 架构审计（交叉审计）](pr895_graph_infra_and_lsw_architecture_audit.md)
* [PR911 审计需求交接](pr911_funclib_actionlib_audit_handoff.md)
* [PR911 FuncLib/ActionLib 架构审计（#914 SSOT）](pr911_funclib_actionlib_architecture_audit.md)
* [PR911 审计修复清单（#913+#914 合并 / Epic #915）](pr911_audit_fix_checklist.md)
* [main 图能力收口审计需求（#932 落地后，按领域/阶段）](pr932_graph_landed_audit_handoff.md)
* [PR932 main 图能力收口架构审计（SSOT）](pr932_graph_landed_architecture_audit.md)
* [GAS + Graph VM 架构审查（SSOT）](gas_graph_architecture_review.md)
* [GAS + Graph VM 架构修复计划（Epic + 子任务）](gas_graph_architecture_fix_plan.md)
* [S 第一批架构审计（#944 / #946 / #943 / #945）](s_batch1_architecture_audit.md)
* [S14 分层物理化设计（第一阶段 · 只出设计）](s14_layering_physicalization_design.md)
* [S 第二批 + S9 架构审计（#951 / #952 / #950 / #948 / #953）](s_batch2_s9_architecture_audit.md)
* [GAS + Graph 修复计划落地后审计需求（#942 全票合入）](s_plan_landed_audit_handoff.md)

## 2 使用边界

* 审计结论如需成为正式规则，必须回写到 `docs/conventions/`。
* 审计中提及的当前实现，如需成为正式描述，必须回写到 `docs/architecture/` 或 `docs/reference/`。

## 3 相关文档

* 文档总览：见 [../README.md](../README.md)
* 开发规范：见 [../conventions/README.md](../conventions/README.md)
* 架构文档：见 [../architecture/README.md](../architecture/README.md)
