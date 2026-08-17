# fx-23 · 视野揭示

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-19-vision.md)；编辑器需求见 [UXD](../uxd/fx-19-vision.md)；引擎实现见 [runtime spec](../spec-runtime/fx-19-vision.md)；editor spec 见 [editor spec](../spec-editor/fx-19-vision.md)；现状见 [reference](../reference/fx-19-vision.md)。

## 1. 定位

revealArea 让效果向知识区域揭示一片圆形视野：半径、层、记忆时长与探测强度共同决定"谁看见哪里、记多久"。

## 2. 产品承诺

- **通用块**：无专属 preset，revealArea 可挂任意模板；限 Instant 与 After 两种生命周期，After 必须带正周期做刷新。
- **范围合同**：radius 必须为正；scope 必须是已注册的作用域名；layers 至少一层且不超上限（见事实页）。
- **记忆合同**：memoryTtlTicks 非负（0 即不留记忆）；detectionStrength 取 0..255。
- 揭示中心不可解析时跳过本次，不炸效果。
- **现状边界**：揭示处理器未通过原子域认证，任何挂载本块的模板都无法通过启动计划编译（治理见 spec E14）。

## 3. 运行行为

揭示写入 viewer 的知识区域；After 生命周期按周期重复揭示；移除相位经 DecayRevealArea 回收；记忆由视野运行时按 TTL 衰减。

## 4. 异常承诺

半径非正、scope 未注册、层数越界、强度越界、After 无周期——启动失败并指明字段；处理器现状在计划编译期被拒。

**相关文档**：[配置说明](../config/fx-19-vision.md) · 见 infra-03（视野与相机）
