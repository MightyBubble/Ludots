# Case E 框选链 · 10k 人口实测

数据行：`case-e-selection-scale.csv`（每次跑追加一行，utc 唯一）。

- 场景：`case_e_selection_field`，10k 世界实体（100 名 team1 marine 可选 + 9894 team2 填充 + 6 授权实体）。
- roster_sync 每出生事件做一次 **O(N) 全量重扫**（QueryAllMapEntities→team/template 过滤→replace）。
- 本趟：单次重扫 0.13ms；100 事件流合计 5.14ms（每事件 0.05ms）；
  完整拖拽 90.54ms（20 次扫框，单拍峰值 41.77ms）；全幅框 77、半幅 49。
- 缩放契约：出生/死亡事件驱动（零轮询），但**每次事件 = O(N) 重扫**——10k 单波 burst 若在一拍涌入
  N 个出生事件，则那拍退化为 O(N²)。这是当前事件驱动方案的已知成本面（增量维护是后续优化方向，
  非本 PR 范围）。
- **发现（本次实测钉出）**：`QueryAllMapEntities` 受 `GraphVmLimits.MaxTargets=256` 硬顶，全图查询只取前
  256 个 MapEntity → 10k 人口下候选集被截断（世界 104 支可选，roster 只进 77 支）。
  10k/100 玩家目标需先解除该顶（分页 / 增量维护），否则候选集完整性不成立——本 PR 不越界去改 VM。

_生成：CaseESelectionScalePressureTests（headless 实测）。_