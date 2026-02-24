---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - Mod与VFS - ModLoader 架构
状态: 草案
---

# ModLoader 架构

# 1 背景与问题定义

Ludots 采用 Mod-First：核心引擎不硬编码内容与玩法扩展点，通过 Mod 的配置与代码注入能力。本文定义 Mod 的加载顺序、依赖解析、VFS 挂载与生命周期边界，避免“多份 Core DLL”“静默 fallback”“类型不匹配”等线上不可追溯问题。

# 2 设计目标与非目标

目标：

- 依赖可解析：拓扑排序、循环检测、失败可解释
- 路径统一：所有数据通过 VFS 挂载点与配置管线收口
- fail-fast：manifest/依赖/入口非法即加载失败，不允许静默降级

非目标：

- 不支持运行时热替换 DLL（数据热替换另行定义）

# 3 核心设计

## 3.1 模块划分与职责

代码入口：

- `src/Core/Modding/ModLoader.cs`
- `src/Core/Modding/DependencyResolver.cs`
- `src/Core/Modding/VirtualFileSystem.cs`

职责边界：

- ModLoader：扫描 `mod.json`、构建依赖图、按序加载、执行 `IMod.OnLoad`
- DependencyResolver：依赖解析与排序（失败必须给出可定位信息）
- VFS：挂载 Core 与 Mods 的 `assets/`，对上层暴露统一读取接口

## 3.2 数据流与依赖关系

```
ModPaths（game.json）
  → 扫描 mod.json
  → 依赖解析（拓扑排序 / 循环检测）
  → VFS 挂载（Core assets + Mod assets）
  → 加载 Mod DLL
  → 执行 IMod.OnLoad（注册 Trigger/MapDefinition/服务扩展等）
```

## 3.3 关键决策与取舍

- Manifest 字段白名单：未知字段一律拒绝（fail-fast）
- 依赖排序是行为真源：配置合并顺序必须与依赖序一致
- 运行时禁止“向后兼容 + 静默 fallback”路径（必须单一真源）

# 4 替代方案对比

- 传统“内容打包到主程序”无法满足跨团队扩展与版本隔离
- 纯脚本化（无 DLL）难以承载高性能玩法逻辑与系统扩展点

# 5 风险与迁移策略

- 风险：Mod 自带或 CopyLocal 了 Core/Arch DLL，导致类型不匹配
- 策略：在 Mod 开发最佳实践中明确“引用策略与发布结构”，并在加载期做强校验

# 6 验收条款

- 依赖循环必定报错且错误信息可定位到 modId
- manifest 非法字段/缺字段必定报错（无静默修正）
- VFS 挂载顺序与配置合并顺序一致（可测试）

