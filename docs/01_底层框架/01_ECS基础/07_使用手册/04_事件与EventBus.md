---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 事件与EventBus
状态: 草案
---

# 事件与EventBus 使用手册

# 1 快速开始
## 1.1 适用人群与目标
面向需要在系统间传递“发生了什么”的开发者。目标是在不引入 Entity churn 与隐式依赖的前提下，建立可审计、可预算、可 0GC 的事件链路。
## 1.2 前置条件
-
  - 已理解结构变更边界与集中阶段：`07_使用手册/02_CommandBuffer与结构变更.md`。
  - 明确“事件”与“状态”的边界：事件是短生命周期消息，状态是组件数据的长期真源。
## 1.3 最小可用示例

Ludots 在 GAS 中提供了一个双缓冲、数组化的轻量事件总线（避免 List 分配与实体抖动）：

```csharp
var bus = new GameplayEventBus();
bus.Publish(new GameplayEvent { Kind = GameplayEventKind.Damage, Target = targetId, Value = 10 });
bus.Update();

for (var i = 0; i < bus.Events.Count; i++)
{
    var evt = bus.Events[i];
    Consume(evt);
}
```
# 2 核心概念与输入输出
## 2.1 术语与约定
## 2.2 输入说明
-
  - World Events：Arch 的世界级事件钩子（实体创建/销毁、组件增删/设置），默认关闭，通过编译期标志启用。
  - EventBus：更通用的事件分发机制（Arch.Extended 提供实现），用于把事件从 ECS 内部桥接到上层。
  - 一帧事件：生命周期极短，通常只在一个 Tick 内生产与消费，消费后必须清理，避免无限堆积。
## 2.3 输出产物
-
  - 输入是事件消息（值类型），或事件组件流（ECS 中的短生命周期组件/Buffer）。
  - 输入必须可预算：单帧上限、丢弃策略与统计要明确。

-
  - 输出是消费侧的状态变更（写组件数据）或结构变更意图（写入 CommandBuffer）。
# 3 常用场景
## 3.1 场景 A：一帧事件的生产与消费
-
  - 推荐：值类型事件写入数组化总线（如 GAS 的 GameplayEventBus），在 Tick 边界 Swap buffer。
  - 或：事件组件写入 ECS，消费侧 Query 扫描后由 Cleanup System 移除事件组件。

## 3.2 场景 B：订阅与轻量 hook
-
  - World Events 只用于调试、统计、桥接，不用于玩法主干。
  - 订阅回调必须轻量：只做记录/入队/计数，禁止触发复杂系统链路或再次结构变更。

## 3.3 场景 C：跨系统联动与清理模式
-
  - 事件生产与消费必须显式：系统依赖关系通过“谁写入事件、谁消费事件”可审计。
  - 必须有清理：一帧事件在同一帧结束前清空（Swap 或 Cleanup），避免事件堆积造成内存与性能风险。
# 4 命令与参数参考
## 4.1 命令列表
本模块无命令行命令。

## 4.2 参数说明
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| 单帧事件上限 | int | 是 | 无 | 必须可配置或常量化，并提供 dropped 统计 |
## 4.3 返回码与失败策略
- 超出预算：允许丢弃并统计 dropped，不允许无限增长或静默扩容造成 GC。
- 事件类型不匹配：fail-fast 或记录可定位错误，不做静默 fallback 到其他事件口径。
# 5 产物消费与联动
## 5.1 运行时如何消费产物
## 5.2 与配置/Mod/VFS 的关系
- 消费事件后应写入组件状态或写入 CommandBuffer 结构变更意图，不在事件通道中继续级联复杂逻辑。
- 事件预算与开关可配置化；Mod 扩展事件类型时必须通过强类型枚举/Token，禁止 magic string。
# 6 诊断与常见问题
## 6.1 常见错误与定位方法
## 6.2 性能与规模上界
- 用 Entity 表达短生命周期事件导致大量创建/销毁：改为数组化总线或事件组件 + Cleanup。
- 事件回调里做结构变更：改为写入 CommandBuffer，在安全阶段回放。
- 事件无限堆积：检查是否缺失 Swap/Cleanup；添加 dropped 统计与上限保护。
- 事件系统的上界由“单帧事件量 + 消费复杂度”决定；必须用预算控制成本，避免事件成为隐藏热路径。
# 7 安全与破坏性操作
## 7.1 默认行为
## 7.2 保护开关与回滚方式
- 默认不启用 World Events；启用必须作为显式构建选项并在文档中记录用途。
- 最小保护：事件预算熔断（达到上限直接丢弃并统计），便于压测与异常隔离。
# 8 关联文档与代码入口
## 8.1 工具设计/接口/配置文档链接
## 8.2 代码入口
- `docs/01_底层框架/01_ECS基础/07_外部参考/ArchECS/06_官方文档摘录_Events.md`
- Arch World Events：`src/Libraries/Arch/src/Arch/Core/Events/*`
- Arch EventBus：`src/Libraries/Arch.Extended/Arch.EventBus/*`
- Ludots GameplayEventBus：`src/Core/Gameplay/GAS/GameplayEventBus.cs`
