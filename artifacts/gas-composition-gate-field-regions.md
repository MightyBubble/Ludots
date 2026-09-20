# GAS Composition Gate — Field Regions（区域实体物化 + 归属差分 + 过境事件）

Issue: #1177（分支 codex/issue-1177-mapfield-regions）

## 1. Core judgment

**PASS。** 本切片新增的是一条**场归属差分系统**（`FieldRegionMembershipSystem`）与**装载期物化器**（`FieldRegionMaterializer`），不新增 effect preset、不新增 profile enum、不新增 BuiltinHandler。区域实体是普通实体 + 组件（`MapEntity`/`RegionCm`/`RegionFootprintCm`），走实体既有生命周期与地图卸载清理，无新生命周期分支。判断标准对照：新变体是 op 组合（场数据 + 实体创建 + 差分维护 + 既有 TriggerManager 事件通道的组合），不是新 enum/开关。

## 2. Layer assignment

| 能力 | Layer | 实现载体 |
|---|---|---|
| 区域实体物化 | Core 系统 | `FieldRegionMaterializer`（烘焙期，`World.Create` + 组件） |
| 位置→区域 O(1) 点查 | Core 查询 | `RegionEntityIndex` 直连表 |
| 归属差分与名单 | Core 系统 | `FieldRegionMembershipSystem`（DeferredTriggerCollection 相位，cell 变才动） |
| 过境事件 | 既有事件通道 | `TriggerManager.FireMapEvent` + 新 EventKey `FieldRegionEntered/Exited`（与 circle/rect 线分线） |
| 名单投影 | 既有行集 | `EntityCollectionStore.Replace`（变更才写，writer=归属系统） |
| 死亡静默移出 | 既有回调 | `World.SubscribeEntityDestroyed`（对齐 MapHeartbeatClockSystem 模式） |

## 3. Reuse list

- `World.Create` / Arch 组件（零注册）；`MapEntity` 随图清理（`MapSession.Cleanup`）。
- 差分母本：`SpatialPartitionUpdateSystem`（缓存组件 + CommandBuffer + cell 变才动）；`RegionTriggerSystem`（inside 集合差分、死亡不是 crossing、FireRegionEvent 上下文构造）。
- 事件通道：`TriggerManager.FireMapEvent` + `MapTriggerEventPayloadKeys`（新增 `FieldLayer` 一个 payload key）。
- 名单存储：`EntityCollectionStore`（Explicit/Display，key 从层 key 派生：`collection.field.<layer>.members`）。
- 死亡回调：`World.SubscribeEntityDestroyed`（MapHeartbeatClockSystem 同款）。

## 4. New Layer 0 ops

无。本切片没有新增 graph 节点、effect 步骤或触发器算子；触发图侧消费 `FieldRegionEntered/Exited` 走既有 entry-trigger 挂载机制（`TriggerGraphMounting`），词汇表经 `CustomEventCatalog.BuildEngineKnownSet` 反射自动收录。

## 5. Transaction boundary

- 物化在地图装载期一次完成（烘焙期档位）；装载失败即地图加载失败。
- 差分每拍跑在 `DeferredTriggerCollection` 相位：组件采用 CommandBuffer 缓冲后 Playback；名单/事件在同一拍内对读者可见（拍边界一致）。
- 不动零成本：cell 未变的实体零写入、零事件（测试锁定）。

## 6. Config SSOT

- 层声明：`Fields/layers.json`（#1175 schema）。
- 追踪勾选：`FieldTrackedCm { LayerId }`——spawn 模板/资产声明实体追踪哪一层，是数据不是代码。
- 区号 → key：`RegionIdRegistry`（装载期排序注册，运行期可续注）。
- 集合 key：从层 key 派生（`collection.field.<layerKey>.members`），无硬编码语义。
- 引擎零业务词：所有 key 占位（测试 `FieldRegionMembershipTests` 用 layerX/r1/r2）。

## 7. Red flag scan

- [x] 未新增 profile enum / preset 开关
- [x] 未新增 BuiltinHandler / effect 步骤
- [x] 未新建第二套事件系统（走 TriggerManager，事件 key 与 circle/rect 线分立）
- [x] 未新建第二套实体生命周期（区域实体随 MapEntity 清理）
- [x] 未绕过写域合同（名单写入者唯一 = 归属系统）
- [x] 未引入轮询（cell 变才动；死亡走销毁回调，不全扫）
- [x] 零硬编码 id / 零业务语义
