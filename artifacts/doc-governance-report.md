# Documentation Governance Report — Presenter 文档修复与导航治理（含合并就绪判定）

Date: 2026-08-24
Scope: `gitbook/architecture/presenter-*.md`（10 页）、`gitbook/architecture/presenter-quickstart.md`（新增）、`gitbook/SUMMARY.md`、`gitbook/reference/raylib-render-config-structure.md`、`gitbook/reference/engine-gallery-wiki/{lighting,material_binding}.md`、`artifacts/techdebt/2026-08-12-kanban-doc-truncated-cjk-bytes.md`、`assets/Presentation/*.schema.json`（新增 5 份）
Ruleset: `scripts/validate-docs.ps1`（链接/反引号路径/命名规则，本轮通过）+ ludots-doc-governance checklist + 对 `origin/main` 的 schema 实测（78 配置文件零误报）+ `git merge-tree` 合并模拟

## Summary

- 本轮无未修复 P0/P1；两项历史 P1（乱码 SSOT、导航孤儿）已在本轮修复。
- Total findings: 5（已修复 3，存量上报 2）。

## Merge 就绪判定（对 origin/main @ 12 commits ahead）

- **git 层：可干净合并**。我方 7 个修改文件与上游 12 个新提交零路径重叠；6 个新文件路径上游不存在（`assets/Presentation/` 上游已有同名目录但无同名文件）。`git merge-tree --write-tree origin/main` exit 0、无冲突清单。
- **语义层：已消除三处漂移后达标**：
  1. 上游 `1c9acb685a` 移除了 `maxVisibilityDistanceCm` 作者面字段——本轮 schema 已同步移除；
  2. 上游 `paramOverrides`（children）/`defaultColor` 已从 mods 清除——schema 已移除；
  3. `yDriftPerSecond` 上游归位到 `motion`——schema 已从 assetBinding/worldText 移除并置于 motion。
- schema 字段集改为**镜像上游装载器 allow-list**（`PresenterDefinitionConfigLoader` 的 `RejectUnknownFields` 强制清单 + `_comment`/`__delete` 装载器容忍项），并新增上游新字段（`activationCondition`、`execution`、`instancedBatch`、`sortId`、`materialCustomData`、`maxLod`、`grounding(offset)`、`attributeName`、`tag`、`graphProgramId` 条件、命令 `route`/`intValue`/`paramGraphProgramId`/`durationRangeSeconds`、`style.alphaPolicy`）。
- 实测口径：全部 5 份 schema 对 `origin/main` 的 78 个 mod 配置文件（15 mesh + 10 host + 5 material + 6 vfx + 42 presenters）**零误报**。
- 文档计数对齐枚举源：13 种 BehaviorKind、11 种 PresenterCommandKind、36 种 PresentationEventKind（引用枚举文件路径，避免魔法数再漂移）。
- quickstart 事实对 origin/main 逐项复核通过：内置 `cube`/`sphere`/`default_surface`（LudotsCoreMod）、preset `raylib_client_parity_raylib`、parity 的 templates/maps/presenters 结构均在。

## Findings

### P1-01（已修复）presenter 路线 SSOT 旗舰页与开发看板乱码
- Problem: `presenter-as-actor-architecture.md` 339 处 U+FFFD（182 行）、`presenter-development-kanban.md` 426 处（200 行，TD-2026-08-12 债务）。
- Impact: presenter 路线的架构 SSOT 与交付看板对中文读者不可读。
- Evidence / Fix: 分别以最后干净底本 `942d077cd0`、`fdddb3aff6` 对齐回填（非猜测），逐处校验回填与行尾重复伪影，两文件现 0 乱码、0 伪影；TD 记录已标注 RESOLVED 并附恢复方法。全仓 gitbook/docs 现存乱码文件数 = 0。

### P1-02（已修复）presenter 文系 10 页导航孤儿
- Problem: presenter 系 9 页 + `quarks-particle-schema.md` 不在 `SUMMARY.md`，门户侧栏（由 SUMMARY 生成）完全不可见。
- Fix: 以层级块登记（as-actor → quickstart → compiled-lanes → … → kanban），插入 Raylib 块之前形成"契约→后端"阅读流；登记后 SUMMARY 92 个 md 链接 0 损坏，`validate-docs.ps1` 通过。

### P2-01（存量上报，不阻塞本合并）上游 fixture JSON 损坏
- Problem: `mods/fixtures/raylib_platform_meshes/RaylibPlatformMeshesMod/assets/Presentation/host_assets.json` 在 origin/main 上首行括号错乱（`["...gltf"  { "id": ...] },`，疑似坏合并残留），严格 JSON 解析失败。
- Impact: 任何加载该 fixture 的路径会失败；schema 实测将其跳过（UNPARSEABLE）。
- Evidence: `git show origin/main:mods/fixtures/.../host_assets.json` 首行。
- Recommendation: 上游单独修复该文件（不在本变更集内，避免夹带）。

### P2-02（已修复）gallery wiki 命名漂移 + quickstart 反引号路径违规
- Problem: `engine-gallery-wiki/{lighting,material_binding}.md` 写 `Presentation/materials.json`（实际 `material_assets.json`）；quickstart 初版两处 mod 相对路径带反引号被 `validate-docs.ps1` 判 missing-backtick-target。
- Fix: 前者改名对齐；后者去反引号（仓库根 `assets/` 只放权威示例，作者面 mod 相对路径不入反引号）。校验器现全绿。

### P3-01（存量上报，明确不修）已知边界
- `presenter-param-blackboard.md` 正文主体仍是已删除 API 的旧设计（首行有诚实注记，SSOT 以代码为准）——需专门重写，不在本轮。
- schema 覆盖边界：5 份覆盖 mesh/host/material/presenters/particle_vfx；animation 链（`animation_clips.json`/`animation_profiles.json`/`animator_controllers.json`）暂无 schema，待作者面稳定后补。

## Fix Order（剩余项）
1. 上游修 `raylib_platform_meshes` fixture JSON（P2-01）。
2. 重写 `presenter-param-blackboard.md`（P3-01）。
3. animation 链 schema + 决定是否挂流水线校验（对齐 mod-editor-prd TODO I10 的口径）。

---

# 文档治理报告 · raylib-asset-acceptance 教程发布（2026-08-25）

Scope：`gitbook/raylib-asset-acceptance.md`（新增教程页）、`gitbook/SUMMARY.md`（Agent Bridge 下挂 1 行）、`gitbook/agent-bridge.md`（导语互链 1 句）、`docs/agent-bridge.html`（左侧树 `asset` 项 + DOCS 登记）、`scripts/build-site.py`（`raylib_asset_acceptance_*` 证据族媒体拷贝）、`artifacts/evidence/raylib_asset_acceptance_{demo,obj}/`（play.mp4/poster.png/截图/manifest.json）。

Ruleset：ludots-doc-governance checklist（仓库相对路径、断言带证据、命令与真实入口一致）+ `build-site.py` 结构自验。

## 检查执行记录

1. 路径完整性：教程页内所有 `src/`、`artifacts/`、`scripts/` 反引号路径与链接均存在；无本地盘符路径。
2. 命令一致性：`dotnet run --project src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance`、`--model/--screenshot/--frames/--demo`、`python scripts/record-raylib-asset-acceptance.py` 与真实入口逐一对应（均有本轮实际运行记录）。
3. 证据规则：强断言（RaylibAdapterTests 80/80、#1050 根因与修复）均带代码/测试/证据路径。
4. 站点构建：`build-site.py` 结构自验通过；告警 1 条为既有 `WeightedPick.md` 孤儿页（P3，非本轮引入）。
5. 渲染验证：本地起服浏览器实测 `agent-bridge.html#doc/asset`：目录树出现教程项、markdown 渲染正常、两段 `<video>` `readyState=4`（1280×720，11.4s/6.76s，可播）。

## Findings

- P3（既有）：`graph-node-op-wiki/WeightedPick.md` 未被 README 收录（build-site 告警），建议后续补录。
- 无 P0–P2 发现；本轮新增内容 0 告警。
=======
### P1-01（已修复）presenter 路线 SSOT 旗舰页与开发看板乱码
- Problem: `presenter-as-actor-architecture.md` 339 处 U+FFFD（182 行）、`presenter-development-kanban.md` 426 处（200 行，TD-2026-08-12 债务）。
- Impact: presenter 路线的架构 SSOT 与交付看板对中文读者不可读。
- Evidence / Fix: 分别以最后干净底本 `942d077cd0`、`fdddb3aff6` 对齐回填（非猜测），逐处校验回填与行尾重复伪影，两文件现 0 乱码、0 伪影；TD 记录已标注 RESOLVED 并附恢复方法。全仓 gitbook/docs 现存乱码文件数 = 0。

### P1-02（已修复）presenter 文系 10 页导航孤儿
- Problem: presenter 系 9 页 + `quarks-particle-schema.md` 不在 `SUMMARY.md`，门户侧栏（由 SUMMARY 生成）完全不可见。
- Fix: 以层级块登记（as-actor → quickstart → compiled-lanes → … → kanban），插入 Raylib 块之前形成"契约→后端"阅读流；登记后 SUMMARY 92 个 md 链接 0 损坏，`validate-docs.ps1` 通过。

### P2-01（存量上报，不阻塞本合并）上游 fixture JSON 损坏
- Problem: `mods/fixtures/raylib_platform_meshes/RaylibPlatformMeshesMod/assets/Presentation/host_assets.json` 在 origin/main 上首行括号错乱（`["...gltf"  { "id": ...] },`，疑似坏合并残留），严格 JSON 解析失败。
- Impact: 任何加载该 fixture 的路径会失败；schema 实测将其跳过（UNPARSEABLE）。
- Evidence: `git show origin/main:mods/fixtures/.../host_assets.json` 首行。
- Recommendation: 上游单独修复该文件（不在本变更集内，避免夹带）。

### P2-02（已修复）gallery wiki 命名漂移 + quickstart 反引号路径违规
- Problem: `engine-gallery-wiki/{lighting,material_binding}.md` 写 `Presentation/materials.json`（实际 `material_assets.json`）；quickstart 初版两处 mod 相对路径带反引号被 `validate-docs.ps1` 判 missing-backtick-target。
- Fix: 前者改名对齐；后者去反引号（仓库根 `assets/` 只放权威示例，作者面 mod 相对路径不入反引号）。校验器现全绿。

### P3-01（存量上报，明确不修）已知边界
- `presenter-param-blackboard.md` 正文主体仍是已删除 API 的旧设计（首行有诚实注记，SSOT 以代码为准）——需专门重写，不在本轮。
- schema 覆盖边界：5 份覆盖 mesh/host/material/presenters/particle_vfx；animation 链（`animation_clips.json`/`animation_profiles.json`/`animator_controllers.json`）暂无 schema，待作者面稳定后补。

## Fix Order（剩余项）
1. 上游修 `raylib_platform_meshes` fixture JSON（P2-01）。
2. 重写 `presenter-param-blackboard.md`（P3-01）。
3. animation 链 schema + 决定是否挂流水线校验（对齐 mod-editor-prd TODO I10 的口径）。
>>>>>>> theirs
