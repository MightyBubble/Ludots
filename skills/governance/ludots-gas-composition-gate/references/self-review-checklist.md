# GAS Composition Self-Review Checklist

复制以下模板，在开工前填写。PR 描述或 `artifacts/gas-composition-gate.md` 必须包含完整填写版。

```markdown
## GAS Composition Gate — Self Review

- **Task / Issue**:
- **Date**:
- **Agent / Author**:

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: ___

结论: PASS / FAIL / BLOCKED

一句话理由: ___

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| | | |

### 3. Reuse list

- Handlers:
- Queues / Systems:
- Resolvers / Registries:
- Existing presets / graphs:

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| | | |

（无则写 N/A）

### 5. Transaction boundary

必须原子 rollback 的步骤: ___

### 6. Config SSOT

行为配置落在: effect template / graph / catalog（路径）: ___

是否新增 JSON schema: YES / NO — 若 YES 说明为何不通过组合表达: ___

### 7. Red flag scan

- [ ] 未新增 profile inherit/placement enum
- [ ] 未新建与 spawn 平行的物化管线
- [ ] 未把 placement 校验塞进 lifecycle op
- [ ] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / Core enum（只能选前两项之一）

若选了 Core enum → FAIL
```
