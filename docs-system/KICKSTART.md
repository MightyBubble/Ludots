# Ludots 文档体系重构 — DeepSeek Kickstart

> 这是一份自包含的执行 brief。你（DeepSeek）没有此前的对话上下文，所有约束都在本文件与 `docs-system/` 下的 schema / registry / example 里。开工前先把 `docs-system/` 全部读一遍。

## 0. 一句话目标

把现在散乱、按类型分、自由 Markdown 的文档体系，重构为 **领域优先组织 + 按类型严格 JSON Schema 校验的结构化文档 + CI 卡死 + 一个可热更新的 Web App 来「查看」和「批改」全部文档** 的新体系。

## 1. 现状（你要替换掉的东西）

- 现有正式文档在 `gitbook/`（约 60 篇 md），深度材料在 `docs/`（约 284 篇 md），通过 `.gitbook.yaml` 走 GitBook 发布。
- 治理脚本：`scripts/validate-docs.ps1`，CI：`.github/workflows/docs-governance.yml`。
- 已知病灶（必须修掉，别复制）：
  - 按**类型**分目录（`architecture/` `reference/` `adr/` `rfcs/` `audits/`），同一领域被切碎到多个筒仓。
  - `docs/conventions/` 是 `gitbook/contributing/` 的影子副本（重复造轮子 + SSOT 通胀）。
  - Markdown 段落自由，YAML 头易过期，文档与 commit 硬绑定、易腐烂、有幻觉式表述。
  - 存在编码乱码（如 `gitbook/SUMMARY.md`）。
- 已有 3 个高质量交互可视化资产，**迁移时保留并接入新站**：
  - `gitbook/reference/spatial-scale-explorer.html`
  - `gitbook/architecture/ui-rendering-and-surface-ownership.html`
  - `gitbook/reference/map-scale-authoring-starter.html`

## 2. 锁定决策（不可推翻）

1. **文档即结构化 JSON**，不再用自由 Markdown 正文。需要散文的地方用受约束的「块模型」（见 `schemas/blocks.schema.json`）。
2. **每种文档类型一套 JSON Schema**，必填/可选字段严格定义，`unevaluatedProperties:false` 锁死，禁止多余字段（杜绝自言自语）。
3. **领域优先**：物理目录按领域（bounded context）组织，类型只是 `type` 字段 + 站点上的虚拟视图。领域 SSOT 是 `docs-system/registry/domains.json`，派生自 `src/Core`。
4. **反幻觉硬约束**：所有 `relatedCode` 与 `codeRef` 块的路径必须指向仓库内真实存在的文件，CI 校验不通过即失败。
5. **CI 卡死**：schema 校验、领域存在性、SSOT 唯一性、formal→deep 单向引用、freshness、路径存在，全部进 CI。
6. **一个可热更的 Web App** 同时承担：查看（渲染 JSON 为可视化站点）+ 批改（schema 驱动表单编辑 + 实时校验）。
7. 发布产物是静态站点（可嵌架构图/流程图/交互组件）。

## 3. 非目标

- 不保留 Markdown 作为作者源（彻底替换）。
- 不保留 GitBook 发布链路（迁移完成后归档 `.gitbook.yaml`、`gitbook/reference/publishing-and-access.md`）。
- 不要发明与 `src/Core` 不一致的领域；领域以代码 bounded context 为准。

## 4. 数据模型（已给地基，按此扩展）

- 基础信封：`docs-system/schemas/document.base.schema.json`（所有类型共享的元数据：id/type/domain/tier/title/summary/status/ssot/owner/updated/reviewBy/relatedCode…）。
- 块模型：`docs-system/schemas/blocks.schema.json`（text/list/code/codeRef/diagram/table/callout，文本有长度上限）。
- 已给类型 schema（照此风格补全其余）：
  - 知识/架构类：`ssot`、`adr`、`config-reference`、`glossary`、`architecture`（含 `phase: planned|as-built`，一套覆盖『架构设计/架构描述』）
  - 研发生命周期类：`prd`、`product-design`、`technical-design`(TDD)、`issue`、`uat`、`user-guide`
  - 过程/治理类：`project-management`、`workflow`、`skill`、`debt`（债务区）、`migration`（长周期迁移）
- 常量清单 schema：`schemas/constants-manifest.schema.json`（生成物的契约）。
- 黄金样例（已迁入 `content/`，真实路径、过校验）：`content/domains/spatial-and-scale/` 下的 `overview` / `scale-ssot` / `glossary` / `debt-terrainchunk-fixed-size` / `migration-chunk-abstraction`。

### 仍需你设计 schema 的类型（给需求，组织方式你定）

下列类型只给**意图与必须捕获的信息**，schema 的字段结构、必填/可选划分、是否复用块模型由你（DeepSeek）判断设计。**硬约束**（不可违反）：每个类型 schema 必须 `type:object` + `allOf(base)` + 收紧 `type` const + `unevaluatedProperties:false`；散文一律用块模型；涉及代码的字段走真实路径/符号（CI 校验）；产出能过本套 ajv 配置校验，并给一个可过校验的黄金样例。

- `overview`：领域/系统总览入口。该领域是什么、边界、关键组件、子文档导航。
- `explanation`：概念解释（为什么/原理），面向理解，不含操作步骤。
- `contract`：跨层不可破坏的接口约定。各方、保证项、由谁（代码符号/路径）强制、禁止事项，可含时序图。
- `reference`：通用查表型参考（非配置、非 API）。
- `api-reference`：代码 API / 扩展点参考。类型/方法/参数/扩展点，绑定真实符号（建议复用常量/符号引用机制）。
- `tutorial`：学习导向，端到端从零跑通，含前置条件与预期结果。
- `howto`：任务导向，"如何做 X"的最短路径步骤。
- `rfc`：提案。问题、候选方案、权衡、决策状态（draft→accepted/rejected/superseded）。
- `audit`：时间点审计/验收证据，默认冻结归档。范围、发现、证据路径、结论。
- `benchmark`：性能基准/预算。指标、目标阈值、测量方法、证据路径，可接入 CI。
- `roadmap`：方向性路线图（与 `project-management` 区分：roadmap 偏方向，PM 偏执行追踪）。
- `faq`：常见问题。问答对 + 关联文档。

你也有权在不破坏上述硬约束、领域优先组织、防过期机制（4.5）与债务/迁移机制（4.6）的前提下，调整这些类型的边界、合并/拆分，或新增确有必要的类型（新增需在 base 的 `type` 枚举登记并说明理由）。

### 研发生命周期类型链路（这些类型如何咬合）

```
prd（为什么/要什么）
  -> product-design（呈现为用户体验）
  -> architecture(phase=planned)（架构设计·开发期）
  -> technical-design / TDD（feature 详设 + 测试计划）
  -> issue（可认领工作项 + 验收标准）
  -> 实现
  -> uat（验收测试设计 + 通过标准）
  -> architecture(phase=as-built)（架构描述·完成后，必带 verification 证据）
```

横切：`project-management`（里程碑/风险）、`workflow`（流程）、`skill`（agent 能力）。
咬合靠 `relatedDocs` 的 `depends-on` / `refines` / `implements` 关系，CI 校验目标 id 存在。`skill.skillId` 与 `skills/registry.json` 交叉校验。

每套类型 schema 必须：`allOf` 引用 base + 收紧 `type` const + 追加该类型的字段化结构 + `unevaluatedProperties:false`。

## 4.5 代码常量 SSOT 与术语 SSOT 的智能绑定（防过期核心）

文档**禁止抄写代码常量的值和术语的定义**，只能引用 SSOT，值/定义在构建期解析回来。

### 代码常量

- 引用形式：块模型的 `constantRef`（`{ "kind":"constantRef", "symbol":"SpatialScaleDefaults.CellCm" }`），或字段里的 `valueRef`（见 `schemas/types/ssot.schema.json` 的 definitions，`value` 与 `valueRef` 互斥）。
- 解析源：`docs-system/generated/constants.json`（生成物，schema 见 `schemas/constants-manifest.schema.json`）。
- **导出器**（你来实现）：一个 C# 导出工具/测试，反射编译后的 Core 程序集，导出标注了 `[DocConstant]`（可带 `Unit`）的 `const`/`static readonly` 字段；key=完全限定符号名，value 用 `GetRawConstantValue()`/字段值。`declaredIn`/`summary` best-effort（升级版可用 Roslyn 取文件行号与 XML doc）。
- **防过期链路**：① 文档只存符号；② CI 每次重新运行导出器生成 `constants.json` 并要求 `git diff` 干净 → 清单永远等于代码；③ 站点渲染时注入清单值 → 文档显示永远最新，没有可遗忘的抄写值；④ 符号被改名/删除 → 引用悬空，CI 直接失败（响亮坏掉，而非静默撒谎）。

### 术语

- 定义只在 glossary 存一份（`schemas/types/glossary.schema.json`，样例 `content/domains/spatial-and-scale/glossary.json`）。
- 别处用 `termRef`（`{ "kind":"termRef", "id":"cell-cm" }`）引用，禁止重复定义。
- 术语可带 `constantRef` 绑定到代码常量，渲染时显示实时值（术语 SSOT × 常量 SSOT 联动）。

### 校验器需新增的规则

- 每个 `constantRef.symbol` / `valueRef` / 术语 `constantRef` 必须存在于 `generated/constants.json`，否则 fail。
- 每个 `termRef.id` 必须存在于某个 glossary 文档，否则 fail。
- `generated/constants.json` 必须由导出器重新生成且 `git diff` 干净（清单新鲜度）。
- definitions 中 `value` 与 `valueRef` 不可同时出现（schema 已用 `not.required` 强制）。

## 4.6 债务区与长周期迁移（强制更新机制）

架构频繁迭代必然产生债务与半完成迁移。本体系把它们做成一等公民并用 CI 强制收敛。

### 债务（`debt`，schema `types/debt.schema.json`，例 `content/domains/spatial-and-scale/debt-terrainchunk-fixed-size.json`）

- 必须显式关联债务对象：`targetDocs`（文档 id）和/或 `codeScope`（代码 path/lines），二者至少有一个，CI 校验都真实存在。
- `debtStatus`: open / in-progress / accepted / resolved；`dueBy` 硬期限。
- **change-coupling gate（强制开发者更新的核心）**：PR CI 比对 `base..head` diff，对每个 `debtStatus=open` 且 `enforce=true` 的债务，若 diff 触碰其 `codeScope` 覆盖的文件，则要求同一 PR 内该债务文档被更新（`updated` 前移或 `debtStatus` 变更）或带 `ack`，否则 CI fail。
- web app 渲染「债务区」聚合视图，并在每个 `targetDocs` 目标文档顶部显示未结清债务横幅。

### 长周期迁移（`migration`，schema `types/migration.schema.json`，例 `content/domains/spatial-and-scale/migration-chunk-abstraction.json`）

- `phases[]` 每阶段独立追踪 `status`（含 `merged`）+ `mergedIn`(PR/commit) + `codeScope` + `exitCriteria`，支持"阶段性并入 main"。
- `sanctionedTemporaryState` + `guardrails`：显式声明迁移期被批准的临时双态及不变量 —— 这是对"禁止 fallback/向后兼容"铁律的**受控、有期限的豁免**，写明白而非偷偷做。
- `expiry` 硬期限；`residueDebt[]` 把未完成阶段挂成 debt id（CI 校验存在），让迁移残留自动进债务区被催。

## 5. 目标目录结构（领域优先）

```
docs-system/
  registry/domains.json            # 领域 SSOT（已给）
  generated/constants.json         # 代码常量清单（生成物，CI 重建）
  schemas/                         # 基础 + 块 + 常量清单 + 各类型 schema（部分已给）
  content/
    domains/<domain-id>/<id>.json  # 90% 内容：按领域归档的结构化文档
    overview/                      # 跨领域系统总览、领域地图
    contributing/                  # 元文档：编码标准、工作流、AI规范、文档治理
    glossary.json                  # 全仓术语 SSOT
  assets/                          # 图片、svg、迁移过来的交互 html
  app/                             # 查看 + 批改 Web App（见第 7 节）
  tools/validate/                  # 校验器（见第 6 节）
```

文档 `id` 形如 `<domain>.<topic>`，文件名 = `<id>.json`，`domain` 字段必须命中 registry。

## 6. 治理 / 校验器（`docs-system/tools/validate/`）

实现一个校验器（Node + ajv，draft 2020-12）。**已验证可用的 ajv 配置**：`new Ajv({ strict: true, strictRequired: false, allErrors: true })` + `ajv-formats`。说明：`strictRequired` 必须关（否则会误报 `not.required` 互斥与 `then.required` 条件必填这两个故意用法）；其余 strict 全开。每个类型 schema 已用 `type:object` + `allOf(base)` + `unevaluatedProperties:false`，多余字段会被拒（已通过正/负用例验证）。CI 跑全量、本地跑增量。必须校验：

1. 每篇文档对应 `type` 的类型 schema 通过。
2. `domain` 存在于 `registry/domains.json`；registry 中每个 `codePaths` 真实存在。
3. 所有 `relatedCode` 与块内 `codeRef.path` 路径真实存在（复用 `scripts/validate-docs.ps1` 的路径解析思路）。
4. **SSOT 唯一性**：同一 `(domain, topic)` 仅一处 `ssot:true`（topic 可由 id 约定推导）。
5. **单向引用**：`tier:formal` 文档的 `relatedDocs` 不得指向 `tier:deep` 文档。
6. **freshness**：`reviewBy` 过期 → warning（可配置为 error）。
7. `id` 全仓唯一；`supersedes`/`relatedDocs` 指向的 id 必须存在。
8. 债务 change-coupling gate：PR diff 触碰某 open 债务的 `codeScope` 时，强制同一 PR 更新该债务或 `ack`（需 CI 拿到 `base..head` diff）。
9. 债务 `targetDocs` / `codeScope`、迁移 `residueDebt` / `phases[].codeScope` 的目标必须存在；`dueBy` / `expiry` 过期报错。

把 `.github/workflows/docs-governance.yml` 改为：跑本校验器 + 跑 Web App 的 build。淘汰旧的 GitBook 专属校验。

## 7. Web App 规格（看 + 批改，可热更）

单个 Vite + React 应用，两种模式共用一套渲染/校验逻辑：

**查看模式（静态发布）**
- 读取 `content/**.json` + registry，生成领域优先的站点：左侧领域导航，文档页把块模型/字段渲染成 HTML。
- 渲染 `diagram` 块（mermaid 构建期渲染；`embed-html` 用 iframe 接入既有交互资产）。
- **虚拟视图（按类型聚合，从元数据生成，不复制文件）**：全部 ADR、全部 SSOT 地图、新手学习路径（串各领域 `type:tutorial`）、过期文档清单、按 owner 的归属表。
- `build` 产出纯静态站，部署到 GitHub Pages 或 Cloudflare Pages（择一，先做 GitHub Pages）。

**批改模式（开发态，HMR 热更）**
- 用 JSON Schema **自动生成表单**（建议 `@rjsf/core`）：必填/可选字段、枚举、长度上限在录入时即被强制 —— 这是从源头掐掉自由发挥/幻觉的关键。
- 编辑时**实时校验**（schema + 路径存在 + SSOT 唯一 + 单向引用）并即时预览渲染结果。
- 提供轻量本地 dev server 负责读写 `content/**.json` 文件；保存即 HMR 刷新预览。
- 「批改」视图：列出 `status`/`reviewBy`/owner，标红过期与校验失败项，支持快速跳转修订。
- 提供原始 JSON（monaco）与表单双视图切换。

## 8. 迁移计划（md → json）

1. 先把 `src/Core` 的目录核对进 `registry/domains.json`（已给草案，补 owner）。
2. 写一次性迁移脚本：把 `gitbook/` + `docs/` 的 md 按主题归入领域，抽取为对应 `type` 的 JSON；`updated` 从 git log 推断，`relatedCode` 从原文中的反引号路径提取。
3. 删除 `docs/conventions/` 影子副本；规范单点化进 `content/contributing/`。
4. 修复编码乱码；统一为 UTF-8 无 BOM。
5. 迁移 3 个交互 html 到 `assets/`，用 `embed-html` 块接入。
6. 更新入口引用：`README.md`、`README_CN.md`、`AGENTS.md`、`CLAUDE.md`；归档 `.gitbook.yaml`。

迁移按领域分批，每批：抽取 → 过校验 → 站点能渲染 → 人工 spot check。

## 9. 验收标准

- 所有 `content/**.json` 通过校验器，CI 绿。
- 任一领域页能看到该领域全貌（overview + ssot + 设计 + 决策 + 参考 + 质量），不再跨筒仓跳转。
- 全仓 `relatedCode`/`codeRef` 零失效路径。
- 同一 topic 全仓唯一 SSOT；无 formal→deep 反向依赖。
- Web App：查看站点可构建为静态产物；批改模式可新建/编辑文档并实时校验 + 预览，保存热更生效。
- 不再有 Markdown 作者源、不再依赖 GitBook。

## 10. 建议技术栈

- 校验：Node + ajv（2020-12）。
- App：Vite + React + `@rjsf/core`（schema 表单）+ mermaid + monaco-editor + 一个 Express/Node dev server 做文件读写。
- 部署：GitHub Pages（CI 自动发布静态产物）。

## 11. Start here（第一步）

1. 读 `docs-system/` 全部文件，在 `tools/validate/` 跑 `npm install && node validate.js`（已实现，应 PASS）。
2. 补全剩余类型 schema（先做 overview / architecture / contract / config-reference 已给 / reference / tutorial / howto / rfc / audit）。
3. 搭 `tools/validate/` 校验器 + 接入 CI。
4. 搭 Web App 骨架（先查看，再批改）。
5. 再开始批量迁移内容。

## 12. 现有锚点（迁移时参考，勿照搬其类型目录）

- 正式区现状：`gitbook/README.md`、`gitbook/SUMMARY.md`、`gitbook/contributing/*`、`gitbook/architecture/*`、`gitbook/reference/*`。
- 深度区现状：`docs/architecture/*`、`docs/reference/*`、`docs/adr/*`、`docs/rfcs/*`、`docs/audits/*`、`docs/conventions/*`（影子副本，删）。
- 校验逻辑可复用：`scripts/validate-docs.ps1`（路径解析、命名、死链）。
