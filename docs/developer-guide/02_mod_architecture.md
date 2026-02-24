# Mod 架构与配置系统

Ludots 采用了“一切皆 Mod”的设计理念，不仅允许用户扩展内容，就连引擎本身的核心内容也以 `Core` Mod 的形式存在。

## 1. Mod 加载流程

`ModLoader` (`src/Core/Modding/ModLoader.cs`) 负责扫描、解析和加载所有 Mod。

1.  **扫描目录**: 启动时扫描 `Mods/` 目录下的所有子文件夹。
2.  **解析 mod.json**: 读取每个 Mod 根目录下的 `mod.json` 文件。
3.  **依赖排序**: 使用 `DependencyResolver` 根据 `dependencies` 字段构建加载顺序图（DAG），确保先加载前置依赖。
4.  **挂载 VFS**: 将每个 Mod 的根目录挂载到虚拟文件系统（VFS）。
5.  **程序集加载**: 如果 `mod.json` 中包含 `main` 字段（指定 DLL 路径），则加载该程序集并实例化入口类（实现 `IMod` 接口）。
6.  **初始化**: 按顺序调用每个 Mod 的 `OnLoad(ModContext)` 方法。

### mod.json 示例

```json
{
  "name": "MyAwesomeMod",
  "version": "1.0.0",
  "main": "MyAwesomeMod.dll",  // 可选，纯资源包不需要
  "dependencies": {
    "Core": ">=1.0.0"          // 声明依赖 Core 1.0.0+
  },
  "priority": 100              // 加载优先级，越大越早
}
```

## 2. 虚拟文件系统 (VFS)

Ludots 通过 `VirtualFileSystem` (`src/Core/Modding/VirtualFileSystem.cs`) 统一管理所有资源路径，实现跨平台与 Mod 隔离。

### 路径格式 (URI)
`ModId:Path/To/Resource`

*   `Core:assets/game.json` -> `Mods/Core/assets/game.json`
*   `MyMod:textures/player.png` -> `Mods/MyMod/textures/player.png`

### 使用示例
```csharp
// 读取文件内容
string content = VFS.ReadAllText("MyMod:config/settings.json");

// 获取绝对路径（仅用于必须访问本地文件的特殊场景，如 Raylib 加载）
string fullPath = VFS.GetAbsolutePath("MyMod:textures/icon.png");
```

## 3. 配置合并 (ConfigPipeline)

引擎启动时会通过 `ConfigPipeline` 自动合并所有 Mod 中的 `game.json` 配置片段。这允许 Mod 覆盖或扩展核心配置，而无需修改核心文件。

### 合并策略
1.  **Core 配置**: 首先加载 `Core:assets/game.json` 作为基础配置。
2.  **Mod 配置**: 按照 Mod 加载顺序，依次查找并合并每个 Mod 中的 `assets/game.json`。
3.  **覆盖规则**: 后加载的 Mod 配置会覆盖先加载的同名字段（Deep Merge）。对于数组类型的配置（如 `OrderTags`），通常是追加（Append）或通过特定语法进行替换。

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
