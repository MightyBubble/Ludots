# 技能词条画廊 Wiki

每个已接通的技能合同一页：一场给玩家看的短剧，加一节给 mod 作者的写法。词条清单的单一事实源是 `scripts/ability_feature_catalog.py` 与宿主 `Vignettes/{Feature}.json`。

生成器：`scripts/generate-ability-feature-wiki.py`（从 catalog / vignette 生成，勿手改正文）。总规矩见 [Ability 词条画廊](../../architecture/ability-feature-gallery.md)。

英雄技能沙盒是把多招串成一栏的组合戏，不是词条入口。

## 时间轴动词

> 作者语义与全量字段见手册分册 [执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)。

- [点一下就打中](EffectSignal.md) — 施法者对着木桩出手，木桩血条马上掉一截。
- [火还在烧](EffectClip.md) — 出手之后火还挂在木桩身上，血条一格格往下掉。
- [自己身上挂一阵印](TagClip.md) — 出手之后施法者头顶先亮一枚印，过一会儿印自己掉。
- [给对面挂一阵印](TagClipTarget.md) — 出手之后木桩头顶亮一枚印，过一会儿印自己掉。
- [瞬间给自己打上印](TagSignal.md) — 出手那一下，施法者头顶立刻多一枚印，不会自己掉。
- [瞬间给对面打上印](TagSignalTarget.md) — 出手那一下，木桩头顶立刻多一枚印。
- [出手那一下敲响铃](EventSignal.md) — 出手那一下场上的铃亮了，字幕报铃响。
- [等你点头才打出去](InputGate.md) — 出手之后先停住等确认；确认一到，木桩才掉血。
- [铃响了才打出去](EventGate.md) — 出手之后先停住等铃；铃一响，木桩才掉血。
- [名单齐了才打出去](TargetCollectionGate.md) — 出手之后先停住等名单；名单一到，近处木桩才掉血。远处那根只是名单上的名字，这一场不挨打。
- [被晕就停手](InterruptAny.md) — 出手之后还没打中，施法者被晕，这一招停在半路，木桩不掉血。

## 同一招换数组

> 作者语义与全量字段见手册分册 [CallerParams 参数池 · ab-03](../mod-editor-prd/config/ab-03-caller-params.md)。

- [同一招两波不同力道](CallerParams.md) — 同一张效果票先轻轻一下，再重重一下，木桩掉两截不同的血。

## 放不放得出去

> 作者语义与全量字段见手册分册 [激活门 · ab-05](../mod-editor-prd/config/ab-05-activation-gates.md)。

- [身上有禁招印就放不出](BlockTagsBlocked.md) — 第一下挂上禁招印；印还在时再出手，字幕说放不出。
- [没有姿态印就放不出](BlockTagsRequired.md) — 没亮姿态印时出手放不出；印一挂上，木桩才掉血。
- [残血才打得中](ActivationPrecondition.md) — 对着满血木桩出手放不出；对着残血木桩出手，残血掉血，满血不动。

## 解锁才能用

> 作者语义与全量字段见手册分册 [激活门 · ab-05](../mod-editor-prd/config/ab-05-activation-gates.md)。

- [没解锁就放不出](UseRequirement.md) — 牌子还没点亮时出手放不出；牌子点亮之后，木桩才掉血。
- [没解锁栏上就没有](ShowRequirement.md) — 牌子点亮之前栏上只有一招；点亮之后，隐藏的那招才出现。

## 开关

> 作者语义与全量字段见手册分册 [开关 · ab-08](../mod-editor-prd/config/ab-08-toggle.md)。

- [再按一次关掉](ToggleSpec.md) — 第一下打开姿态印；再按一次，印灭掉。
- [关掉时补一刀收尾](ToggleDeactivateExec.md) — 打开时什么也不打；再按一次关掉，收尾那一刀打在木桩上。

## 打在谁身上

> 作者语义与全量字段见手册分册 [执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)。

- [这一刀打回自己](DispatchTarget.md) — 对着木桩出手，血条掉的是施法者自己，木桩不动。

## 技能自带的图

> 作者语义与全量字段见手册分册 [技能定义骨架 · ab-01](../mod-editor-prd/config/ab-01-definition.md)。

- [出手之后图跟着跑](TriggerGraphs.md) — 出手之后，技能自己带着的图跟着跑起来，字幕报图跑了。

## 换姿态换栏

> 作者语义与全量字段见手册分册 [形态路由 · ab-07](../mod-editor-prd/config/ab-07-form-sets.md)。

- [换姿态换一栏招](FormSet.md) — 先按换姿态，栏上那一格从炮打换成锤砸；再出手，木桩挨的是锤。
