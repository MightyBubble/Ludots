# Mod 架构

Ludots 采用“一切皆 Mod”的设计。Core 能力、业务功能、调试工具和展示能力都应通过正式 Mod 机制组合。

## 1 加载流程

`ModLoader` 负责：

1. 决定要加载哪些 mod roots
2. 读取每个根目录下的 `mod.json`
3. 解析依赖并计算加载顺序
4. 挂载 `ModId:` 前缀的虚拟文件系统
5. 按需加载程序集入口
6. 调用 `IMod.OnLoad(IModContext)` 完成注册

## 2 正式扩展点

`IModContext` 是 Mod 的正式扩展 API。典型接入点包括：

- `OnEvent`
- `SystemFactoryRegistry`
- `TriggerDecorators`
- `FunctionRegistry`
- `VFS`
- `Log`

Mod 不应绕过这些入口直接侵入 Core 内部状态。

## 3 配置与资源

- 资源通过 VFS 以 `ModId:Path/To/Resource` 访问
- 运行时配置通过 `ConfigPipeline` 合并
- 纯资源 Mod 可以没有程序集入口

## 4 设计边界

- 当前 Mod 专用逻辑放在当前 Mod
- 两个以上 Mod 可能复用的逻辑，应提取到 Core 或公共基础设施
- 完整独立功能，应优先拆成独立 Mod

## 5 深度材料

- 仓库深度版：`docs/architecture/mod_architecture.md`
- 运行时单一事实：`docs/architecture/mod_runtime_single_source_of_truth.md`
