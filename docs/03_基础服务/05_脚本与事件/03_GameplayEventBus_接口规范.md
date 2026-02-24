---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 基础服务 - 脚本与事件 - GameplayEventBus
状态: 草案
---

# GameplayEventBus 接口规范

# 1 概述

GameplayEventBus 是 gameplay（当前主要为 GAS）侧的事件发布/订阅机制，用于在不直接耦合系统实现的前提下传播“可观测事件”。

本规范明确其与引擎 Trigger 系统的边界：EventBus 负责 gameplay 内部的事件分发；引擎 Trigger 负责生命周期与地图等关键点的扩展执行。

# 2 核心约束

1. 事件类型必须受控：禁止 magic string；事件标识必须可审计。  
2. 顺序必须稳定：同一输入序列下分发顺序可复现。  
3. 异常必须可观测：订阅者异常不得静默吞掉。  

# 3 代码入口（文件路径）

- `src/Core/Gameplay/GAS/GameplayEventBus.cs`
