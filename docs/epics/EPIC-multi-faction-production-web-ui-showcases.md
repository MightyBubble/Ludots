# 多势力 RTS 生产体系 Showcase 套件 — 产品与工程规格

本规格交付四个可玩的实时战略（RTS）/策略演示 Mod，分别对应四种经典商业游戏的生产与经济范式。每个 Mod 都是一个完整可玩的场景：玩家选择并切换不同势力的视角，亲手建造基地、训练单位、研究科技树、与其他势力进行外交与资源交易。所有界面均为运行在游戏内嵌浏览器中的真实 Web 应用。

本文档面向两类读者：评审/合作方（看「做什么、长什么样」），以及实现工程师/agent（看「用什么基建、怎么接线、验收标准」）。阅读本文不需要任何外部背景。

---

## 1. 术语表（先读，全文沿用）

### 1.1 通用概念

| 术语 | 含义 |
|------|------|
| **势力 / Faction** | 一方玩家阵营（如同盟国、苏联、人族）。一局里有 2–3 个势力。 |
| **势力视角 / Participant View** | 当前以哪个势力的眼睛看世界：决定镜头、战争迷雾（可见范围）、可操作的单位、可用的指令。玩家可随时切换查看不同势力的局面。 |
| **资源 / Resource** | 用于建造和训练的货币（如金钱、矿、木、食物）。由采集单位获取或随时间增长。 |
| **单位 / Unit** | 可移动的实体（士兵、车辆、工人、采集车）。 |
| **建筑 / Structure** | 不可移动的实体（生产建筑、防御塔、资源精炼厂）。 |
| **生产建筑 / Production Building** | 能训练单位或研究科技的建筑（兵营、车间、孵化场等）。 |
| **集结点 / Rally Point** | 生产建筑产出的新单位自动前往的位置，可由玩家在地图上设置。 |
| **战争迷雾 / Fog of War** | 未被己方单位探明的区域在该势力视角下不可见。 |
| **科技树 / Tech Tree** | 由前置依赖连成的有向图：研究一个科技节点后，解锁其下游的单位/建筑/升级。 |
| **外交 / Diplomacy** | 势力之间的关系状态（战争 / 中立 / 停火 / 同盟）与协定（通商条约、禁运等）。 |
| **交易 / Trade（资源互换）** | 势力之间用资源（或物品）做一笔交换：一方提供、一方接收，双方同意后原子结算。 |

### 1.2 五种生产范式（本套件分布在四个 Mod 中全部覆盖）

| 范式 | 说明 | 出自 |
|------|------|------|
| **直接建造 / Direct Build** | 在「建造场」里直接生产建筑（进度条），完成后玩家把建筑「放置」到基地附近的合法地块。 | 红警 / C&C |
| **部署建造 / Deploy Build** | 一台可移动的「基地车（MCV）」开到目标位置后「展开/部署」，原地变成建造场，从而开辟新基地。**MCV = Mobile Construction Vehicle，移动建造车，是一台车辆单位，部署后消失并变成建造场建筑。** | 红警 / C&C |
| **工人建造 / Worker Build** | 选中工人单位（农民 / SCV / 苦工），选建筑并在地图选址，工人走到工地并花时间盖好；工人越多越快。 | 星际(人族) / 帝国 / 魔兽 |
| **产卵孵化建造 / Spawn-Morph Build** | 没有独立工人：基地建筑周期性「产卵」出**幼虫（Larva）**；玩家把幼虫「孵化（Morph）」成单位（幼虫被消耗）。盖建筑则是把一个工人单位（**Drone**）直接「形变（Morph）」成建筑（Drone 被消耗）。**「产卵」= 建筑定时生成幼虫；「孵化/形变」= 把已有实体变成另一种实体并消耗掉原实体。** | 星际(虫族) |
| **城市排产 / City Production** | 每座城市有一个生产队列；玩家把建筑/单位/奇观加入队列，按生产力随时间逐个完成；可调整顺序、取消。 | 4X（文明 / Stellaris） |

### 1.3 范式相关专有名词

| 术语 | 含义 |
|------|------|
| **建造场 / Construction Yard** | 红警里基地核心建筑，是「直接建造」其他建筑的来源。 |
| **MCV / 基地车** | 见上「部署建造」。车辆单位，命令卡里有「部署」按钮。 |
| **采集车 / Harvester** | 红警里自动往返矿田与精炼厂、把矿换成金钱的车辆。 |
| **Pylon / 水晶塔（神族供能）** | 星际神族建筑，在其周围形成「能量场」，神族建筑只能建在能量场内。 |
| **Warp（折跃）/ Warp Gate（折跃门）** | 神族科技：研究后「传送门 Gateway」升级为「折跃门」，可直接把单位「折跃」到 Pylon 能量场内的目标点（带折跃读条），而不是在建筑内训练。 |
| **Larva / 幼虫**、**Drone / 工蜂** | 见上「产卵孵化建造」。 |
| **时代 / Age（帝国）** | 帝国里的科技层级（如 I/II/III 时代）。在「市政厅」研究「进入下一时代」，完成后解锁更高层的建筑/单位/升级。是多层科技树的体现。 |
| **市政厅 / Town Center**、**农民 / Villager** | 帝国主基地与工人单位。 |
| **城市 / City、定居者 / Settler** | 4X 里产出与扩张的核心。 |

---

## 2. 工程基线与平台能力（实现 agent 必读）

**分支基线**：`main`（commit `d289bd3d1`，即合入 Browser UI Runtime 的主线）。运行宿主为 **Raylib 适配层 + CEF 浏览器运行时**。本套件**不使用、不修改** `src/Adapters/Web`、`src/Client/Web`、`src/Platforms/Web`（那是另一套与本套件无关的服务端流式渲染栈）。

平台已提供以下能力，本套件在其上构建。每条给出「是什么 + 关键入口 + 参考实现」。

### 2.1 浏览器 UI 运行时（UI 的载体）

游戏可在画面里嵌入一个真实浏览器表面（CEF），加载本地打包的 Web 应用（HTML/JS/CSS，可用 React 等框架）。该表面作为一个 `Ui.Canvas` 节点挂在游戏 UI 场景里；指针/键盘/焦点仍由游戏统一管理（透明像素穿透到原生层）。Raylib 宿主把浏览器帧直传到纹理。

- 关键类型：`IBrowserRuntime`（service key `"BrowserRuntime"`，由 `BrowserCefRuntimeMod` 提供）、`IBrowserSurface`、`BrowserSurfaceCanvasContent`、`BrowserAppResourceResolver`、`BrowserViewport`、`BrowserNavigationRequest`（导航到 `ludots-app://app/`）。
- 路径：`src/Libraries/Ludots.UI.Browser*`、`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibBrowserLayerRenderer.cs`。
- 文档：`docs/architecture/browser_ui_runtime.md`、ADR `docs/adr/ADR-0003-browser-ui-runtime-contract.md`。

### 2.2 WebUI DataPlane（Web 与游戏的数据/指令通道）

Web 应用与游戏运行时之间唯一的通信层：游戏把状态以「topic」推送给 Web（snapshot/delta），Web 把玩家操作以「command」回传给游戏。

- 宿主侧（C#，命名空间 `Ludots.WebUI.DataPlane`）：`IWebUiTopicProducer`（产出 topic 数据）、`WebUiDataPlaneRuntime`（`RegisterTopic` / `AttachSession` / `PublishAsync`）、`WebUiCommandRouter` + `IWebUiCommandHandler`（处理 Web 发来的指令）、`IWebUiCommandPermissionValidator`（权限校验）、二进制批量帧 `WebUiEntityColumnarPacket`（SoA）。传输 `BrowserMessageBridgeDataTransport(surface.Messages)`。
- Web 侧（JS SDK，见参考实现 `client.js`）：`createLudotsDataPlaneClient()` → `handshake()` / `subscribe(topic, handler)` / `command(name, payload)`。开发期有 fake transport，可脱离游戏单独调 UI。
- **数据复用律**：topic 必须是既有 Core 存储的投影，不得另造：实体列表/检视/命令源 → `EntityCollectionStore`（owner + collectionKey + window + revision）；高频地图标记 → `MinimapMarkerBuffer` / `MinimapScreenMarkerBuffer`（SoA/bucket/drop 诊断）。未知 topic / 缺服务必须 fail-fast。
- 文档：`docs/architecture/webui_dataplane_architecture.md`。
- **黄金参考实现（务必精读并照搬接线）**：`mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/`（React + React Flow + DataPlane，含 `...ModEntry.cs`、`...DataPlane.cs`、`WebApp/`）。**科技树的树状视图直接用该范例里的 React Flow。**

### 2.3 其他领域基建

| 能力 | 用途 | 关键入口 |
|------|------|----------|
| **Entity Association（AAC）** | 单位/建筑的归属（`Owns` 边）、父子（驻军/建造附着）、命名集合（供 topic 投影）、势力视角切换 | `src/Core/Association/OwnershipResolver.cs`、`src/Core/EntityCollections/`、`mods/capabilities/participant_view/ParticipantViewCapabilityMod/` |
| **Progression（科技树后端）** | 数据驱动的解锁状态与前置需求（`useRequirement`=可见但锁定、`showRequirement`=隐藏、`CompleteProgression`=授予解锁） | `src/Core/Gameplay/Progression/`；参考 `mods/showcases/progression_scope`、`team_research` |
| **Exchange + Relationship（交易+外交后端）** | 原子资源结算 `ExchangeRuntime.TryExecute`（+动态报价 `ExchangeScopedOperationStore`）；关系 metric/flag（Trust、TradePact、Embargo、AtWar） | `src/Core/Gameplay/Exchange/`、`src/Core/Gameplay/Relationships/`；参考 `mods/showcases/{diplomacy_trade_gate,gold_market,item_system,fourx_association}` |
| **Save（存档）** | 世界快照存读档 | `src/Core/Persistence/`（需实现玩家文件系统 `ISaveStorage`） |
| **AI** | Utility AI 主循环（已支持战斗 autocast；宏观建造/训练/研究需出货 AI 配置 + order 类型） | `src/Core/Gameplay/AI/`、`mods/AIInspectorMod/` |
| **生产 gameplay 现成数据** | 已有 5 种范式的 GAS 实现可复用/迁移 | `mods/RtsDemoMod/assets/GAS/`、`Entities/templates.json` |

### 2.4 架构铁律（SSOT）

1. UI 全部在 Web 应用内实现，经 DataPlane topic/command 通信；显示数据是 Core 存储的投影，操作经 command → 领域服务。
2. 被生产单位/建筑的归属真相用 `OwnershipResolver.EnsureOwnership`；势力切换走 `ParticipantViewCapabilityRuntime`。
3. 科技树用 `Progression`（`scopes/progressions/requirements` JSON），禁 tag 解锁；多层用 `ProgressionLevelAtLeast` / 链式 `ProgressionCompleted`。
4. 外交=Relationship、交易=Exchange、报价握手=Mod 层状态机；禁造 Merchant/Trade/Treaty 的 Core runtime。
5. 数据驱动、无隐式硬编码；坏配置/缺资源加载期 fail-fast。

---

## 3. 通用界面规格（四个 Mod 共用，逐屏写清「画面里有什么 / 能点什么」）

下列组件实现一次（共享 React 组件 + 共享 DataPlane topic/command 助手），四个 Mod 复用，仅换皮肤/数据。每个组件标注：**画面内容**、**可操作项**、**数据来源(topic)**、**操作回传(command)**。

### 3.1 顶部资源栏 Resource Bar
- 画面：当前势力拥有的每种资源一个芯片（图标 + 当前数量 + 每秒/每回合产出速率）；右侧显示人口/补给（已用/上限）。
- 可操作：纯展示，不可点。
- topic：`<mod>.resources`（来自该势力资源属性/库存）。

### 3.2 命令卡 Command Card（右下角）
- 画面：当前选中实体的能力按钮网格（如 4×3）。每个按钮：图标、热键字母、悬停提示（名称、消耗、冷却、前置）。状态：可用 / 锁定（置灰，提示缺少的前置）/ 隐藏。
- 可操作：点击按钮发出该能力——训练单位、研究、特殊指令；若是「建造」能力则进入放置模式（见 3.3）。多选时显示共有能力。
- topic：`<mod>.selection.commands`（由选中实体 + GAS ability slots + progression 需求投影）。
- command：`activateAbility { entityRef, abilityId, target? }`。

### 3.3 建造菜单 + 放置模式 Build Menu & Placement
- 画面：选中工人/生产建筑时，列出可建造项（图标、名称、资源消耗、建造时间、热键）；锁定项显示锁与前置提示。
- 可操作：
  - 点击可建造项 → 进入「幽灵放置」模式：一个半透明建筑轮廓跟随光标，合法地块显示绿色、非法红色（占用/超出建造半径/地形不合）；在地图点击合法点放置。
  - 工人建造：放置后工人走到工地并施工（进度）；直接建造：先在建造场排队生产，完成后再放置。
- topic：`<mod>.buildables`（按当前势力科技解锁状态过滤）。
- command：`placeBuild { builderRef?, structureId, x, y }`。

### 3.4 生产队列 Production Queue
- 画面：选中生产建筑（或全局）时，显示在产项目列表：每项图标 + 进度环/条 + 剩余时间。红警车间的分步扣费在条上显示分段扣费节点。
- 可操作：点击某项 → 取消（按已投入比例退款）；可设置集结点（在地图点击）。
- topic：`<mod>.production.queue`。
- command：`cancelQueueItem { buildingRef, index }`、`setRallyPoint { buildingRef, x, y }`。

### 3.5 选中信息面板 Selection Info（底部中央）
- 画面：头像、名称、HP/护盾条、关键属性（攻/防/射程）、当前状态/命令（建造中/训练中/移动/待命）、所属势力。多选 → 单位图标网格（可点单个聚焦）。
- 可操作：点击多选网格里的单位 → 聚焦该单位。
- topic：`<mod>.selection.info`。

### 3.6 小地图 Minimap（左下角）
- 画面：俯视地图缩略图，含地形、当前势力视角的战争迷雾、按关系着色的实体标记（己方/盟友/敌方/中立）、当前镜头视野矩形。
- 可操作：点击 → 镜头跳转；右键 → 对该点下达移动/攻击（可选）。
- topic：`<mod>.minimap.markers`（marker buffer 投影，SoA/bucket）。
- command：`moveCamera { x, y }`、`issueOrder { x, y, kind }`。

### 3.7 势力切换器 Faction Switcher（顶栏）
- 画面：所有势力的标签/下拉，标注哪一个是人类控制、哪些是 AI；高亮当前视角势力。
- 可操作：点击某势力 → 切换 participant view（镜头、迷雾、归属过滤、可用指令随之变化）。
- command：`switchParticipantView { participantId, mode: "player"|"team" }`。

### 3.8 科技树视图 Tech Tree（树状视图，必须是图）
- 画面：一个可平移/缩放的画布，**节点 = 科技**，**有向边 = 前置依赖（箭头）**；按层级/时代分列（从左到右或自下而上），帝国按时代分泳道并标注时代名。
  - 节点内容：图标 + 名称；状态配色——已解锁(实心/绿)、可研究(高亮/可点)、锁定(置灰，显示缺失前置)、研究中(进度环 + 预计完成)。
  - 悬停节点 → 提示卡：描述、消耗、解锁内容（哪些单位/建筑/升级）、前置列表。
- 可操作：点击「可研究」节点 → 发起研究（command）→ 节点转为研究中并显示进度 → 完成后自身变已解锁、下游节点变为可研究、对应生产项在建造菜单/命令卡解锁。
- 实现：用 React Flow（见 2.2 黄金参考）。
- topic：`<mod>.techtree`（节点 + 边 + 各节点状态，来自 `Progression` 后端：`ProgressionStateBuffer` + 各 requirement 评估）。
- command：`researchTech { techId, scopeHostRef }`。

### 3.9 外交面板 Diplomacy Panel
- 画面：当前势力与每个其他势力一行：对方名称/旗帜、当前态势（战争/中立/停火/同盟）、信任度 Trust 条(0–100)、生效协定标记（通商✓、禁运⛔、防御同盟…）。底部「待处理提案」收件箱：来自其他势力（含 AI）的提案，带「接受/拒绝」。
- 可操作（每个对方一组按钮）：提议停火/通商条约、缔结同盟、宣战、施加/解除禁运、**发起交易**（打开 3.10）。收件箱里接受/拒绝提案。
- topic：`<mod>.diplomacy`（关系矩阵 + 待处理提案，来自 `RelationshipRuntime` + Mod 层提案状态机）。
- command：`proposePact`、`declareWar`、`setEmbargo`、`respondProposal { proposalId, accept }`。

### 3.10 交易界面 Trade Interface（外交面板里点「发起交易」打开的模态）
- 画面：标题「与 <对方势力> 交易」。两列：
  - 左列「你方提供 / You give」：可交易资源（及可选物品/科技）列表，每项带 +/− 步进或输入框设定数量。
  - 右列「对方提供 / You receive」：同上。
  - 中部：双方总价值/公平度指示；若需要「通商条约」或被「禁运」阻断，显示原因与门控状态。
- 可操作：调整两列数量 → 点「发送报价 / Send Offer」（对方/AI 收件箱出现待处理提案）；点「取消」。对方接受后由 `ExchangeRuntime` 原子结算并提示成功/失败（失败如 `RelationshipDenied` 给出原因）。对中立「市场」可做即时买/卖（直接结算，无需握手）。
- topic：`<mod>.trade.offers`（待处理报价）。
- command：`sendTradeOffer { toParticipant, give:[{resource,amount}], receive:[{resource,amount}] }`、`respondProposal`、`marketBuySell { operationId, ... }`。

### 3.11 存档面板 Save/Load
- 画面：存档槽列表（名称、地图、时间、tick）；自动存档标记。
- 可操作：保存（新建/覆盖）、读取。
- topic：`<mod>.saves`（`SaveSlotStore.ListSlots`）；command：`saveGame`、`loadGame`。

### 3.12 通知与事件日志 Toasts & Event Log
- 画面：右侧滚动事件（建造完成、单位训练完成、研究完成、遭受攻击、收到交易/外交提案、外交状态变化）。
- topic：`<mod>.events`。

---

## 4. 四个 Mod 详细规格

> 每个 Mod 给出：势力、生产范式分步操作、单位与建筑清单、科技树内容、外交与交易内容、**场景/地图元素**、Mod 专属界面。所有 Mod 复用 §3 通用界面。

### 4.1 M1 — RedAlertLike（红警 / C&C 流派）

**势力**：同盟国 Allied、苏联 Soviet（机制相同，外观/单位名不同：同盟国 GI/灰熊坦克、苏联 动员兵/犀牛坦克）。
**主打范式**：直接建造 + MCV 部署。
**资源**：金钱 Credits（采集车从矿田采矿，精炼厂转为金钱）。可选：电力（建筑耗电，低电减速生产）。

**生产分步（玩家具体操作）**：
1. **直接建造建筑**：选中「建造场 Construction Yard」→ 建造菜单列出（电厂、矿石精炼厂、兵营、车间、雷达、作战实验室…）→ 点某建筑 → 建造场排队生产（队列进度）→ 完成显示「就绪 Ready」→ 点击进入放置模式 → 在基地建造半径内的合法地块点击放置。
2. **MCV 部署建造**：选中 MCV（车辆）→ 命令卡点「部署 Deploy」→ MCV 原地展开为一座新「建造场」（MCV 消失），从而开第二基地。
3. **训练单位**：选「兵营」→ 训练步兵（GI/动员兵）；选「车间」→ 训练车辆（灰熊/犀牛坦克），**分步扣费**（建造过程中分多次扣金钱）。新单位在建筑集结点产出。
4. **采集经济**：采集车自动在矿田与精炼厂间往返产出金钱（玩家可手动指定矿田）。

**科技树（节点示例，树状视图）**：电厂 → 兵营 →（雷达 → 作战实验室 →〔同盟国: 光棱坦克 / 苏联: 天启坦克〕）；车间需雷达解锁高级车辆。研究=建造对应科技建筑或在其内研究，经 `useRequirement`/`showRequirement` 门控对应训练项。

**外交与交易**：同盟国↔苏联：停火/同盟/宣战/禁运。交易：用金钱或矿石互换（如一方出金钱换对方一次性单位援助，或纯资源对换）；交易需「通商条约」生效。

**场景元素（地图：海岸线，参考 RA 海滨）**：
- 地形：陆地 + 水域 + 桥梁、可通行/阻挡区。
- 起始基地（同盟国东北、苏联西南），各含：建造场 ×1、电厂 ×1、矿石精炼厂 ×1、采集车 ×1、兵营 ×1、起始步兵若干、**MCV ×1（用于演示部署）**。
- 资源：各基地旁矿田，中央争夺矿田 ×1。
- 中立可选：油井/科技建筑（可被占领提供加成）。

**专属界面**：命令卡含 MCV「部署」按钮；建造菜单以「建筑列表 + 就绪后放置」呈现红警风格底部指挥栏 + 右侧建造列表。

### 4.2 M2 — StarCraftLike（星际流派）

**势力**：人族 Terran、神族 Protoss、虫族 Zerg（**三族三种建造范式**）。
**资源**：晶体矿 Minerals + 高能瓦斯 Gas（工人采矿、采气需精炼建筑）；人口/补给上限。

**生产分步（按种族）**：
- **人族（工人建造）**：选 SCV → 建造菜单（补给站、兵营、精炼厂、重工厂…）→ 选址放置 → SCV 走到工地施工（进度，期间占用）。训练：兵营 → 机枪兵 Marine。
- **神族（部署/折跃建造）**：先建 **Pylon（水晶塔）** 提供「能量场 + 补给」→ 选 Probe（探机）→ 在 Pylon 能量场范围内折跃建筑（传送门、控制核心、锻炉…）→ 建筑就地折跃成型（探机不消耗）。训练：传送门 Gateway → 狂热者 Zealot。**科技：研究「折跃门 Warp Gate」→ 传送门升级为折跃门 → 命令卡出现「折跃狂热者 Warp In」→ 在 Pylon 能量场内目标点放置，带折跃读条。**
- **虫族（产卵孵化建造）**：母巢 Hatchery 定时**产卵**出幼虫 Larva（界面显示幼虫数）。建单位：选幼虫 → 孵化菜单（工蜂 Drone / 跳虫 Zergling / 王虫 Overlord…）→ 幼虫孵化成单位（消耗该幼虫）。建建筑：选工蜂 Drone → 「形变为建筑」（孵化池 Spawning Pool 等）→ Drone 形变成建筑（Drone 消耗）。补给靠王虫 Overlord。

**科技树**：以神族「折跃门」为主线展示项；各族另给 1–2 个节点（如人族重工厂解锁坦克、虫族孵化池解锁跳虫）。树状视图按种族分支展示。

**外交与交易**：三族多边——两两之间停战/宣战；交易晶体矿/瓦斯。

**场景元素（地图：高地）**：
- 地形：高地 + 斜坡(ramp) + 关口(choke)。
- 三个起始基地（每族一个），各旁有晶体矿田 + 瓦斯泉。
- 起始配置：人族（指挥中心 + SCV×4 + 兵营）；神族（星灵枢纽 + Pylon×1 + 探机×4 + 传送门）；虫族（母巢 + 工蜂×4 + 孵化池 + 幼虫若干）。

**专属界面**：种族专属命令卡；虫族显示幼虫计数器；神族显示 Pylon 能量场覆盖叠加层与折跃放置 UI。

### 4.3 M3 — EmpireLike（帝国 / Age of Empires 流派）

**势力**：两个文明（如 罗马 vs 汉，含少量专属单位/加成；机制：村民建造 + 训练 + 时代科技树）。
**资源**：食物 Food、木材 Wood、黄金 Gold、石料 Stone（村民采集，回收到市政厅/相应建筑）。

**生产分步**：
1. **村民建造**：选村民（可多选）→ 建造菜单（房屋、兵营、磨坊、市场、箭塔…）→ 选址放置 → 村民走到工地施工（多村民更快）。
2. **训练**：市政厅训练村民；兵营训练步兵；箭术场训练弓兵；马厩训练骑兵。
3. **时代/科技树（多层，核心展示）**：市政厅「进入下一时代 Advance Age」（需满足前置建筑 + 资源）→ 完成后解锁下一时代的建筑/单位/升级（如铁匠铺攻防升级）。共 ≥3 个时代（I/II/III），形成多层树。

**科技树**：按时代分泳道；每时代内若干升级节点（攻防、采集效率、解锁兵种）。

**外交与交易**：两文明：进贡/同盟/宣战。交易：通过「市场」按浮动汇率买卖资源 + 直接向对方进贡资源（交易界面支持市场买卖与对外进贡两种）。

**场景元素（地图：森林）**：
- 地形：草地 + 森林（木材）+ 河流。
- 两个文明出生点；各附近：金矿、石矿、浆果丛/猎物（食物）。
- 起始配置：市政厅 ×1 + 村民 ×3 + 侦察 ×1 + 起始少量资源。

**专属界面**：四资源栏；村民建造菜单；市政厅「时代升级」按钮 + 进度；科技树带时代泳道。

### 4.4 M4 — FourXLike（4X / 文明流派）

**势力**：3 个帝国。
**主打范式**：城市排产 + 深科技树；**外交与交易为核心展示**。
**资源**：黄金 Gold、科研 Science、生产 Production、食物 Food；战略资源（铁、石油等，用于交易）。

**生产分步**：
1. **城市排产**：选一座城市 → 生产面板显示队列 → 添加项目（建筑：粮仓/图书馆/兵营；单位：定居者/战士；奇观）→ 每项显示生产力消耗 + 预计完成 → 可调整顺序/取消。
2. **科技树**：帝国级研究面板——选下一个要研究的科技，按科研产出推进，完成后解锁建筑/单位/改良；树状视图带前置。

**外交与交易（核心）**：
- 外交面板：3 帝国关系矩阵；两两之间态势 + 信任 + 条约（开放边境、通商条约、防御同盟、禁运、战争）。
- 交易界面：完整两列报价构造——提供/接收：黄金（一次性或按回合）、战略资源，可选科技/城市；待处理提案收件箱；接受/拒绝；AI 帝国会主动发起提案。
- 贸易路线：与对方建立商路（商队）→ 周期性收益（经 `EffectRequest` 周期 buff）。

**场景元素（地图：世界地图）**：
- 地形：陆地/海洋/地块资源（战略资源散布）。
- 3 个帝国首都 + 各自少量城市；中立城邦（可选）。
- 起始配置：首都城市 ×1（含起始生产）+ 定居者 ×1 + 战士 ×1 + 起始黄金/科研。

**专属界面**：帝国仪表盘（各资源每回合产出）；城市列表 + 城市生产面板；科技树；外交面板 + 交易界面；世界小地图。

---

## 5. 跨切面要求

- **AI 势力**：每个 Mod 至少 1 个 AI 势力，出货 `AI/{profiles,decisions,tasks,target_filters}.json` + `GAS/order_types.json`（建造/训练/研究/交易/攻击/移动），执行落到生产能力层；人类可切视角观战；可经 `AIInspectorMod` 调试。
- **存档**：实现玩家文件系统 `ISaveStorage`；存档面板为 Web 应用一部分，经 DataPlane topic/command 列出/保存/读取；至少 1 个 Mod 端到端「存档→改局→读档→确定性续跑」。
- **Launcher**：四个 Mod + CEF runtime preset 登记 `launcher.config.json` / `launcher.presets.json`（参考已有 `BrowserCefRuntimeMod` / react-flow preset）；`scripts/run-mod-launcher.cmd cli launch '$rts_<flavor>' --adapter raylib` 可启动并可玩。

---

## 6. 工作分解（建议 PR 切分）

| # | 工作项 | 交付 |
|---|--------|------|
| 0 | 跑通 CEF 范例 | 用 launcher CEF preset 在 Raylib 启动 `BrowserReactFlowShowcaseMod`，确认浏览器层 + DataPlane + alpha 穿透可用，作为模板 |
| 1 | 共享 Web DataPlane SDK | 抽出 `client.js`（含二进制解码、fake transport）为四个 Web 应用复用的本地包 |
| 2 | EntityCollection topic 适配器 | 可复用 `IWebUiTopicProducer`，把 `EntityCollectionStore`(owner+key+window+revision) 投影为 snapshot/delta |
| 3 | marker topic 适配器 | 仿 `MinimapMarkerBuffer` 的 SoA/bucket/drop 单位标记 topic |
| 4 | command 路由助手 | 权限校验 + 错误回传标准化 |
| 5 | 共享 RTS 生产能力 | 从 `RtsDemoMod` 抽生产/建造/训练/驻军 runtime，接 AAC（`EnsureOwnership` + 发布 collection），数据化 5 范式 |
| 6 | 科技树投影 + 势力切换服务 | `ProgressionStateBuffer`+requirement 评估投影为 `<mod>.techtree` topic；势力切换 command 接 `ParticipantViewCapabilityRuntime` |
| 7 | 外交/交易 offer-accept 状态机 | 提议→pending→接受/拒绝；接受用 `ExchangeScopedOperationStore`+`TryExecute`；暴露 diplomacy/trade topic + command |
| 8 | 共享 React UI 组件库 | §3 全部通用组件（资源栏/命令卡/建造菜单/队列/选中信息/小地图/势力切换/**科技树 React Flow**/**外交面板+交易界面**/存档/通知） |
| 9 | M1 RedAlertLike | root mod + Web 应用 + 数据 + 场景 |
| 10 | M2 StarCraftLike | 同上，含 WarpGate 迁移到 progression |
| 11 | M3 EmpireLike | 同上，含多层时代科技树 |
| 12 | M4 FourXLike | 同上，外交/交易为核心 |
| 13 | AI 势力 | 各 Mod ≥1 AI |
| 14 | 存档（Web 面板 + 平台存储） | `ISaveStorage` + 存档面板 |
| 15 | Launcher + 文档 + 验收 | preset 登记、capability-standard 登记、UAT 矩阵、验收测试 |

---

## 7. 验收标准（每个 Mod 必须全部满足）

1. **势力切换**：可在 ≥2 势力视角间切换；UI（命令卡/队列/科技树/外交）随当前势力重投影；可见性/选择按视角正确。
2. **生产范式**：该 Mod 主打范式可玩，单位/建筑归属正确（`Owns` + collection）。本套件合计覆盖直接/部署/工人/产卵孵化/城市排产五种。
3. **训练**：从建筑训练单位，队列/进度/扣费在界面正确（含红警分步扣费），新单位到集结点。
4. **科技树（树状视图）**：节点+边+状态正确显示，悬停有提示，点击可研究节点 → 进度 → 解锁下游与对应生产项。
5. **外交协议**：势力间可签订/撕毁协定（态势/信任/标记变化），按钮驱动；M4 含 offer/accept 握手与收件箱。
6. **交易界面（资源互换）**：两列报价构造可用，发送/接受/拒绝流程完整，经 `ExchangeRuntime` 结算，外交门控生效（被禁运/无条约时给出原因）。
7. **全 Web UI**：所有交互经 CEF 表面 Web 应用 + DataPlane；缺 CEF runtime 时给出明确提示场景；不依赖 `src/Adapters/Web`/`src/Client/Web`。
8. **场景完整**：地图含规格所列地形、各势力起始基地/单位、资源点；进入即可玩。
9. **存档**：≥1 Mod 端到端存读档续跑。
10. **AI 势力**：≥1 Mod 有自动建造/训练/研究的 AI。
11. **Launcher**：CEF preset 可启动。
12. **数据驱动 / fail-fast**：无隐式硬编码；坏配置/缺资源加载期报错。

---

## 8. 参考索引

| 主题 | 路径 |
|------|------|
| 浏览器 UI 运行时 | `src/Libraries/Ludots.UI.Browser*`、`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibBrowserLayerRenderer.cs`、`docs/architecture/browser_ui_runtime.md`、`docs/adr/ADR-0003-browser-ui-runtime-contract.md` |
| WebUI DataPlane | `src/Libraries/Ludots.WebUI/`、`docs/architecture/webui_dataplane_architecture.md` |
| **黄金参考 mod（React + React Flow + DataPlane）** | `mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/` |
| 原生 HTML 浏览器范例 | `mods/showcases/browser_ui/BrowserUiShowcaseMod/`、CEF 运行时 `mods/browser/BrowserCefRuntimeMod/` |
| 生产 gameplay 现成数据 | `mods/RtsDemoMod/assets/GAS/`、`Entities/templates.json`、`Maps/rts_entry.json`、`Systems/RtsRelationRuntimeSystem.cs` |
| 归属/集合/势力视角 | `src/Core/Association/OwnershipResolver.cs`、`src/Core/EntityCollections/`、`mods/capabilities/participant_view/ParticipantViewCapabilityMod/`、`src/Core/Presentation/Minimap/` |
| 科技树后端 | `src/Core/Gameplay/Progression/`、`mods/showcases/{progression_scope,team_research}/` |
| 外交/交易后端 | `src/Core/Gameplay/Exchange/`、`src/Core/Gameplay/Relationships/`、`mods/showcases/{diplomacy_trade_gate,gold_market,item_system,fourx_association}/`、`mods/FourXDemoMod/` |
| 存档 | `src/Core/Persistence/`、`src/Platform/Ludots.Platform.Abstractions/ISaveStorage.cs` |
| AI | `src/Core/Gameplay/AI/`、`mods/AIInspectorMod/`、`gitbook/architecture/ai-utility-autocast-contract.md` |
| Launcher | `launcher.config.json`、`launcher.presets.json`、`scripts/run-mod-launcher.cmd` |
