# Interaction → EntityView → Order → MassNavigation 边界

Epic: [#522](https://github.com/MightyBubble/Ludots/issues/522)  
RFC: [`RFC-0061-interaction-view-order-massnav-boundary-unification.md`](../../docs/rfcs/RFC-0061-interaction-view-order-massnav-boundary-unification.md)

## 目标数据流

```text
CastProfile (ScreenBox / ScreenPoint / …)
  → EntityCollectionStore
    → EntityView profile (viewKey → collectionKey + role)
      → OrderQueue（唯一 order intake）
        → OrderBufferSystem
          → MassNavigationOrderIngestionSystem
            → NavGroupRuntime → MassNavigationFlowSolver
```

## 域边界

| 域 | 拥有 | 禁止 |
|----|------|------|
| **Interaction** | Cast profiles、acquisition collections、pointer semantic | OrderBuffer、NavGroup、Performer |
| **EntityView** | viewKey → collection + role 配置 SSOT | Raw input、OrderBuffer |
| **Order** | OrderQueue intake、mapping、OrderBuffer | MassNav solver、screen hit test |
| **MassNavigation** | Agent binding、ingestion、NavGroup、Flow、ECS writeback | AuthoritativeInput、SelectionRuntime、SubmitOrder from input |

## 三条铁律

1. **OrderQueue 统一 intake** — 生产路径禁止 bypass `OrderQueue`
2. **MassNav 不读 Input / Selection** — 只消费 OrderBuffer 中已提交的 movement order
3. **命令目标集 SSOT 是 EntityView** — UI acquisition 只 promote EntityView collection；不 dual-write `SelectionRuntime`

## 关键实现锚点

| 职责 | 类型 | 路径 |
|------|------|------|
| EntityView 配置 SSOT | `EntityViewRuntimeConfig` | `mods/LudotsCoreMod/assets/game.json` → `entityViews` |
| UI acquisition | `CurrentSelectionApplySystem` | `src/Core/Input/Selection/` |
| Command semantic | `CommandInteractionSemanticSystem` | `mods/CoreInputMod/` |
| MassNav move intake | `MassNavigationMoveOrderSourceSystem` | `src/Core/Input/Orders/`（CoreInputMod 注册） |
| MassNav execution | `MassNavigationOrderIngestionSystem` | `src/Core/MassNavigation/Systems/` |
| Selection marker 桥接 | `EntityViewDisplaySelectionPresentationEventSystem` | `src/Core/Presentation/Systems/` |

## Selection 退役范围

- **已退役：** acquisition dual-write；MassNav 读 Selection/Input 提交 move order；`MassNavigationLocalCommandInputSystem` / `MassNavigationSelectionSyncSystem`
- **仍保留（legacy）：** `SelectionRuntime` 容器模型、显式 mod 视图绑定、order snapshot lease、部分 presentation 消费者

深度材料：`docs/architecture/entity_selection_architecture.md` · 数据流图：`docs/reference/ludots-selection-order-dataflow.html` · MassNav 链路：`../reference/mass-navigation-formal-chain.md`
