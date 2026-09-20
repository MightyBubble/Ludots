# gr-op-14 配置说明 · 节点：Script 控制流

> 配置写法与行为。第一性需求见 [gr-op-14 PRD](../prd/gr-op-14-control-flow.md)；编辑器需求见 [UXD](../uxd/gr-op-14-control-flow.md)；现状见 [reference](../reference/gr-op-14-control-flow.md)。

## 1. 示例配置

节点画廊真实文件（`JumpIfFalse.json` 摘要：喝水循环——计数到 3 每轮让出一次，见 Yield 与 HaltReturnInt 收尾）：

```json
{ "id": "zeroWater", "op": "ConstInt", "intValue": 0, "pinRegister": 0 },
{ "id": "limitValue", "op": "ConstInt", "intValue": 3, "pinRegister": 1 },
{ "id": "oneValue", "op": "ConstInt", "intValue": 1, "pinRegister": 2 },
{ "id": "waterBelowLimit", "op": "CompareLtInt" },
{ "id": "jif", "op": "JumpIfFalse" },
{ "id": "sipAdd", "op": "AddInt", "pinRegister": 0 },
{ "id": "sipYield", "op": "Yield" },
{ "id": "done", "op": "HaltReturnInt" }
```

控制边：`jif` 的 `true` 口接循环体、`false` 口接 `done`；循环体尾 `sipYield` 的 `next` 回 `readWater`。`HaltReturnInt.json` 是最小例（ConstInt 7 → HaltReturnInt）；`InvokeScript.json` 按名调 `demo.const.seven` 子图（`_constSevenCallee.json`）。

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| Jump | SC | —（控制边目标） | — | 相对跳转 |
| JumpIfFalse | E+SC | condition Bool | — | 条件假走 false 口、真走 true 口 |
| Call | SC | imm 目标 | — | 绝对地址调用 |
| Return | SC | — | — | 子程序返回 |
| Yield | SC | — | — | 让出执行权到下一片（scriptOnly） |
| HaltReturnInt | L+Q+SC | value Int 可缺省 | 终结 | 图终结出 Int；缺省读环境槽 I[0] |
| InvokeScript | L+Q+SC | imm=FuncLib 函数名 | Int | 跨图调子图；深度有上限、子图禁 Yield |
| MoveInt | SC | value | Int | 整数搬运（寄存器间复制） |

作者糖五个（写在 `op` 字段，编译展开为上表指令）：

| 糖 | 可用图 | 展开 |
|---|---|---|
| BranchBool | Script/Effect | JumpIfFalse 形态的双出口分支 |
| SwitchInt | Script | 多路整数分支 |
| While / Until | Script | 条件循环 |
| Wait | Script | Yield 别名 |

互斥与陷阱：

- **缺 Halt 即编译失败**：图没有显式 HaltReturnInt 不给过（MissingHalt）——线性图与 Query 图同样要显式终结。
- HaltReturnInt 省 value 读 I[0]：这是"传感器/环境寄存器"合同，与 Script Host ABI 同槽——图里别把 I[0] 当普通暂存用。
- 子图（被 InvokeScript 调的 FuncLib 图）内禁 Yield：跨帧挂起只属于顶层 Script 图。
- 循环变量要 `pinRegister` 钉槽（如上例 water 钉 0 槽），否则编译器分配的暂存会被复用冲掉。
- JumpIfFalse 在 Effect 图也可用（E+SC）：效果相位图里的"条件执行"就靠它。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`functionName` 写 FuncLib 名，见 gr-02 与 gr-06。

## 4. 运行时加载效果

糖在编译期展开；跳转/调用编译为程序计数器指令；执行期受步数与调用深度预算（vm 限额）约束，Run-to-Halt 语义见 gr-05。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 图缺显式 Halt | 编译失败（MissingHalt），指明入口 |
| 子图含 Yield | 拒绝 |
| 调用深度/步数超限 | 执行失败并报预算 |
| 糖用于 Script 外（Wait/While/Until） | 拒绝 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/JumpIfFalse.json`
- 同目录 `Jump.json`、`Call.json`、`Return.json`、`Yield.json`、`HaltReturnInt.json`、`InvokeScript.json`、`MoveInt.json`、`_constSevenCallee.json`

**相关文档**：[gr-op-14 PRD](../prd/gr-op-14-control-flow.md) · [gr-05 配置说明](gr-05-execution.md) · [gr-06 配置说明](gr-06-funclib.md)
