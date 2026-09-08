已核实 loader 两处（`src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs`）：带 `activationCondition` 时 `ActiveByDefault` 强制 false，缺省也是 false；`TryParseDefinedEnum` 走 `Enum.TryParse(ignoreCase: false)` + `Enum.IsDefined`，且显式拒绝纯数字字符串。未知 inline 名抛错，不会静默回退 `None`。DeepSeek 的阻断项 1 消解，我的"未验证项"也随之落地。

同时修正我上一轮的两处：`graph.case_e.box_commit` / `selection_commit` 写 `selection.pending` / `selected`，不读 `box_hover`，我当时的推测不成立；以及我把 `SolePossessedRep` 描述成"座位数 ≠ 1 就失败"混了两份合同——`RequireSolePossessedRep` 抛错，而 presenter 路径走 `TryGetSolePossessedRep`，非唯一时返回 false → `Entity.Null` → 相关行为全部停用，不抛错（`PresenterBehaviorSystem.RefreshPossessionActivation`；那里唯一的抛错是 `_globals == null`）。补注释时不能把这两条写成一句。

## 1 最终接受映射

| 原名 | 最终名 |
|---|---|
| `presenter.case_e.ring.attachment` | `presenter.case_e.selection_marker` |
| `presenter.case_e.ring.preview` | `presenter.case_e.selection_preview_marker` |
| `presenter.case_e.boxing_marker` | `presenter.case_e.box_select_rectangle` |
| `presenter.case_e.collection_decoration` | `presenter.case_e.selection_rules` |
| 集合键 `case_e.box_hover` | `case_e.selection_preview` |
| graph id / 文件名 `graph.case_e.box_hover_tick` / `_clear` | `graph.case_e.selection_preview_tick` / `_clear` |
| 参数键 `case_e.ring.visible` | `case_e.marker.visible` |
| 行为槽 `body` | 不改 |
| `Source/TargetIsSolePossessedRep` | 不改，只补 XML doc |
| 两个标记槽的 `activeByDefault: true` | 删除 |
| `activationCondition` / `visibilityParamKey` / `scopeSource` / `AssetBinding` / `Scoped` | 不改 |

`box_select_rectangle` 接受综合稿：既保住 box 手势族（`interaction.context.case_e.boxing`、`graph.case_e.box_begin` / `box_hit` / `box_commit` 本轮不动），又对作者拼全 rectangle。据此驳回 DeepSeek 的 `selection_rectangle`：它把屏幕空间矩形塞进 `selection*` 族，而这个定义是唯一走真销毁（`DestroyScopedPresenter`）的实例，与两个世界空间标记不同类。

`selection_rules` 采用，`presentation` 在 Presenter 域内是冗余词；DeepSeek 的 `selection.presentation` 同理不取。

## 2 对分歧的明确处理

`is_member` 驳回，维持 `case_e.marker.visible`。DeepSeek 把该 int 定性为"集合成员资格镜像"，这是写方视角；读方是 `AssetBindingConfig.VisibilityParamKey`，合同是 0=隐藏、非 0=允许输出。两点不成立：同一键也存在于预览实例的黑板里，那里的成员语义是预览名单而非 `selection`，`selection.is_member` 对预览实例直接是错的；而且它与 `activationCondition` 是两道独立闸门，`is_member` 读起来像可由成员态推导的纯数据标志，正好诱导后人把它改成派生值，那会破坏"离开集合写 0 后切回当前玩家不复活"这条已验收行为。值得注意的是 DeepSeek 自己在其 §2.1 把同一条列为最大长期风险——那个风险恰恰是新名字带来的，靠文档钉死不如不引入。`marker.visible` 保持消费者合同，且不谎称属于哪个集合。

枚举不改。有了严格解析这条事实，改名不再有"漏改静默失效"的风险（会 fail-fast），所以这不再是安全性问题，而是术语唯一性问题：`SolePossessedRep` 是全仓既有词（`TryGetSolePossessedRep`、`RequireSolePossessedRep`、`InputActionAttributeTargetKind.SolePossessedRep`、contextKey `solePossessedRep`，`client-local-seat-and-logic-view.md` §3.3 / §3.6 点名）。只展开 Presenter 侧两个成员，等于给同一跨域概念造第二种拼法，并让全仓引用该 inline 名的 mod JSON 变成破坏性改动——收益只是长度。DeepSeek 的偏好项 3 实际同意本 PR 不做 infra 全量改名，那么正确结论是两侧都不改，而不是先在一侧留个孤名。清晰度缺口在注释：`InlineConditionKind.cs` 的 `<summary>` 要写明这是唯一操控座位的基数条件，与观看者 / CurrentPlayer 无关，并按上面区分 TryGet 与 Require。

槽名 `body` 不改。`presenters.json` 五个定义的主资产槽统一是 `body`，只改两个破坏文件内约定，且 `marker` 与新定义名重复。槽名是定义内私有标识，零收益。

测试上采纳你的意见，撤回我上一轮那条"断言 graph collectionKey ⊆ presenters 规则 key"的新增测试——它镜像实现，而现有预览 E2E 已经能抓住半改（只改写方 → 预览标记不出现；只改读方 → 黄标记不清除）。10k 零分配与切换座位的既有测试重跑即可，不新增。

文档面：`presenters.annotated.jsonc` 自述"与运行配置一致"，是活镜像，随改；REPORT.md 里的配置片段同改；实测数字与原始日志一律不动。这一条同时回答 DeexpSeek 的阻断项 2——不是"接受过期"，是活文档随改、冻结证据不动。

## 3 剩余阻断与需点名的取舍

一个真阻断：集合键是否进存档。`EntityCollectionStore` 地址是 `(participantRep, collectionKey)`，若 collectionKey 以字符串进存档域，`case_e.box_hover → case_e.selection_preview` 在禁止 fallback 的前提下会让旧存档直接失败。这是 showcase mod 的键，代价可能可接受，但必须在 PR 里点名接受，不能默默改。我未读存档域代码，无法自证。

一个流程阻断：PR #1473 当前 target 是 `codex/case-e-10k-showcase`。要合进 main 得先 retarget，并确认它不夹带那条分支的未评审内容。

一个范围提醒：把 op registry 排序与生成的 node wiki 页两个 CI 修复塞进"命名唯一 delta"的 PR，会让 diff 不再是纯命名。同 PR 可以，但要拆成独立 commit 并在描述里分节，否则后续 bisect 和评审都要为此付账。

范围边界建议在 PR 描述里写死一句，防下一个人来"补齐"：几何 / 手势 / 形状词保留（`box_*`、`case_e.select_ring_slab`、`case_e.rect.*`、`case_e.press.*`），集合角色词用 `selection*`；`selected` / `selection.pending` 是跨 mod 约定键，本轮不动，与 `case_e.selection_preview` 的不对称是有意的。`mod.json` 的 description 明文列了 `case_e.box_hover`，属于必改落点。
