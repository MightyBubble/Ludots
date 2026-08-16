# cfg-01 配置说明 · mod 数据

> 配置写法与行为。第一性需求见 [cfg-01 PRD](../prd/cfg-01-mod-manifest.md)；编辑器需求见 [cfg-01 UXD](../uxd/cfg-01-mod-manifest.md)；现状见 [cfg-01 reference](../reference/cfg-01-mod-manifest.md)。

## 1. 示例配置

演示场景 RTS 根 mod 的真实 mod.json：

```json
{
  "name": "RtsRedAlertLikeShowcaseMod",
  "version": "1.0.0",
  "description": "Thin root mod for the Red Alert / C&C style RTS production browser showcase.",
  "main": "bin/net8.0/RtsRedAlertLikeShowcaseMod.dll",
  "priority": 220,
  "dependencies": {
    "LudotsCoreMod": "^1.0.0",
    "CoreInputMod": "^1.0.0",
    "EntityCommandPanelMod": "^1.0.0",
    "BrowserRtsProductionShowcaseMod": "^1.0.0"
  },
  "author": "Ludots Team",
  "tags": ["showcase", "rts", "production", "red-alert-like"]
}
```

读法：依赖四个 mod（两个基础加指令面板与生产浏览器），闭包解析把它们全部排在它前面；带一份代码；priority 220 不影响正常启动的顺序。

最小合法清单（纯数据 mod 只要前两行）：

```json
{
  "name": "MyMod",
  "version": "1.0.0",
  "dependencies": { "LudotsCoreMod": "^1.0.0" }
}
```

## 2. 字段与行为

| 字段 | 类型 | 必填 | 这样配会产生什么效果 |
|---|---|---|---|
| `name` | string | 是 | 全局唯一标识；同时是挂载点名与所有配置地址的前缀（cfg-02）。两个 mod 同名直接启动失败 |
| `version` | string | 是 | 三段版本号（如 `1.0.0`）；被依赖时按范围校验 |
| `dependencies` | 对象 | 否 | `{依赖名: 范围}`；声明即获得覆盖权——闭包把你排在依赖之后，你写的字段覆盖它们。范围语法 `^ ~ >= <= > < =` 与 `*`；校验失败报出要求值与实际值 |
| `main` | string | 否 | 代码入口 DLL 的 mod 内相对路径；装配时加载并执行入口（见第 4 节）。省略即纯数据 mod |
| `priority` | int | 否 | **不影响正常启动顺序**；仅调试直启的平局决胜与目录列表展示排序。默认 0 |
| `description` / `author` / `url` / `changelog` | string | 否 | 展示元信息，不参与行为 |
| `tags` | string[] | 否 | 分类标签 |
| `processSharedAssemblies` | string[] | 否 | 进程级共享程序集简单名（高级用法） |

书写规则：字段集合封闭，出现未知字段启动直接失败并指出字段名；语义字符串禁首尾空白。

## 3. 文件结构

**mod 目录可以放磁盘任何位置**——条件是发现根能找到它：

| 场景 | 发现根 | 效果 |
|---|---|---|
| 正常启动 | 启动器扫描根配置（当前为仓库 `mods/` 递归） | 生成计划时把每个 mod 的根路径写进计划；运行期只按计划加载 |
| 调试直启 | 宿主显式传入的目录列表 | 引擎就地扫描解析 |
| 计划已生成 | 计划里的各 mod 根路径 | 挪目录后重新生成计划即可 |

目录内规则：`mod.json` 固定在 mod 根；有它的目录即 mod，不再下钻；`bin`、`obj` 被忽略。

演示场景底座的工程结构（后续各篇示例都落在这棵树里）：

```text
RtsRedAlertLikeShowcaseMod/        ← mod 根：有 mod.json 即 mod
├── mod.json                       ← 身份数据（本篇）
├── assets/                        ← 内容根，一切从这里寻址（cfg-02）
│   ├── GAS/                       ← 配置表（cfg-04/05）
│   ├── Configs/GAS/               ← 配置表的第二个合法位置
│   ├── Entities/  Maps/           ← 实体模板、地图（卷 11）
│   ├── Presentation/              ← 表现资产（卷 12）
│   └── game.json                  ← 游戏配置覆盖（cfg-06）
└── bin/                           ← 代码构建产物（main 指向这里；发现期被忽略）
```

## 4. 运行时代码加载

声明了 `main` 的 mod，装配时发生这条链：

1. 引擎加载该 DLL，扫描其中**第一个实现 mod 入口接口的类型**，实例化并调用其 `OnLoad(context)`；卸载时调用 `OnUnload()`。
2. `OnLoad` 拿到的上下文提供这个 mod 能用的一切挂靠点：自己的标识与文件系统（按地址读文件）、函数注册表、系统工厂注册表、触发器装饰器、日志通道、事件订阅、按地址取资源流。
3. 典型模式是轻入口：`OnLoad` 里只订阅"游戏开始"事件，等开局再注册系统——演示场景底座的入口就是这个模式：开局挂一个知识投影系统与一个选择反馈表现系统。
4. DLL 里没有入口类型也能启动：只记录一条"未找到入口"日志，资产与配置照常生效。
5. **扩展注册面**：`OnLoad` 窗口内可经 `Extensions` 注册四类扩展（效果处理器、图节点、表现命令/行为），三条铁律见 cfg-08。

时序：**装配先于配置编译**——入口先注册扩展与挂系统，配置在其后编译并引用扩展键；运行期系统读到的注册表永远是合并后的最终结果。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 缺 `name` 或 `version`、值为空 | 启动失败，指出坏文件与缺失字段 |
| 未知字段、类型不符 | 启动失败，指出字段名 |
| 依赖缺失、版本不在范围内 | 启动失败，指明谁缺谁、要求范围与实际版本 |
| 两个 mod 同名 | 启动失败 |
| 依赖成环 | 启动失败，指明环路 |
| `main` 指向的 DLL 不存在或无法加载 | 启动失败 |

## 6. 实例

- 演示场景 RTS 根 mod：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/mod.json`（入口见同目录 `RtsRedAlertLikeShowcaseModEntry.cs`，轻入口活样例）
- 最简核心 mod：`mods/LudotsCoreMod/mod.json`（被所有 mod 依赖，实际最先加载）

**相关文档**：[cfg-01 PRD](../prd/cfg-01-mod-manifest.md) · [cfg-01 UXD](../uxd/cfg-01-mod-manifest.md) · [cfg-02](../prd/cfg-02-vfs.md) · [cfg-08](../prd/cfg-08-mod-extensions.md)
