# Region Volume 教科书 showcase 设计

> 状态：**已实现，headless 真机验收通过**（`RegionVolumeTextbookAcceptanceTests`，真实 GameEngine 装载与心跳驱动）。可玩交付闸门中的交互式验收（launcher 启动 + Agent Bridge 操作取证）尚未执行，本文档随验收补齐。

## 一句话与目标用户

「部队踏进 xx市，一支伏兵刷出，且只刷一次」——写给要用区域触发做关卡设计的 mod 作者，五个文件零代码。

## 主循环

- **谁改变世界**：部队（带 `Unit.Army` tag）与斥候（无 tag）两支实体在地图上移动（测试夹具驱动位置；人工玩时走输入/命令）。
- **用户看到什么变**：部队进入城市多边形 → `RegionEntered(xx_city)` → 图体闸门判定 → `SpawnTemplate` 刷出两个 goblin；再次进入 → `ambush.blocked` 计数 +1、不再刷。
- **惊喜时刻**：斥候先进城——什么都没发生（体积的 `entityTags` any-of 过滤把无标签单位整个吞掉）；换部队进城立刻触发。

## 消融对照

同一张图里并存的对照组：**有 tag 的部队 vs 无 tag 的斥候**穿过同一区域（过滤层消融）；**第一次 vs 第二次进入**（图体一次性闸门消融，`ambush.fired`/`ambush.blocked` 两个 map variable 就是 A/B 读数）。

## 解释层

- `ambush.fired` / `ambush.raiders` / `ambush.blocked` 三个地图变量即 HUD 读数（正式运行状态，非测试回调）。
- 区域几何、tag 过滤、发射合同全部是模板/地图明文数据，改 JSON 即改语义。

## 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|------|------|----------|
| 区域形状 | 模板 `RegionVolumeCm.shape`（circle/rect/凸 polygon/segment） | 触发区几何如何声明 |
| 区域位置 | 地图 `PositionXCm/PositionYCm` 摆放 | 锚点兜底：体积随摆放走 |
| 谁触发 | `RegionVolumeTagFilterCm.tags` | any-of 过滤语义 |
| 一次性粒度 | 图体闸门链（按过境次数/换成按 SourceEntity 记名单） | UE DoOnce 同款图体组合，入口 once 之外的正规做法 |
| 刷什么刷哪 | `SpawnTemplate.template` + 坐标/锚点 | 波次内容数据化 |

（形状/位置/内容为配置面旋钮；运行时旋钮待交互式验收时以面板/命令接入。）

## 场景结构

单场景 `xx_city_ambush`：城市多边形体积（2800,1400 锚点）、部队、斥候、goblin 营地（team 2 关系代表）、触发图 `Graph.Textbook.Ambush`。

## 门户资产

本页即门户文档；acceptance 证据在 `src/Tests/GasTests/Production/RegionVolumeTextbookAcceptanceTests.cs`；截图/录屏随交互式验收补。

## 反向 API 审计

- 需要「按 VolumeKey 查询目录」→ 已有（`LoadPlacedRegion`、`filters.region` 挂载校验）。
- 需要「图体内读过境实体」→ 已有（`LoadEntryPayloadEntity` + `MapTrigger.SourceEntity`）。
- 需要「图体一次性/可重置流量控制糖」→ **已落地**（#1467 DoOnce 编译期糖）：教科书 ambush 图的闸门即 `DoOnce` 节点（首过 true 臂刷怪、后续 false 臂计数），Reset ≡ WriteMapVarInt(var, 0)。

## 交付边界与完成判据

- 本次实现：mod（模板/地图/图/清单）、注册表条目、headless acceptance（真机装载链路）。
- 待补：launcher 交互入口与 Agent Bridge 取证（可玩闸门 6/7）、运行时旋钮面板、门户截图。
- 教科书要点：入口级 `once` 是关卡导演糖（per-entry、不可重置）；正规的一次性/可重置/按实体粒度控制流在图体内组合——本图即为标准写法。
