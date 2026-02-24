---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 基础服务 - Graph运行时 - GraphProgram
状态: 草案
---

# GraphProgram 接口规范

# 1 概述

GraphProgram 是 GraphRuntime 的“可序列化程序表示”，用于把图编译结果固化为可加载、可校验、可确定性执行的 blob。

本接口规范只定义最小契约与约束：如何表达指令流/常量/元信息、如何版本化、以及哪些行为必须 fail-fast。

# 2 概念形状

## 2.1 Program 组成

1. 指令流：以固定 opcode 集合表达执行步骤。  
2. 常量区：数值/字符串/句柄等只读数据。  
3. 元信息：图签名（输入/输出）、节点库依赖、调试符号（可选）。  

## 2.2 版本与兼容性

- Program 必须携带版本号；Executor 在版本不兼容时必须 fail-fast。  
- 禁止隐式升级：任何迁移必须通过显式工具链完成并可回放。  

# 3 校验与失败策略

## 3.1 必须校验的内容

1. 指令越界：PC/跳转目标/常量索引越界必须 fail-fast。  
2. 类型不匹配：输入/输出签名不匹配必须 fail-fast。  
3. 节点库依赖：缺失依赖必须 fail-fast（禁止静默降级）。  

## 3.2 确定性约束

- 指令执行顺序固定；禁止依赖容器遍历顺序或 wall-clock。  
- 若存在集合类输出，必须定义稳定序与 tie-break。  

# 4 代码入口（文件路径）

- `src/Core/GraphRuntime/GraphProgramBlob.cs`
- `src/Core/GraphRuntime/GraphProgramRegistry.cs`
