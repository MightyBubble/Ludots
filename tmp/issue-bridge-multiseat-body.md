## 来源

#1058 双 seat 分屏真机验收（[验收评论](https://github.com/MightyBubble/Ludots/issues/1058)）发现：引擎侧多座位链路已通，但 AgentBridge 的工具面仍停在 sole-seat 假设，双 seat 下三个工具拒服或失真，验收取证被卡。

## 残留消费方（验收已定位到调用点）

- [ ] **A（mod 层，挡面板验收）**：`mods/showcases/interaction/InteractionShowcaseMod/Runtime/InteractionShowcaseRuntime.cs:698` `RequireSolePossessedRep` 双 seat 抛错，其 knowledge 发布 / 面板初始化全部跳过——修复后补面板 per-seat 验收
- [ ] **B**：`src/Libraries/Ludots.AgentBridge/Tools/SessionTools.cs:21` 与 CameraTools 仍走 `ResolveAuthorityCamera`（多 seat 未迁移消费方），session.info / camera.control 失真
- [ ] **C**：`src/Libraries/Ludots.AgentBridge/Tools/SpatialProbeTools.cs:56` entities.pick 以 sole-seat 为知识门控 owner，双 seat 直接拒服
- [ ] **D**：`input.inject` 语义注入无 per-seat 路由（多 seat 下全局链按设计 dormant，注入被接受但无效）——加 seat 参数或按设备绑定路由

## 边界

- 工具协议加可选 seat 参数时保持向后兼容（缺省=sole seat 行为或首个 binding），桥工具零改动原则（#902 §3.5 端口模式）不破
- A 是 showcase mod 修复，B/C/D 是桥工具修复，可分 PR

## Acceptance criteria

- [ ] 双 seat 会话下：session.info 列出双 seat、camera.control 可指定 seat、entities.pick 按 binding rect 路由、input.inject 可指定 seat 注入
- [ ] InteractionShowcaseMod 双 seat 不再抛 sole-seat 断言，其面板/knowledge 正常初始化，#1058 面板 per seat 验收补齐
