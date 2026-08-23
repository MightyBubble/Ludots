# Documentation Governance Report — Raylib 引擎画廊对齐 Graph 节点画廊

Date: 2026-08-23（第二轮：对齐最新 main `6daa88a45d` 后按用户四点缺口补全）
Scope: `gitbook/reference/engine-gallery-wiki/`（21 文件）、`gitbook/architecture/raylib-{render-code-shape,engine-gallery-dev-guide,render-productization,engine-capabilities,render-lighting-guide}.md`、`gitbook/reference/raylib-render-config-structure.md`、`gitbook/SUMMARY.md`、`docs/raylib-engine.html`、`docs/index.html`、`docs/site-assets/site.js`、`scripts/build-site.py`、`scripts/validate-docs.ps1`
Ruleset: `scripts/validate-docs.ps1`（链接/反引号路径/命名规则）+ `scripts/build-site.py` 结构自验 + ludots-doc-governance checklist

## Summary

- Total findings: 9（第一轮 5 + 第二轮 4）
- P0: 1（跨目录深链 404，存量，已修复）
- P1: 1（下划线文件名被命名规则拒绝，规则已对齐）
- P2: 4（完成度差距本体、非法字符 token、四文档零截图、指南两处失真；均修复）
- P3: 2（topbar 标签、productization 失联；均修复）
- 附带治理改进 1 项：文档内示例一律用真实路径（假想路径占位符会被校验器拦截两次，已改用 `vegetation_cutout` 真实场景作走查样本）

## Findings

### P0-01 画廊门户跨目录文档深链 404（存量，影响线上 Graph 节点画廊）
- Problem: `graph-op-wiki.html` 与（本次新增的）`raylib-engine.html` 的链接重写均按约定剥掉 `.md`，生成 `index.html#docs/<无扩展名路径>`；`docs/index.html` 的 `loadDoc` 原样 fetch，静态服务器 404。
- Impact: 线上 Graph 节点画廊每篇 op 页的手册分册链接（如 `gr-op-01-context`）点击后显示「无法加载」；引擎画廊场景页的深读链接同样中招。
- Evidence: `docs/index.html`（loadDoc 无扩展名处理）；浏览器实测 `#docs/architecture/render-lighting-guide` → 「无法加载 gitbook/architecture/render-lighting-guide」。
- Recommendation（已实施）: `loadDoc` 入口统一补 `.md`；修复后实测无扩展名深链正常加载文档正文。

### P1-01 gitbook 文件命名规则不允许下划线，与 SSOT 场景 id 冲突
- Problem: `validate-docs.ps1` 的 `gitbook-name` 规则为 `[a-z0-9-]+`（无下划线），而引擎场景 id 是 snake_case（`sky_daynight`）；wiki 文件名需与 scene id / preset 后缀 1:1 对应。PowerShell `-notmatch` 大小写不敏感使 PascalCase 的 graph wiki 页「碰巧」合法，掩盖了该规则与子目录命名实践（`docs/` 各子目录均允许下划线）的不一致。
- Impact: 文件名被迫偏离场景 id（弱化 SSOT 对应）或校验失败。
- Recommendation（已实施）: 规则放宽为 `[a-z0-9_-]+`，对齐 `docs/` 子目录既有约定；文件名保持与 `SceneCatalog` 场景 id 完全一致。

### P2-01 raylib-engine.html 完成度远低于 graph 画廊（本次主任务）
- Problem: 页面原为单文档加载壳（92 行总览 md），无逐场景讲解、无导航、无证据呈现；graph 画廊为 130 页可导航 wiki（侧栏九家族 + 过滤 + 上下场翻页 + 页内录像）。
- Impact: 引擎能力对 mod 作者/玩家的可发现性差；20 个场景的验收证据（截图 + stats）与注册表信息不可从门户触达。
- Recommendation（已实施）: 新增 `gitbook/reference/engine-gallery-wiki/`（README 总目录 + 20 场景页，每页含验收截图、作者写法表、演示点、帧统计、怎么跑、边界与深读）；`raylib-engine.html` 升级为画廊门户（侧栏 6 家族 20 场景 + 过滤 + `#scene/<id>` 路由 + 翻页），交互合同与 `graph-op-wiki.html` 一致；`build-site.py` 复用 wiki 解析器生成 `engine-gallery-nav.js` 并纳入结构自验。

### P2-02 README 反引号 token 含 Windows 非法路径字符 `|`
- Problem: `engine-gallery-wiki/README.md` 初版 token ``artifacts/acceptance/engine_raylib_lighting|crowd_anim/`` 因带 `artifacts/` 前缀进入路径解析，`Path.IsPathRooted` 抛「路径中具有非法字符」。
- Impact: `validate-docs.ps1` 直接崩溃（非 finding 而是 exception）。
- Recommendation（已实施）: 拆成两个独立 token；全部 21 文件重扫通过。

### P3-01 topbar 标签「Raylib 引擎」语义过时
- Problem: 页面升级为画廊门户后，导航标签仍为「Raylib 引擎」。
- Recommendation（已实施）: 改为「Raylib 引擎画廊」，与「Graph 节点画廊」对仗。

### P2-03 四个 Raylib 架构文档零截图（用户第二轮缺口①）
- Problem: `raylib-engine-capabilities.md` / `render-lighting-guide.md` / `engine-capability-showcases.md` / `raylib-render-productization.md` 均无任何证据图；graph 画廊水准要求页内有可看的东西。
- Recommendation（已实施）: 嵌入 9 张引擎画廊验收截图（`artifacts/` 根相对路径，两个查看器均有豁免先例），图即证据、路径即出处。

### P2-04 光照指南两处事实失真（用户第二轮缺口②的根因之一）
- Problem: 指南写 `Presentation/materials.json`，装载器真名是 `Presentation/material_assets.json`（`PresentationMaterialConfigLoader.DefaultRelativePath`）；示例 `"flags": "Opaque"` 是字符串，装载器要求数组（字符串直接抛出）。
- Recommendation（已实施）: 改为真实文件名与数组写法，并链接到新配置结构文档。

### P3-02 productization 文档失联 + 过期表述
- Problem: `raylib-render-productization.md` 不在 `gitbook/SUMMARY.md`（导航不可达）；「光照栈」节仍写"平面投影阴影"（该车道已退役）。
- Recommendation（已实施）: 注册进 SUMMARY；表述改为"方向光 shadow map"。

## 第二轮新增交付（用户四点缺口的对应物）

| 缺口 | 交付物 |
|---|---|
| 截图 | 四个架构文档嵌 9 张验收截图；Wiki 21 页本就有图 |
| 配置结构说明 | `gitbook/reference/raylib-render-config-structure.md`——material_assets 字段全表（对照 `PresentationMaterialConfigLoader` 逐字段）、host_assets 双行型、mesh_assets、环境配置树、preset 结构 |
| 开发指南 | `gitbook/architecture/raylib-engine-gallery-dev-guide.md`——加场景六处登记 / 加着色器五处登记+三铁律 / 加材质两处登记，附决策表 |
| 代码形状分析 | `gitbook/architecture/raylib-render-code-shape.md`——六装配体依赖方向图、合同层速查、38 文件渲染器分组清单、15 组着色器清单、帧内数据流 |

三份新文档全部注册 `gitbook/SUMMARY.md` 并从 Wiki 总目录、能力总览互链。

## Fix Order（执行顺序）

1. 新增 wiki 21 文件（内容主体，所有强断言带具体路径）。
2. `build-site.py`：解析器泛化 + `engine-gallery-nav.js` + 结构自验清单。
3. `raylib-engine.html` 门户重写 + `site.js` 标签 + 两个架构文档回链。
4. `validate-docs.ps1` 下划线规则对齐；修 README 非法 token。
5. `index.html` `loadDoc` 扩展名补回（P0-01，同时修复线上 graph 画廊存量问题）。
6. 验证：`build-site.py` 0 告警；`validate-docs.ps1` 通过；浏览器实测门户渲染/路由/图片加载/翻页器/跨目录深链。

## Residual Risks

- 场景页正文为手写（graph wiki 为生成器产物）：scene id/标题/摘要若在 `SceneCatalog.cs` 或 `showcase.registry.json` 变更，wiki 需人工同步；构建期已防 404（README 条目缺页硬失败、孤儿页告警），但不防内容漂移。
- 帧统计表摘自当前验收工件（`engine_gallery_all/*.json` 等），证据重跑后数值会变化；页面已标注工件路径供对账。
- 侧栏 Playwright 自动化点击在粘性滚动容器内偶发超时（人工路径不受影响；哈希路由 `#scene/<id>` 等价可达，实测正常）。
- 本次操作事故记录：在他人工作树 `.worktrees/audit-raylib-main` 做 stash 验证时，因多工作树共享 stash 栈弹入了他人 stash 造成冲突残留；已 `git reset --hard` 恢复至该分支干净 HEAD（`a4b594a118`），他人 stash（wip-nr）完好保留。教训：不在共享仓库的他人工作树执行 stash/pop。
