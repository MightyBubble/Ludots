# cfg-02 配置说明 · 虚拟文件系统

> 配置写法与行为。第一性需求见 [cfg-02 PRD](../prd/cfg-02-vfs.md)；编辑器需求见 [UXD](../uxd/cfg-02-vfs.md)；现状见 [reference](../reference/cfg-02-vfs.md)。

## 1. 示例

地址到实际文件的解析效果：

| 地址 | 解析到 |
|---|---|
| `MobaDemoMod:assets/GAS/graphs.json` | 该 mod 根目录下的对应文件 |
| `Core:GAS/graphs.json` | 引擎默认资产根（assets/）下的对应文件——所有 mod 的共同基底 |

mod 代码里按地址读资源：

```csharp
void OnLoad(IModContext context)
{
    using var stream = context.GetResource("MyMod:assets/data/tables.json");
}
```

## 2. 地址组成与行为

| 组成 | 取值 | 这样写的效果 |
|---|---|---|
| 挂载点 | `Core` | 指向引擎默认资产根；`Core:Configs/...` 即其中 Configs 子树 |
| 挂载点 | mod 的 `name` | 该 mod 装配时根目录挂到这个名字下——`名字:路径` 永远落在它自己目录里 |
| 相对路径 | 挂载根内的路径 | 不得用 `..` 逃出挂载根，越界被拒绝 |

## 3. 文件结构

VFS 不是文件，是寻址层：挂载随 mod 装配建立。作者只写地址；mod 内两个合法位置（`assets/` 与 仓库 `assets/` 根（引擎默认））的地址形态见 cfg-04。

## 4. 运行时加载效果

- 装配时：每个 mod 根目录挂到它的 `name` 下，引擎资产根挂到 `Core` 下。
- 配置收集时：管线按地址收集片段；分片目录按稳定顺序枚举 json。
- 代码运行时：入口上下文提供按地址取资源流与文件系统访问。
- 卸载时：挂载移除，该 mod 全部地址立即失效。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 挂载点未挂载（拼错 mod 名或未加载） | 文件未找到异常，消息指明 mod 未挂载 |
| 文件不存在 | 文件未找到异常，消息含路径 |
| 路径越界 | 访问拒绝异常，拒绝访问 |

## 6. 实例

- mod 配置地址：`MobaDemoMod:assets/GAS/graphs.json`
- 引擎默认地址：`Core:GAS/graphs.json`

**相关文档**：[cfg-02 PRD](../prd/cfg-02-vfs.md) · [cfg-01 配置说明](cfg-01-mod-manifest.md) · [cfg-04 配置说明](cfg-04-config-tables.md)
