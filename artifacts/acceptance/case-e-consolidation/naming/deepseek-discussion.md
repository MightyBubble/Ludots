# 交叉复审：最终结论

已读 opus.log（另一审阅方）与 deepseek.log（我方首轮）。采纳 discussion.md 的 resolved facts 与合成建议。我方此前的反对点全部有对应撤回，见下。

## 1. 最终接受的映射

| 现状 | 最终名 | 出处 |
|---|---|---|
| `presenter.case_e.ring.attachment` | `presenter.case_e.selection_marker` | 双方一致 |
| `presenter.case_e.ring.preview` | `presenter.case_e.selection_preview_marker` | 双方一致 |
| `presenter.case_e.boxing_marker` | `presenter.case_e.box_select_rectangle` | 采纳 opus（我方原 `selection_rectangle` 撤回） |
| `presenter.case_e.collection_decoration` | `presenter.case_e.selection_rules` | 采纳 opus 收紧（我方 `selection_presentation_rules` 撤回） |
| 集合键 + graph 前缀 `box_hover` | `case_e.selection_preview`（图 `graph.case_e.selection_preview_tick/_clear`，id 与文件名一致） | 双方一致 |
| `case_e.ring.visible` | `case_e.marker.visible`（per-instance 可见闸门） | 采纳 opus（我方 `is_member` 撤回） |
| 行为槽 `body` | 保留 `body` | 采纳 opus（我方原批准撤回） |
| `Source/TargetIsSolePossessedRep` | 保留原名，只补 XML doc | 采纳 opus（我方原批准撤回） |
| 两处与 `activationCondition` 并存的 `activeByDefault:true` | 删除 | 双方一致，loader:2954 已证实安全 |
| schema 字段 / 复用机制 | 全保留，无别名 | 双方一致 |

同步更新（同一 delta）：annotated JSONC（配置镜像）、REPORT.md §3 引用配置标识的代码片段；**实测数字与原始日志冻结不动**。`selected` / `selection.pending` / `case_e.selectable` / `case_e.press.*` / `case_e.rect.*` / mesh 资产 `case_e.select_ring_slab` / `interaction.context.case_e.boxing` / `graph.case_e.box_begin|box_hit|box_commit` 全部不动。

## 2. 逐项对另一审阅方的异议表态

1. **`is_member` → `marker.visible`（撤回我方批准）。** opus 两点成立：① 读方合同是 `AssetBindingConfig.VisibilityParamKey`（0=隐藏/非0=允许输出），`is_member` 描述的是写方来源而非读方合同；② 同名键同时存在于 preview 实例，其镜像的是**预览集合**成员，`selection.is_member` 对 preview 实例语义为假——这是决定性一击：共享键不能用任一集合名命名。`marker.visible` 是集合中性的消费者闸门名，与座位 possession 激活条件组成文档化的双闸门，不新增门。命名后须在注释/annotation 钉住"写 0 保留实例、切回玩家不复活"的既有语义，防后人按 viewer 推导该值。
2. **保留 `body`（撤回我方批准）。** 文件内 5 个定义的主资产槽统一 `body`，只改两个破坏文件内约定；槽名是定义私有 token，零语义收益纯 churn。采纳。
3. **保留 `SolePossessedRep`（撤回我方批准）。** opus 论证正确：`TryGetSolePossessedRep` / `RequireSolePossessedRep` / `InputActionAttributeTargetKind.SolePossessedRep` / contextKey `solePossessedRep` 是全仓既有术语，只改两个枚举成员恰好在禁止别名前提下制造同概念两种拼法。清晰度缺口在注释：补 XML doc 说明"唯一座位基数断言（座位数≠1 失败、非观看者/CurrentPlayer 条件）"，并区分 `TryGet…` 非唯一返回 false 与 `Require…` 抛错的合同（resolved facts 已点名，勿混写）。
4. **`body` 槽之外我首轮另批准的 `attachment` 语义**：opus 补的论据更锋利——`ring.attachment` 的 `attachment` 与 Core 正式概念 `BehaviorKind.Attachment` / `AttachmentTarget.Bone` 撞词，而这两定义走 `anchor.offset` 非骨骼挂载。这是本轮最值得改的一处，原提案方向正确，维持 `selection_marker`。
5. **`box_select_rectangle`（撤回我方 `selection_rectangle`）。** 接受边界规则：**box = 输入手势/屏幕原语（context、box_begin/hit/commit、ScreenRect 矩形），selection = 集合角色与单位标记**。`selection_rectangle` 会把矩形从 box 手势家族拆散且丢"屏幕空间、真销毁（DestroyScopedPresenter）"两类事实；`box_select_rectangle` 还与动作名 `CaseE.BoxSelectBegin/End` 呼应。接受合成词（拼写 rectangle 而非 rect 无异议）。
6. **`selection_rules`（接受收紧）。** `presenters.json` 域内 `presentation` 冗余；纯 rules 宿主、同管集合与上下文事件，`selection_rules` 准确。

## 3. 残余阻断项

Resolved facts 已消除我方首轮两处阻塞（loader 未知枚举 fail-fast @4789；annotated/REPORT 的归属裁定）。现有 E2E 已能捕获键失配，按指示**不再新增镜像实现的测试**；10k 零分配与玩家切换既有测试重跑即可。因此**无命名层面的残余 blocker**，仅剩一条合并门禁性质的要求：

- **CI 修复出处须可证**：本 PR 将夹带两项与命名无关的既有失败修复（op registry 排序、缺生成的 node wiki 页）。合并前需在 PR 描述给出"在改动前 base 上复现同一失败"的日志/证据，证明是既有失败而非本 delta 引入后被"修复"掩盖；命名改动与两项修复分 commit 提交，便于复审与二分。若无法在 base 复现，即为 blocker，暂停合并。

## 4. 需要显式记录的不对称（写入 PR/commit 说明，不进代码注释）

- box（手势/屏幕原语）与 selection（集合/标记）的边界划分；
- `selected` / `selection.pending` 为既有约定键（scan 未见其他 mod 用，但为跨域约定词）不随 case E 改名，避免下一位"补齐"者蔓延范围；
- `case_e.marker.visible` 双闸门语义与 removal 写 0 保留实例的 retention 合同。

最终判定：**全部异议已收口，命名 delta 可执行**；唯一门禁条件是 §3 的 CI 修复出处证据。
