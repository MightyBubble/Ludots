# GAS Composition Gate — Self Review

- **Task / Issue**: Epic #990 family 9 — 黑板与配置 13 op 零字幕重设计（BlackboardNodeDriver / AttrNodeDriver C 档演后果）
- **Date**: 2026-08-17
- **Agent / Author**: pi (epic/990-zero-caption-gallery)

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 已有 graph op 的连线/参数组合 + 驱动器可见化

结论: PASS

一句话理由: 本家族没有新增任何 graph 节点或 profile 开关；全部行为变化都是「图 JSON 里把已有 op 连成尾巴」（cfgF→neg→hit / src→readF→neg→hit / cfgFx→explicit→applyDyn / beginTx→materialize），driver 只做断言升级与可见化。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| LoadConfigFloat 真结算 | Layer 2 | 图尾 `cfgF→neg→explicit→hit`（NegFloat+ModifyAttributeAdd 组合） |
| ReadBlackboardFloat 真结算 | Layer 2 | 图尾 `src→readF→neg→explicit→hit` |
| LoadConfigEffectId 悬空输出修复 | Layer 2 | 图重写 `cfgFx→explicit→applyDyn`（ApplyEffectDynamic 端口照抄 ApplyEffectDynamic.json） |
| BeginLifecycleTransaction 造身 | Layer 1+2 | 图加 `materialize`（InvokeBuiltin MaterializeTemplate，复用 Effect.GraphOps.Lifecycle 的 targetEntityTemplate） |
| InvokeBuiltin 正式演员 | Layer 2 | LastMaterializedTarget 经 GraphOpsStageVisuals.BindMapEntity 绑正式演员 |
| 记事板/配置册/情境信封/台账 可见化 | Layer 3 | BlackboardNodeDriver.DrawOverlay 用共享视觉原语 P1-P11 |

## 3. Reuse list

- Handlers: `InvokeBuiltin`(MaterializeTemplate / ClearActiveEffects)、`ApplyEffectDynamic`、`ModifyAttributeAdd`、`NegFloat`、`LoadExplicitTarget`、`LoadConfig*`、`Read/WriteBlackboard*`（全部已有 op）
- Queues / Systems: `EffectRequestQueue`（ApplyEffectDynamic 真结算）、runtime `SettlePendingEffectRequests`
- Resolvers / Registries: `ConfigKeyRegistry`、`EffectTemplateIdRegistry`、`EffectTemplateRegistry`、`BuiltinHandlerRegistry`、`GraphOpsNodeActorBinding`
- Existing presets / graphs: `Effect.GraphOps.Config`（power=40/tier=2/chainEffect=Strike）、`Effect.GraphOps.Lifecycle`（targetEntityTemplate=GraphOps.Ally）、`Effect.GraphOps.Strike`（-18）、ApplyEffectDynamic.json 端口模式、MulFloat.json graphSettled 图尾模式

## 4. New Layer 0 ops (if any)

N/A

## 5. Transaction boundary

BeginLifecycleTransaction 的真实回滚由 lifecycle 执行器承担；演出只画真实发生的三拍（Begin 执行拍 / 记账拍 / 关账拍），不为演出伪造失败事务。

## 6. Config SSOT

行为配置落在: effect template（Effect.GraphOps.Config / Effect.GraphOps.Lifecycle）+ graph 连线。未新增 JSON schema。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

## 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（换 config 值 / 换 effect 票即换伤数与效果）
