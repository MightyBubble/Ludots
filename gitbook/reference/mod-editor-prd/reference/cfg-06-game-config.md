# cfg-06 reference · 游戏配置

> 现状参考。第一性需求见 [cfg-06 PRD](../prd/cfg-06-game-config.md)；配置说明见 [cfg-06 配置说明](../config/cfg-06-game-config.md)；目标实现见 [cfg-06 runtime spec](../spec-runtime/cfg-06-game-config.md)。

## 1. 现状快照

- game.json 走专用合并入口，不经配置目录：收集全部来源片段后深合并（对象递归、标量与数组后到者覆盖）并反序列化为运行配置对象。
- presentation 必填在引擎装配处硬校验，缺失即抛。
- 容量配置 `gasRuntimeCapacity` 共 17 项（项数以事实页为准，含效果请求队列容量），全部要求为正（两项工作预算另校验有限），另有两项交叉约束：订单准入结果容量 ≥ 订单队列容量 × 2、订单准入拒绝容量 ≥ 订单队列容量；违反启动期抛错并带字段路径。
- 常量表五张字典：orderTypeIds、responseChainOrderTypeIds、attributes、intValues、stringValues。
- 引擎默认一份（仓库 `assets/game.json`），各 mod 按加载顺序覆盖；无编辑器表单。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 运行配置对象全字段（窗口/启动/仿真/世界/物理/表现/常量） | src/Core/Config/GameConfig.cs:14-70 |
| 容量配置与校验（17 项 + 交叉约束 :146-159） | src/Core/Config/GameConfig.cs:77-213 |
| 常量表五字典 | src/Core/Config/GameConfig.cs:242-270 |
| 专用合并入口（深合并 + 反序列化） | src/Core/Config/ConfigPipeline.cs:27-51 |
| 深合并原语（标量与数组覆盖） | src/Core/Config/ConfigPipeline.cs:59-92 |
| presentation 必填校验 | src/Core/Engine/GameEngine.cs:471-472 |
| 引擎默认值 | assets/game.json |

**相关文档**：[cfg-06 prd](../prd/cfg-06-game-config.md) · [cfg-06 spec](../spec-runtime/cfg-06-game-config.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
