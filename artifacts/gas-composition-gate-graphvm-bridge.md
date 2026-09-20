# GAS Composition Gate - Graph Runtime Trigger Bridge

- **Task / Issue**: Graph runtime `FireEventKey` bridge hardening
- **Date**: 2026-08-26
- **Agent / Author**: Codex with PI Opus review

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**。

结论: **PASS**

一句话理由: 本次只收紧已有 Graph Runtime API 到生产 `TriggerManager.FireMapEvent` 的 scope 和事务合同，没有新增 profile enum、平行事件管线或配置 schema。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| `FireEventKey` host bridge | Layer 0 service boundary | `GasGraphRuntimeApi` -> `TriggerManager.FireMapEvent` |
| 事件 key 名称解析 | Layer 0 registry lookup | `ConfigKeyRegistry` |
| map/source context 组装 | Layer 2 graph execution context | `ScriptContext` + existing payload keys |

## 3. Reuse list

- Handlers: existing `GasGraphRuntimeApi` and `ConfigKeyRegistry`.
- Queues / Systems: existing `TriggerManager` map dispatch; no new queue.
- Resolvers / Registries: existing `MapEntity`, `MapId`, and graph side-effect transactions.
- Existing presets / graphs: existing graph runtime API; no new opcode or graph asset.

## 4. New Layer 0 ops

N/A. No new atomic op is introduced.

## 5. Transaction boundary

`FireEventKey` validates manager, key, entity liveness, map ownership, and map id before dispatch. It is rejected during derived-attribute writes and during an effect phase transaction because trigger dispatch has no staging path.

## 6. Config SSOT

Behavior remains in the existing config-key registry and graph runtime contract. No new JSON schema is added.

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback；无效 scope、未知 key、未绑定 bridge 均明确抛错

## 8. Next variant test

「下一个 Mod 变体」将修改 graph 连线或 effect 步骤，不新增 Core enum、profile 开关或第二条事件分发管线。

## Call-site audit

2026-08-26 source scan found `FireEventKey` only in the graph API contract, its host implementation, and focused tests. No graph opcode dispatch, JSON graph asset, Mod, or other host call currently depends on the removed invalid-scope-to-global fallback. `SpawnTemplate` and other existing map-aware APIs continue using their own optional `ResolveMapId` contract.
