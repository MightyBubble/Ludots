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

- [互相都认的朋友](RelationshipQueryMutual.md) — 双向都有链的人亮。
- [只看受信任的](RelationshipFilterFlag.md) — 信任旗开着的人亮。
- [好感平均多少](RelationshipAggAverageMetric.md) — 字幕报平均。
- [好感落在区间里的人](RelationshipFilterMetricRange.md) — 只留好感 30~80 的。
- [我主动交的朋友](RelationshipQueryOutgoing.md) — 从自己出发的链，亮出那些朋友。
- [我们有没有连着](RelationshipHasLink.md) — 有链显示连着，无链显示没连。
- [打上失和标记](RelationshipSetFlag.md) — 最弱那条链上插起失和旗。
- [把好感加总](RelationshipAggSumMetric.md) — 字幕报好感总和。
- [把最弱的那条链拆掉](RelationshipRemoveLink.md) — 好感最低的朋友断链，条掉光、人变灰。
- [按好感排个序](RelationshipSortByMetric.md) — 最高好感排前面，字幕点名第一。
- [最低好感是多少](RelationshipAggMinMetric.md) — 最弱那条。
- [最高好感是多少](RelationshipAggMaxMetric.md) — 字幕报最高值，对应的人条最满。
- [读出这个人的好感](RelationshipGetMetric.md) — 点名好友，字幕和好感条显示读到的数。
- [谁把我当朋友](RelationshipQueryIncoming.md) — 箭头指着自己的链亮。
- [谁是好感最低的人](RelationshipAggMinEntityByMetric.md) — 最弱的人被点名。
- [谁是好感最高的人](RelationshipAggMaxEntityByMetric.md) — 那个人被点名高亮。
- [这两人之间有没有链](RelationshipQueryBetweenPair.md) — 自己和某好友之间查到链。

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

- [两刀取更大的那一刀](MaxFloat.md) — 左边 12、右边 28，打出去的是更大的 28。
- [两刀取更小的那一刀](MinFloat.md) — 左边 30、右边 18，打出去的是更小的 18。
- [两段伤害叠在一起](AddFloat.md) — 左边先算出一刀，右边再叠一截。头顶那根条按总和往下掉，是把算式结果画上去的示意，不是结算出来的伤。
- [伤害拉长一半](MulFloat.md) — 20 的伤害段被拉长一半，原样留着影子，拉成多长就掉多少血。
- [伤害钳在上下限里](ClampFloat.md) — 算出来 90，但这一刀最多 40、最少 10，头顶那根条按钳住后的数往下掉，是把算式结果画上去的示意，不是结算出来的伤。
- [写死的一刀](ConstFloat.md) — 这一刀不读装备、不算距离，算式写死是 42。头顶示意条变成这个数，不是结算出来的伤。
- [减益翻成正数](NegFloat.md) — 减益 -8 翻成 +8 再打出去。
- [出手许可](ConstBool.md) — 这一刀有没有被允许打出去，看许可开关；开着才能打。
- [按距离摊薄](DivFloat.md) — 同样 40 点伤害，距离翻倍就摊成一半。
- [有没有打出暴击](CompareGtFloat.md) — 伤害 30 对比暴击线 15，过线就是暴击。
- [负面修正取绝对值](AbsFloat.md) — 修正是 -8，取绝对值变成 8 再叠上去。
- [距离把伤害削掉一截](SubFloat.md) — 走远了，50 点里被削掉 12 点，头顶那根条按剩下的数往下掉，是把算式结果画上去的示意，不是结算出来的伤。
- [这一刀带随机抖动](RandomFloat01.md) — 每次抖动不一样，血条每次掉的数不完全相同。

## 组合短剧

- [先把关系链接上](RelationshipEnsureLink.md) — 施法者和盟友之间出现友谊/好感链。
- [只点最近的几个](QueryLimit.md) — 圈里很多人，只留下前 3 个。
- [圈里每人挂一层](FanOutApplyEffect.md) — 圈中单位都被挂上状态，血条或字幕说挂上了。
- [圈里的人排个稳定顺序](QuerySortStable.md) — 同样距离时顺序不乱跳。
- [好感再加一截](RelationshipAddMetric.md) — 好感从 40 加到 70。
- [把好感写成指定值](RelationshipSetMetric.md) — 好感被写成 80。
- [把状态牌翻成面板字](LookupTagDisplayToken.md) — 选中的牌变成玩家能读的字。
- [按读到的模板打到点名目标](ApplyEffectDynamic.md) — 模板号不是写死的，读出来再打木桩。
- [按读到的模板打圈里所有人](FanOutApplyEffectDynamic.md) — 动态模板扇出。
- [摸一圈看看谁在近处](QueryRadius.md) — 施法者周围一圈亮起来，圈外的人不动。
- [读出当前状态牌](SelectTagInMask.md) — 从状态掩码里选出当前牌。
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

- [从情境取出出手的人](LoadContextSource.md) — 情境来源是施法者。
- [从情境取出额外那个人](LoadContextTargetContext.md) — 情境里还有一个关联目标。
- [从技能配置读出威力](LoadConfigFloat.md) — 配置写着 40，不是写死在图常数里。头顶示意条按这个数往下掉，不是结算出来的伤。
- [从技能配置读出阶位](LoadConfigInt.md) — 配置阶位 2。
- [从记事板读出威力](ReadBlackboardFloat.md) — 板上写着 35 点威力，读出来画在示意条上，不是结算出来的伤。
- [从记事板读出层数](ReadBlackboardInt.md) — 板上层数 4。
- [从记事板读出点名的人](ReadBlackboardEntity.md) — 板上记着木桩，读出来锁定他。
- [从配置读出要放的效果](LoadConfigEffectId.md) — 配置指着某效果再打出去。
- [开一笔生命周期事务](BeginLifecycleTransaction.md) — 先开账再做事，字幕说事务已开。
- [把层数写到记事板](WriteBlackboardInt.md) — 记下 4 层。
- [把点名的人写到记事板](WriteBlackboardEntity.md) — 木桩被记到板上。
- [把这一拳的威力记上板](WriteBlackboardFloat.md) — 35 落进威力格，格子亮了。
- [跑一个内置步骤](InvokeBuiltin.md) — 事务里先生成新身体，再清掉新身体上的残留效果。
