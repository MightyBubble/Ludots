---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 基础服务 - 脚本与事件 - ScriptContext
状态: 草案
---

# ScriptContext 接口规范

# 1 概述

ScriptContext 是 Trigger/脚本侧访问运行时能力的统一入口。它通过“强约束键（ContextKeys）+ 类型化 Get/Set”向脚本暴露引擎上下文，避免魔法字符串与隐式全局状态。

# 2 核心约束

1. 键必须集中定义：统一由 `src/Core/Scripting/ContextKeys.cs` 定义，禁止散落字符串。  
2. 获取必须显式：缺失依赖必须可观测并 fail-fast 或显式降级（由调用方裁决）。  
3. 不允许越权：脚本不得通过反射/全局单例绕过上下文边界。  

# 3 常用能力入口（SSOT）

- `ContextKeys`：`src/Core/Scripting/ContextKeys.cs`
- `ScriptContext`：`src/Core/Scripting/ScriptContext.cs`
- 扩展方法：`src/Core/Scripting/ScriptContextExtensions.cs`

# 4 代码入口（文件路径）

- `src/Core/Scripting/ScriptContext.cs`
- `src/Core/Scripting/ScriptContextExtensions.cs`
