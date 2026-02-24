---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - Events
状态: 草案
---

# Events 摘录

- 来源: https://arch-ecs.gitbook.io/arch/documentation/utilities/events
- 说明: 本文为官方 Events 页面节选，非完整镜像。

## 1 启用 EVENTS 标志

- 官方说明：通过在 Arch 源码中设置 `EVENTS` 标志，可以让 World 在关键操作上发出事件回调。
- 该标志属于编译期开关，默认关闭；开启后会在创建/销毁实体与组件增删上引入钩子调用成本。

## 2 事件订阅 API（节选）

```csharp
world.SubscribeEntityCreated((Entity entity) => { });
world.SubscribeEntityDestroyed((Entity entity) => { });

world.SubscribeComponentAdded((Entity entity, ref Position _) => { });
world.SubscribeComponentSet((Entity entity, ref Position _) => { });
world.SubscribeComponentRemoved((Entity entity, ref Position _) => { });
```

- 这些方法在对应操作执行后立即被调用，几乎是直接方法调用，延迟非常小。

## 3 设计要点（摘要）

- Events 用于观察 World 内部行为（创建/销毁/组件变更），方便做日志、调试、统计或桥接到外部事件总线。
- 官方未建议把 Events 直接用作业务主干逻辑，而是作为“知情通道”。

