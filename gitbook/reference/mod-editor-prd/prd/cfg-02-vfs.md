# cfg-02 · 虚拟文件系统

> 产品承诺 · 已冻结。理想实现见 [cfg-02 spec](../spec-runtime/cfg-02-vfs.md)；现状见 [cfg-02 reference](../reference/cfg-02-vfs.md)。

## 1. 定位

mod 里的文件从不直接用磁盘路径引用——一律走**虚拟文件系统（VFS）的地址**。你在合并预览、保存路径、启动计划里看到的 `{modId}:assets/GAS/effects.json` 这类写法就是它。读懂地址文法，才能读懂配置片段从哪来、代码怎么读文件。

## 2. 示例

地址到实际文件的解析效果：

| 地址 | 解析到 |
|---|---|
| `MobaDemoMod:assets/Configs/GAS/graphs.json` | 该 mod 根目录下的 `assets/Configs/GAS/graphs.json` |
| `Core:Configs/GAS/graphs.json` | 引擎默认资产根下的 `Configs/GAS/graphs.json`——所有 mod 的共同基底 |

mod 代码里按地址读资源（入口上下文提供）：

```csharp
void OnLoad(IModContext context)
{
    using var stream = context.GetResource("MyMod:assets/data/tables.json");
    // 读到的是该 mod 目录里的真实文件，与磁盘位置无关
}
```

## 3. 地址组成与效果

地址以第一个冒号切成两段：

| 组成 | 取值 | 效果 |
|---|---|---|
| 挂载点 | `Core` | 指向引擎默认资产根；`Core:Configs/...` 即其中的 Configs 子树 |
| 挂载点 | mod 的 `name` | 该 mod 装配时，它的根目录被挂到这个名字下——所以 `名字:路径` 永远落在它自己的目录里 |
| 相对路径 | 挂载根内的路径 | 路径不得用 `..` 逃出挂载根；越界被拒绝 |

mod 名的全局唯一性同时保证挂载点唯一——这也是 mod 重名直接启动失败的原因之一。

## 4. 文件结构

VFS 不是文件，是寻址层：挂载随 mod 装配建立、随卸载移除；作者只写地址，永远不写磁盘路径。

## 5. 运行时加载效果

- **装配时**：每个 mod 的根目录挂到它的 `name` 下，引擎资产根挂到 `Core` 下——此后一切文件引用都经过这两类挂载点。
- **配置收集时**：配置管线按地址收集各 mod 的片段（候选地址规则见 cfg-05），磁盘物理位置由挂载决定；分片目录按稳定顺序枚举其中的 json 文件（见 cfg-04）。
- **代码运行时**：mod 入口上下文提供"按地址取资源流"与文件系统访问；同一份 mod 目录挪到磁盘任何位置，只要挂载不变，全部地址照常工作。
- **卸载时**：挂载移除，该 mod 的全部地址立即失效。

## 6. 预期反馈

- **启动期**：挂载建立后，配置收集、资产加载按地址进行。
- **编辑器内**：合并预览、保存路径、资产浏览器统一显示与使用地址；地址与物理路径的换算只走一条解析通道。

## 7. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 挂载点未挂载（拼错 mod 名或 mod 未加载） | 报"未挂载"错误 |
| 地址指向的文件不存在 | 报"文件不存在"错误 |
| 相对路径试图逃出挂载根 | 报"路径越界"错误，拒绝访问 |

## 8. 编辑器要点

- 保存与预览需要落磁盘时，统一走"地址 → 物理路径"解析，不自行拼接路径，保证编辑器与引擎看到同一份文件。
- 新建 mod 命名时即校验全局唯一（挂载点冲突 = mod 重名冲突）。
- 热应用级别：不适用——寻址层随 mod 装配建立。

## 9. 实例

- mod 配置地址：`MobaDemoMod:assets/Configs/GAS/graphs.json`
- 引擎默认配置地址：`Core:Configs/GAS/graphs.json`

**相关文档**：[cfg-02 spec](../spec-runtime/cfg-02-vfs.md) · [cfg-02 reference](../reference/cfg-02-vfs.md) · [cfg-01](cfg-01-mod-manifest.md)（挂载点名与代码入口从哪来）· [cfg-05](cfg-05-config-pipeline.md)（地址如何参与片段收集）
