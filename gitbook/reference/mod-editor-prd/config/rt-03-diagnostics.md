# rt-03 配置说明 · 诊断与错误码

> 配置写法与行为。第一性需求见 [rt-03 PRD](../prd/rt-03-diagnostics.md)；编辑器需求见 [UXD](../uxd/rt-03-diagnostics.md)；现状见 [reference](../reference/rt-03-diagnostics.md)。

## 1. 示例配置

诊断与错误码是引擎合同，**无 mod 配置文件**。作者接触到的只有错误码字面量本身（异常信息里的稳定格式）：

```text
GAS.GAMEPLAY_EVENT_BUS.ERR.CapacityExceeded: capacity=4096, tagId=17.
GAS.DIAGNOSTICS.ERR.BufferCapacityExceeded: frame=1042, capacity=32.
```

## 2. 作者可配什么与在哪配

| 想控制什么 | 在哪 | 说明 |
|---|---|---|
| 触发哪类诊断 | 改自己的效果/技能/订单配置 | 诊断是结果不是开关——预算与容量怎么配（见 rt-02）决定会看到什么 |
| 诊断缓冲容量 | 无入口（引擎默认） | 引擎内部默认容量，mod 不可配 |
| 错误码 | 不可配 | 码是引擎合同；集中字典尚在治理（见 spec-runtime 治理项 R2） |

规则表——九域各自报告什么：

| 域 | 报告的典型指标 |
|---|---|
| ResponseChain | 创建丢弃/深度丢弃/步预算熔断/队列溢出 |
| TagContainer、ActiveEffectContainer、PhaseListener、GameplayEventBus | 各自容器/注册/派发/总线丢弃 |
| OrderAdmission | 结果溢出/backlog/高水位/八种拒绝原因 |
| EffectProposal、EffectLifetime、EffectApplication | 提案、生命周期、应用侧丢弃与计数 |

## 3. 文件结构

无文件。诊断事件与错误码全部来自引擎运行时，消费面是编辑器与工具（见 UXD）。

## 4. 运行时加载效果

引擎装配期创建诊断缓冲并注册为服务；帧首清零、逐帧发布非零指标；mod 加载期若触发错误码（注册失败、超上限等）以启动失败形式出现。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 诊断缓冲满 | 抛错带容量与帧号（诊断通道 fail-fast） |
| 订单溢出计数回退 | 发布专用诊断错误 |
| 任何 GAS.*.ERR.* 字面量 | 异常信息携带稳定码——可作为工单/检索键 |

## 6. 实例

- 预算类诊断来源：见 rt-02 配置说明
- 事件总线丢弃类：见 rt-05 配置说明

**相关文档**：[rt-03 PRD](../prd/rt-03-diagnostics.md) · [rt-02 配置说明](rt-02-budgets.md) · [UXD](../uxd/rt-03-diagnostics.md)
