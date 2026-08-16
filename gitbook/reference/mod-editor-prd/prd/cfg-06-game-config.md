# cfg-06 · 游戏配置

> 产品承诺 · 已冻结。理想实现见 [cfg-06 spec](../spec-runtime/cfg-06-game-config.md)；现状见 [cfg-06 reference](../reference/cfg-06-game-config.md)。

## 1. 定位

`game.json` 是**游戏实例级的运行配置**：开多大窗口、启动哪张地图、谁是本地玩家、跑多少帧、各运行时容量给多少。它与内容配置（技能、效果、地图数据）是两回事——一个是"这台机器这次怎么跑"，一个是"游戏里有什么"。

## 2. 字段与行为

| 分组 | 字段 | 类型 | 配置后的行为 |
|---|---|---|---|
| 启动 | `defaultCoreMod` | string | 默认核心 mod 名 |
| 启动 | `startupMapId` | string | 启动时加载的地图 |
| 启动 | `startupLocalPlayerId` | int | 本地玩家编号 |
| 启动 | `startupInputContexts` | string[] | 启动即激活的输入上下文 |
| 窗口 | `windowWidth` / `windowHeight` | int | 窗口尺寸，默认 1280 / 720 |
| 窗口 | `windowResizable` / `windowStartMaximized` | bool | 可缩放 / 最大化启动 |
| 窗口 | `windowTitle` | string | 窗口标题 |
| 帧率 | `targetFps` | int | 目标帧率，默认 60 |
| 仿真 | `simulationBudgetMsPerFrame` | int | 每帧仿真时间预算（毫秒），默认 4 |
| 仿真 | `simulationMaxSlicesPerLogicFrame` | int | 每逻辑帧最大切片数，默认 120 |
| 世界 | `gridCellSizeCm` | int | 网格单元边长（厘米），默认 100 |
| 世界 | `worldWidthInMacroTiles` / `worldHeightInMacroTiles` | int | 世界宏格数，默认 64 / 64 |
| 物理 | `physics2D.enabled` | bool | 2D 物理开关 |
| 容量 | `gasRuntimeCapacity.*` | int 组 | GAS 运行时 17 项容量（队列、快照、扇出等），是性能合同；两项交叉约束：准入结果容量 ≥ 订单队列 × 2、准入拒绝容量 ≥ 订单队列 |
| 表现 | `presentation` | 对象 | 表现运行时配置；**必填** |
| 日志 | `logging` | 对象 | 日志通道配置 |
| 浏览器 | `browserRuntime` | 对象 | 内嵌浏览器运行时（编辑器 UI 依赖） |
| 常量 | `constants.*` | 表 | 五张数据驱动常量表：订单类型号、响应链订单号、属性名表、通用整数、通用字符串 |

## 3. 文件结构

`game.json` 不走配置目录：引擎默认一份（`Core:Configs/game.json`），各 mod 可带一份，全部深合并成单一运行配置——对象递归合并，标量与数组后到者覆盖。后加载的 mod 改窗口标题、换启动地图都是合法用法。

## 4. 预期反馈

- **启动期**：合并一次即定形；窗口、地图、本地玩家、预算按合并结果生效。
- **运行期**：容量项决定各子系统队列与快照上限；常量表供引擎与 mod 查编号、共享名字。
- **编辑器内**：作为"项目设置"页数据源，按上表分组渲染。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `presentation` 缺失 | 启动失败 |
| 容量字段非正数或为无限 | 启动失败，指明字段 |
| 交叉约束不满足（如准入结果 < 队列 × 2） | 启动失败，指明字段与要求 |
| 字段类型错误 | 启动失败 |

## 6. 编辑器要点

- **项目设置表单**：与内容编辑完全分区；新建项目时生成"引擎默认 + 项目 mod"两份骨架，让基线与定制从第一天分清。
- **容量页带预算提示**：17 项容量展示含义与交叉约束，改动前提示影响面。
- 热应用级别：全部字段为重启级。

## 7. 实例

- 引擎默认基线：`assets/Configs/game.json`（窗口、仿真预算、17 项容量、世界参数）
- 项目级覆盖示例由 reference 篇维护

**相关文档**：[cfg-06 spec](../spec-runtime/cfg-06-game-config.md) · [cfg-06 reference](../reference/cfg-06-game-config.md) · [cfg-05](cfg-05-config-pipeline.md)（一般配置合并规则对照）· rt-02（容量的运行时语义）
