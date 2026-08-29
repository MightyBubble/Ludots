# GAS Composition Gate — UI 面板归属三轴（owner / audience / surface）

- **Task / Issue**: #1058（UI per-seat owner 切片，SetPanelAudience graph op）
- **Date**: 2026-08-28
- **Agent / Author**: Codex（pi-1058-ui-triaxis worktree）

## GAS Composition Gate — Self Review

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A——新增 graph 节点 `SetPanelAudience`（op 464）+ 面板模板三轴声明字段

结论: PASS

一句话理由: hotseat 轮换的每个玩法变体 = 同一个 op 指向不同 panelType/seat 符号（改图不改码）；owner/audience 是数据声明字段，不是 preset 开关。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| SetPanelAudience（受众覆盖写入） | 0 | GraphNodeOp 464 → IGraphRuntimeApi → PanelActivationApi → UiPanelActivationStore |
| ownerKind / audienceSeats 声明 | 2 | 面板模板 JSON（PanelTemplateLoader 既有校验链） |
| 受众准入 admission | 2 | PanelEventDispatcher.FireFromSeat（模板受众 + 覆盖，拒绝回流 reason） |
| panel 级 per-seat surface 挂载 | 2 | PanelSeatSurfacePlacement + PanelPresentationSystem |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable.Register（ShowPanel/HidePanel 同链）
- Queues / Systems: UiPanelActivationStore（既有显隐 store，扩展受众覆盖 map；不新建平行 store）
- Resolvers / Registries: ConfigKeyRegistry（panelType/seat 符号解析）、PanelTemplateRegistry、PanelOpEncoding（CreatePanel 双符号打包先例）
- Existing presets / graphs: ShowPanel op 439 / HidePanel op 440（写入口形态先例）、SetInteractionMode op 463（最近新增先例）

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| SetPanelAudience | 覆盖/清除一个 panelType 的受众 | ShowPanel/HidePanel 只写可见性布尔；受众是另一个轴，无既有 op 可组合（CreatePanel 造实例、DestroyPanel 毁实例，都不是受众语义） |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无——受众覆盖是幂等单写（后写覆盖先写，clear 恢复声明受众），无多步事务。

### 6. Config SSOT

行为配置落在: 面板模板 JSON（ownerKind / audienceSeats）+ 图节点符号（panelType / panelSeat）+ UiPanelActivationStore（运行时覆盖，不进存档）

是否新增 JSON schema: NO——模板根字段加两个可选字段，走 PanelTemplateLoader 既有 unknown-field fail-fast 链；图节点加一个可选 `panelSeat` 字段，走既有文档/编译/符号链。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺省 ownerKind=seat / audienceSeats=all-seats 是既有行为的显式化，非新回退）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（换 panelType/seat 符号即可造新轮换玩法；ownerKind=participant/team 的运行期归因链落在面板事件线，不改本 op）
