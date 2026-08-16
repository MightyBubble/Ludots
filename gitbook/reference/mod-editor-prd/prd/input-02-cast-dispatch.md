# input-02 · 施法派发档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/input-02-cast-dispatch.md)；编辑器需求见 [UXD](../uxd/input-02-cast-dispatch.md)；引擎实现见 [runtime spec](../spec-runtime/input-02-cast-dispatch.md)；编辑器实现见 [editor spec](../spec-editor/input-02-cast-dispatch.md)；现状见 [reference](../reference/input-02-cast-dispatch.md)。

## 1. 定位

施法派发档案决定"一次技能命令分给选中的哪些演员、按什么顺序、共享一单还是各下各的"——群体施法的分配器。

## 2. 产品承诺

- **三种选人**：全体照收；按评分取前 N；轮转制每次推进到下一位演员。
- **评分可组**：效用评分器按考虑因素列表打分（如离目标越近分越高），排序只服务于选人。
- **两种路由**：并行（可整组共享一个订单号，一拒俱拒一目了然）或顺序逐个。
- **方案级默认**：控制方案声明默认派发档案；一次命令一个档案裁决。

## 3. 运行行为

命令意图路由出演员组后，按生效派发档案选人与排序，决定共享单号或提交顺序，交订单系统提交。

## 4. 异常承诺

选择器形状非法（缺 N、未知种类）、评分器未知种类——启动失败并指明档案；轮转档案必须随每次接受推进，推进不可静默丢失。

**相关文档**：[配置说明](../config/input-02-cast-dispatch.md) · [input-01](input-01-command-intent.md) · [ord-03](ord-03-pipeline.md)
