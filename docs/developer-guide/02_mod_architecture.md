# Mod 架构与配置系统

Ludots 采用了“一切皆 Mod”的设计理念，不仅允许用户扩展内容，就连引擎本身的核心内容也以 `Core` Mod 的形式存在。

## 1 Mod 加载流程

`ModLoader` (`src/Core/Modding/ModLoader.cs`) 负责扫描、解析和加载所有 Mod。

1.  **决定要加载哪些 Mod 目录**：引擎初始化时接收一个 `modPaths` 列表。通常这个列表来自应用旁边的 `game.json`（只包含 `ModPaths`），由启动器生成与覆盖。
2.  **解析 mod.json**：读取每个 Mod 根目录下的 `mod.json` 文件。`name` 字段是 Mod 的唯一标识（下文称 ModId）。
3.  **依赖排序**：根据 `dependencies` 解析依赖图，计算加载顺序，确保先加载前置依赖。
4.  **挂载虚拟文件系统**：把每个 Mod 的根目录挂载为 `ModId:` 前缀，用于资源与配置读取。
5.  **程序集加载**：如果 `mod.json` 声明 `main`（入口 DLL 相对路径），则加载该程序集并查找入口类型进行初始化；没有 `main` 的 Mod 也可以作为“纯资源 Mod”存在。
6.  **初始化**：按加载顺序调用入口的 `OnLoad(ModContext)`，在这里注册 Trigger、函数、配置等扩展点。

### mod.json 示例

```json
{
  "name": "MyAwesomeMod",
  "version": "1.0.0",
  "main": "MyAwesomeMod.dll",
  "dependencies": {
    "Core": ">=1.0.0"
  },
  "priority": 100
}
```

## 2 虚拟文件系统

Ludots 通过 `VirtualFileSystem`（下文简称 VFS）统一管理所有资源路径，实现跨平台与 Mod 隔离。

### 路径格式
`ModId:Path/To/Resource`

*   `Core:Configs/game.json` -> `assets/Configs/game.json`
*   `MyMod:assets/Configs/game.json` -> `<MyModRoot>/assets/Configs/game.json`

### 使用示例
```csharp
using var stream = vfs.GetStream("MyMod:assets/Configs/game.json");
using var reader = new StreamReader(stream);
string json = reader.ReadToEnd();

if (vfs.TryResolveFullPath("MyMod:assets/Configs/game.json", out var fullPath))
{
    // fullPath 仅用于必须访问本地文件路径的边界场景
}
```

## 3 配置加载与合并总览

引擎启动时通过 `ConfigPipeline` 自动合并各来源的 `game.json` 片段。这允许 Mod 覆盖或扩展核心配置，而无需修改核心文件。

### 合并策略
1.  **Core 默认配置**：先加载 `Core:Configs/game.json`。
2.  **Mod 配置**：按 `LoadedModIds` 顺序依次尝试合并：
    *   `${modId}:assets/game.json`
    *   `${modId}:assets/Configs/game.json`
3.  **合并规则**：对象做递归合并；数组与标量做覆盖（不追加）。

### game.json 结构示例

```json
{
  "WorldSize": { "Width": 1000, "Height": 1000 },
  "FixedHz": 60,
  "OrderTags": [ "Attack", "Move" ]
}
```

如果 Mod 想要修改 `FixedHz` 为 30，只需在其 `assets/game.json` 中写：
```json
{
  "FixedHz": 30
}
```
引擎最终运行时使用的配置将是合并后的结果。

如需了解更完整的“配置来源、优先级、合并规则与限制”，参见 [07_config_pipeline.md](07_config_pipeline.md)。

地图配置与地图切换的生命周期（以及空间服务的热切换）参见 [10_map_mod_spatial.md](10_map_mod_spatial.md)。
