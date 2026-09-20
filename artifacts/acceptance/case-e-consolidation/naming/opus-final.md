已读 final.diff（全 1152 行）、生产 presenters.json、annotated jsonc、新旧 graph 文件、REPORT.md、生成器 `scripts/generate-graph-op-node-wiki.py`，并对照了 `graph.case_e.box_hit.json` 与两页已生成 wiki。映射本身落地一致，不重开命名讨论。结论：**有条件 PASS，2 个阻断项**。

## 阻断一：两个重命名后的 graph 文件不在 diff 里

final.diff 只有删除，没有新增：`graph.case_e.box_hover_clear.json` / `box_hover_tick.json` 各一段 `deleted file`，路径序上本该紧随其后的 `graph.case_e.selection_preview_clear.json` / `_tick.json` 完全缺席，diff 直接跳到 `assets/Input/interaction_context_profiles.json`。

这两个文件在工作区里是存在的（我读到了内容，id 与 `collectionKey` 都已是 `case_e.selection_preview`，`InvokeGraph` 仍指向未改名的 `graph.case_e.box_hit`，正确）。文件存在而 diff 无新增段，符合"未 `git add`、`git diff` 不显示未跟踪文件"的特征。

若按此 diff 成commit：graphs 目录只剩删除，`interaction_context_profiles.json` 的 `triggers: graph.case_e.selection_preview_tick` 与 `onDeactivated: graph.case_e.selection_preview_clear` 引用不存在的图 → 装载 fail-fast，整个 boxing 手势起不来。本地测试全绿也抓不到，因为本地文件在。

要做的只有一件：确认 `git status` 里这两个文件已跟踪并进入本次 commit，`git show --stat` 能看到两个 `A`。同理请顺带确认没有其他新增文件（新证据产物等）漏 add。

## 阻断二：生成器改了正文模板，却只刷新两页

`scene_section` 的措辞被改写（`final.diff` 第 337-342 行）：

- 旧：`上面的录像不是特效，是画廊里一张真实可跑的图（…）。照抄这张图，你就能在自家 mod 里得到同样的效果：`
- 新：`这场演示使用画廊里的作者图 …。下列调用顺序可供编写自己的图时参考：`

这段对**每一页**生效，不只对缺失的两页。已核实分歧真实存在：新生成的 `QueryScreenRegionCollection.md` 是新措辞，而未刷新的 `InvokeGraph.md` 仍是旧措辞。也就是说现在跟踪的 wiki 页里两种口径并存，且"跑生成器 → 产物应无 diff"这类校验会对所有其余页面报差异——正是本次要修的那条 CI 检查的同族。

两条出路，选一条：把 `scene_section` 措辞改动撤回（本轮只修 driver 标签 + 媒体缺失提示，产物差异就只剩那两页 + index），或者跑全量重新生成并把所有页面纳入 commit。前者更符合"only the two missing pages and index are refreshed"这个已声明的范围。

`media_section` 的重构不在此列：`has_media=True` 分支与旧模板逐字节一致，有录像的页面产物不变，这部分没问题。

## 应修一项：`has_media` 与 `require_media` 门槛不一致

`require_media` 判的是存在 **且** `play.mp4 ≥ 20000 B`、`poster.png ≥ 1000 B`；新增的 `has_media` 只判 `is_file()`。于是 authoring 模式下一个 0 字节占位或 LFS 指针文件会被当成"有录像"，照旧输出 `<video>` 标签——恰好是这次改动要消除的坏标记。让 `has_media` 复用 `require_media` 的同一组尺寸判断即可（抽一个 `media_present(repo, op) -> bool`，`require_media` 在其为假时抛错）。

## 其余观察（不阻断）

`graph_node_op_coverage.registry.json` 把两个 op 的 `authorableKinds` 改成 `TriggerGraph, Script` / `TriggerGraph, Query`，而生成器在掩码含 `|` 时按 `ALL_KINDS`（…Query, Script, TriggerGraph）归一化，生成页面写的是 `Query / TriggerGraph`。同一事实两处顺序不同。只要没有校验拿这两处互相比对就只是观感问题，但既然这次动的就是排序类 CI，请确认权威源是哪一处。

`gitbook/architecture/graph-callable-function-vision.md` 那两行随改名更新了键名，但陈述本身有错且是改名前就错的：`graph.case_e.box_hit` 里没有 `WriteCollection`，它是 `Query` 图，尾节点是 `QueryScreenRegionCollection(case_e.selectable)` + `HaltReturnInt`，写预览集发生在调用方 `selection_preview_tick`。改名后这句话的副作用更容易被当真——照它去 grep `box_hit` 找 `selection_preview` 会一无所获。既然已经动了这两行，顺手改成"命中函数返回 TargetList，调用方 `selection_preview_tick` 写 `case_e.selection_preview`"更省事。

wiki README 的 diff 里夹了两处与命名无关的存量漂移回填（`DispatchMapEvent` 条目措辞、章节标题 `集合透传 → 集合写入`）。这些来自 vignette 与 `DRIVER_LABELS` 中早已存在的值，是产物落后于生成器，不是本轮引入。PR 描述里点一句，免得评审误以为命名改动碰了这些。

annotated jsonc 与生产配置逐字段一致（已逐段核对两个标记定义、`selection_rules` 六条规则、`box_select_rectangle`），`activeByDefault` 同步删除；`box_select_rectangle` 的 screenRect 槽正确保留了 `activeByDefault: true`（它没有 `activationCondition`，删掉会让矩形永不显示）。副作用是 annotated 现在不再说明这两个标记槽的初始激活状态；原来那句"加载器会将初始值覆盖为 false"没了替代。可选补一句注释在 `activationCondition` 行，说明带条件的槽初始为关闭。

REPORT.md 的配置节选已改成 `case_e.marker.visible` 且同步删了 `activeByDefault`，实测数字与日志段落未动，符合"活文档随改、冻结证据不动"。

测试里的残留只是名字层面：新 graph 文件内节点 id 仍叫 `write_hover`（图内私有，无引用），`AssertMarkerVisible` / `AssertMarkerHidden` / `CountVisibleMarkersOwnedBy` 的形参仍叫 `ringDefId`，断言文案仍用"蓝环/黄环"（描述颜色外观，可留）。都不影响行为。

## 我未能验证的

没有 shell，无法自证全仓已无 `case_e.box_hover` / `case_e.ring.visible` / 旧 presenter id 残留，只能确认 diff 覆盖到的落点与我读过的文件是干净的。也无法确认那两个新 graph 文件的 git 跟踪状态——这正是阻断一需要你们用一条 `git show --stat` 回答的。10k 零分配与切换座位的测量结果我尚未见到，PASS 以它们绿为前提。
