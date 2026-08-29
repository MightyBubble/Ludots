# input-03 editor spec · 交互上下文档案

> 编辑器实现任务书。编辑器需求见 [input-03 UXD](../uxd/input-03-interaction-context.md)；引擎侧见 [runtime spec](../spec-runtime/input-03-interaction-context.md)。

## 1. 概述
上下文编辑器实现：五拼装位档案卡、跨表补全、活动上下文预览。

## 2. 设计
- **档案卡**：写 `Input/interaction_context_profiles.json`；五键全可空，下拉含"留空"项。
- **跨表补全**：集合键/视图键/过滤档案/输入上下文/意图档案五源各自注册视图；悬空引用保存期红条（引擎报错在执行期，编辑器提前）。
- **活动上下文预览**：会话期订阅挂载的 `ActiveInteractionContext` 快照（原「栈预览」；栈已退役，帧机制实体化），条目带来源能力与存续时长。

## 3. 精确语义与不变量
- 卡片可产生的档案形状 = 加载器接受的形状。
- 预览与引擎挂载状态逐帧一致，无编辑器侧推算。

## 4. 依赖接口与验收
- 消费：档案表、五类引用注册视图、活动上下文快照接口。
- 验收：新档案被能力引用后运行可见压帧；悬空引用在编辑器先于运行期暴露。

**相关文档**：[input-03 UXD](../uxd/input-03-interaction-context.md) · [input-03 runtime spec](../spec-runtime/input-03-interaction-context.md)
