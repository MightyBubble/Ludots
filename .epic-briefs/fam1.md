# 家族方案 ① 事件与吸附（15 op）

> 隶属零字幕重设计 Epic。每条方案：档位｜工作量；现状 → 零字幕画面 → 改动（场景/原语/链路/新文案）。
> 家族基建落点：`EventNodeDriver.cs`（BuildPayload 硬编码注入、SeedTargetPos、SeedOwnershipAndKnowledge 压平 Owns 链、PrefillFanOut 全演员）、`GraphShowcaseStagePresenter.cs`（无箭头/徽章/ghost）。

### LoadViewer｜B(P2+P3)｜M
- 现状：字幕硬编码"自己这侧"与取到的实体无关，断言只查非空；画面无"观众"锚点。
- 零字幕画面：观众头顶落下**眼睛徽章**（外圈白环+中心点），徽章位置取自 featured 节点真实返回的实体；右侧立**镜位寄存板**，一枚与观众同色的芯片从观众头顶沿虚线飞入 1 号槽。
- 改动：断言升级 `result.EntityValue == viewer 实体`，徽章位置绑定 result（禁止硬编码坐标）；删本 op 的误导仇恨线。文案：title「取出镜头背后的人」beat「眼睛徽章落在观众头上，观众飞进镜位槽。」detail「镜位取出的是观众（{result}）。」

### SnapToNearestInCollection｜B(P8+P7+P1)｜M
- 现状：花名册只有 1 个候选，"最近"无从比较；吸附后画面零变化。
- 零字幕画面：花名册 3 人（近员 1.79m / 中距员 2.84m / 远员 4.56m 圈外暗色）各套青色点名圈；红色 X 落点吸附后原位留 ghost，弯曲箭头把 X 压到近员脚下。
- 改动：vignette 重写（3 成员+落点 marker 演员），graph `ConstFloat 200→400`；断言升级为吸附结果==真实最近成员且落点距其 <5cm。文案：title「贴到花名册里最近的人」beat「X 标记离开原地，压到花名册里够得着的最近那人身上。」detail「落点吸到{result}身上。」

### SnapToNearestGraphEdge｜B(P7+P1)｜S
- 现状：偏移仅 0.2m，无残影无箭头，位移肉眼难辨。
- 零字幕画面：一条厚带"路"（沿 y=0 铺满，中虚线），X 落点悬在路上方 1.8m，吸附后原位 ghost + 垂直下落箭头把 X 钉到路面，落点白点。
- 改动：落点演员改 (1.2,1.8)；路网节点扩为 (-100,0)/(100,0)/(300,0)/(400,0)；断言 |aim.Y|<5cm。文案：title「离路太远就拽回路边」beat「X 从半空掉到路上，原位留下残影。」detail「落点已经钉在路边 ({x},{y})。」

### SendEvent｜C(演后果)｜L
- 现状：挨打真实但"广播给听事件的人"是假的——场上没有听众，事件只进总线。
- 零字幕画面：打中木桩（真掉血）同一瞬间，信号箭头飞向木桩头顶**铃铛徽章**并点亮——徽章亮起的条件是木桩真实挂着一条 Effect.GraphOps.Mark（真实结算）。
- 改动：新增监听图 `SendEvent.listener.json`（LoadExplicitTarget→ApplyEffectTemplate Mark，作用于事件目标），由 driver 在本帧总线真实读到事件后以"总线事件→执行图"方式触发（复刻 PresenterRuleSystem 生产范式）；断言：总线恰 1 条事件 + 木桩 ActiveEffectContainer 真含 Mark。文案：title「打出去，对方听得见」beat「木桩挨打掉血，头顶的铃同时被这一下敲亮。」detail「木桩血条从 {healthBefore} 掉到 {healthAfter}，铃亮了。」

### ControlDomainResolve｜B(P1+P2)｜M
- 现状：解析=队长正确且 fail-close，但"说了算的是队长"只在字幕；SeedOwnershipAndKnowledge 会压平 vignette 的 Owns 链。
- 零字幕画面：队长→班长→小兵三人指挥链白色实心箭头；结果芯片从小兵头顶沿链逐段飞到队长头顶，队长插起三角旗徽章。
- 改动：vignette 三人+`links[]`（type:"Owns"，走真实 RelationshipRuntime）；SeedOwnershipAndKnowledge 改为只对无主演员 Ensure；断言加 OwnershipResolver 链深校验。文案：title「一路问到说了算的人」beat「从小兵往上问，问到插旗的队长为止。」detail「小兵说了算的人是{result}。」

### FanOutDispatchEffect｜B(P9+P1+P11)｜M
- 现状：圈内 3 人真实掉血但派发模板是零后果的 DispatchStub；无扇出连线；"全演员"预填与圈语义不符。
- 零字幕画面：施法者脚边钉**黄色预设卡**；三支箭头从卡上同时射向圈内三人，每人头顶浮出红色 -18 浮标，三根血条真实掉到 82；圈外暗色路人纹丝不动（对照）。
- 改动：graph effectTemplate→`Effect.GraphOps.Strike`（删除前置 wound 双算）；PrefillFanOut 按 2.6m 圈真实过滤；断言圈内三人 Health==82、路人==100。文案：title「按预设发同一招给全圈」beat「卡一亮，圈内三人一起掉 18 血，圈外人无感。」detail「派给圈里 {count} 人，各掉 {damage} 血。」

### FanOutDispatchEffectDynamic｜B(P3+P1+P9)｜M
- 现状：真读模板号再派发，但与静态版画面同构，"读出来的"不可见。
- 零字幕画面：两拍——第一拍场边**信使**头顶的招式芯片（Mark 铃铛刻痕）沿虚线飞进施法者脚边**空着的卡槽**（静态版是卡预先插好）；第二拍三支箭头扇出，圈内三人头顶各亮起铃铛徽章（真实 Mark 结算）而血条不掉——与静态版"集体掉血"形成一眼可辨的反差。
- 改动：BuildPayload 模板号改 `EffectTemplateIdRegistry.GetId("Effect.GraphOps.Mark")` 实时读；断言：三人真挂 Mark、无人掉血。文案：title「先看信使带来的卡，再照卡发招」beat「芯片插进空槽，圈里三人各挂上一枚铃。」detail「按卡给圈里 {count} 人挂上铃。」

### ClampTargetToRange｜A｜S
- 现状：标杆——黄圈+落点拉到圈边已零字幕可懂；缺"从哪拉来"。
- 零字幕画面：远处 (20,0) 留半透明 ghost X，拉回箭头指向圈边 (5,0)；圈用双色描边。
- 改动：仅补 ghost+箭头（复用共享原语）。文案：title「够不着就拉回射程边」beat「远处那个 X 被拽到黄圈边上。」detail「落点拉回到 ({x},{y})。」

### KnowledgeHasProjection｜B(P8+P1+P2)｜M
- 现状：判定正确但"知识投影"黑话进字幕；只有正例无对照。
- 零字幕画面：观众面向木桩与暗处陌生人两拍对照——木桩：实心视线+睁眼徽章+血条完整；陌生人：断续虚线+红叉+血条被真实知识门控隐去（不写披露即真看不见，非涂黑）。
- 改动：加陌生人演员，驱动不为其写 Knowledge 披露；featured 图二次执行断言 false。文案（黑话清除）：title「观众名下有记录才看得见」beat「木桩有记录、亮着；陌生人没记录，连血条都不显示。」detail「观众对{result}。」

### LoadEventPayloadFloat｜C(演后果)｜L
- 现状：载荷 2.5f 由 BuildPayload 硬编码注入，EventBus 上没有事件——"事件带来的"是假的。
- 零字幕画面：施法者先真实放出一发信号（箭头飞向木桩，木桩头上小铃晃一下=事件真发生）；随后写着 2.5 的数值芯片从木桩（事件发生地）飞进右侧**信件板** 1 号槽。
- 改动：新增生产者图 `LoadEventPayloadFloat.producer.json`（LoadExplicitTarget→ConstFloat 2.5→SendEvent），driver 每帧先执行生产者、EventBus.Update() 后从总线真实读回，`FloatA=evt.Magnitude`（删常量注入）；断言：总线本帧恰 1 条事件 + featured 结果==总线值。文案：title「从飞来的那一发里读出小数」beat「信号落地后，2.5 从事件里飞进信件板。」detail「这一发带来的小数是 {result}。」

### LoadEventPayloadInt｜C(演后果)｜S（随 Float 合并）
- 现状：PayloadA=99 同为注入。
- 零字幕画面：与 Float 同舞台同一封信，芯片写真实事件编号（运行时从注册表读），飞入信件板 2 号槽——两 op 并排即"同一封信两个槽"。
- 改动：共享 Float 的生产者图；`PayloadA=真实注册表号`，删常量；断言结果==真实读出编号。文案：title「从飞来的那一发里读出编号」beat「信号落地后，编号从事件里飞进信件板。」detail「这一发带来的编号是 {result}。」

### LoadTargetPosY｜B(P11+P1)｜M
- 现状：200 正确但无单位无坐标轴，"字幕报纵深位置"元语言。
- 零字幕画面：世界原点立南北向标尺（每米刻度+数字名牌）；水平虚线箭头从落点 (3.6,2.0) 打向标尺 (0,2.0) 刻度，刻度亮起浮出"200"。
- 改动：与 X 共建 `_fields/pos.json`（施法者+落点+双标尺道具）；断言 `result == round(落点.Y*100)`。文案：title「读出落点在南北标尺上的读数」beat「虚线打到标尺上，亮出 200。」detail「落点南北读数 {result}。」

### LoadTargetPosX｜B(P11+P1)｜S（共享舞台）
- 现状：360 同上。
- 零字幕画面：同一落点，东西向标尺，垂直虚线打向 (3.6,0) 刻度浮出"360"——横竖两把尺各读各的数。
- 改动：共享舞台；断言 `result == round(落点.X*100)`。文案：title「读出落点在东西标尺上的读数」beat「虚线打到标尺上，亮出 360。」detail「落点东西读数 {result}。」

### IsPointInCircle｜A（补对照）｜S
- 现状：圈+落点较好，但 bool 只演 true。
- 零字幕画面：圈内点 (0.5,0) 与圈外点 (6.5,0) 两拍交替真执行——圈内：圈变黄、X 实心绿、绿勾徽章；圈外：圈回灰、X 红、红叉徽章。
- 改动：两个独立 marker 演员，双执行双断言（true/false）。文案：title「圈里圈外，当场见分晓」beat「圈里的点亮绿勾，圈外的点吃红叉。」detail「圈内点{resultIn}，圈外点{resultOut}。」

### ControlDomainControls｜B(P8+P1)｜M
- 现状：只演"管得着"，bool 语义空心。
- 零字幕画面：我方队长→队员绿色实线指挥箭头+令徽章；敌队长→同一队员红色断续虚线+大红叉。两拍真执行，徽章/线色随真实 IsControllableBy 切换。
- 改动：加敌队长演员；驱动二次执行断言 true/false。文案：title「同一个兵，不是谁都指挥得动」beat「自家队长的箭头是实线，敌队长的线断在半路。」detail「队长对队员{result}；敌队长对队员{resultFoe}。」

## 家族小结
- 共用原语：P1 箭头（含虚线/扇出）、P2 徽章（眼/铃/勾叉/令/旗）、P3 寄存板/卡槽/信件板、P7 ghost、P8 两拍对照、P9 真实结算（Strike+Mark 双模板）、P11 标尺与浮标——DebugDraw 现有 Lines/Circles/Boxes（含 RotationRadians）足够拼出全部，无需动 Core。
- 共享场景：新建 `_fields/pos.json`（双标尺）、"信使+信件板"舞台（两 Payload op 共用）、指挥链场景（两 ControlDomain op 合用）、FanOut 静/动同舞台双模板互为对照页。
- 合并机会：LoadEventPayloadInt+Float 一套生产者图一套面板；LoadTargetPosX+Y 一场两尺；IsPointInCircle 与 ControlDomainControls 复用双执行机制。
- 链路修复清单（禁止字幕配音的部分）：① BuildPayload 注入→总线真读；② SendEvent 无听众→监听图走真总线；③ SeedOwnershipAndKnowledge 压平链→保留既有 owner；④ PrefillFanOut 全演员→按圈过滤；⑤ 单正例断言→双向断言；⑥ 误导仇恨线按 op 裁剪。
- 统计：S×5、M×7、L×3。

