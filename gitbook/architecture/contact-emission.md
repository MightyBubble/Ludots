# 接触发射合同（传感器桥）

> 状态：**引擎能力已落地**（`ContactEmissionTap`，#1469）；纯数据物理 showcase 待物理体模板授权（见后续票）。

## 是什么

物理接触（压力板/绊线语义：kinematic×static SensorOnly 配对的 Begin/End 边沿）接上区域触发的统一发射合同：实体模板里一个组件，接触开始/结束就发你声明的事件——和 RegionVolume、场图层区域**同一份合同、同一个事件出口（`RegionEmissionFiring`）、同一套词典与 schema 校验**。

## 配置（实体模板 components）

```json
"RegionVolumeEmissionCm": { "enter": "demo.plate.pressed", "exit": "demo.plate.released",
                            "payload": { "plate.zone": "gate" } }
```

- 前提：实体带 `ContactEventEmitter2D`（+ 所在层进入 `Physics2D/kinematic.json` 的 `contactEventEmitterLayers` 白名单）——发射合同不豁免物理侧的门槛。
- 接触对方实体随 `MapTrigger.SourceEntity`；静态 payload 按 schema；装载期 bake 校验（未知事件/保留键/schema 不符 fail-closed）。
- 订阅与区域事件完全同构：TriggerGraph 入口写 `event: "demo.plate.pressed"`，`mapId → eventKey → 订阅者` 两级字典精确投递，入口过滤器照常生效。

## 与 RegionVolume 的分工

| | RegionVolume | 接触发射 |
|---|---|---|
| 判定 | 包含（+扫掠防隧穿） | 物理窄相接触 |
| 物理作用 | 无（纯谓词） | 有（真碰撞体） |
| 节拍 | think-wave | 物理步 |
| 用途 | 经过/驻留区域 | 压力板、物理绊线 |

## 边界

- 无 `RegionVolumeEmissionCm` 的接触方零成本（一次组件 TryGet）。
- 层消费者（`IContactEventConsumer2D`）通道保留——宿主集成的代码路径，与发射合同并行不悖。
- 纯数据物理 showcase 依赖"物理体模板授权"（Position2D/Collider2D/Mass2D 的 ComponentRegistry setter），该能力未落地前，物理实体由代码建（CrowdPhysicsArena 模式）。
