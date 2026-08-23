# 家族方案 ⑦ 组合短剧（13 op）

> 核实：`SandboxNodeDriver.DrawOverlay` 以 `ProgramHasQueryRadius` 早退（SandboxNodeDriver.cs:80）——EnsureLink/SetMetric/HasFlag 画面全空的根因；`Effect.GraphOpsSandbox.Mark/Buff` 均无 modifier、`Effect.GraphOps.Strike`（真实 -18）闲置；SortStableDedup=实体身份排序+去重（SpatialQueryPostProcessor.cs:31），无距离语义。

### RelationshipEnsureLink｜B(P1+P2)｜S
- 零字幕画面：施法者与盟友之间第一拍**灰色虚线**（还没接上），下一拍**咔哒变青色实线**+中点扣白色环扣徽章；盟友血条由隐藏变可见。
- 改动：复用 RelNodeDriver 连线写法改青色。文案：title「把两人连成一条关系链」beat「灰色虚线先比划一下，然后咔哒扣成青色实线。」detail「施法者和盟友之间扣上了关系链，环扣亮起。」

### QueryLimit｜B(P6+P8)｜M ⚠️语义级修复
- 现状：title"只点最近的几个"与实现相反（SortStable 按实体身份号排序，取"名单前三个"）；同距的近处丁没亮；"圈里很多人"实际 6 个。
- 决策：**文案改真**（保留现有 op 组合）——现有词汇表没有按距离排序的算子，造 QuerySortByDistance 违反"走现有基建"。
- 零字幕画面：8m 黄圈；圈内 5 兵（已剔除施法者）按最终名单顺序压 pips 名次角标，前三个亮黄+黄圈+血条可见；第 4、5 名角标转灰、圈与血条熄灭；圈外两人全灰——"名单排队、取前三"。
- 链路：图加 `QueryFilterNotEntity` 前缀（radius→notSelf→sort→limit），pips 只标人；站位沿弧形按 id 排布顺弧可读。文案：title「名单取前三个」beat「圈里五个人各有一个编号，亮着的是编号最靠前的三个。」detail「按编号点名，留下前三个。」

### FanOutApplyEffect｜B(P9+P10)｜S
- 现状："血条显示挂上了"实为 HUD 可见性谎言（Mark 无 modifier 血条全程 100/100）。
- 零字幕画面：圈内 5 人血条**同拍 100→82 真实掉血**（-18），头顶白色「82/100」世界数字闪现；圈外 2 人满血隐藏。
- 链路：graph effectTemplate→`Effect.GraphOps.Strike`；血量回写链路已存在零改动。文案：title「圈里每人挨一记」beat「黄圈内五个人同时掉一截血，圈外两个没事。」detail「圈内{applied}人每人挨了一记，血条都掉了一截；圈外两人完好。」

### QuerySortStable｜B(P6+P7)｜M ⚠️语义级修复
- 现状：beat"同样距离时顺序不乱跳"编造距离语义；字幕顺序按 vignette 定义序拼（非真实排序序）；名单含施法者。
- 零字幕画面：圈内 5 兵按真实 TargetList 顺序头顶亮黄 pips 1→5；每波执行后**上一波角标以灰色 ghost 残影偏移半格保留一拍**——灰影与新角标逐个重合，即"顺序稳了"；施法者站圈心无编号。
- 链路：图加 QueryFilterNotEntity 前缀；`JoinHitNames` 改按 HitTargets 真实顺序拼名（修掉定义序谎言）；去重语义不进 beat（本场景无重复输入，禁描述画面外事件），加强版可后续用 collections 预置重复成员演示。文案：title「点名名单按编号排好，次次一样」beat「每波点名，五个人 1 到 5 的编号顺序一模一样，灰影对得上。」detail「点名顺序稳定：{order}。」

### RelationshipAddMetric｜B(P3+P4+P10)｜M（记事板首建）
- 零字幕画面：青色关系链（沿用 EnsureLink 原语）；盟友身侧立**白色记事板**——好感条先有灰色 40% 一段，执行拍新长出亮绿 30%、指针停在 70%；板角 4 格方块示意 40+30。血条不动——好感不是血量，信息全在记事板。
- 文案：title「好感再加一截」beat「记事板上好感条原本四成，新亮的一截补到七成。」detail「好感从 40 加 30，记事板停在{loyalty}。」

### RelationshipSetMetric｜B(P3+P10)｜S
- 零字幕画面：同板同链——好感条先显示灰色 40% 旧值残影，执行拍整条被亮绿 80% 新条**整个换掉**；与 AddMetric"补一段"形成视觉对偶：这里是"整条换长"。
- wiki 旧文案"盟友条到八成"随重生成淘汰。文案：title「好感直接写成指定值」beat「灰色的旧条被一条更长的绿条整个换掉。」detail「好感不看原来多少，直接写成{loyalty}。」

### LookupTagDisplayToken｜B(P2+P5)｜S
- 零字幕画面：侦察兵头顶点亮**橙红火苗徽章**（身上的状态）；施法者手边白色面板牌执行拍亮起**同色同形火苗图形**——"身上的状态→面板上的名"用同形同色对照表达。
- 链路已真实（种 Burning→查表→「灼烧」→未映射 fail-close）。文案：title「身上的状态翻成面板上的名」beat「侦察兵头上的火苗，原样出现在施法者的面板牌上。」detail「身上的灼烧状态翻成面板名「{token}」。」

### ApplyEffectDynamic｜B(P3+P9)｜S
- 现状：title 与 detail 原文复读、"点名目标/模板号"黑话；挂的 Buff 无 modifier 画面与普通打击无区别。
- 零字幕画面：施法者手边**抽屉牌面板**——第一拍翻出并亮起一张黄牌（这次拿到哪个效果），第二拍木桩血条 100→82 真实下坠。与静态版的差异=翻牌前拍。
- 链路：`assets/GAS/sandbox/catalog.json` 的 `buffEffect`→`Effect.GraphOps.Strike`（黑板种真实模板 id，与 FanOutApplyEffectDynamic 共用此 key）。文案：title「先翻出一张效果牌，再照着打」beat「施法者从抽屉翻出一张牌，木桩照着牌掉了一截血。」detail「黑板里读到的这张牌是打击，照牌把木桩打掉一截血。」

### FanOutApplyEffectDynamic｜B(P3+P9+P8)｜S
- 现状：beat 仅"动态模板扇出。"五字空句+黑话；与静态版不可区分。
- 零字幕画面：同布景——第一拍抽屉牌翻亮，第二拍圈内 5 人同拍掉血、圈外 2 人满血隐藏；与静态版背靠背对比即懂：差别只在"牌是当场翻出来的"。
- 文案：title「翻出一张牌，圈里每人照牌挨一下」beat「先翻牌再动手：圈内五个人同时掉一截血，圈外没事。」detail「翻到的牌是打击，圈内{applied}人每人挨一下。」

### QueryRadius｜B(P8)｜S
- 现状：count=6 含施法者（AABB 查询不排除自己）："摸到6个近处的人"误导；caster 恒亮污染。
- 零字幕画面：黄圈内 5 兵亮黄圈+血条可见；施法者站圈心只有自己的方块造型、脚下无命中圈（作为观察者）；圈外两人灰暗——直接数出"圈里 5 个"。
- 链路（图改）：加 `QueryFilterNotEntity`（caster→radius→notSelf），count 6→5；Query 族换 LightHitsOnly 绑定。文案：title「站圈心喊一嗓子，看看圈里有谁」beat「黄圈内五个兵亮起来，施法者自己不算，圈外两人没反应。」detail「圈内亮起{count}个兵，不含施法者；圈外两人不亮。」

### SelectTagInMask｜B(P2+P5)｜S
- 零字幕画面：侦察兵头顶火苗徽章点亮（同一枚）；施法者牌桌上一张火苗形**暗牌（灰轮廓）执行拍翻亮为橙红**——同形同色三点连线（兵→徽章→亮牌）。RequireOne 策略下不虚构"多选一"过程，就是一张牌从暗翻亮。
- 文案：title「桌上翻出当前生效的那张状态牌」beat「侦察兵头上的火苗亮起，桌上那张火苗牌跟着翻开。」detail「当前生效的状态牌是{card}。」

### HasTag｜B(P2+P8)｜S
- 现状：查的是 State.Sandbox.Marked，title"敌人标记"名不副实；只演"有"。
- 零字幕画面：并排两个侦察兵——甲头顶**白菱形标记徽章**，执行拍徽章白闪+脚下绿圈亮=「有」；乙头顶只有灰色检查框（查过了）、无徽章、圈不亮=「无」。真假分支同帧对照。
- 链路：乙由 driver 用同一 `Api.HasTag` 真实跑一次并断言 false（灰圈即真实检查结果，非道具）。文案：title「查一查身上有没有那枚标记」beat「带标记的侦察兵亮绿圈，没标记的那个查完没反应。」detail「带标记的查为「{result}」，没标记的查为「无」。」

### RelationshipHasFlag｜B(P1+P2+P8)｜M
- 零字幕画面：青色关系链上插**小绿旗**（Trusted 真实种入）；执行拍绿旗白边闪两下+盟友脚下绿圈亮=旗开着；右下路人（无链）之间只有灰虚线（driver 按 `HasLink==false` 真实判定后画），执行拍无反应。
- 改动：加路人演员（4,-4）。文案：title「这条关系上插没插信任旗」beat「青色链上插着绿旗，旗子闪两下；没链的那位啥也没有。」detail「信任旗插着，检查结果：{result}。」

## 家族小结
- 前置基建 M：① SandboxNodeDriver overlay 分发（早退改为按 op 族分发：Relationship 族画链+挂件、Tag 族画徽章+牌、Query 族加 pips；每个状态从真实运行时读）；② `LightHitsOnly` 绑定（不无条件亮 caster）。
- 真实结算清单：catalog `buffEffect→Effect.GraphOps.Strike`（驱动 ApplyEffectDynamic 两 op）；`graphs/FanOutApplyEffect.json`→Strike；无 modifier 的 Sandbox.Mark/Buff 保留给 attr 家族不在本家族引用。
- 合并：QueryRadius/QueryLimit/QuerySortStable 统一为同一条 radius→FilterNotEntity 前缀链（三图只差尾部）；静态/动态 FanOut 同布景背靠背。
- 统计：S×9、M×4、L×0；基建落地后 13 op 平均 S。

