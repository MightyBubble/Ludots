# cfg-02 reference · 虚拟文件系统

> 现状参考。第一性需求见 [cfg-02 PRD](../prd/cfg-02-vfs.md)；配置说明见 [cfg-02 配置说明](../config/cfg-02-vfs.md)；目标实现见 [cfg-02 runtime spec](../spec-runtime/cfg-02-vfs.md)。

## 1. 现状快照

- 接口四个操作：挂载、卸载、取流、URI 到物理路径解析。
- URI 按第一个冒号切分挂载点与相对路径。
- 挂载点：`Core` 挂引擎资产根（assets/）；每个已加载 mod 挂其根目录，挂载点名 = mod.json 的 name。
- 安全：相对路径做逃逸前缀校验，越界、未挂载、文件缺失分别抛对应异常。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 接口定义（Mount / Unmount / GetStream / TryResolveFullPath） | src/Core/Modding/IVirtualFileSystem.cs:5-12 |
| URI 文法（按第一个冒号切分） | src/Core/Modding/VirtualFileSystem.cs:31-56 |
| 逃逸与异常校验 | src/Core/Modding/VirtualFileSystem.cs:80-95 |
| Core 挂载点（引擎资产根） | src/Core/Engine/GameEngine.cs:424 |
| mod 挂载点（mod 根目录，随加载建立） | src/Core/Modding/ModLoader.cs:204 |

**相关文档**：[cfg-02 prd](../prd/cfg-02-vfs.md) · [cfg-02 spec](../spec-runtime/cfg-02-vfs.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
