# GAS Composition Gate — Self Review

- **Task / Issue**: PANEL-1 收口（#1010）——面板实例化图 op + 实时/手动刷新（用户裁定 2026-08-18）
- **Date**: 2026-08-18
- **Agent / Author**: Kimi（接续 GLM 会话）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** —— 新增 graph 节点（`CreatePanel`/`DestroyPanel`）+ 既有 `PanelTemplate` JSON 的增量字段（`realtime`），无新 profile DSL、无平行管线。

结论: PASS

一句话理由: 实例化走"图 op → IGraphRuntimeApi → PanelHost"与"代码 → PanelHost"同一条终端 API；刷新是 PanelHost 上对既有 PanelProjectionReader 的按需调用，二者均为 op/参数级增量。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| CreatePanel / DestroyPanel | Layer 0 op | GraphNodeOp 441/442 + GasGraphOpHandlerTable |
| 面板实例生命周期与刷新 | Layer 0 服务 | `PanelHost`（Core/UI/PanelHosting） |
| 蓝图作者用法 | Layer 2 | 关卡蓝图/Script 图 JSON 里调节点 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`（注册/元数据模式同 ShowPanel/HidePanel）
- Queues / Systems: 无新增队列；刷新由宿主系统调 `PanelHost.RefreshRealtime()`
- Resolvers / Registries: `ConfigKeyRegistry`（模板 id/锚点符号）、`GraphProgramSymbolPatcher`、`GraphOpDescriptorTable`、`PanelTemplateLoader`、`PanelProjectionReader`、`GraphLookupTableRegistry`、`ConfigPipeline`（模板目录加载同 GraphLookupTableLoader）
- Existing presets / graphs: 画廊 vignette/graph/wiki/coverage 四件套模式（同 ShowPanel）

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| CreatePanel | 按模板 id + 锚点 + scope 实例化一个面板 | ShowPanel/HidePanel 只写显隐标记，不携带模板/锚点/scope，无法表达实例化 |
| DestroyPanel | 销毁匹配模板（+可选 scope）的面板实例 | 同上，生命周期语义不能由显隐标记组合 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无跨步骤事务——Instantiate 单步完成（模板缺失/绑定失败当场抛出，不产生半实例）。

### 6. Config SSOT

行为配置落在: `Panels/panel_templates.json`（ConfigPipeline ArrayById，同 GraphTables 模式）+ 图 JSON 节点参数（panelType/panelAnchor）

是否新增 JSON schema: NO —— 复用既有 `PanelTemplate` schema，仅新增可选字段 `realtime`（布尔，默认 false）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未知模板/锚点/坏绑定全部点名抛出）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（在新图里调 CreatePanel 节点）或 **effect 步骤**——不改 Core enum。
