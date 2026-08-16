# gr-05 配置说明 · 函数库 FuncLib

> 配置写法与行为。第一性需求见 [gr-05 PRD](../prd/gr-06-funclib.md)；编辑器需求见 [UXD](../uxd/gr-06-funclib.md)；现状见 [reference](../reference/gr-06-funclib.md)。

## 1. 示例配置

引擎默认 `assets/GAS/func_lib.json` 全量：

```json
[
  { "name": "demo.const.seven",   "graph": "Graph.FuncLib.Demo.ConstSeven", "kind": "Script", "purity": "pure" },
  { "name": "ability.slash",      "graph": "Graph.Ability.Slash",  "kind": "Script", "purity": "pure" },
  { "name": "ability.bash",       "graph": "Graph.Ability.Bash",   "kind": "Script", "purity": "pure" }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `name` | 函数名，跨 mod 合并键；调用点（节点 functionName 字段）按名解析；不得与 action_lib 撞名 |
| `graph` | 必须指向已注册图，且该图 kind 与本条 kind 一致 |
| `kind` | 当前只接受 `Script`——非 Script 图属于挂接消费（gr-07），不做函数 |
| `purity` | 可选，当前唯一合法值 `pure`；非 pure 直接拒 |

入库即触发纯度闭包校验：从入口沿控制流（含跳转、调用、跨图调用）遍历，任何可达挂起、跨图调用环、非法闭包边界都拒绝入库。

## 3. 文件结构

`assets/GAS/func_lib.json`（目录登记条目，跨 mod 同名合并）；引用许可序在 graphs 之后（gr-00 第 4 节）。

## 4. 运行时加载效果

装载时逐条校验（name 唯一、图已注册、kind 一致、纯度闭包），全部通过后目录生效；随后所有图的 functionName 调用点被统一回写为图 id 并清 FuncLib 位（gr-03 符号 patch），最后做调用终检。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `kind` 非 Script | 装载失败（Score/Validation 走挂接不做函数） |
| 引用未注册图 / kind 不一致 | 装载失败，指明函数名 |
| 可达挂起（直接或经跨图调用） | 装载失败（提示挂起属于 ActionLib） |
| 跨图调用环 | 装载失败（环诊断码） |
| 与 action_lib 撞名 | 装载失败 |

## 6. 实例

- 引擎默认：`assets/GAS/func_lib.json`（3 条，计数见事实页目录节）
- 被调用方：`assets/GAS/graphs.json` 的 `Graph.FuncLib.Demo.ConstSeven`

**相关文档**：[gr-05 PRD](../prd/gr-06-funclib.md) · [gr-03 配置说明](gr-04-compilation.md) · [gr-06 配置说明](gr-07-actionlib.md)
