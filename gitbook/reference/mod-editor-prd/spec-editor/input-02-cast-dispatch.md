# input-02 editor spec · 施法派发档案

> 编辑器实现任务书。编辑器需求见 [input-02 UXD](../uxd/input-02-cast-dispatch.md)；引擎侧见 [runtime spec](../spec-runtime/input-02-cast-dispatch.md)。

## 1. 概述
派发编辑器实现：三段卡、因素语法编辑器、编队沙盘干跑。

## 2. 设计
- **三段卡**：写 `Input/cast_dispatch_profiles.json`；kind 联动显隐在视图模型层实现。
- **因素编辑器**：两段式输入（因素+修饰），补全源与评分器解析器同源。
- **沙盘**：调用派发干跑接口，用会话（或模拟）演员集合预演出手序与共享单号；cycle 显示游标位置。

## 3. 精确语义与不变量
- 三段卡可产生的形状 = 加载器接受的形状。
- 沙盘预演与运行期选人排序一致（同源）。

## 4. 依赖接口与验收
- 消费：派发档案表、评分器因素清单、派发干跑接口。
- 验收：topN 档案保存后启动即生效；沙盘序与实测一致；缺 N 在保存前拦截。

**相关文档**：[input-02 UXD](../uxd/input-02-cast-dispatch.md) · [input-02 runtime spec](../spec-runtime/input-02-cast-dispatch.md)
