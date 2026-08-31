## GAS Composition Gate — Self Review

- **Task / Issue**: #1410 / #1411 / #1412 / #1413 graph audit harden
- **Date**: 2026-08-31
- **Agent / Author**: cloud-agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（收紧既有 Kind 策略与作者白名单，无新 op）

结论: PASS

一句话理由: 不新增 profile/enum/开关；把误标 Pure 的副作用 op 从只读 Kind 白名单挪走，并让 Register 与 FrontDoor 共用 AuthorableKinds 闸。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 副作用作者掩码 | 1 | GraphOpDescriptorTable.Data |
| 只读 Kind 策略 | 1 | GraphOpDescriptorTable.IsPolicyAllowed |
| route:global 退役 | 2 TriggerGraph 挂载 | TriggerGraphMount.ParseRoute |
| 宿主寄存器清扫 | 2 BT/FSM host | ClearRegisters |

### 3. Reuse list

- Handlers: 既有 GasGraphOpHandlerTable（元数据暂仍 Pure，供 Script 策略）
- Queues / Systems: 无
- Resolvers / Registries: GraphProgramRegistry.ValidateProgram
- Existing presets / graphs: 现网副作用均在 TriggerGraph/Effect，无需迁资产

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无新事务边界；登记失败关闭。

### 6. Config SSOT

行为配置落在: graph descriptor / mount parse / showcase.registry.json

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线
