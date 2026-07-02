# Entity Insight Panel Architecture

## Scope

本文档是 Ludots 跨题材实体信息面板的架构 SSOT，覆盖以下内容：

- 4X / RTS / MOBA 共用的 insight profile 组织方式
- 单选头像、头像融合、全战风格兵牌的同源组合方案
- 多语言文本、图标、静态资源的组织边界
- 选择集合、控制组、信息面板之间的统一数据流
- 高性能 SoA 采样、虚拟窗口、跨引擎适配边界

目标不是再造一个 UI 子系统，而是在现有 `SelectionRuntime`、`EntityInfoPanelService`、`PresentationTextCatalog`、`UIRoot` 上收敛出一条可复用链路。

## Reuse Checklist

复用基建：

- Registry: `src/Core/Gameplay/Spawning/EntityTemplateKeyRegistry.cs` — 把实体模板映射成稳定整数键，作为 profile 绑定入口
- Registry: `src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs` 与 `src/Core/Gameplay/GAS/Registry/TagRegistry.cs` — 复用属性与标签 ID，不新增 panel 私有字典
- Pipeline: `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs` — runtime spawn 时写入 `EntityTemplateKeyRef`，让面板可直接按模板取 profile
- Pipeline: `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Sample.cs` — 从 ECS / GAS 采样到固定槽位 SoA 缓冲，再驱动 UI 与 overlay
- Pipeline: `src/Core/EntityCollections/EntityCollectionStore.cs` — 承载非 selection 语义的 query/display 实体集合
- System: `src/Core/Input/Selection/SelectionRuntime.cs` 与 `src/Core/Input/Selection/SelectionControlGroupRuntime.cs` — 统一 live / formation / control group 选择真相
- System: `mods/showcases/info_panels/GenreInfoShowcaseMod/Systems/GenreInfoShowcasePanelPresentationSystem.cs` — 只做刷新，不持有第二份选择态
- Mod: `mods/capabilities/entityinfo/EntityInfoPanelsMod/` — 能力层负责 insight brief、采样、图标、文本解析
- Mod: `mods/showcases/info_panels/GenreInfoShowcaseMod/` — showcase 只负责题材示例、控制组预设与可玩验收

这条链路满足 `docs/conventions/02_ai_assisted_development.md` §4 的 reuse-first 要求，没有引入第二套选择存储、第二套文本系统或 host-only UI 分支。

## Single Source Of Truth

权威真相分三层，各层职责不得串位：

- 实体语义真相：ECS 组件与模板键
  - `src/Core/Gameplay/Spawning/EntityTemplateKeyRef.cs`
  - `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs`
- 选择真相：容器化 selection / view / control group
  - `src/Core/Input/Selection/SelectionComponents.cs`
  - `src/Core/Input/Selection/SelectionRuntime.cs`
  - `src/Core/Input/Selection/SelectionControlGroupRuntime.cs`
- 面板展示真相：按 slot 预分配的 sampled panel state
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.cs`
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Storage.cs`
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Insight.cs`
- 查询/展示集合：`EntityCollectionStore`
  - `src/Core/EntityCollections/EntityCollectionStore.cs`
  - `src/Core/EntityCollections/EntityCollectionTypes.cs`

禁止做法：

- 用名字、tag 文本或 UI element id 直接决定题材面板样式
- 在 UI controller 里缓存第二份“当前选中实体”列表
- 为 Skia / Raylib / Web 分别写三套信息面板数据模型
- 把 collection 读取失败静默 fallback 到当前 selection

## Static Organization

### 1. Template To Profile

题材面板不绑定实体名字，而绑定模板键：

- runtime 写键：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs`
- profile 加载：`mods/capabilities/entityinfo/EntityInfoPanelsMod/Insight/EntityInsightProfileLoader.cs`
- showcase profile 资产：`mods/showcases/info_panels/GenreInfoShowcaseMod/assets/EntityInfo/insight_profiles.json`

`insight_profiles.json` 的最小组织单元是 `EntityInsightProfile`：

- `templateIds`: 哪些模板复用同一视觉与信息组织
- `accentColorHex` / `surfaceColorHex`: 题材色与表面色
- `genreGlyph` / `portraitGlyph`: 跨引擎图标 glyph
- `badges` / `stats` / `tips` / `actions`: 图文混排的静态骨架

这让“SC2/War3 风格单选头像”、“全战风格兵牌”、“4X 纵向战略卡”共享同一 profile 结构，只是 profile 内容与 selection 规模不同。

### 2. Text Tokens And Locales

所有稳定文案走 token，不把最终文案写死在 C#：

- showcase 题材词表：
  - `mods/showcases/info_panels/GenreInfoShowcaseMod/assets/Presentation/text_tokens.json`
  - `mods/showcases/info_panels/GenreInfoShowcaseMod/assets/Presentation/text_locales.json`
- capability 通用词表：
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/assets/Presentation/text_tokens.json`
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/assets/Presentation/text_locales.json`
- runtime 解析：
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/Insight/EntityInsightTextResolver.cs`
  - `src/Core/Presentation/Hud/PresentationTextLocaleSelection.cs`

规则：

- capability 层持有通用动作词，如 `Close`、`Expand All`
- showcase 层持有题材词，如 `G3 Squadron`、`全战风格兵牌`
- locale 切换只改 `PresentationTextLocaleSelection`，不改 profile / selection / UI 结构

### 3. Icons

当前方案不依赖引擎私有 icon atlas，而统一使用 glyph 生成图标 URI：

- `mods/capabilities/entityinfo/EntityInfoPanelsMod/Insight/EntityInsightIconFactory.cs`
- `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Insight.cs`

好处：

- Skia、Raylib、Web 走同一资源描述
- 不需要为 showcase 维护三套平台位图
- 题材切换只改 profile glyph 与色板，不改 renderer 合同

## Composition Modes

## 1. MOBA 单选头像

MOBA hero 面板走“头像优先，动作少而重”的单选结构：

- 左侧 dock 显示单实体头像卡
- 右侧 insight brief 显示 portrait、role、关键技能状态
- 代表代码：
  - `mods/showcases/info_panels/GenreInfoShowcaseMod/UI/GenreInfoShowcasePanelController.cs`
  - `mods/capabilities/entityinfo/EntityInfoPanelsMod/UI/EntityInfoPanelUiComposer.cs`

这条路径覆盖 SC2 / War3 的单选高可读头像思路。

## 2. RTS 多选头像融合 + 全战兵牌

RTS 多选时分成三层：

- avatar strip：前 6 个头像做快速扫读
- primary portrait card：保留主单位语义
- virtualized unit-card grid：用兵牌承载大编组读数

虚拟窗口由 `ReactiveContext.GetVerticalVirtualWindow(...)` 驱动，showcase host id 为 `genre-info-selection-grid`，实现位于：

- `mods/showcases/info_panels/GenreInfoShowcaseMod/UI/GenreInfoShowcasePanelController.cs`
- `src/Libraries/Ludots.UI/Runtime/UiScene.cs`

这条路径覆盖“头像融合”、“SC2 主头像 + 多选补充”、“Formation Capability 兵牌网格”三种读取需求。

## 3. 4X 战略纵卡

4X governor 面板仍复用同一 profile / brief 结构，但强调：

- 长线资源与科技
- 扩张与经济 action lens
- 相对克制的选择规模

对应 profile 见 `mods/showcases/info_panels/GenreInfoShowcaseMod/assets/EntityInfo/insight_profiles.json` 中的 `fourx_governor_profile`。

## Runtime Flow

端到端数据流如下：

1. `MapLoader` / runtime spawn 创建实体，并在 `RuntimeEntitySpawnSystem` 写入 `EntityTemplateKeyRef`
2. `GenreInfoShowcaseRuntime` 预设控制组与当前 view
3. `SelectionRuntime` / `SelectionControlGroupRuntime` 维护 live、formation、control group 容器
4. `EntityInfoPanelService.Refresh(...)` 按 slot 从 ECS / GAS 采样 stats、tips、actions
5. `GenreInfoShowcasePanelController` 读取 viewed selection，生成 dock + virtual window
6. `EntityInfoPanelUiComposer` 读取 sampled insight slot，生成右侧 insight brief
7. `UIRoot` / `UiScene` 把统一 scene 交给具体 renderer

EntityInfo 也可以直接查看 `EntityCollectionStore` 中的显式 collection：

1. caller 用 `(owner entity, collection key)` 创建 `EntityInfoPanelTarget.EntityCollection(...)`
2. `EntityInfoPanelService` 解析 collection view 和 row window
3. 每个 row 仍走 insight profile、text resolver 和 icon factory
4. UI composer 渲染 collection title、summary、row subtitle/accent/body

这条路径用于 query/display collection，不改变 `SelectionRuntime` viewed selection。缺失 collection 或 template id 会显式失败。

关键代码路径：

- `mods/showcases/info_panels/GenreInfoShowcaseMod/Runtime/GenreInfoShowcaseRuntime.cs`
- `mods/showcases/info_panels/GenreInfoShowcaseMod/UI/GenreInfoShowcasePanelController.cs`
- `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Sample.cs`
- `mods/capabilities/entityinfo/EntityInfoPanelsMod/UI/EntityInfoPanelUiComposer.cs`
- `src/Libraries/Ludots.UI/UIRoot.cs`
- `src/Libraries/Ludots.UI/Runtime/UiScene.cs`

## Performance And SoA Boundaries

## 1. SoA Sampling

`EntityInfoPanelService` 用固定容量数组保存 panel state，而不是每帧拼对象树：

- 固定 panel 容量：`PanelCapacity = 96`
- 固定 stat/action 槽位：
  - `_insightStatCurrentValues`
  - `_insightStatBaseValues`
  - `_insightActionFlags`
- 固定 UI / overlay 可见槽位：
  - `_visibleUiSlots`
  - `_visibleOverlaySlots`

相关代码：

- `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.cs`
- `mods/capabilities/entityinfo/EntityInfoPanelsMod/EntityInfoPanelService.Storage.cs`

## 2. Zero-Allocation Boundary

本方案的“零分配热路径”边界在 authoritative runtime 和 sampled state：

- selection truth 通过 ECS 容器维护，不复制成 panel 私有集合
- query/display rows 通过 `EntityCollectionStore.CopyWindow(...)` 进入 sampled state
- insight stats / actions 刷新写回预分配数组
- dirty 判定靠 revision，而不是每帧无脑重建

需要明确的是：

- UI retained tree 在状态变化时仍会构建 `UiElementBuilder`，因此整个展示层不是绝对零分配
- 但状态变化被 `UiRevision`、selection signature 和 virtual window 显式收敛，避免大编组全量 mount

这是当前实现中“高性能 + 可维护 + 不额外造 runtime”的最佳平衡。

## 3. Virtualization

showcase 的 RTS 战群故意扩到 26 个单位，用于验证窗口化：

- 资产：`mods/showcases/info_panels/GenreInfoShowcaseMod/assets/Maps/genre_info_showcase.json`
- 验收：`src/Tests/PresentationTests/GenreInfoShowcasePlayableAcceptanceTests.cs`
- 证据：`artifacts/acceptance/genre-info-showcase/trace.jsonl`

验收中虚拟窗口从 `1-7 / 9` 滚到更深位置，且不需要 remount 整个 scene。

## Cross-Engine Adaptation Rules

跨引擎适配只允许发生在 UI runtime 边界，不允许把题材逻辑下沉到 host：

- 统一 scene：`src/Libraries/Ludots.UI/UIRoot.cs`
- 统一 retained tree：`src/Libraries/Ludots.UI/Runtime/UiScene.cs`
- renderer 注入：
  - `IUiRenderer`
  - `IUiTextMeasurer`
  - `IUiImageSizeProvider`

因此：

- profile、token、selection、sampling 全部与引擎无关
- icon 使用统一 URI/glyph 表达
- 引擎差异只存在于“如何测量文本、如何渲染图片、如何导出截图”

## Multilingual Rules

多语言方案遵循以下约束：

- 模板和 selection alias 永远不翻译，保持稳定键
- 用户可见文案都走 `PresentationTextCatalog`
- locale 切换只触发 panel refresh，不重建 gameplay state
- capability 词表与 showcase 词表分层归属，避免 demo 反向污染能力层

## Template Rules

EntityInfo templates 是 presentation-only descriptor：

- 模板注册在 `EntityInfoPanelTemplateCatalog`
- request 使用 `EntityInfoPanelRequest.TemplateId`
- 模板可以控制 section flags、collection row 字段、布局模式
- 模板复用现有 profile、text token、icon 和 `UIRoot` 路径

模板不能定义实体真相、selection 真相或独立文本系统。缺失模板、缺失必需 profile、缺失 token key 都必须显式失败。

当前验收覆盖：

- 英文 RTS 多选战群
- 英文 MOBA 单选英雄
- 中文 4X 总督卡
- 中文 RTS 建筑卡

## Acceptance Evidence

代码与可玩证据：

- 验收测试：`src/Tests/PresentationTests/GenreInfoShowcasePlayableAcceptanceTests.cs`
- battle report：`artifacts/acceptance/genre-info-showcase/battle-report.md`
- trace：`artifacts/acceptance/genre-info-showcase/trace.jsonl`
- path：`artifacts/acceptance/genre-info-showcase/path.mmd`
- screenshots：
  - `artifacts/acceptance/genre-info-showcase/screens/01-rts-squad-en.png`
  - `artifacts/acceptance/genre-info-showcase/screens/02-moba-hero-en.png`
  - `artifacts/acceptance/genre-info-showcase/screens/03-fourx-governor-zh.png`
  - `artifacts/acceptance/genre-info-showcase/screens/04-rts-barracks-zh.png`

## Residual Debt

当前已知残余项：

- glyph icon URI 目前以运行时生成字符串为主，如后续 profiling 证明有热点，可在 `EntityInsightIconFactory` 上增加缓存，但不能下沉成引擎私有图标分支
- Component / GAS inspector 仍以诊断阅读为主，不属于本次题材化 panel 的视觉主路径
- 通用 collection/query 基建见 `docs/architecture/entity_collection_query_infrastructure.md`
