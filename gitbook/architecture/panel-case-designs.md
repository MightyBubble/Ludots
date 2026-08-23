# 面板典型案例全设计

35 案已按 wiki 结构拆为一案一文：[`architecture/panel-cases/`](panel-cases/README.md)（门户"面板矩阵"页同源）。本页保留总览与覆盖矩阵；**案内容唯一源在 panel-cases/**，请勿在本页重复维护。

- [前四案（全设计：纯展示/交互全链/模态浮层/零变量命令）](panel-cases/README.md)
- [其余 31 案（七组）](panel-cases/README.md)
- 总合同：[面板目录设计](panel-catalog-designs.md) · 上手：[快速上手](panel-quickstart.md)

## 四案覆盖矩阵

| 维度 | A 聚合 | B 时间控制 | C 设置 | D 全局指令 |
|---|---|---|---|---|
| scope=global (G3) | ✅ | ✅ | ✅ | ✅ |
| 变量/binds | 有+realtime | 有（回读） | 有（回读） | **无（G6）** |
| events/intents | 无 | click+载荷 | **change 连续+合流** | click 无载荷 |
| 显隐 | 常驻 | 常驻 | **模态编排（开/关图）** | 互斥编排 |
| 锚点 | topLeft | topRight | **modal.center（G5）** | bottomCenter |
| 手势载荷/args 常量（G8） | — | ✅ | ✅(change) | — |
| actorSource none（G9） | — | — | ✅ | ✅ |
| 图消费 UI 事件（G10） | — | — | ✅(开/关编排) | ✅(互斥编排) |
| 溯源形态 | 图聚合输出 | 图输出回读闭环 | 图输出+连续意图 | 纯意图 |

---

