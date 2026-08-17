# gr-06 配置说明 · 编译与校验

> 配置写法与行为。第一性需求见 [gr-05 PRD](../prd/gr-04-compilation.md)；编辑器需求见 [UXD](../uxd/gr-04-compilation.md)；现状见 [reference](../reference/gr-04-compilation.md)。

## 1. 示例配置

作者糖的真实用法（`assets/GAS/graphs.json` 的 `Graph.Script.DrinkUntilFull` 摘录）：

```json
{ "id": "branchNeedDrink", "op": "BranchBool" }
```

出边 fromPort 为 `true` / `false`；循环体经 `Call` 进入、`Yield` 挂起、`Return` 返回——糖在编译期展开为基础控制流，落盘永远是糖名。

## 2. 字段与行为

| 写法 | 这样配会产生什么效果 |
|---|---|
| `BranchBool` | 布尔分支，出边必须齐 true/false + condition 值边；Script/Effect 可用 |
| `SwitchInt` / `While` / `Until` / `Wait` | Script 专属糖；Wait 是 Yield 的别名 |
| 链式 Effect/Score/Validation/Derived | Linear 链尾自动补 HaltReturnInt(A=0)，作者不必写收尾 |
| HaltReturnInt 不连 value 边 | 读 I[0]（环境寄存器约定，见 G3——写法上建议显式连边） |
| 符号字段（tag/attribute/graphId/configKey…） | 装载期解析为整数 id 并回写指令（gr-03 第 2 节字段族） |
| 跨图调用 functionName | func_lib 装载后二次 patch 换成图 id 并清 FuncLib 位 |

## 3. 文件结构

编译不落盘、无中间文件；一切写法都体现在 graphs.json 文档（gr-03）。

## 4. 运行时加载效果

检查按固定顺序：头校验 → 节点 id 唯一 → entry 存在 → 控制边三段必填+唯一 → 值边四段+唯一 → 寄存器分配 → 必需边 → 端口白名单 → 不可达检测 → 未定义读 SSA → 预算 → 前缀 Jump → 输出 schema。符号 patch 在注册前后两段完成；装载链末尾图名注册表冻结（gr-02）。

## 5. 异常处理

| 诊断 | 含义 |
|---|---|
| GASG0008 | 不可达节点（死代码） |
| GASG0009 | 编译期预算超限 |
| GASG0012 / GASG0015 | 缺控制边 / 缺值输入（如 BranchBool 缺 condition） |
| GASG0017 / GASG0021 | 寄存器越界 / 别名冲突 |
| GASG0018 | 未定义读（SSA 数据流） |

全部诊断码共 21 个（GASG0001-0021，见 reference）。

## 6. 实例

- 糖与控制流全集：`assets/GAS/graphs.json` 的 `Graph.Script.DrinkUntilFull`
- 链尾自动收尾：任意 Effect 链（如 AbsFloat 分片，gr-03 第 6 节）；上限见 [事实与取值表](../facts.md)

**相关文档**：[gr-05 PRD](../prd/gr-04-compilation.md) · [gr-03 配置说明](gr-02-document.md) · [gr-08 配置说明](gr-06-funclib.md)
