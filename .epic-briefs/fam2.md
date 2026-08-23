# 家族方案 ② 关系与好感（17 op）

> 场景基准 `_fields/rel.json`：self→好友1..4 好感 85/62/48/35（好友1、2 带 Trusted 旗）；好友2→self 70、好友3→self 55。现状缺口：连线无方向（DrawAggroLine 纯红线）、头顶无名字、血条是好感代理但无"好感"标注、旗无图标。

### RelationshipQueryMutual｜B(P1+P8)｜M（首落方向箭头）
- 零字幕画面：开场全部 6 条链画灰色细线底图；结算后好友2、好友3 的链变**两端箭头的亮黄粗线**（=互相都认），好友1、好友4 保持灰色单向箭头。
- 改动：presenter 新增 `DrawDirectedLine`（箭头=终点处两条 45° 短翼）；driver 先画灰底再叠亮线。文案：title「互相都认的朋友」beat「两头都有箭头的链才亮。」detail「互相都认的有 {friendCount} 人：{friend}。」

### RelationshipFilterFlag｜B(P2+P8)｜S
- 零字幕画面：好友1、好友2 的链中点各插**黄色三角信任旗**，带旗两条链转亮绿；好友3、4 无旗保持灰。
- 改动：driver 用 `Relationships.HasFlag` 真实读旗画图标。文案：title「只看挂了信任旗的」beat「链上插着信任旗的才留下。」detail「挂着信任旗的有 {friendCount} 人。」

### RelationshipAggAverageMetric｜B(P4+P11)｜M（算式台首建）
- 零字幕画面：右侧立**算式台**——4 格数值牌 85/62/48/35 依次从各链中点飞入，中间"÷4"挡板，结果槽落下 57（真值 230/4 截断，数值全来自 GetMetric 真值）。
- 文案：title「好感平均多少」beat「四份好感倒进算式台，除以四人。」detail「四人好感平均 {avg}（230÷4 截断）。」

### RelationshipFilterMetricRange｜B(P3+P11 微调)｜S
- 零字幕画面：保留 3 亮 1 暗；增一道**量尺门**（两根白柱+80/30 两道黄刻度线），四人头顶记事板显示 85/62/48/35——好友1(85) 高过上门槛被挡、好友4(35) 踩在下门槛之上。
- 文案：title「好感落在区间里的人」beat「好感量尺卡在 30 到 80 之间才留。」detail「好感 30~80 的有 {friendCount} 人。」

### RelationshipQueryOutgoing｜B(P1+P8)｜S
- 零字幕画面：全 6 链灰底；self→四友 4 条亮黄且**箭头在朋友端**（从自己射出）；两条反向链保持灰、箭头朝自己。
- 文案：title「我主动交的朋友」beat「箭头从自己射出去的链才算。」detail「我交的朋友有 {friendCount} 人。」

### RelationshipHasLink｜C(演后果对照)｜L（或退化 B 档 S）
- 零字幕画面：加第 6 名"路人"（无链）。左半场：自己↔好友1 **链扣连线**（串联圆环，绿）=有链的世界内形象；右半场：自己↔路人**断口虚线**（两截灰线+断口问号框）。
- 改动：graph 加第二探测 LoadExplicitTarget(路人)→HasLink(非 featured)→JumpIfFalse 演出"没连"分支；HasLink 断言只针对 featured（好友1 true），路人 false 由分支自证。文案：title「我们有没有连着」beat「和好友链环扣紧，和路人线断在半路。」detail「和{friend}链着；和路人没连。」（退化方案：只演真分支+链扣实体+删"无链"承诺，S。）

### RelationshipSetFlag｜B(P2)｜S
- 零字幕画面：结算后最弱链（好友4）中点插**红色三角失和旗**，链转暗红——红旗 vs FilterFlag 的黄旗构成家族旗语体系。
- 改动：driver 真实读 Estranged 旗画旗。文案：title「打上失和标记」beat「最弱那条链上插起失和旗。」detail「给{friend}的链插上失和旗。」

### RelationshipAggSumMetric｜B(P4)｜S
- 零字幕画面：复用算式台，运算符换"+"：四块数值牌串联滑入，结果槽落 230，四链保持亮——观众可手动验算。
- 文案：title「把好感加总」beat「四份好感在算式台上连加。」detail「好感总和 {sum}。」

### RelationshipRemoveLink｜A｜S
- 零字幕画面：保留 4→3 线+好友4 变灰+条掉的标杆行为；断链原位补 ghost 残影（断成两截的断口），好友4 名牌同步变暗。
- 文案：title「把最弱的那条链拆掉」beat「好感最低那条链断开，线少一条。」detail「拆掉{friend}的链，剩 {linksAfter} 条。」

### RelationshipSortByMetric｜B(P6+P11)｜M（角标首落）
- 零字幕画面：四人头顶各挂**名次角标 1/2/3/4**（白框黑字/刻痕）；四链亮度按名次阶梯递减（85 最亮→35 最暗）或线宽递减。
- 改动：按 `ctx.HitTargets` 真实降序取 rank。文案：title「按好感排个序」beat「按好感高低挂出名次牌。」detail「排第一是{friend}，好感 {loyalty}。」

### RelationshipAggMinMetric｜B(P4+P6+P11)｜S
- 零字幕画面：算式台 min 模式——四块牌中**最矮的 35 被托举到结果槽**（其余沉入台面变暗）；好友4 头顶"▼35"角标、其链最亮。
- 文案：title「最低好感是多少」beat「四块数值牌里最矮的浮出来。」detail「最低好感 {min}，是{friend}。」

### RelationshipAggMaxMetric｜B(P4+P6+P11)｜S
- 零字幕画面：镜像——最高的 85 浮到结果槽；好友1 挂"▲85"、其链最亮。
- 文案：title「最高好感是多少」beat「四块数值牌里最高的浮出来。」detail「最高好感 {max}，是{friend}。」

### RelationshipGetMetric｜B(P3+P11)｜S
- 零字幕画面：从自己→好友1 链中点**弹出读数牌「85」**沿链滑向自己；手边记事板翻到"好友1：85"页。
- 文案：title「读出这个人的好感」beat「从链上抽出读数牌，写着 85。」detail「读到{friend}的好感 {loyalty}。」

### RelationshipQueryIncoming｜B(P1+P8)｜S
- 零字幕画面：全 6 链灰底；好友2→self、好友3→self 两条亮黄且**箭头画在自己端**；4 条 outgoing 保持灰、箭头朝朋友端。重录 poster。
- 文案：title「谁把我当朋友」beat「箭头指着自己的链亮。」detail「把我当朋友的有 {friendCount} 人。」

### RelationshipAggMinEntityByMetric｜B(P6+P11)｜S
- 零字幕画面：好友4 头顶名牌「好友4」+「▼35」角标浮标，唯其链亮，其余三人连人带线压暗——被选中者=被照亮者。
- 文案：title「谁是好感最低的人」beat「数值牌最矮的人被照亮。」detail「好感最低的人是{friend}。」

### RelationshipAggMaxEntityByMetric｜B(P6+P11)｜S
- 零字幕画面：镜像——好友1 名牌+「▲85」，唯其亮。
- 文案：title「谁是好感最高的人」beat「数值牌最高的人被照亮。」detail「好感最高的人是{friend}。」

### RelationshipQueryBetweenPair｜B(P1+P2)｜S～M
- 零字幕画面：先演示选人（好友1 短暂高亮+▲角标），随后自己↔好友1 拉出**双头粗亮线+中点链扣徽章**，旁浮"×1"；其他灰链留底图。重录 poster。
- 文案：title「这两人之间有没有链」beat「这一对之间拉出一条双头链。」detail「自己和{friend}之间查到 {friendCount} 条链。」

## 家族小结
- 基建（一次实现 17 op 受益）：① `DrawDirectedLine`（S）三模式覆盖出/入/双头；② **世界内文字通道**（M，最关键）——DebugDraw 无文字原语，名牌/数值牌/角标/算式台全依赖，落点为画廊 PresentationSystem 的世界坐标→屏幕投影 + `DrawWorldLabel`；③ 算式台组件（M）四聚合共用；④ 旗语体系（S）黄=Trusted、红=Estranged，一律真实读旗；⑤ 角标+浮标（S）；⑥ 全貌灰底（S）——query/filter 的 N→M 基础。
- 场景：数值与旗种子已齐基本不动；仅 HasLink 加 stranger；记事板/浮标一律读 `Relationships.GetMetric` 真值而非 Health 代理；rel.json 的 links 方向数据即灰底图数据源。
- 合并：四聚合共享算式台；MinEnt/MaxEnt 与 Min/Max 共享呈现路径；四方向类共用箭头三模式；HasLink 与 BetweenPair 共用链扣语言。
- 链路修复：仅 HasLink（false 分支从"即抛"改 JumpIfFalse 演出）；其余 16 op 已真实结算。
- 统计：S×13、M×3、L×1；前置基建 文字通道 M + DrawDirectedLine S + 算式台 M（建议最先做文字通道——13 个 S 档的公共依赖）。

