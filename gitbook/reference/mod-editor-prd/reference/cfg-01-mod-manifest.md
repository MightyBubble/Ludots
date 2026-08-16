# cfg-01 reference · mod 数据

> 现状参考。第一性需求见 [cfg-01 PRD](../prd/cfg-01-mod-manifest.md)；配置说明见 [cfg-01 配置说明](../config/cfg-01-mod-manifest.md)；目标实现见 [cfg-01 runtime spec](../spec-runtime/cfg-01-mod-manifest.md)。

## 1. 现状快照

- 清单解析为封闭字段白名单严格校验：白名单 11 个字段（name、version、description、main、priority、dependencies、author、url、changelog、tags、processSharedAssemblies），白名单外字段、类型不符、必填缺失均抛出并阻止加载。
- 无 `configRoots` 字段。
- 规范化序列化能力存在（解析对象 → 标准缩进 JSON），编辑器尚未接入。
- 入口上下文带扩展注册面 `IModContext.Extensions`：可注册效果内建处理器、图节点 op、表现器命令、表现器行为四类；注册只在加载窗口，扩展枢纽冻结后拒绝，语义键单主声明（重复即错）。已在 main（合并提交 9e05ca07f5），合同正本见架构章 mod-extensible-runtime。
- 发现为字母序深度优先、遇 mod.json 即停、跳过 bin/obj；产品路径加载顺序来自启动计划有序清单（依赖闭包在生成期烘焙，见 cfg-03 reference），priority 不参与该路径排序。
- 依赖解析失败模式四种：缺依赖、版本不符、重名、循环依赖，全部启动失败。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 严格解析（白名单 :10-23、逐字段校验 :31-170、空名/空版本拒绝 :159-167） | src/Core/Modding/ModManifestJson.cs |
| 规范化序列化 | src/Core/Modding/ModManifestJson.cs:172-178 |
| mod 发现（字母序 DFS、mod.json 即停、跳过 bin/obj） | src/Core/Modding/ModDiscovery.cs:68-108 |
| 目录扫描与依赖解析入口 | src/Core/Modding/ModLoader.cs:93-123、218 起 |
| 产品路径顺序烘焙（依赖闭包 DFS、依赖按键名字母序） | src/Tools/Ludots.Launcher.Backend/LauncherService.cs:729-777 |
| 本地回退拓扑排序（priority 降序、发现序升序；仅显式 modPaths 且无计划时） | src/Core/Modding/DependencyResolver.cs:82-136；调用侧 src/Core/Engine/GameEngine.cs:459-461 |
| 版本范围语法（`^ ~ >= <= > < =`、`*`） | src/Core/Modding/DependencyResolver.cs:196-262 |
| launch graph 顺序直用（跳过本地发现与解析） | src/Core/Modding/ModLoader.cs:127-214 |
| 扩展注册面（IModContext.Extensions） | src/Core/Modding/IModContext.cs |
| 扩展运行时 SSOT（四扩展面与铁律） | gitbook/architecture/mod-extensible-runtime.md |

**相关文档**：[cfg-01 prd](../prd/cfg-01-mod-manifest.md) · [cfg-01 spec](../spec-runtime/cfg-01-mod-manifest.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
