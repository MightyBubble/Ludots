# 玩家动作 UX 图鉴 · Agent Checkpoint

后续 Agent 读此页再改，避免和旧口头结论打架。

## 生成时身份

- 生成脚本：`scripts/generate-player-action-ux-catalog.py`
- 逻辑文案：`scripts/player_action_ux_beat_logic.py`
- 实现标注：`scripts/player_action_ux_impl_notes.py`
- 动作编号/平台变体：`scripts/player_action_ux_action_index.py`
- 生成时 HEAD：`43e9a1672`（以你拉取后的 `git rev-parse` 为准；合并后会变）
- 分支语境：`cursor/ux-action-id-platform-tabs-4211`（unique 动作编号 + 主机/键鼠/触控 tab）
- 已合 main：图鉴 #743–#755（含按游戏分类、时序三参与者、一镜一对、双人审核）

## 页面交互约定（改 UI 前先读）

- 三栏：**复刻目标游戏** | **唯一动作列表（UX-NNN）** | 详情
- 左栏 id 来自 `TARGET_GAMES`；筛选看动作任一平台变体的 `targets`，允许重复
- 中间列表按 `actions[]`（查重后的 unique 交互），不是原始 case 堆叠
- 同一交互在主机 / 键鼠 / 触控上的不同实现 → 详情顶部分 tab 切换 `variants[]`
- `case.category` / `family` = 功能族，只给 impl_notes 与详情副标
- 详情内：**一镜一对**——每拍一行，左 Mermaid / 右分镜，共用一条滚动轴（禁止左右各自滚）
- 拍号芯片 / ←→ / h·l 跳到对应那一对；每拍单独一张时序图
- **时序图参与者只有三个：设备输入 → 逻辑处理 → 画面输出**；禁止再把手感/爽点做成泳道
- 每拍必有 `input` / `logic` / `screen`；`logic` 来自 `BEAT_LOGIC`，缺键直接 fail 生成
- 每个 case 必有 `ludots` / `todos` / `actionNo` / `platform`；勿手改 `catalog-data.js`

## 双人交叉审核（本轮）

- 10 路审核：5 批 × 双人独立，覆盖全部 168 case
- 双方一致 225 条（high 96 / med 95 / low 34）；单人 high 19 作参考
- F0–F4 修复合并后，R1×R2 再审原 high 案；双方仍共指 5 案已补修
- 重点：缺 cast（框/圈/锥/菜单/卡/键/条）、该拆未拆拍、文案与画面不一致

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
- `fps` **FPS / TPS**（32）— 准星 · 开镜 · 射击换弹
- `zelda` **塞尔达 / 开放世界**（16）— 情境按键 · 攀爬采集互动
- `shared` **跨品类通用**（22）— 拒绝反馈 · 设计手势 · 共通走位

- 同一动作出现在多个游戏下是预期
- 仍可后续补目标：战棋格子、塔防造塔、炉石对战、观战裁判（见 todos）

## 规模

- unique_actions = 163
- multi_platform_actions = 6
- 平台覆盖（唯一动作计）：主机 12、键鼠 151、触控 6 —— 图鉴目前以键鼠为主，主机/触控实现是内容缺口，不是渲染 bug
- cases = 169（含平台变体实现）
- beats = 359
- target_games = 13
- target_memberships = 481（含跨游戏重复）

## 分镜画面审计

- 仅 badge / 空 cast 的弱分镜拍数：0
- 弱分镜不阻断生成，但改数据时应补单位/光标/指示器，禁止「只有字没有画面」
- `_audit_storyboard()` 是硬闸，规则表在 `scripts/player_action_ux_storyboard_rules.py`：
  1. 平台标注 vs 画面元件（键鼠不许画摇杆、触控不许画鼠标、主机不许画鼠标）
  2. 平台标注 vs 文案设备词（键鼠文案不许出现摇杆/扳机，反之同理）
  3. 文案承诺的元素必须画出（准星/菜单/选中圈/读条/落点圈/扇形/摇杆/选框/技能栏/键帽/敌人/卡牌/轨迹/触点/WASD/轮盘/锚点）
  4. 画面本身合法（有看得见的主体、坐标不出界、同类元件不画重）
  5. 镜位未登记、光标状态渲染器画不出、箭头压住单位
  6. 元件参数只能用白名单里的枚举值（写别的渲染器会静默画错）
- 任一命中直接 fail 生成；要放宽先改规则表，不许在数据里绕开
- 机器判不出来、要人做内容决策的遗留项在 `AUDIT-BACKLOG.md`（平台补齐、编号合并、该拆的动作、缺的失败拍、还缺的元件）—— 动图鉴前先读那一页
- 光标状态白名单：idle / down / drag / up / aim；`aim`=施法准星，`up`=松手波纹
- 镜位角标只出人话：俯视战场、斜俯视、越肩视角、第一人称

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
