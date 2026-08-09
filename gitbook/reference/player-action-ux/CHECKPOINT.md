# 玩家动作 UX 图鉴 · Agent Checkpoint

后续 Agent 读此页再改，避免和旧口头结论打架。

## 生成时身份

- 生成脚本：`scripts/generate-player-action-ux-catalog.py`
- 逻辑文案：`scripts/player_action_ux_beat_logic.py`
- 实现标注：`scripts/player_action_ux_impl_notes.py`
- 生成时 HEAD：`1c47882f4`（以你拉取后的 `git rev-parse` 为准；合并后会变）
- 分支语境：`cursor/ux-sequence-io-4211`（时序=设备→逻辑→画面；左栏仍为复刻目标）
- 已合 main：图鉴 #743–#750（含按游戏分类）

## 页面交互约定（改 UI 前先读）

- 三栏：**复刻目标游戏** | 动作列表 | 详情
- 左栏 id 来自 `TARGET_GAMES`；筛选看 `case.targets.includes(id)`，允许重复
- `case.category` / `family` = 功能族，只给 impl_notes 与详情副标
- 详情内：**一镜一对**——每拍一行，左 Mermaid / 右分镜，共用一条滚动轴（禁止左右各自滚）
- 拍号芯片 / ←→ / h·l 跳到对应那一对；每拍单独一张时序图
- **时序图参与者只有三个：设备输入 → 逻辑处理 → 画面输出**；禁止再把手感/爽点做成泳道
- 每拍必有 `input` / `logic` / `screen`；`logic` 来自 `BEAT_LOGIC`，缺键直接 fail 生成
- 每个 case 必有 `ludots` 与 `todos`；勿手改 `catalog-data.js`

## 复刻目标分类

- `sc2` **星际争霸2**（49）— 框选 · 指令队列 · 控制组 · 热键栏
- `ra2` **红色警戒2**（47）— 生产建造 · 电力 · 右键语境指令
- `war3` **魔兽争霸3**（47）— 英雄技 · 物品栏 · RTS 混战
- `lol` **英雄联盟**（46）— QWER · 技能瞄准 · 补刀走位
- `wow` **魔兽世界**（63）— 技能栏 · 读条 · 任务与社交循环
- `clash` **皇室战争**（7）— 拖卡部署 · 圣水 · 触控车道
- `rotk` **三国志式选单**（6）— 武将 → 指令 → 目标分层菜单
- `gow` **战神式动作**（70）— 近战连段 · 临时武器栏 · 闪避窗
- `diablo` **暗黑式 ARPG**（68）— 点地走打 · 技能落点 · 刷宝
- `twin` **双摇杆射击**（8）— 左走右瞄 · 弹幕清屏
- `fps` **FPS / TPS**（31）— 准星 · 开镜 · 射击换弹
- `zelda` **塞尔达 / 开放世界**（16）— 情境按键 · 攀爬采集互动
- `shared` **跨品类通用**（22）— 拒绝反馈 · 设计手势 · 共通走位

- 同一动作出现在多个游戏下是预期
- 仍可后续补目标：战棋格子、塔防造塔、炉石对战、观战裁判（见 todos）

## 规模

- cases = 168
- beats = 350
- target_games = 13
- target_memberships = 480（含跨游戏重复）

## 分镜画面审计

- 仅 badge / 空 cast 的弱分镜拍数：0
- 弱分镜不阻断生成，但改数据时应补单位/光标/指示器，禁止「只有字没有画面」

## 高频 TODO（去重）

- FPS 开镜换弹等偏展示/模组，无统一枪械 UX 主链
- InputCastSpec（套索/多边形）RFC 有、代码未落地
- RA2/SC2 级完整矩阵靠数据填满，不是代码写死兵种表
- RFC-0065 欲退役专用 aim 事件，CastCommit 配置当前多为空 profiles
- Settings UI 未把偏好链完整接到玩家可点选项
- TODO: MenuCommandStack（推入/弹出/目标阶段）
- TODO: ProximityContextProbe + 交互动词优先级表
- TODO: TempAbilityKit（授予/计时/收回）运行时 + 命令卡绑定
- TODO: TouchDragCast（手指拖卡片→地面/车道部署）
- TODO: item-use 交互（自用/对目标/对地）与快捷栏拖放正式化
- TODO: party / trade / dialogue 产品基建
- TODO: 临时授予/收回的 UX 主链（变身整栏、饰品主动、偷技拷贝）
- TODO: 分层选单（武将→指令→目标）的正式导航栈与取消链
- TODO: 情境交互探测 + 优先级 + 同一键路由（处决/拾取/开门）
- TODO: 每技能 Autocast 开关写入偏好并参与 CastCommit
- TODO: 玩家 Autocast 开关、进距门闩、多技能优先级、手动抢占规则
- TODO: 统一拒绝原因码 → 提示/音效/图标闪烁
- TODO: 触控拖拽卡牌、部署落点、双指缩放/平移的 InputCast / pointer 扩展
- TODO: 采集读条、商人、复活、任务追踪等世界循环
- 双目标连续点选要靠能力配置与多次 commit，缺统一 UX 向导
- 完美闪避窗口若要用引擎级 Prompt，需接 GasInputResponse，产品层未铺全
- 宝宝 AI 自动技见 Utility Autocast，与玩家开关不是一条链
- 技能主链仍大量依赖旧 InteractionModeType，未完全切到 CastCommitProfile
- 按住连发与通道打断的统一手感表仍分散在各 ability
- 控制组存取仅 InteractionShowcase，未进 Core 正式 API
- 提升为 Core：编队存取与跨会话策略
- 无统一「连段编辑器」产品链，多在具体 showcase/模组
- 点地走自己与 WASD 冲突仲裁需在 scheme/意图层写清；冲刺耐力属能力/属性
- 玩法向轮盘/信号需接到 CommandIntent 或 UI→Order 桥，尚未标准菜谱
- 磁吸辅助、翻滚中转向等属手感策略，需模组/配置声明，非全家桶默认开
- 缺统一动态 context 交互键（与二十三类同源缺口）
- 钩索等品类手感在模组，不在 Core 通用动词里写死
- 默认 intent 配置偏 moveTo，复杂兵种语义需数据补齐
- 默认工程配置可能未挂齐，需模组显式声明 dispatch profile
