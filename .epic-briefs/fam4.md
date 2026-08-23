# 家族方案 ④ 属性与效果（15 op）

> 核实补充：CompareLtInt 与 SelectEntity 的图**没有任何攻击节点**（"打全力/改打木桩"比审查时更空）；画廊 tick 流程 RestoreVignetteHealth→Clear 队列→执行→SyncActorHealthFromWorld——ApplyEffect 入队的请求被下一拍 Clear() 吞掉永不结算。
> 家族线语义分工（统一视觉语法）：**红实线=攻击、黄虚线=读值、青自环=读自己、金竖线=写入自己、白弧=卸状态、粗白闪=重击**。

### LoadContextTarget｜A｜S
- 零字幕画面：开场木桩脚下亮**青色单据环**（这一击自带的单据），取数瞬间环收缩进红线、一击落下 100→88。
- 文案：title「这一击的单据上写着打谁」beat「从这一击自带单据里取出目标，取到木桩，扣 12 血。」detail「单据取出的目标是木桩，木桩从 {healthBefore} 掉到 {healthAfter}。」

### LoadAttribute｜A｜S
- 零字幕画面：施法者→木桩改画**黄色虚线侦查线**（≠攻击红线），木桩头顶浮出放大读数「80」、血条数字闪白一次，无人掉血——先看了一眼，没打。
- 文案：title「出手前先看一眼对方的血」beat「黄虚线搭到木桩，读出当前生命，头顶浮出 80。」detail「读出木桩当前生命 {hp}。」

### ConstInt｜C(图解页/铭牌)｜M
- 零字幕画面：中央**铸数铭牌**——一格亮黄插槽铸着「3」+锁形刻印（写死=不可变）；取数后木桩头顶亮 3 枚空心层叠圈等待接收。
- 文案：title「写死的整数：一刀三层」beat「铭牌铸死数字 3，带锁印；取数后木桩头顶亮三层空圈。」detail「这一刀的层数写死是 {layers}。」

### CompareEqInt｜B(P9+P2+P4)｜L
- 现状："叠满就爆"空头支票，画面无爆炸。
- 零字幕画面：木桩头顶 3 枚火苗层徽章；**对撞天平**左盘「3 层」右盘「满层 3」，对齐瞬间粗白闪真实一击 100→82（Effect.GraphOps.Strike），火苗随爆消散。
- 链路修复：图改 ConstInt(3)→ConstInt(3)→CompareEqInt→JumpIfFalse(收手)／真分支 LoadExplicitTarget→ApplyEffectTemplate(Strike)；驱动结算（家族基建）；断言补 targetAfter。文案：title「层数叠满就引爆」beat「三层火苗对满层 3，天平对齐，爆出一击扣 18 血。」detail「层数对满层，叠满引爆，木桩从 {healthBefore} 掉到 {healthAfter}。」

### CompareEqEntity｜B(P7+P8+P9)｜L
- 现状：叙事用节点输出的"非"，画面无对比。
- 零字幕画面：先播**假分支**——施法者位叠半透明**自我 ghost** 充当点名目标，**证同章徽章**盖住本体与残影两个身份框，红线抬起又收；再播真分支——章裂开，红线打木桩真实 -18。
- 链路修复：图尾接 LoadExplicitTarget→ApplyEffectTemplate(Strike)。文案：title「先对脸：打的是不是自己」beat「残影演示点名自己→同一个人，收手；点名木桩→不是同一人，一刀扣 18。」detail「木桩不是施法者本人，这一刀打了出去，木桩从 {healthBefore} 掉到 {healthAfter}。」

### RemoveEffectTemplate｜B(P2+P9)｜M（依赖效果可见化基建）
- 零字幕画面：开场木桩头顶挂**紫色菱形标记徽章+180 tick 倒计时环**；白弧净化扫过，徽章碎裂三片淡出，血条纹丝不动。
- 改动：链路已真；徽章消费 `ActiveEffectContainer` 真实活跃效果（生产状态直接投影），碎裂与 CancelRequested 同步，与 ApplyEffectTemplate 共用基建。文案：title「把身上的状态摘掉」beat「木桩头顶紫色标记先挂着，白弧扫过，标记碎掉消失，血条不动。」detail「木桩身上的标记被卸掉了，血量保持 {healthAfter}。」

### SelectEntity｜B(P7+P8+P9)｜L
- 现状：条件 1==1 恒真、输出无消费者、只演真分支。
- 零字幕画面：**岔路牌**两道门旗——假分支幕：红线折向自我 ghost，残影吃真实 -18 随即消散；真分支幕：红线打木桩 100→82。
- 链路修复：条件改真实比较 LoadCaster vs LoadExplicitTarget→CompareEqEntity；SelectEntity 输出接 ApplyEffectTemplate(Strike)；双分支各自断言。文案：title「岔路口选人打」beat「残影幕条件不成立→挑了自己挨打；正幕条件成立→挑了木桩，扣 18。」detail「这一刀挑中木桩，木桩从 {healthBefore} 掉到 {healthAfter}。」

### ModifyAttributeAdd｜A｜S
- 零字幕画面：全片最扎实的真实结算只补过程表现——命中瞬间头顶浮红色伤害浮标「-25」上飘，血条被扣的 25 格留 0.5s 白色残影格再消失。
- 文案：title「直接在血条上做加法」beat「一刀 -25 写进血条，木桩 100 掉到 75，头顶浮出 -25。」detail「加算 {delta}，木桩从 {healthBefore} 掉到 {healthAfter}。」

### LoadSelfAttribute｜B(P1+P11)｜S
- 现状：双方都 100，"读自己"无法验证。
- 零字幕画面：caster 预设 62/100（与满血木桩拉开）；**青色自查回环线**（起点终点都在自己），回环点亮时头顶浮出「62」——线不指向任何人。
- 文案：title「看自己还剩多少血」beat「自查线绕回施法者自己，头顶浮出 62；木桩满血没人碰。」detail「读自己的生命，还剩 {hp} 点。」

### ApplyEffectTemplate｜B(P9+P2)｜L（重设计核心，与 Remove 共摊基建）
- 现状：真实入队但被 Clear 吞掉；模板纯 Buff 无 modifier，"可见状态"画面无物。
- 零字幕画面：红线末端**贴附**不扣血——木桩头顶**钉上紫色菱形标记徽章+脚下淡紫光环**，徽章带倒计时环、落位时从大到小"钉上"；血条不动。读法：这一击不是伤害，是挂状态；状态可见、有期限。
- 链路修复（三步共用基建）：① 画廊 runner 把 EffectRequests.Clear() 改为驱动一次真实结算 tick；② 给 `Effect.GraphOpsAttr.Mark` 补真实 modifier 或新增 `Effect.GraphOpsAttr.WoundMark`；③ 徽章消费 ActiveEffectContainer。断言从"入队>0"升级为"结算后 target 存在活跃效果"。文案：title「给木桩挂上看得见的状态」beat「红线贴附不扣血：木桩头顶钉上紫色标记，带倒计时环，血条不动。」detail「木桩被挂上标记，状态可见，血量 {healthAfter}。」

### WriteSelfAttribute｜A｜S
- 零字幕画面：**金色写入竖线**从施法者头顶落下；头顶浮出绿色「**=90**」（等号强调写成定值）；血条 60→90，涨上的 30 格高亮金色与旧 60 格区分。
- 文案（去"金块血条"黑话、"回一口"歧义）：title「把血直接写成 90」beat「施法者血 60，一道写入线落下，血条直接抬到 90，头顶浮出 =90。」detail「直接写入生命值，从 {casterBefore} 写成 {casterAfter}。」

### CompareLtInt｜B(P9+P4+P10)｜M
- 现状：图只有比较没有攻击，"全力"空头。
- 零字幕画面：木桩 50/100 血条上横**阈值标尺**（80 红刻线）；判定瞬间刻线闪红、蓄力光环亮起→粗白闪重击线真实 -18：50→32。
- 链路修复：图改 LoadExplicitTarget→LoadAttribute(Health)→ConstInt(80)→CompareLtInt→JumpIfFalse(轻击 -6)／真分支(-18)→ModifyAttributeAdd——**比较输入来自生产管线读血，非写死 50**；断言 targetAfter=32。文案：title「血量过线没：过线轻击，没过线全力」beat「木桩 50 血低于 80 刻线，标尺闪红，全力一击扣 18，掉到 32。」detail「木桩 {healthBefore} 低于 80，打{style}，掉到 {healthAfter}。」

### LoadCaster｜B(P2+P1)｜S
- 零字幕画面：两演员先同时灰暗，**白色身份光柱**打在施法者头顶亮出**金色出手人徽章**（印章样式）；徽章亮起后攻击红线才从施法者端点亮。
- 文案：title「先认出是谁出手」beat「白光柱落在施法者头顶，亮出出手人徽章，攻击线才从这亮起。」detail「出手的确认是自己。」

### AddInt｜C(图解页/算式台)｜M
- 零字幕画面：**算式台**铸「2 + 1」+翻牌计数槽；施法者连挥两刀，每次一枚铜色计数币飞进台面，翻牌翻出「3」；木桩头顶亮 3 枚连击火花徽章。
- 链路修复（推荐）：AddInt→ApplyEffectTemplate 带 EffectArgs(level=3) 真实落地层数；最小改法徽章数取 result。文案：title「连击数加一」beat「两刀打进算式台 2+1，翻牌翻出 3，木桩头顶亮三枚连击火花。」detail「连击 2 加 1，算出 {combo}。」

### LoadExplicitTarget｜A｜S
- 零字幕画面：**红色准星括号**从施法者飞出锁扣木桩（收拢+锁定闪）；随后红线沿准星打出 100→85，浮「-15」——先指名后出刀。与 LoadContextTarget（单据环）构成"两种找目标"的差异。
- 文案（统一命名删"红块"）：title「点名谁就打谁」beat「红色准星飞出锁扣木桩，一刀沿线打下，木桩 100 掉到 85。」detail「点名木桩，木桩从 {healthBefore} 掉到 {healthAfter}。」

## 家族小结
- **效果可见化基建**（Apply/Remove 共同前置，也是 Compare 三连/SelectEntity 的间接前置）：(a) runner 停 Clear、驱动真实结算；(b) Mark 补 modifier 或新增 WoundMark（effects.json 注册）；(c) presenter 消费 ActiveEffectContainer 画徽章/倒计时环/碎裂。
- 落地顺序：基建→Apply/Remove→Compare 三连（复用 Strike 结算驱动）→文案微调。ModifyAttributeAdd 系五个 op（LoadContextTarget/LoadExplicitTarget/ModifyAttributeAdd/WriteSelfAttribute/LoadSelfAttribute）**不依赖基建可并行先做**。
- 可合并：`DrawStatusBadge` 一个渲染器服务 6+ op；双分支残影演出 CompareEqEntity/SelectEntity 共用；DrawFloatingValue 参数化一个实现；"找目标三 op"=DrawOverlay 一个 switch。
- 统计：S×7、M×4、L×4（基建先行后 Apply/Remove 降为 S/M，净约 8S+5M+3L+基建 L×1）。

