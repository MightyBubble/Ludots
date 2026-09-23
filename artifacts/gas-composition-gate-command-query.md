## GAS Composition Gate - Self Review

- **Task / Issue**: #1084 Control Query Graph first execution slice
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 这条切片只复用现有 `GraphKind.Query`、`GraphProgramRegistry`、`GraphExecutor` 和 caller-owned target buffer；没有新增 GraphKind、专用 executor、profile enum 或平行配置管线。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| Query kind/副作用 authoring gate | 0 | `GraphOpDescriptorTable` / `GraphKindOperationPolicy` |
| 显式 subject 绑定与存活校验 | 0 | `GraphExecutor.ExecuteQuery` |
| EntitySet 输出 | 0/1 | `GraphFrame.TargetList` 写入调用方 span |

### 3. Reuse list

- Handlers: 现有 `GasGraphOpHandlerTable`。
- Queues / Systems: 无新增队列；执行复用现有 Graph VM。
- Resolvers / Registries: `GraphProgramRegistry`、`GraphOpDescriptorTable`、`GraphKindOperationPolicy`。
- Existing graphs: 现有 `GraphKind.Query` 程序合同。

### 4. New Layer 0 ops

N/A。`ExecuteQuery` 是既有 `GraphExecutor` 的调用入口，不是新的 graph opcode。

### 5. Transaction boundary

Query 不写世界、集合 Store、选择、命令或 UI；结果以 caller-owned buffer 一次性返回。容量不足由既有查询合同显式报错，不静默截断。

### 6. Config SSOT

行为配置落在现有 Query graph JSON / `GraphProgramRegistry`。没有新增 JSON schema。

### 7. Red flag scan

- [x] 未新增 GraphKind。
- [x] 未新增 `GraphQueryExecutor` 或平行 registry。
- [x] 未让 Query 写入 `EntityCollectionStore`。
- [x] 未接入 Selection、CommandSource、InputOrderMapping 或 TriggerGraph。
- [x] 未添加 fallback。

### 8. 明确未覆盖

- Query 标量结果的统一 caller-owned ABI 尚未在本切片扩展。
- Control Query 首版仍需后续 validator 拒绝 `QueryFromCollection`，不能把通用 Query authoring 直接当作最终 Control Query 合同。
- TriggerGraph -> Query 调用门属于 #1099；在该门落地前，showcase 不能宣称完成 Query 主链。
- 八类游戏的可玩 showcase 仍按 #1102 单独验收；ViewMode/文案切换不计为覆盖。

### 9. Next variant test

下一个变体修改 **graph 连线 / Query graph 组合**，不修改 Core enum、profile 开关或新运行时管线。
