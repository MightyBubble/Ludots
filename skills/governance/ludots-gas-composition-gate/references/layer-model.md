# Entity Lifecycle Layer Model

```text
Layer 3  Preset          DeployConsumeSource — 给人抄的封装名
Layer 2  Composition      Effect chain / Graph program — Mod 可改
Layer 1  Transaction       Ordered ops + rollback — 薄 executor
Layer 0  Atomic ops       Materialize / Consume / TransferStableId / Copy* / Clear*
```

## Layer 0 原则

- 一个 handler 一件事
- 无业务命名（无 deploy、morph、RTS）
- 可无头测试

## Layer 1 原则

- 只保证顺序与 rollback
- 不解析 inherit.mode

## Layer 2 原则

- 数据驱动行为差异
- SSOT 在 effect template + graph assets

## Layer 3 原则

- 可选糖衣
- 编译到 Layer 2，无独立运行时解释器

## 设计 SSOT

`gitbook/architecture/entity-lifecycle-atomic-ops.md`
