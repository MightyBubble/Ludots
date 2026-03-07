# Ability Interaction — User Experience Scenario Checklist

> **Purpose**: 从玩家视角穷举所有"技能使用体验"情景，作为架构验收矩阵。
> 每个条目 = 一种玩家可感知的**独特交互体验**，不重复、不遗漏。
>
> **覆盖游戏**: LoL, Dota 2, SC2, Overwatch, Dark Souls, Sekiro, Batman Arkham, Spider-Man, God of War (2018)

---

## A. Passive / Auto-Trigger — 被动/自动生效（玩家无操作）

| # | Scenario | Examples |
|---|----------|----------|
| A1 | 永久属性加成 | DS Combat Shield, LoL Vayne被动移速 |
| A2 | 条件自动触发(叠层到阈值) | LoL Vayne W银箭三环, Caitlyn暴击叠满 |
| A3 | 条件自动触发(血量/状态阈值) | LoL Warwick低血量感知, DS低血量戒指 |
| A4 | 受击自动触发 | Dota反伤刺甲, LoL荆棘之甲 |
| A5 | 死亡触发 | LoL Anivia蛋, Dota Aegis复活, DS幽魂 |
| A6 | 光环持续影响周围 | Dota Assault Cuirass, LoL日炎, SC2护盾 |
| A7 | 击杀触发(收割/重置) | LoL Darius大招重置, Dota Axe Culling Blade |
| A8 | 连击叠层被动 | LoL致命节奏, Dota Jingu Mastery |

---

## B. Instant Press — 按键即生效（无需选目标）

| # | Scenario | Examples |
|---|----------|----------|
| B1 | 自我buff/增强 | Dota BKB, LoL Olaf R, DS/Sekiro嗑药 |
| B2 | 以自身为中心AoE | LoL Amumu R, Dota Ravage, GoW Spartan Rage爆发 |
| B3 | 全图即时生效 | LoL Karthus R, Dota Zeus R/Silencer R |
| B4 | 闪烁/短距瞬移 | LoL Flash, Dota Blink Dagger |
| B5 | 时间回溯(回到几秒前的位置) | OW Tracer Recall, LoL Ekko R |
| B6 | 召回投出的武器/物体 | GoW 斧头召回(回途造成伤害) |
| B7 | 解除控制(净化) | LoL水银QSS, Dota Lotus Orb, DS翻滚解控 |
| B8 | 自爆/牺牲 | Dota Techies自爆, LoL Kog'Maw被动 |
| B9 | 嘲讽/强制聚怪 | LoL Shen嘲讽(自身周围), Dota Axe Berserker's Call |

---

## C. Unit Target — 选取单位施放

| # | Scenario | Examples |
|---|----------|----------|
| C1 | 点选敌方单位 — 伤害/控制 | Dota Laguna Blade, LoL Annie Q, SC2 Feedback |
| C2 | 点选友方单位 — 治疗/增益 | LoL Janna E, SC2 Medivac治疗 |
| C3 | 点选任意单位 — 效果因敌友而异 | LoL Lulu W(变羊/加速), Dota Io Tether |
| C4 | 点选死亡友方 — 复活 | OW Mercy Rez, Dota Chen圣令(早期) |
| C5 | 点选地形/可破坏物 | Dota Timbersaw钩树, Monkey King Tree Dance |
| C6 | 点选自己的召唤物 — 操控/消耗 | LoL Ivern踢Daisy, Dota吞噬亡灵 |
| C7 | 复制/窃取目标技能 | Dota Rubick偷技能, LoL Sylas偷大招 |
| C8 | 点选传送到目标身边 | LoL Shen R(传送到友方), Dota Io Relocate |
| C9 | 连接/牵引(持续链接两实体) | Dota Io Tether, Razor Link, LoL Karma W |

---

## D. Point Target — 选取地面点施放

| # | Scenario | Examples |
|---|----------|----------|
| D1 | 圆形AoE落点指示器 | Dota Chrono, LoL Veigar E, SC2 Psi Storm |
| D2 | 环形/甜甜圈区域 | LoL Diana R边缘效果, GoW符文攻击 |
| D3 | 放置墙体/地形阻挡 | LoL Anivia W, Azir R, Dota Invoker Ice Wall |
| D4 | 全图点选(小地图可用) | SC2 Scan, Dota NP Teleport |
| D5 | 约束区域放置(只允许特定区域) | SC2 Warp Gate(须水晶塔能量场) |
| D6 | 传送到指定地面点 | Dota NP Teleport, LoL TF R落点 |
| D7 | 延迟AoE(标记位置,延迟爆炸) | Dota Kunkka Torrent, LoL Zilean Q |
| D8 | 持续区域(放下后持续生效) | LoL Morgana W, Dota Keeper Illuminate |

---

## E. Direction / Skillshot — 方向/弹道射击

| # | Scenario | Examples |
|---|----------|----------|
| E1 | 直线弹道(可被阻挡) | Dota Mirana Arrow, LoL Ezreal Q, OW Ana睡眠针 |
| E2 | 直线弹道(穿透) | LoL Ezreal R, Dota Powershot, OW Reinhardt火击 |
| E3 | 锥形范围 | Dota Dragon Slave, LoL Annie W, GoW斧头横扫 |
| E4 | 矩形/线形范围 | LoL Rumble R |
| E5 | 弧线弹道(抛物线) | OW Junkrat榴弹, GoW掷斧 |
| E6 | 回旋弹道(去+回) | LoL Ahri Q, Sivir Q, GoW斧头召回 |
| E7 | 弹射/链式(命中后弹跳) | Dota Shuriken弹射, LoL Brand R |
| E8 | 矢量/拖拽(两点确定路径) | LoL Viktor E, Dota Pangolier Swashbuckle |
| E9 | 反射弹道(碰墙反弹) | 特定场景(Dota特定地形交互) |
| E10 | 贯穿后分裂/扩散 | LoL Lux R(穿透全部), Miss Fortune弹射 |

---

## F. Charge / Hold / Release — 蓄力/按住

| # | Scenario | Examples |
|---|----------|----------|
| F1 | 按住蓄力→松开射击(方向) | LoL Varus Q, OW Hanzo/Widow |
| F2 | 按住蓄力→松开爆发(自身周围) | DS R2蓄力重击, GoW蓄力重斧 |
| F3 | 按住持续发射(连射) | OW Soldier/Bastion/Tracer |
| F4 | 按住持续维持效果(盾/格挡) | DS举盾L1, GoW举盾, OW Reinhardt盾 |
| F5 | 按住瞄准+另一键施放 | GoW L2瞄准+R1/R2, OW各英雄 |
| F6 | 蓄力时可缓慢移动 | LoL Vi Q(减速), GoW部分蓄力 |
| F7 | 蓄力时完全不能移动 | LoL Xerath Q, DS部分法术 |
| F8 | 蓄力最大值自动释放 | LoL Varus Q满蓄自动射, OW Widow满充 |
| F9 | 持续按住 → 属性持续变化 | Dota Morphling敏捷/力量Shift |

---

## G. Combo / Multi-Stage — 连击/多段

| # | Scenario | Examples |
|---|----------|----------|
| G1 | 同键轻攻击连段(动画递进) | DS R1×3, GoW R1×4, Arkham□×N |
| G2 | 轻+重混合连段(不同终结) | DS R1→R2, GoW R1→R1→R2, DMC |
| G3 | 方向+攻击=不同起手式 | DS前+R2=跳劈, 后+R1=后撤 |
| G4 | 技能二段重激活 | LoL Lee Sin Q, Zed W(回影子) |
| G5 | 技能三段/多段重激活 | LoL Ahri R×3, Riven Q×3 |
| G6 | 连击时间窗口(太慢重置) | DS/GoW/Arkham所有连击 |
| G7 | 必须命中才能接下段 | 特定终结技前置命中要求 |
| G8 | 连击计数器→解锁特殊招 | Arkham连击满解锁Special Combo |
| G9 | 翻滚/闪避后接攻击=特殊招 | DS翻滚R1, GoW闪避R1, Sekiro |
| G10 | 格挡后接攻击=反击招 | DS Guard Counter, GoW盾弹R1 |
| G11 | 引导中多次分段射击 | LoL Jhin R(锥形内4发, 每发选方向) |

---

## H. Defense / Parry / Counter — 防御/弹反/反应

| # | Scenario | Examples |
|---|----------|----------|
| H1 | 持续按住格挡(减伤) | DS L1, GoW L1, OW Reinhardt盾 |
| H2 | 精准时机弹反(parry) | DS/Sekiro L1精准时机 |
| H3 | 看到提示按反击键 | Arkham反击(头上感叹号), Spider-Man蜘蛛感应 |
| H4 | 闪避+方向(无敌帧) | DS翻滚, GoW闪避, Spider-Man闪避 |
| H5 | 精准闪避=额外奖励 | GoW Realm Shift减速, Spider-Man Perfect Dodge |
| H6 | 弹反成功后开启处决窗口 | Sekiro弹反积posture→忍杀, DS弹反→致命 |
| H7 | 反射/弹开弹道 | OW Genji弹反, LoL Yasuo风墙 |
| H8 | 闪入特定攻击(定向闪避) | Sekiro Mikiri Counter(前闪进突刺) |
| H9 | 吸收/格挡转化为资源 | OW Zarya吸收变能量, DS受击回蓝 |

---

## I. Movement Abilities — 移动技能

| # | Scenario | Examples |
|---|----------|----------|
| I1 | 向指定方向冲刺 | LoL Lucian E, OW Tracer Blink, DS翻滚 |
| I2 | 冲刺到目标单位(指向性位移) | LoL Leona E, Lee Sin W |
| I3 | 瞬移到地面点 | LoL Ezreal E, Dota Blink |
| I4 | 勾爪/拉到锚点 | Arkham钩爪, Spider-Man蛛丝, Sekiro钩绳 |
| I5 | 荡绳/摆荡(持续物理) | Spider-Man R2摆荡 |
| I6 | 击退/推开目标 | LoL Lee Sin R, GoW盾撞 |
| I7 | 把目标拉过来 | OW路霸Hook, Dota Pudge Hook |
| I8 | 位置互换 | Dota VS互换, LoL Urgot R |
| I9 | 墙壁攀爬/奔跑 | OW Genji/Hanzo, Spider-Man墙面 |
| I10 | 滑翔/空中移动 | Arkham滑翔, OW Mercy滑翔 |
| I11 | 冲锋(持续直到撞目标) | OW Reinhardt冲锋, LoL Sion R |
| I12 | 击飞/升空(把目标弹起) | LoL Yasuo Q3, GoW上挑, DS上弹 |

---

## J. Toggle / Stance / Transform — 切换/姿态/变身

| # | Scenario | Examples |
|---|----------|----------|
| J1 | Toggle开/关(持续消耗) | Dota Rot, LoL Singed Q |
| J2 | 全技能组切换(变身) | LoL Elise/Jayce/Nidalee, Dota Troll |
| J3 | 临时变身(大招期间) | OW Genji龙刃, GoW Spartan Rage |
| J4 | 武器切换→连击组变 | DS换武器, GoW斧/拳切换 |
| J5 | 单手/双手持握切换 | DS Y键切换双持 |
| J6 | 弹药/元素类型切换 | GoW Atreus箭矢(光/暗) |
| J7 | 选择强化哪个技能再施放 | LoL Karma R(下个技能增强) |
| J8 | 元素组合产生不同技能 | Dota Invoker QWE→Invoke |
| J9 | 感知模式切换 | Arkham侦探视觉, Sekiro义眼道具 |

---

## K. Placement / Summon — 放置/召唤

| # | Scenario | Examples |
|---|----------|----------|
| K1 | 地面放陷阱/地雷 | LoL Teemo R, Caitlyn夹子, Jhin E |
| K2 | 地面放建筑/炮塔 | LoL Heimerdinger Q, SC2造建筑, OW Torb |
| K3 | 召唤可控制单位 | Dota Lone Druid熊, SC2产兵 |
| K4 | 召唤AI自动单位 | LoL Malzahar虫, Dota NP树人 |
| K5 | 放置双端传送门 | OW Symmetra传送门(入口+出口) |
| K6 | 放置视野/侦查装置 | Dota Observer Ward, SC2 Scan |
| K7 | 释放可操控投射物 | OW Junkrat轮胎(释放后切换视角操控) |
| K8 | 放置持续区域效果(不随人移动) | LoL Morgana W地板, Dota Macropyre |

---

## L. Mark / Detonate / Follow-up — 标记/引爆/后续

| # | Scenario | Examples |
|---|----------|----------|
| L1 | 技能A标记 → 技能B手动引爆 | LoL Zed影标引爆, Tristana E |
| L2 | 叠层满自动引爆 | LoL Vayne W三环, Brand叠满 |
| L3 | 叠层+手动引爆(可选等/触发) | LoL Tristana E(叠加或等时间到) |
| L4 | 放置标记→回溯/传送到标记 | OW Tracer Recall, LoL Zed R回影 |
| L5 | Debuff→后续技能增强 | GoW冰冻后碎裂伤害, Dota各种debuff互动 |
| L6 | 技能链(A命中后自动触发B) | Dota连招(先晕后打), LoL自动combo |

---

## M. Channel / Sustained — 引导/持续施法

| # | Scenario | Examples |
|---|----------|----------|
| M1 | 站桩引导(可被打断) | Dota CM大招, LoL Katarina R, SC2 Nuke |
| M2 | 引导中可调整方向 | LoL Lucian R(微调), Jhin R(锥形内选方向) |
| M3 | 引导中可缓慢移动 | 部分边走边引导的技能 |
| M4 | 引导TP(传送读条) | Dota TP Scroll, LoL Recall |
| M5 | 持续光束(按住维持连接) | OW Zarya/Symmetra光束 |
| M6 | 牵引/连接(距离过远断裂) | Dota Io Tether, Razor Link, LoL Karma W |
| M7 | 持续追踪(锁定目标持续生效) | OW Mercy治疗光束, Dota Life Drain |

---

## N. Context-Scored / Smart — 上下文智能施放

| # | Scenario | Examples |
|---|----------|----------|
| N1 | 自动选最优近战目标 | Arkham攻击, Spider-Man攻击, GoW攻击 |
| N2 | 距离决定技能变体 | Arkham远距离飞扑 vs 近身拳击 |
| N3 | 自身状态决定(空中/地面/墙上) | Spider-Man空中连击, DS空中下劈 |
| N4 | 目标状态决定(倒地/眩晕/持武器) | Arkham地面终结, Sekiro忍杀 |
| N5 | 环境决定(近墙/有物体/有悬崖) | Arkham环境处决, Spider-Man扔物体 |
| N6 | 连击数/仪表决定 | Arkham Special Combo, Spider-Man终结 |
| N7 | 摇杆方向偏移影响选取 | Arkham推向远处敌人+攻击=飞扑 |
| N8 | 锁定辅助(Soft Lock-on) | DS/Sekiro/GoW锁定后朝向目标 |

---

## O. Response Window — 响应窗口/反应链

> 游戏暂停或给予玩家一个时间窗口，让玩家在**对手/系统行为发生后**做出反应决策。

| # | Scenario | Examples |
|---|----------|----------|
| O1 | **陷阱/反制激活窗口** — 对手施法时弹出"是否激活反制" | 游戏王陷阱卡, Dota Linken's Sphere(自动), LoL Sivir E(手动) |
| O2 | **连锁响应(chain)** — 多方依次获得响应权 | 游戏王连锁(我放陷阱→你连锁魔法→我再连锁), Dota Lotus Orb反弹 |
| O3 | **重定向(redirect)** — 在效果结算前更改目标 | 游戏王重定向陷阱(把攻击目标改为另一个怪), Dota Scepter改变技能目标 |
| O4 | **暂停选目标** — 游戏暂停,让玩家选择受影响的目标 | 三国志/信长野望(暂停→选将→施计), XCOM(中断射击选目标) |
| O5 | **Hook/取消** — 在效果生效前取消/无效化 | 游戏王无效陷阱, Dota Counter Helix(被动触发可被Linken), LoL Banshee |
| O6 | **Modify/修改** — 在效果结算时修改数值 | Dota Bristleback减伤(被动修改伤害), LoL护盾减免 |
| O7 | **Chain/追加** — 在效果结算后追加新效果 | 游戏王连锁追加, Dota Templar Refraction(受击消耗层数) |
| O8 | **超时自动通过** — 响应窗口有时限,超时=Pass | 所有卡牌游戏定时器, RTS暂停限制 |

---

## P. Insertable Context Window — 插入式上下文填充

> 技能执行到一半时，**系统暂停并请求玩家提供额外信息**（目标、方向、参数等），填充后继续执行。

| # | Scenario | Examples |
|---|----------|----------|
| P1 | **选择额外目标** — 技能命中后,选择分摊/传导给谁 | 游戏王选效果目标, LoL Kalista R(拉回友方→选方向投出) |
| P2 | **选择效果变体** — 弹出选项让玩家选不同后续 | Dota Invoker(Invoke后选技能), LoL Karma R(选Q/W/E增强) |
| P3 | **拖拽确定方向/落点** — 技能第一阶段命中后,拖拽确定第二阶段 | LoL Kalista R(投出方向), LoL Zoe R(选目标点) |
| P4 | **填写数值参数** — 选择分配的数量/比例 | 卡牌游戏分配伤害, TRPG选择分配点数 |
| P5 | **确认/取消** — 高代价技能弹出确认窗 | SC2 Nuke确认, MOBA大招确认(某些游戏) |
| P6 | **多次选取(Selection Gate)** — 技能要求选多个目标 | SC2 Archon合体(选2单位), RTS框选多单位后下命令 |
| P7 | **条件分支选择** — 系统根据上下文给出可用选项,玩家选一个 | RPG对话选择影响技能走向, 策略游戏外交选项 |

---

## Q. Finisher / Execution — 终结/处决

| # | Scenario | Examples |
|---|----------|----------|
| Q1 | 血量阈值处决 | LoL Pyke R/Urgot R血线处决 |
| Q2 | 架势值/posture破满处决 | Sekiro忍杀, GoW Stun Grab |
| Q3 | 背后位置处决 | DS背刺, Sekiro潜行忍杀 |
| Q4 | 居高临下处决 | DS跳劈, Arkham倒挂击倒 |
| Q5 | 弹反后处决 | DS弹反→致命, Sekiro弹反→忍杀 |
| Q6 | 大招仪表蓄满施放 | OW Ultimate, Spider-Man L3+R3 |
| Q7 | 连击数够高施放 | Arkham Special Combo Takedown |
| Q8 | QTE式处决(连续按键) | GoW QTE终结, Arkham连打 |

---

## R. Companion / Multi-Unit — 同伴/多单位指挥

| # | Scenario | Examples |
|---|----------|----------|
| R1 | 指挥同伴攻击指定目标 | GoW按□射箭, LoL Annie控Tibbers |
| R2 | 指挥同伴使用特定技能 | GoW Atreus切换箭矢类型 |
| R3 | 微操多单位(选中+指令) | SC2多单位, Dota Meepo/Chen |
| R4 | 装载/卸载(运输) | SC2 Medivac装载 |
| R5 | 合并/变形(多单位融合) | SC2 Archon合体 |
| R6 | 设置集结点 | SC2 Rally Point |

---

## S. Special Input — 特殊输入方式

| # | Scenario | Examples |
|---|----------|----------|
| S1 | 组合键同时按(L1+R1) | GoW符文攻击, Spider-Man终结技 |
| S2 | 双击某键=特殊招 | GoW双击L1=盾击 |
| S3 | 方向+按键同时=不同招 | DS前+R2=跳劈, 后+R1=后撤斩 |
| S4 | 自我施放修饰(Alt+技能) | Dota/LoL Alt+技能强制对自身 |
| S5 | 快捷施放(跳过确认) | LoL/Dota Quick Cast设置 |
| S6 | 小地图点击施放 | SC2 Scan, Dota全图技能 |
| S7 | 排队指令(Shift+点击) | SC2/Dota Shift-Queue |

---

## T. Resource / Gating — 资源/门控（玩家可感知）

| # | Scenario | Examples |
|---|----------|----------|
| T1 | 法力/蓝量消耗 | LoL/Dota蓝量 |
| T2 | 冷却时间 | 几乎所有游戏 |
| T3 | 充能次数(用完等恢复) | LoL Akali R充能, OW Tracer Blink×3 |
| T4 | 怒气/专属资源条 | GoW Spartan Rage, LoL怒气英雄 |
| T5 | 以血换技能 | Dota Huskar, LoL Mundo |
| T6 | 弹药数(用完需换弹) | OW各武器弹药 |
| T7 | 连击不中断才能维持 | Arkham连击条(被打中断归零) |
| T8 | 消耗品(一次性使用) | DS Estus, 所有游戏药水/炸弹 |

---

## U. Environmental Interaction — 环境交互

| # | Scenario | Examples |
|---|----------|----------|
| U1 | 拾取并投掷场景物体 | Spider-Man拿井盖扔, GoW拿石头 |
| U2 | 把敌人撞到墙/推下悬崖 | DS踢下悬崖, LoL Poppy撞墙 |
| U3 | 利用可破坏物(砍树/破墙) | Dota砍树获得视野, GoW破坏遮挡 |
| U4 | 墙壁/特殊表面战斗 | Spider-Man墙面战斗 |
| U5 | 水中/特殊地形战斗(连击组变化) | DS水中战斗限制, Sekiro水下 |
| U6 | 利用环境机关 | Arkham环境处决, GoW环境陷阱 |
| U7 | 地形创造/改变 | LoL Anivia墙, Dota Ice Wall |

---

## 总计

| Category | Count |
|----------|-------|
| A. 被动/自动 | 8 |
| B. 无目标瞬发 | 9 |
| C. 指向单位 | 9 |
| D. 指向地面 | 8 |
| E. 方向/弹道 | 10 |
| F. 蓄力/按住 | 9 |
| G. 连击/多段 | 11 |
| H. 防御/弹反 | 9 |
| I. 移动技能 | 12 |
| J. 切换/变身 | 9 |
| K. 放置/召唤 | 8 |
| L. 标记/引爆 | 6 |
| M. 引导/持续 | 7 |
| N. 上下文智能 | 8 |
| O. **响应窗口** | 8 |
| P. **插入式上下文** | 7 |
| Q. 终结/处决 | 8 |
| R. 同伴/多单位 | 6 |
| S. 特殊输入 | 7 |
| T. 资源/门控 | 8 |
| U. 环境交互 | 7 |
| **Total** | **163** |

---

## 如何使用本清单

1. **需求对照**: 设计师描述需求时, 在清单中定位对应编号
2. **架构验证**: 每个编号都能用 `InteractionConfig + TargetMode + Acquisition + Tag/Effect/Attribute` 表达 → 架构完备
3. **避免过度工程**: 若某编号在现有架构内可表达, 不新增枚举/接口
4. **回归测试**: 新功能上线后, 用清单对应编号创建验收 case
