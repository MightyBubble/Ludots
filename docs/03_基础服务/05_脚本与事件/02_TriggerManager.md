---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 基础服务 - 脚本与事件 - TriggerManager
状态: 草案
---

# TriggerManager 接口规范

# 1 概述

TriggerManager 提供运行时触发器框架：Mod 可以在加载期注册 Trigger；GameEngine 在关键生命周期事件（例如 MapLoaded）触发 EventKey；TriggerManager 以稳定顺序执行 Trigger 逻辑，形成“可扩展但可控”的事件响应机制。

裁决：

1. 事件键必须强类型，禁止 magic string。  
2. Trigger 执行必须 fail-fast 且可观测，禁止静默吞错。  

# 2 核心接口

## 2.1 注册（Register）

输入：`EventKey` + `Trigger` 实例

约束：

- 只允许在加载期或明确的安全阶段注册。  
- 重复注册策略必须明确（覆盖/追加/拒绝），不得静默覆盖。  

## 2.2 触发（FireEvent）

输入：`EventKey` + `ScriptContext`

约束：

- 执行顺序必须稳定：至少需要固化为“事件键排序 + 注册顺序”或“注册顺序”（由实现裁决并写死）。  
- 任何异常不得静默吞掉：要么上抛并中止推进，要么统一转为可观测错误事件（不得两头都不做）。  

# 3 使用约束

1. ContextKeys 必须集中定义：`src/Core/Scripting/ContextKeys.cs`  
2. 标准事件键集合必须集中定义：`src/Core/Scripting/GameEvents.cs`  
3. 触发点必须由引擎显式调用：不允许隐藏触发路径或在系统内部递归触发。  

# 4 代码入口（文件路径）

- `src/Core/Scripting/TriggerManager.cs`
- `src/Core/Scripting/Trigger.cs`
- `src/Core/Scripting/EventKey.cs`
- `src/Core/Scripting/GameEvents.cs`
