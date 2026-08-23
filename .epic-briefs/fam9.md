# 家族方案 ⑨ 黑板与配置（13 op）

> 核心命题：把"记事板"和"配置册"变成世界内真实道具。
> 核实补充：`Effect.GraphOps.Config` 参数 power=40/tier=2/chainEffect=Strike；LoadConfigEffectId 图中 wound/hit 两节点实为替身（cfgFx 悬空属实）；`DebugDrawCommandBuffer` 无文字 API——板上数字用 **DrawDigit 七段数码管折线**（全家族公共件）；`MaterializeTemplate` 会 presenter-bootstrap 新实体，"新身体"可救活不必造假演员；InvokeBuiltin 现场的"印记"是独立演员藏血条借位表演，需清除。

### P3 数据道具三件套（一张设计全家用）
- **记事板**（黑板 6 op）：木框平板悬于施法者右侧 (x+1.0, y+1.4)，约 2.0×1.4m，顶部压夹；三槽横排：威力（红）/层数（黄）/点名（青）；int/float 槽用七段数字，entity 槽放与目标同色同形小芯片。板常驻常显——黑板是"一直挂在那的数据"。
- **配置册**（配置 4 op）：两页摊开的书，施法者左侧 (x-1.2)；左页"品阶"刻度行、右页"威力"行+"衔接效果"票券行。配置是"印好的册子"——只读不改。
- **情境信封**（Context 2 op）：施法者头顶白色封套+红封蜡，三卡（出手金/挨打红/关照青）。情境是"这一击随身携带的快递单"。
- **读写节拍（4 拍统一）**：读=槽白框闪 2 帧→数字牌沿弧线飞出（槽内留浅色残值）→落点反馈；写=源头数字牌现→弧线飞入→落槽绿闪+**回读小勾**（driver 写后 TryRead 断言的可见化，断言失败即抛错不画勾）。

### LoadContextSource｜B(P8)｜M（信封首建）
- 零字幕画面：出手瞬间头顶浮出**情境信封**，内插三卡；本 op 抽出第一张金卡（卡上画着金色小方块=施法者的形状颜色），卡飞到施法者头顶与本体重叠贴合。
- 去重建议：与 LoadCaster 在当前模型下同值（E=Caster）。首选 wiki 合并词条"谁出手：从情境 vs 从图自身"双图对照（画廊保留两个绑定指向同一页）；若保两场，本场用信封与 LoadCaster 的自指环形成区分教学点。文案：title「从情境信封认出出手人」beat「拆开这一击的信封，出手人那格画的正是金块自己。」detail「信封出手人那格，贴上了{named}。」

### LoadContextTargetContext｜B(P8+P1)｜S
- 零字幕画面：同一信封抽第三张青卡（额外关照的人），卡飞向第三个演员；到点后施法者→该演员拉青色弧线+脚下青色锁定环——两拍内"额外那个人"被点名；木桩（红卡位）保持不亮。
- 文案（去"关联到关联的人"拗口）：title「从情境信封找出额外那个人」beat「信封第三格写着这一击还要照顾谁。」detail「信封额外那格指向{named}。」

### LoadConfigFloat｜B(P3+P11)｜M
- 零字幕画面：配置册右页"威力"行印着红 40；读值时该行白框闪、**"40"数字牌沿弧线飞出砸向木桩**、头顶立浮标，血条同拍真掉 40——册上写多少掉多少，一比一。
- 链路修复：图扩为 `cfgF→neg→hit`（NegFloat+ModifyAttributeAdd，target 接 LoadExplicitTarget），删 driver `SubtractTargetHealth` 代打；wiki §3 与 poster 按新事实重生成（修两处漂移）。文案：title「翻开技能册照威力办事」beat「册上写 40，木桩就真挨 40。」detail「册上威力 {result}；木桩血量 {healthBefore} 掉到 {healthAfter}。」

### LoadConfigInt｜B(P3+P2)｜S
- 零字幕画面：配置册左页"品阶"行两道刻度 II；读值后施法者头顶**盾形品阶徽章**内两颗星逐颗盖章点亮（灰→金）。
- 文案（"阶位"→"品阶"）：title「翻开技能册认品阶」beat「册上品阶两颗星，头顶徽章照着点亮。」detail「册上品阶 {result}，徽章已点亮。」

### ReadBlackboardFloat｜B(P3+P9)｜M
- 现状：vignette/wiki/poster 三版本互相矛盾（示意/打在血条上/旧字幕）。
- 零字幕画面：记事板威力红格写 35；读值时格白框闪、"35"飞出砸木桩，血条真掉 35——**掉的 35 就是图里读的 35**，道歉性旁白消失。
- 链路修复：图扩为 `src→readF→neg→hit`，删 driver 代打；wiki 三处行（L3/L30/L54）与 poster 全部重生成。文案：title「照记事板上的威力出拳」beat「板上写 35，木桩就真掉 35。」detail「板上威力 {result}；木桩血量 {healthBefore} 掉到 {healthAfter}。」

### ReadBlackboardInt｜B(P3+P2)｜S
- 零字幕画面：层数黄格写 4；读值时四枚小方块牌从格中飞出，一枚枚**叠落在木桩头顶摞成 4 层"叠层印"**——层数的语义直接长在画面上。
- 文案：title「照记事板上的层数挂印」beat「板上 4 层，木桩头顶落满 4 层印。」detail「板上层数 {result}，落在木桩头顶。」

### ReadBlackboardEntity｜B(P3+P1)｜S
- 零字幕画面：点名青格里嵌**青色小木桩芯片**（形状颜色=场上木桩本尊）；读值时芯片放大飞出化作**套索箭**从板射向真木桩，木桩脚下绿锁定环——读出的实体=被套住的人。
- 文案：title「照记事板点名叫阵」beat「板上那格贴着木桩的画像，读出来就套住他。」detail「点名格指向{named}。」

### LoadConfigEffectId｜B(P3+P9)｜M ⚠️硬伤修复（悬空输出）
- 现状：cfgFx 输出悬空，-18 来自 ConstFloat 替身；beat"配置指着某效果再打出去"是数字巧合成假。
- 零字幕画面：配置册"衔接效果"行贴**红色闪电票券**（Strike 图标）；读值时票券**从册页撕下、飞进施法者手中**，依票打出真实一击（-18 真伤+浮标）——册上贴哪张票就打哪一下，换成别的票伤数立刻不同。
- 链路修复（必做）：图重写为 `cfgFx→explicit→applyDyn`，valueEdges `cfgFx.value→applyDyn.value`、`explicit.value→applyDyn.target`（端口模式照抄 ApplyEffectDynamic.json），删 wound/hit 替身；断言升级"世界血量每拍恰减 18 且 ConstFloat 节点不存在"，可用换模板反证。文案：title「册上贴哪张效果票，就照票开打」beat「撕下打击票，木桩真挨票面那一下。」detail「照册上的打击票，木桩血量 {healthBefore} 掉到 {healthAfter}。」

### BeginLifecycleTransaction｜C(演后果·生命台账)｜L
- 零字幕画面：脚边**账台+生命台账**三拍——①开账：账本从合到开、封页盖青章（Begin 执行拍）；②记账：账页写"造新身"墨迹，**新身体以残影→实心**在场上显形；③关账：账本合上盖红"讫"章，新身体留在场上。读法：先开账，记在账上的事，账关了也算数。
- 链路：图加 materialize 节点（复用 `Effect.GraphOps.Lifecycle` 的 targetEntityTemplate=GraphOps.Ally）；driver 把 LastMaterializedTarget 用 BindMapEntity 绑成正式演员；断言加"新身体存活"。文案：title「先开生命台账再动土」beat「账本一开，造身记上一笔；账一关，新身体已站在场上。」detail「台账记了一笔造身，新身体已就位。」

### WriteBlackboardFloat｜B(P3+P7)｜S
- 零字幕画面：威力格初始为空（灰底虚框=写前态）；一枚红色"35"数字砝码牌沿弧线飞入威力格，落槽绿闪+右上角**回读小勾环**（写后 TryRead 断言的可见化）。
- 文案：title「把这一拳的威力记上板」beat「35 落进威力格，格子亮了。」detail「威力格记下 {result}。」

### WriteBlackboardInt｜B(P3+P7)｜S
- 零字幕画面：层数黄格置空；四枚小方块牌逐枚飞入摞成 4 层（与 Read 版叠层印同一视觉语言，读写互为镜像），落定绿闪+回读勾。
- 文案：title「把层数记上板」beat「四枚层印叠进层数格。」detail「层数格记下 {result}。」

### WriteBlackboardEntity｜B(P3+P1)｜S
- 零字幕画面：点名青格置空；**牵引箭从木桩身上"揭"下一枚青色芯片**，飞回落入点名格贴好，绿闪+回读勾——与 Read 版套索箭互为反向（读=射出、写=收回）。
- 文案：title「把要盯的人记上板」beat「从木桩身上揭张画像，贴进点名格。」detail「点名格贴上了{named}。」

### InvokeBuiltin｜C(演后果·账本步骤)｜M
- 现状：图真实执行 beginTx→materialize→clear，但新身体无画面载体；"清印记"靠独立演员藏血条借位表演。
- 零字幕画面：承接台账场景**逐行执行**——"造新身"行亮起一拍，新身体残影→实心两拍落成正式演员（头顶挂名）；"清印"行亮起，新身体头顶**效果挂架**（一排空卡槽小框）闪白扫净、斜杠划掉收起——内置步骤=账本里逐条办、办完看得见。
- 链路：LastMaterializedTarget 绑正式演员；**删除 mark 借位演员与 SetHudLit hack**；断言"新身体存活"+"mark 角色不存在"。文案：title「账本里的步骤逐条办」beat「造出新身体，再把新身体的效果挂架扫净。」detail「新身体已上场，效果挂架已清空。」

## 家族小结
- 家族级公共件 M：DrawDigit 七段数字助手 + 三件道具绘制器 + 弧线飞牌动画框架 + 回读勾环——落地后各场边际成本普遍降为 S。
- 边界说明：`EndBuiltinInvocation` 是提交式关账（不回滚已造实体），回滚仅存在于 lifecycle 执行器程序路径——**不为演出伪造失败事务**，台账只演真实发生的三拍。
- 去重：LoadContextSource vs LoadCaster 首选合并 wiki 词条双图对照；Write/Read 三连维持"一场一 op"（SSOT 结构），wiki 家族页做三联拼图对照（零代码成本）。
- 统计：S×6、M×5、L×1（BeginLifecycleTransaction；InvokeBuiltin 为 M 级 C 档）+ 公共件 M。

**实施分支已推送**：`epic/990-zero-caption-gallery`（https://github.com/MightyBubble/Ludots/tree/epic/990-zero-caption-gallery）

进度（110/110 画廊测试绿，8 试点 op 已录屏）：
- `2c19d89ca4` Stage 0/基建：Config Shards 迁移修复（121 图分片数组化 + TagDisplayTable 进 catalog + Relationship 分片）、vignette↔wiki↔registry 同步门测试、录屏管线（Windows 适配 / poster 首拍抓帧 / 免输入相机档 Camera.Profile.GraphOpsGallery / Skia 禁用 env 仅 Linux）
- `faa896d2b2` 真结算基建（headless 引擎驱动结算 pass、停 Clear、断言升级为结算后真实状态）+ 共享视觉原语库 P1-P11
- `2b609d5bcb` Stage 2 试点 8 op 样板：QueryIncoming（箭头）/RelationshipSetFlag（徽章）/QuerySortByAttribute（角标）/WriteBlackboardFloat（面板）/MulFloat（算式台+graphSettled 真结算）/Yield（茶杯水位）/SnapToNearestGraphEdge（ghost）/ApplyEffectTemplate（结算徽章）

注意：分支基座 db75962f7a 为当时主工作区快照（含进行中的 Configs→assets 迁移 WIP），PR 合并前需与 main 对账。后续家族批次（112 op）在此分支继续推进。
