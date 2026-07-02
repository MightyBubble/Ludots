# Ludots 文档体系（结构化 / 领域优先 / 机器可治理）

把文档从「自由 Markdown」迁移为「按 JSON Schema 约束的结构化文档」：领域优先组织、元数据可机检、常量/术语在构建期从代码注入、债务与迁移作为一等公民，并提供查看 + 批改的 Web App。

> 决策与需求正本见 [`KICKSTART.md`](./KICKSTART.md)。

## 目录

```
docs-system/
  registry/domains.json        # 领域注册表（领域 SSOT，派生自 src/Core 的 bounded context）
  schemas/                     # JSON Schema：基础信封 + 块模型 + 28 个文档类型
    document.base.schema.json
    blocks.schema.json
    constants-manifest.schema.json
    types/*.schema.json
  content/                     # 真实文档（领域优先：content/domains/<domain>/*.json）
  generated/constants.json     # 代码常量清单（生成物，由导出器重建，CI 校验无漂移）
  tools/
    validate/                  # Node + ajv 校验器（schema + 全部治理规则）
    constants-export/          # C# 反射导出器，生成 constants.json
  app/                         # Web App（查看 + 批改：块渲染 / mermaid / 实时校验 / 存盘）
```

## 三件套怎么跑

### 1) 校验（CI 的核心闸）

```bash
cd docs-system/tools/validate
npm install
node validate.js                 # 加 --strict-freshness 让过期文档直接失败
```

校验内容：每篇文档过对应 type 的 Schema；`domain` 在注册表内；所有代码路径字段
（`relatedCode` / `sourceCode` / `enforcedBy` / `code` / `evidence` / `codeRef.path` /
`codeScope.path` 等）真实存在；`constantRef` / `valueRef` 命中常量清单；`termRef` 命中术语表；
`id` 全仓唯一；`relatedDocs/supersedes/targetDocs/residueDebt` 目标存在；
`(domain, ssotTopic)` 下 SSOT 唯一；`formal` 不得引用 `deep`；过期 `reviewBy/dueBy/expiry` 告警。

### 2) 常量导出器（代码 → 文档）

```bash
cd docs-system/tools/constants-export
dotnet run -c Release             # 反射 allowlist.json 列出的静态类，写 generated/constants.json
```

文档只写常量「符号」（如 `SpatialScaleDefaults.CellCm`），实时值在 Web App 渲染时解析、在 CI
校验存在性。改了代码常量值无需改文档；CI 重建清单并要求 `git diff` 干净，防止漂移。

### 3) Web App（查看 + 批改）

```bash
cd docs-system/app
npm install
npm run dev                       # http://localhost:4321 —— 可查看 + 批改并存盘到 content/
npm run build                     # 烘焙只读数据 + tsc 类型检查 + 静态产物（dist/，适合部署）
```

- 查看：领域优先侧边栏 + 虚拟视图（SSOT 地图 / 债务看板 / 待复审）；块渲染（text/list/code/
  codeRef/diagram/table/callout）；mermaid 实时渲染；`constantRef`/`termRef` 实时解析显示值/定义。
- 批改：JSON 编辑 + 实时 ajv + 治理校验（与 CI 同源规则）+ 一键存盘（仅 `npm run dev`）。

## 设计取舍（与 KICKSTART 一致）

- ajv 配置 `strict: true, strictRequired: false`：条件 `required` 与互斥 `not/required` 是合法
  Schema 习语，关掉 `strictRequired` 以避免误报。
- SSOT 唯一性以 `(domain, ssotTopic)` 为键：同一领域可有多个不同主题的 SSOT（如 `scale` 与 `terms`）。
- 编辑器采用「结构化 JSON + 实时 Schema 校验」而非按 Schema 自动生成的表单：28 个类型含深度嵌套
  块模型，代码编辑 + 实时校验对这种复杂度更可靠、与严格 JSON 模型一致（rjsf 可后续叠加）。
- 静态构建只读：编辑写盘依赖 dev API；部署产物（`dist/`）是只读站点。
