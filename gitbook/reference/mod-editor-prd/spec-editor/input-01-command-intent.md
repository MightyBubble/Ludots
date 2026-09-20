# input-01 editor spec · 命令意图档案

> 编辑器实现任务书。编辑器需求见 [input-01 UXD](../uxd/input-01-command-intent.md)；引擎侧见 [runtime spec](../spec-runtime/input-01-command-intent.md)。

## 1. 概述
意图编辑器实现：规则梯视图模型、条件/路由补全、分派模拟。

## 2. 设计
- **规则梯**：rules 有序数组；拖拽排序写回 priority，层序即裁决序。
- **补全源**：订单类型注册表、tag 总账、姿态枚举、上下文组表；slot 输入按前缀约束补全。
- **分派模拟**：调用意图解析与规则匹配干跑接口（无副作用），逐演员输出路由终点；遮蔽统计由模拟计数派生。

## 3. 精确语义与不变量
- 梯子可产生的规则形状 = 加载器接受的形状。
- 模拟裁决与引擎逐演员路由逐字一致。

## 4. 依赖接口与验收
- 消费：档案表加载器、订单类型注册表、上下文组表、意图干跑接口。
- 验收：新增规则保存后启动即生效；模拟分派与实测一致；悬空路由保存前拦截。

**相关文档**：[input-01 UXD](../uxd/input-01-command-intent.md) · [input-01 runtime spec](../spec-runtime/input-01-command-intent.md)
