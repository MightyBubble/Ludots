# gr-op-14 · 节点：Script 控制流

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-14-control-flow.md)；编辑器需求见 [UXD](../uxd/gr-op-14-control-flow.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-14-control-flow.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-14-control-flow.md)；现状见 [reference](../reference/gr-op-14-control-flow.md)。

## 1. 定位

Script 图的程序计数器件八件：相对跳转、条件双出口跳转、绝对调用、返回、让出、终结、跨图调脚本、整数搬运；外加五个作者糖（BranchBool/SwitchInt/While/Until/Wait）把汇编形态翻译成结构化写法。

## 2. 产品承诺

- **终结必须显式**：每张图以 HaltReturnInt 收尾出 Int；省略 value 引脚时读环境槽 I[0]——与 Script Host ABI 同槽。
- **双出口条件**：JumpIfFalse 按 Bool 出 true/false 两条控制边，是分支的唯一原生形态。
- **跨图调用有界**：InvokeScript 按 FuncLib 名调子图，嵌套深度有上限；子图内禁让出。
- **让出是切片点**：Yield 只在 Script 图存在，是跨帧挂起的唯一手段；Wait 是它的别名糖。
- **糖只是糖**：五个作者糖编译展开为上述八件，不引入新指令。

## 3. 运行行为

跳转与调用改写程序计数器；Yield 把执行权交还宿主等下一片；HaltReturnInt 结束本图把值交宿主。步数与深度预算见事实页与 vm 限额。

## 4. 异常承诺

图缺显式终结指令——编译失败（MissingHalt）。调用深度超限、子图内让出——编译期或执行期按预算拒绝。糖用在 Script 之外（Wait/While/Until）——拒绝。

**相关文档**：[配置说明](../config/gr-op-14-control-flow.md) · [gr-05](gr-05-execution.md) · [gr-06](gr-06-funclib.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
