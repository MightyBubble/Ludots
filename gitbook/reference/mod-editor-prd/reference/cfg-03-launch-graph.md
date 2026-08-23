# cfg-03 reference · 启动计划

> 现状参考。第一性需求见 [cfg-03 PRD](../prd/cfg-03-launch-graph.md)；配置说明见 [cfg-03 配置说明](../config/cfg-03-launch-graph.md)；目标实现见 [cfg-03 runtime spec](../spec-runtime/cfg-03-launch-graph.md)。

## 1. 现状快照

- 计划文档模型字段：schema 版本、生成时间、计划指纹、适配器描述、构建模式、选择器、根 mod、有序 mod 清单、各 mod 计划项（根路径 / 工程路径 / 主程序集 / 类型 / 构建状态 / 绑定名）、运行时产物、浏览器运行时、计划诊断（设置合并溯源 + 警告）。
- 生成侧：启动器后端从选择器与根 mod 解析依赖闭包、计算指纹、写出计划文件与锚文件；闭包解析为 DFS 后序遍历，依赖按键名字母序访问、根按选择顺序访问，缺依赖/循环/同名歧义抛错；priority 不参与排序，仅影响目录索引展示序。只有完整生成入口，无 dry-run。
- 消费侧：引导器读取可执行文件旁的锚文件定位计划，做新鲜度校验（产物自指、锚-计划逐项核对、适配器一致），通过后把顺序交给 mod 加载器（跳过本地发现与依赖解析）。
- 仓库内的计划文件为轻量变体，只填必填子集（有序 mod 清单 + 各 mod 根路径）。
- 编辑器未接入计划生成链路；启动预设存在于启动器配置侧（binding / preset），编辑器侧无。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 计划文档模型（全字段落点） | src/Core/Hosting/LauncherGraphDocument.cs:21-82 |
| 引导配置（锚文件字段：计划路径、选择器、根 mod、顺序、指纹、schema 版本、浏览器运行时） | src/Core/Hosting/GameBootstrapper.cs:19-30 |
| 引导入口（读锚 → 解析计划 → 校验 → 交引擎） | src/Core/Hosting/GameBootstrapper.cs:41-95 |
| 计划路径解析与缺失校验 | src/Core/Hosting/GameBootstrapper.cs:97-130 |
| 生成侧：依赖闭包解析（DFS 后序、依赖按名字字母序、错误抛出） | src/Libraries/Ludots.Launcher.Backend/LauncherService.cs:729-777 |
| 生成侧：priority 的现实作用（目录索引展示排序） | src/Libraries/Ludots.Launcher.Backend/LauncherService.cs:1303 附近 |
| 消费侧：引擎按计划有序清单加载 | src/Core/Engine/GameEngine.cs:451-461 |
| 生成侧：指纹计算 | src/Libraries/Ludots.Launcher.Backend/LauncherService.cs:569 附近 |
| 生成侧：计划与锚文件写出 | src/Libraries/Ludots.Launcher.Backend/LauncherService.cs（WriteLaunchGraphDocument / WriteRuntimeBootstrap） |
| 计划顺序直用加载路径 | src/Core/Modding/ModLoader.cs:127-214 |
| 计划文件实例 | src/Apps/Raylib/Ludots.App.Raylib/raylib.launch.graph.json |

**相关文档**：[cfg-03 prd](../prd/cfg-03-launch-graph.md) · [cfg-03 spec](../spec-runtime/cfg-03-launch-graph.md) · [cfg-01 reference](cfg-01-mod-manifest.md)
