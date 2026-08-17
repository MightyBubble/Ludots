# Graph 节点画廊 Wiki

每个可执行图节点一场能看懂的短剧。下面按玩法家族分组；点进去能看录像、字幕合同和启动命令。

生成器：`scripts/generate-graph-op-node-wiki.py`（从 vignette 生成，勿手改正文）。

## 事件与吸附

- [从观众自己看](LoadViewer.md) — 镜头和观众实体被读出，字幕说从自己这侧看。
- [吸到花名册里最近的人](SnapToNearestInCollection.md) — 落点吸到最近单位身上。
- [打出一记并广播出去](SendEvent.md) — 木桩挨打，同时这件事被广播给听事件的人。
- [找出谁说了算](ControlDomainResolve.md) — 单位归属到控制域代表，字幕说了算的是队长。
- [按预设把效果派给圈里的人](FanOutDispatchEffect.md) — 圈中单位同时挨打、挂上派发出来的状态。
- [模板号读出来再派发](FanOutDispatchEffectDynamic.md) — 先从事件里读出模板号，再按这个号把效果扇出给圈里的人。
- [离路太远就拽回路边](SnapToNearestGraphEdge.md) — X 从半空掉到路上，原位留下残影。
- [落点拉回够得着的地方](ClampTargetToRange.md) — 点太远会被拉回射程圈内。
- [观众知不知道那个人](KnowledgeHasProjection.md) — 观众对木桩有知识投影就显示看得见。
- [读出事件里的小数](LoadEventPayloadFloat.md) — 事件带来的小数载荷显示在字幕。
- [读出事件里的整数](LoadEventPayloadInt.md) — 事件带来的整数载荷显示在字幕。
- [读出击落点的前后](LoadTargetPosY.md) — 落点前后坐标被读出，字幕报纵深位置。
- [读出击落点的左右](LoadTargetPosX.md) — 落点左右坐标被读出，字幕报水平位置。
- [这一点在不在圈里](IsPointInCircle.md) — 落点在圈内显示在圈里，圈外显示在圈外。
- [这个人能不能指挥那个](ControlDomainControls.md) — 队长能指挥队员显示管得着。

## 关系与好感

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

- [只留下侦察兵](QueryFilterTemplate.md) — 士兵暗、侦察兵亮。
- [只留下敌对阵营](QueryFilterTeam.md) — 友军被滤掉，敌人亮。
- [只留下残血的人](QueryFilterAttributeRange.md) — 生命低于阈值的人亮。
- [只看花名册上的人](QueryFromCollection.md) — 小队花名册里的人亮，册外的不亮。
- [平均生命多少](AggAverageAttribute.md) — 字幕报平均。
- [把场上的人都找出来](QueryAllMapEntities.md) — 全图搜到一圈人，字幕报人数。
- [把生命加总](AggSumAttribute.md) — 字幕报生命总和。
- [按血量从厚到薄排队](QuerySortByAttribute.md) — 最厚的顶着三道杠，箭头顺着血条一路排下去。
- [最低生命是多少](AggMinAttribute.md) — 最低值。
- [最高生命是多少](AggMaxAttribute.md) — 最高值+对应人高亮。
- [没有阵亡标记的人](QueryFilterTagNone.md) — 死掉的被滤掉。
- [谁最残](AggMinEntityByAttribute.md) — 最残的人被点名。
- [谁最能打（血最厚）](AggMaxEntityByAttribute.md) — 最厚的人被点名。
- [身上带着敌人标记的人](QueryFilterTagAny.md) — 有敌人标记的亮。

## 属性与效果

- [从这一击的情境里取出目标](LoadContextTarget.md) — 情境里的目标就是木桩，读出来再扣血。
- [先开生命台账再动土](BeginLifecycleTransaction.md) — 账本一开，造身记上一笔；账一关，新身体已站在场上。
- [先看对方还有多少血](LoadAttribute.md) — 出手前先读木桩当前生命，字幕报出读到的数。
- [写死的整数](ConstInt.md) — 这一刀的层数写死是 3，不读装备。
- [层数有没有叠满](CompareEqInt.md) — 当前 3 层对比满层 3，叠满就爆。
- [打的是不是自己](CompareEqEntity.md) — 点名目标和施法者不是同一人，这一刀打出去；若是自己就收手。
- [把状态卸掉](RemoveEffectTemplate.md) — 先挂上再卸掉，字幕说卸效果。
- [有条件就换目标](SelectEntity.md) — 条件成立时改打木桩，不成立打自己。
- [直接扣血](ModifyAttributeAdd.md) — 不绕圈子，木桩血条按加算结果往下掉。
- [看自己还剩多少血](LoadSelfAttribute.md) — 不靠情境，施法者读自己生命，字幕报出。
- [给木桩挂上看得见的状态](ApplyEffectTemplate.md) — 红线贴附不扣血：木桩头顶钉上紫色标记，带光环，血条不动。
- [给自己回一口](WriteSelfAttribute.md) — 施法者血从 60 写回 90，金块血条涨上去。
- [血量够不够打全力](CompareLtInt.md) — 木桩血低于 80 就打全力，否则轻击。
- [认出自己](LoadCaster.md) — 图从施法者自己读起，确认出手的人是台上这个金块。
- [账本里的步骤逐条办](InvokeBuiltin.md) — 造出新身体，再把新身体的效果挂架扫净。
- [连击数加一](AddInt.md) — 连击从 2 加到 3，字幕报连击。
- [锁定点名目标](LoadExplicitTarget.md) — 点到谁就打谁，血条在被点名的红块上掉。

## 空间圈人

- [只取这一圈六角环](QueryHexRing.md) — 只有环上的人亮，里圈和环外都不亮。
- [只打敌对关系](QueryFilterRelationship.md) — 不是敌人关系的不进名单。
- [只打敌对层](QueryFilterLayer.md) — 友军层被滤掉，只留敌人。
- [圈人时排除自己](QueryFilterNotEntity.md) — 自己在圈里但不进名单。
- [圈里一共几个人](AggCount.md) — 字幕报人数，舞台上那些人亮着。
- [扇形里有谁](QueryCone.md) — 面前扇形扫过，扇里的人亮、扇外的人暗。
- [点名单上的第一个](TargetListGet.md) — 名单第一人被点名，血条或高亮在他身上。
- [矩形里有谁](QueryRectangle.md) — 身前一块矩形框人。
- [谁离我最近](AggMinByDistance.md) — 最近的那个人闪出来。
- [贴着的六格邻居](QueryHexNeighbors.md) — 身边六格各站一个人就亮，再远一格的人是暗的。
- [这几格六角范围内](QueryHexRange.md) — 两格以内的人亮，再远一格的人是暗的。
- [这条线上有谁](QueryLine.md) — 一道直线穿过去点到的人。

## 算术与比较

- [一刀摊给两根木桩](DivFloat.md) — 40 的伤害段从中间切开，两根木桩各接一半。
- [两刀里挑大的一刀](MaxFloat.md) — 两块刀伤 12 和 28 摆上台面，挑中的是更长的那块，打出去按它的长度掉血。
- [两刀里挑小的一刀](MinFloat.md) — 两块刀伤 30 和 18 摆上台面，挑中的是更短的那块，打出去按它的长度掉血。
- [两段伤害叠成一刀](AddFloat.md) — 30 的一段先摆上，12 的一段接在尾巴上，接成的一整段有多长，木桩就掉多少血。
- [伤害拉长一半](MulFloat.md) — 20 的伤害段被拉长一半，原样留着影子，拉成多长就掉多少血。
- [刻死的一刀](ConstFloat.md) — 台上没有表盘，只有一块刻好长度的铭牌；每一刀都和铭牌一样长。
- [对折零轴取长度](AbsFloat.md) — 负 8 的修正段沿零轴对折，折过来的长度是多少就打多少。
- [撞到上限就停](ClampFloat.md) — 90 的伤害段沿轨道左移，撞上 40 的墙就停住，打出去的是停下来的那一段。
- [格挡先咬掉一截](SubFloat.md) — 50 的伤害段送到木桩前，格挡块先咬掉头上的 12，剩下的才进血条。
- [永远放行的许可](ConstBool.md) — 门闩每一拍都开着，亮一个绿点放一刀，一排刻记里从来没有红点。
- [砍不砍得死，比一下](CompareGtFloat.md) — 同样长的一刀，血条比它长的木桩挨不动，血条比它短的木桩一刀就没。
- [负债翻面成正数](NegFloat.md) — 负 8 的欠条摆在零轴左边，沿零轴翻到右边变成正 8，翻过来的就是打出去的一刀。
- [骰子决定这一刀](RandomFloat01.md) — 每一拍重掷一次骰子，掷出多长这一刀就多长，一列掷点史里没有两根一样长。

## 组合短剧

- [先把关系链接上](RelationshipEnsureLink.md) — 施法者和盟友之间出现友谊/好感链。
- [只点最近的几个](QueryLimit.md) — 圈里很多人，只留下前 3 个。
- [圈里每人挂一层](FanOutApplyEffect.md) — 圈中单位都被挂上状态，血条或字幕说挂上了。
- [圈里的人排个稳定顺序](QuerySortStable.md) — 同样距离时顺序不乱跳。
- [好感再加一截](RelationshipAddMetric.md) — 好感从 40 加到 70。
- [把好感写成指定值](RelationshipSetMetric.md) — 好感被写成 80。
- [按读到的模板打到点名目标](ApplyEffectDynamic.md) — 模板号不是写死的，读出来再打木桩。
- [按读到的模板打圈里所有人](FanOutApplyEffectDynamic.md) — 动态模板扇出。
- [摸一圈看看谁在近处](QueryRadius.md) — 施法者周围一圈亮起来，圈外的人不动。
- [身上有没有敌人标记](HasTag.md) — 侦察兵带着敌人标记，检查为「有」。
- [这条关系受不受信任](RelationshipHasFlag.md) — 信任旗开着，字幕说信得过。

## 脚本控制流

- [出门办事再回来](Call.md) — 走到驿站办完事再回到原点，像叫了一声子程序。
- [办完事交回](Return.md) — 子程序结束，人回到主线。
- [把定数管线跑一遍](InvokeScript.md) — 叫另一张图来算出 7。
- [把数字挪到下一个格子](MoveInt.md) — 3 被挪到结果槽，字幕报 3。
- [没满就再续一杯](JumpIfFalse.md) — 茶没满时继续续，满了才停。头顶那根条是水位示意，不是血量。
- [算出一个整数就收工](HaltReturnInt.md) — 管线算出 7 就停，字幕报 7。
- [续一杯，歇一口气](Yield.md) — 每续一杯就停一拍：人影顿一下，杯里水涨一格，三格满就完。
- [跳过这一口](Jump.md) — 茶杯已经满了，直接跳到收束，不再续杯。

## 黑板与配置

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
