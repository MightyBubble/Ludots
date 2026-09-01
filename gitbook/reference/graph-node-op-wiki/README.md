# Graph 节点画廊 Wiki

每个可执行图节点两页视角合一：一场给玩家看的短剧（录像 + 人话字幕），加一节给 mod 作者的写法（签名表 + 真实用例 + 手册分册链接）。下面按玩法家族分组。

生成器：`scripts/generate-graph-op-node-wiki.py`（从 vignette 与引擎描述表生成，勿手改正文）。

## 事件与吸附

> 作者语义与全量字段见手册分册 [事件与情境 · gr-op-01](../mod-editor-prd/config/gr-op-01-context.md)。

- [一路问到说了算的人](ControlDomainResolve.md) — 从小兵往上问，问到插旗的队长为止。
- [从飞来的那一发里读出小数](LoadEventPayloadFloat.md) — 信号落地后，2.5 从事件里飞进信件板。
- [从飞来的那一发里读出编号](LoadEventPayloadInt.md) — 信号落地后，编号从事件里飞进信件板。
- [先接飞来的卡，再照卡发招](FanOutDispatchEffectDynamic.md) — 芯片插进空槽，圈里三人各挂上一枚铃。
- [取出镜头背后的人](LoadViewer.md) — 眼睛徽章落在观众头上，观众飞进镜位槽。
- [同一个兵，不是谁都指挥得动](ControlDomainControls.md) — 队长指挥队员是实线；反过来队员指挥队长，线断在半路。
- [圈里圈外，当场见分晓](IsPointInCircle.md) — 圈里的点亮绿勾，圈外的点吃红叉。
- [打出去，对方听得见](SendEvent.md) — 木桩挨打掉血，头顶的铃同时被这一下敲亮。
- [按预设发同一招给全圈](FanOutDispatchEffect.md) — 卡一亮，圈内三人一起掉 18 血。
- [离路太远就拽回路边](SnapToNearestGraphEdge.md) — X 从半空掉到路上，原位留下残影。
- [落点拉回够得着的地方](ClampTargetToRange.md) — 点太远会被拉回射程圈内。
- [观众名下有记录才看得见](KnowledgeHasProjection.md) — 木桩有记录、亮着；陌生人没记录，连血条都不显示。
- [读出落点在东西标尺上的读数](LoadTargetPosX.md) — 虚线打到标尺上，亮出 360。
- [读出落点在南北标尺上的读数](LoadTargetPosY.md) — 虚线打到标尺上，亮出 200。
- [贴到花名册里最近的人](SnapToNearestInCollection.md) — X 标记离开原地，压到花名册里够得着的最近那人身上。

## 事件载荷捕获

> 作者语义与全量字段见手册分册 [地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)。

- [倒下的是谁，问载荷就知道](LoadEntryPayloadEntity.md) — 木桩倒下那一刻，名册记下了它的名字。
- [场上还剩几个人，载荷报数](LoadEntryPayloadInt.md) — 清点哨一响，存活数从事件载荷里飞进信件板。
- [落点读数，小数也不丢](LoadEntryPayloadFloat.md) — 号令一落，东西读数带着小数从事件载荷里飞进信件板。

## 关系与好感

> 作者语义与全量字段见手册分册 [关系与好感 · gr-op-08](../mod-editor-prd/config/gr-op-08-relationship.md)。

- [互相都认的朋友](RelationshipQueryMutual.md) — 两头都有箭头的链才亮。
- [只看挂了信任旗的](RelationshipFilterFlag.md) — 链上插着信任旗的才留下。
- [好感平均多少](RelationshipAggAverageMetric.md) — 四份好感倒进算式台，除以四人。
- [好感落在区间里的人](RelationshipFilterMetricRange.md) — 好感量尺卡在 30 到 80 之间才留。
- [我主动交的朋友](RelationshipQueryOutgoing.md) — 箭头从自己射出去的链才算。
- [我们有没有连着](RelationshipHasLink.md) — 和好友的链环扣紧，就是连着。
- [打上失和标记](RelationshipSetFlag.md) — 最弱那条链上插起失和旗。
- [把好感加总](RelationshipAggSumMetric.md) — 四份好感在算式台上连加。
- [把最弱的那条链拆掉](RelationshipRemoveLink.md) — 好感最低那条链断开，线少一条。
- [按好感排个序](RelationshipSortByMetric.md) — 按好感高低挂出名次牌。
- [最低好感是多少](RelationshipAggMinMetric.md) — 四块数值牌里最矮的浮出来。
- [最高好感是多少](RelationshipAggMaxMetric.md) — 四块数值牌里最高的浮出来。
- [读出这个人的好感](RelationshipGetMetric.md) — 从链上抽出读数牌，写着 85。
- [谁把我当朋友](RelationshipQueryIncoming.md) — 箭头指着自己的链亮。
- [谁是好感最低的人](RelationshipAggMinEntityByMetric.md) — 数值牌最矮的人被照亮。
- [谁是好感最高的人](RelationshipAggMaxEntityByMetric.md) — 数值牌最高的人被照亮。
- [这两人之间有没有链](RelationshipQueryBetweenPair.md) — 这一对之间拉出一条双头链。

## 名单筛选与汇总

> 作者语义与全量字段见手册分册 [名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)。

- [全场平均血量](AggAverageAttribute.md) — 十三个人的血条凑上台面，台面亮出平均数。
- [全场最低血量](AggMinAttribute.md) — 台面翻出最低一格，亮出的数短得像那条空血条。
- [全场最高血量](AggMaxAttribute.md) — 台面翻出最高一格，亮出的数顶着满格血条。
- [全场生命合计](AggSumAttribute.md) — 十三根血条一根根收进台面，台面亮出总数。
- [只圈残血的](QueryFilterAttributeRange.md) — 全场先亮一圈，再只剩短血条的留着。
- [只挑侦察兵](QueryFilterTemplate.md) — 全场先亮一圈，再只剩两个矮个子亮着。
- [圈出对面十个](QueryFilterTeam.md) — 红的一排留圈，蓝的退成灰影。
- [戴敌徽的全圈出来](QueryFilterTagAny.md) — 头顶红徽的九个留圈，没徽的退成灰影。
- [把场上的人全点名](QueryAllMapEntities.md) — 扫描弧从指挥席扫过全场，点到谁谁亮。
- [把身上的效果全点名](QueryCollectActiveEffects.md) — 指挥身上三条效果被点名线牵住，头上浮出计数。
- [按血量从厚到薄排队](QuerySortByAttribute.md) — 最厚的顶着三道杠，箭头顺着血条一路排下去。
- [摘掉阵亡徽的留下](QueryFilterTagNone.md) — 戴阵亡徽的退成灰影，没戴徽的留着圈。
- [点出当前对话选项](QueryCollectActiveDialogueChoices.md) — 能回的话被点名线牵住。
- [点出技能格](QueryCollectAbilitySlots.md) — 英雄身上的技能格被点名线牵住。
- [点出身上印记](QueryCollectPresentTags.md) — 身上的印记被点名线牵住。
- [点出进度节点](QueryCollectProgressionNodes.md) — 进度节点被点名线牵住。
- [点出进行中的差事](QueryCollectActiveTasks.md) — 进行中的差事被点名线牵住。
- [点出进行中的活动](QueryCollectActiveActivities.md) — 进行中的活动被点名线牵住。
- [点名最残的那个](AggMinEntityByAttribute.md) — 全场退成灰影，空血条那个被点名徽钉住。
- [点名最能扛的](AggMaxEntityByAttribute.md) — 全场退成灰影，满血条那个被点名徽钉住。
- [照着名册点名](QueryFromCollection.md) — 名册板六格点亮，点名线拉向场上六人。
- [翻开效果图鉴](QueryCollectEffectTemplates.md) — 墙上贴着一批效果说明书。
- [翻开物品图鉴](QueryCollectItemDefinitions.md) — 物品说明书贴在墙上。
- [翻开背包](QueryCollectInventoryItems.md) — 背包里的物被点名线牵住。
- [谁会这招](QueryCollectAbilityHolders.md) — 会这招的人被点名线牵住。

## 子图调用与事件派发

> 作者语义与全量字段见手册分册 [地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)。

- [先存参数，再点子图](StoreArgInt.md) — 整数参数放进暂存表，子图按名字取走，回执就是同一数字。
- [实体参数递过去，子图亲自搬人](StoreArgEntity.md) — 把木桩实体暂存给子图，子图按参数把它搬到新位置。
- [按事件账本派发心跳](DispatchMapEvent.md) — 载荷按 schema 组装成事件，地图上的监听者按账本收货。
- [浮点参数过一手，地图变量作回音](StoreArgFloat.md) — 比例系数暂存后交给子图，子图把它写进地图变量当回音。
- [点名子图，指定入口直接回话](InvokeGraph.md) — 主图一声令下，子图从 boost 入口出发，把九号命令带回来。

## 属性与效果

> 作者语义与全量字段见手册分册 [属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)。

- [先对脸：打的是不是自己](CompareEqEntity.md) — 残影演示点名自己→同一个人，收手；点名木桩→不是同一人，一刀扣 18。
- [先开生命台账再动土](BeginLifecycleTransaction.md) — 账本一开，造身记上一笔；账一关，新身体已站在场上。
- [先认出是谁出手](LoadCaster.md) — 白光柱落在施法者头顶，亮出出手人徽章，攻击线才从这亮起。
- [写死的整数：一刀三层](ConstInt.md) — 铭牌铸死数字 3，带锁印；取数后木桩头顶亮三层空圈。
- [出手前先看一眼对方的血](LoadAttribute.md) — 黄虚线搭到木桩，读出当前生命，头顶浮出 80。
- [图内把血量写成 42](ModifyAttributeSet.md) — 面板按钮触发 TriggerGraph，木桩当前生命直接写成 42，属性变化仍从 GAS 正式入口落账。
- [层数叠满就引爆](CompareEqInt.md) — 三层火苗对满层 3，天平对齐，爆出一击扣 18 血。
- [岔路口选人打](SelectEntity.md) — 残影幕条件不成立→挑了自己挨打；正幕条件成立→挑了木桩，扣 18。
- [把血直接写成 90](WriteSelfAttribute.md) — 施法者血 60，一道写入线落下，血条直接抬到 90，头顶浮出 =90。
- [把身上的状态摘掉](RemoveEffectTemplate.md) — 木桩头顶紫色标记先挂着，白弧扫过，标记碎掉消失，血条不动。
- [点名谁就打谁](LoadExplicitTarget.md) — 红色准星飞出锁扣木桩，一刀沿线打下，木桩 100 掉到 85。
- [直接在血条上做加法](ModifyAttributeAdd.md) — 一刀 -25 写进血条，木桩 100 掉到 75，头顶浮出 -25。
- [看效果叠了几层](LoadEffectStack.md) — 自查线绕回施法者身上的层数，头顶浮出 ×3。
- [看效果还剩多久](LoadEffectTiming.md) — 自查线绕回施法者身上的计时，头顶浮出剩余 55。
- [看自己还剩多少血](LoadSelfAttribute.md) — 自查线绕回施法者自己，头顶浮出 62；木桩满血没人碰。
- [给木桩挂上看得见的状态](ApplyEffectTemplate.md) — 红线贴附不扣血：木桩头顶钉上紫色标记，带光环，血条不动。
- [血量过线没：过线轻击，没过线全力](CompareLtInt.md) — 木桩 50 血低于 80 刻线，标尺闪红，全力一击扣 18，掉到 32。
- [账本里的步骤逐条办](InvokeBuiltin.md) — 造出新身体，再把新身体的效果挂架扫净。
- [这一击的单据上写着打谁](LoadContextTarget.md) — 从这一击自带单据里取出目标，取到木桩，扣 12 血。
- [连击数加一](AddInt.md) — 两刀打进算式台 2+1，翻牌翻出 3，木桩头顶亮三枚连击火花。

## 放置区域名册

> 作者语义与全量字段见手册分册 [地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)。

- [点名地图区域，名册有没有就报数](LoadPlacedRegion.md) — 记录官点名营地圈，地图名册回 1；点名不存在的鬼区，名册回 0。

## 放置实体名册

> 作者语义与全量字段见手册分册 [地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)。

- [点名放置的木桩，名册一翻就到](LoadPlacedEntity.md) — 记录官翻出名册一点名，放置的木桩大王立刻在岗应答；倒下后名册读出空位。
- [点名预放置锚点，名册一翻就到](LoadPlacedAnchor.md) — 记录官翻出名册点到锚点，营地锚立刻在岗应答；倒下后名册读出空位。

## 瞄准源

> 作者语义与全量字段见手册分册 [空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)。

- [光标点到谁](ScreenPointToEntity.md) — 光标压在人群一侧，点名线牵住被点到的那人。
- [把光标钉到地上](ScreenPointToGround.md) — 光标点下的地方，落点圈在地图上亮起。
- [摇杆掰方向](StickToDirection.md) — 摇杆斜掰，箭头随之指向东北。
- [朝落点转向](PointToDirection.md) — 指挥转身，炮口指向地图东北的落点。
- [框一圈点名](ScreenRegionToEntities.md) — 虚线框罩住西边一段，框里的人被点名线逐个牵住。

## 空间圈人

> 作者语义与全量字段见手册分册 [空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)。

- [两格以内的六角范围](QueryHexRange.md) — 范围内格子描黄框，第三格描灰框，人也不亮。
- [只取半径 2 的六角环](QueryHexRing.md) — 描边那一圈上的人亮，里圈和更外圈都不亮。
- [只留敌对关系的人](QueryFilterRelationship.md) — 扇内先亮一片，敌对关系的留下。
- [只留敌方层的人](QueryFilterLayer.md) — 扇内先亮一片，敌方层的留下。
- [圈人时把你自己抠出去](QueryFilterNotEntity.md) — 滤前自己也在名单里，一步后自己暗掉。
- [扇形里数出几个人](AggCount.md) — 扇内每人头顶弹一下，刻痕一道道加上去。
- [扇形里谁离我最近](AggMinByDistance.md) — 每人拉一条线，最短的那条留下。
- [按名单取第一个](TargetListGet.md) — 名单按序编号，红线只连 1 号。
- [朝这个方向的扇形里有谁](QueryCone.md) — 描边扇形罩住的人亮，贴着边站歪一点的不亮。
- [贴身六格邻居](QueryHexNeighbors.md) — 六个邻格描出来，格里的亮，多一格的灰着。
- [身前这块矩形里有谁](QueryRectangle.md) — 身前的框描出来，框里的人亮。
- [这条窄线穿过谁](QueryLine.md) — 带内的人亮，贴着带边差几厘米的也不亮。

## 算术与比较

> 作者语义与全量字段见手册分册 [算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)。

- [一刀摊给两根木桩](DivFloat.md) — 40 的伤害段从中间切开，两根木桩各接一半。
- [两刀里挑大的一刀](MaxFloat.md) — 两块刀伤 12 和 28 摆上台面，挑中的是更长的那块，打出去按它的长度掉血。
- [两刀里挑小的一刀](MinFloat.md) — 两块刀伤 30 和 18 摆上台面，挑中的是更短的那块，打出去按它的长度掉血。
- [两段伤害叠成一刀](AddFloat.md) — 30 的一段先摆上，12 的一段接在尾巴上，接成的一整段有多长，木桩就掉多少血。
- [亮出情报面板](ShowPanel.md) — 选中单位的一瞬间，属性卡跟着亮起来。
- [伤害拉长一半](MulFloat.md) — 20 的伤害段被拉长一半，原样留着影子，拉成多长就掉多少血。
- [刻死的一刀](ConstFloat.md) — 台上没有表盘，只有一块刻好长度的铭牌；每一刀都和铭牌一样长。
- [命运袋里掏一件](WeightedPick.md) — 掌心探进命运袋，掏出第几件全看权重，木桩照数挨一下。
- [图内切瞄准](SetInteractionMode.md) — 不用碰键位表，一枚目标在图里被切进了瞄准模式。
- [图内造兵](SpawnTemplate.md) — 不用预置实体，阈值一到援军从图里长了出来。
- [对折零轴取长度](AbsFloat.md) — 负 8 的修正段沿零轴对折，折过来的长度是多少就打多少。
- [按编号翻名册点将](ResolveTableRow.md) — 报出 2 号，名册翻到那一行，册上的扣血照着木桩落下。
- [撞到上限就停](ClampFloat.md) — 90 的伤害段沿轨道左移，撞上 40 的墙就停住，打出去的是停下来的那一段。
- [收起情报面板](HidePanel.md) — 点掉选中，属性卡跟着隐去。
- [格挡先咬掉一截](SubFloat.md) — 50 的伤害段送到木桩前，格挡块先咬掉头上的 12，剩下的才进血条。
- [永远放行的许可](ConstBool.md) — 门闩每一拍都开着，亮一个绿点放一刀，一排刻记里从来没有红点。
- [热座换手](SetPanelAudience.md) — 回合一换，面板受众跟着换到当令座位，等待的座位点不动面板。
- [砍不砍得死，比一下](CompareGtFloat.md) — 同样长的一刀，血条比它长的木桩挨不动，血条比它短的木桩一刀就没。
- [读名册上的扣血力度](TableReadFloat.md) — 同一行名册，读出这一击该扣多少血，木桩照单落账。
- [读名册上的星数](TableReadInt.md) — 点到 2 号那行，册上记着三颗星，照数挂印。
- [负债翻面成正数](NegFloat.md) — 负 8 的欠条摆在零轴左边，沿零轴翻到右边变成正 8，翻过来的就是打出去的一刀。
- [隔空落子](SetWorldPosition.md) — 不用挪动命令，一枚棋子从图里被放到了指定点。
- [面板落地](CreatePanel.md) — 关卡蓝图一句话，属性卡从模板里长了出来。
- [面板退场](DestroyPanel.md) — 节点一句话，面板实例连同它的绑定一起收走。
- [骰子决定这一刀](RandomFloat01.md) — 每一拍重掷一次骰子，掷出多长这一刀就多长，一列掷点史里没有两根一样长。

## 组合短剧

> 作者语义与全量字段见手册分册 [图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)。

- [先翻出一张效果牌，再照着打](ApplyEffectDynamic.md) — 施法者从抽屉翻出一张牌，木桩照着牌掉了一截血。
- [名单取前三个](QueryLimit.md) — 圈里五个人各有一个编号，亮着的是编号最靠前的三个。
- [圈里每人挨一记](FanOutApplyEffect.md) — 黄圈内五个人同时掉一截血，圈外两个没事。
- [好感再加一截](RelationshipAddMetric.md) — 记事板上好感条原本四成，新亮的一截补到七成。
- [好感直接写成指定值](RelationshipSetMetric.md) — 灰色的旧条被一条更长的绿条整个换掉。
- [把两人连成一条关系链](RelationshipEnsureLink.md) — 灰色虚线先比划一下，然后咔哒扣成青色实线。
- [查一查身上有没有那枚标记](HasTag.md) — 带标记的侦察兵亮绿圈，没标记的那个查完没反应。
- [点名名单按编号排好，次次一样](QuerySortStable.md) — 每波点名，五个人 1 到 5 的编号顺序一模一样，灰影对得上。
- [站圈心喊一嗓子，看看圈里有谁](QueryRadius.md) — 黄圈内五个兵亮起来，施法者自己不算，圈外两人没反应。
- [翻出一张牌，圈里每人照牌挨一下](FanOutApplyEffectDynamic.md) — 先翻牌再动手：圈内五个人同时掉一截血，圈外没事。
- [这条关系上插没插信任旗](RelationshipHasFlag.md) — 青色链上插着绿旗，旗子闪两下；没链的那位啥也没有。

## 脚本控制流

> 作者语义与全量字段见手册分册 [脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)。

- [写死一句字幕](ConstText.md) — 作者把「你好」钉在图上；跑完，字幕口吐出同样三个字。
- [出门办事，办完回家](Call.md) — 人走到驿站歇一脚，脚一落地就回原点，家这格空着时留个虚影。
- [办完差事，交回原点](Return.md) — 差事办完这一步，人从驿站那格退回原点，虚影收回真人。
- [句子送进对话框](SinkPresentationText.md) — 图里写好「字幕到了」，指定对话框通道，口吐出同样一句。
- [叫另一张图来帮忙算](InvokeScript.md) — 主卷轴上叫一声外援，旁边那张小卷轴亮起来，算完把 7 送回来。
- [士气指针](ReadMapVarFloat.md) — 士气存在地图变量里，指针一动读数就到。
- [士气补给](WriteMapVarFloat.md) — 一次补给写回士气变量，地图记得这份涨幅。
- [左右两段字接成一句](ConcatText.md) — 左边「左」、右边「右」并进同一句；字幕口吐出「左右」。
- [开局战绩上墙](ReadMapVarInt.md) — 地图变量记得每一场胜利，开局张口就报数。
- [战绩加一](WriteMapVarInt.md) — 赢一场就写回地图变量，战绩板自己会涨。
- [把 3 抄一份到结果槽](MoveInt.md) — 左边格子里的 3 原样不动，右边结果格里多出一份 3。
- [把小数念成字](FloatToText.md) — 小数 1.5 先变成文字，再送进字幕口。
- [把整数念成字](IntToText.md) — 数字 7 先变成文字，再送进字幕口。
- [按文案键出字幕](LoadTextKey.md) — 作者从名册里挑 gallery.hello；跑完，字幕口吐出本地化的「你好」。
- [没满就再续一杯](JumpIfFalse.md) — 茶杯一格格见满：没满时绿箭头带着续一杯，满了那一下改走黄箭头，直接收工。
- [满了就跳过续杯](Jump.md) — 杯是满的：续杯那几行被划掉，指针直接飞到收工行。
- [点名派发任务](OfferTask.md) — 图节点指定任务 id；运行后，任务进入指定实体的任务列表，字幕显示「任务已派发」。
- [点名派发待办活动](OfferActivity.md) — 图节点点名活动 id；跑完，活动已上桌，字幕报「活动已派发」。
- [等回话再往下走](AwaitCallback.md) — 图停在门口等确认；回话一到，下一拍接着演。
- [算出一个整数就收工](HaltReturnInt.md) — 数落进托盘、卷轴拉下打烊条、人挪到答案旁边——这三件事同时发生，就是收工。
- [续一杯，歇一口气](Yield.md) — 每续一杯就停一拍：人影顿一下，杯里水涨一格，三格满就完。
- [进图开一场对话](StartDialogue.md) — 图节点点名对话 id；跑完，会话已开，字幕报「对话已开」。

## 黑板与配置

> 作者语义与全量字段见手册分册 [黑板与配置 · gr-op-05](../mod-editor-prd/config/gr-op-05-blackboard.md)。

- [从情境信封找出额外那个人](LoadContextTargetContext.md) — 信封第三格写着这一击还要照顾谁。
- [从情境信封认出出手人](LoadContextSource.md) — 拆开这一击的信封，出手人那格画的正是金块自己。
- [册上贴哪张效果票，就照票开打](LoadConfigEffectId.md) — 撕下打击票，木桩真挨票面那一下。
- [把层数记上板](WriteBlackboardInt.md) — 四枚层印叠进层数格。
- [把要盯的人记上板](WriteBlackboardEntity.md) — 从木桩身上揭张画像，贴进点名格。
- [把这一拳的威力记上板](WriteBlackboardFloat.md) — 35 落进威力格，格子亮了。
- [照记事板上的威力出拳](ReadBlackboardFloat.md) — 板上写 35，木桩就真掉 35。
- [照记事板上的层数挂印](ReadBlackboardInt.md) — 板上 4 层，木桩头顶落满 4 层印。
- [照记事板点名叫阵](ReadBlackboardEntity.md) — 板上那格贴着木桩的画像，读出来就套住他。
- [翻开技能册照威力办事](LoadConfigFloat.md) — 册上写 40，木桩就真挨 40。
- [翻开技能册认品阶](LoadConfigInt.md) — 册上品阶两颗星，头顶徽章照着点亮。
