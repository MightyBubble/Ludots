PASS.

- 阻断一解除：commit-proof 显示两个图为 `{old => new}` 重命名（各 4 +-），非删除；`interaction_context_profiles.json` 的引用有落点。
- 阻断二解除：`scene_section` 现按 `has_media` 分支，True 分支正文与旧模板逐字相同 → 已有录像页面重新生成字节不变，措辞不再两套并存。
- 应修项解除：`media_present` 单点判定（play.mp4 ≥ 20000、poster.png ≥ 1000），`require_media` 与 authoring 判定同源，占位/LFS 指针不会再被当成有录像。

范围提示（非阻断）：e26ab21fd7 的 stat 里没有生成器、wiki 两页 + README、`graph_node_op_coverage.registry.json`——与"CI 修复另起一 commit"的声明一致，请确认那一 commit 确实在同一 PR 内，别只推命名 commit。

两点顺带确认，不必回复：`presenters.annotated.jsonc` 显示为 289 行纯新增（说明此前未跟踪，同路径非新副本）即符合预期；`graph-callable-function-vision.md` 恢复原样后仍留 `case_e.box_hover` 字样，仅在该页确属 PR1444 历史方案记录时可接受。
