# Composition Judgment Standard

## 核心问题（必答）

为本次需求新增能力时，主要交付物是什么？

| 答案 | 判定 | 行动 |
|------|------|------|
| A. 新的 graph 节点、effect 步骤、或已有 op 的连线/参数 | **通过** | 继续自审；优先 Layer 2 组合 |
| B. 新的 profile 字段、inherit mode、placement enum、preset 内声明式开关 | **不通过** | 停止实现；拆 atomic op 或开重构 issue |
| C. 新的整条并行管线（第二个 Morph/Spawn DSL） | **不通过** | 停止；合并进 Layer 0/1 |
| D. 说不清 | **阻塞** | 补发现；不得编码 |

## 辅助问题

1. **事务边界**：哪些步骤必须 all-or-nothing rollback？（仅 Layer 1 壳承担）
2. **坐标 SSOT**：是否复用 `EffectTargetPointResolver`？placement 校验是否在 propose 阶段？
3. **物化 SSOT**：是否复用 `EntityBuilder` + `PerformerEntitySpawnBootstrap`？是否新建第二套物化？
4. **配置 SSOT**：行为是 `effect template` + `graph` 组合，还是新 JSON schema？
5. **变体扩展**：下一个 Mod 变体改连线还是改 Core enum？

## 通过条件（全部满足）

- [ ] 新变体可用 **op 组合** 表达，无需新 enum
- [ ] 未新增平行 profile loader / registry（除非 Layer 0 op 注册表且职责单一）
- [ ] 已列出复用 handler / queue / resolver
- [ ] 事务与组合职责分离
- [ ] 文档更新路径明确（`gitbook/`，非 `docs/adr` 平行 ADR）

## Morph 反例（写入记忆）

| 需求 | 错误做法 | 正确做法 |
|------|----------|----------|
| deploy 清 effect | `inherit.effects.mode: StripAll` | effect 链加 `ClearActiveEffects` |
| deploy 继承血量 | `inherit.attributes.mode` | `CopyAttributeSlice` 步骤 + 参数 |
| deploy 落点 | `placement: AtTargetPoint` in profile | `EffectTargetPointResolver` + propose 校验 |
| 新 deploy 变体 | 新 morph profile id | 新 graph 连线或 effect 链副本 |
