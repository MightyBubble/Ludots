# 编码标准

本页定义 Ludots 的正式编码标准。具体实现细节可在仓库深度材料中继续追溯，但规则判断以本页为准。

## 1 核心架构铁律

### 1.1 六边形架构

- `src/Core/` 不得引用平台 API。
- gameplay 逻辑必须可无头测试。
- 数据在边界层做翻译，例如 `Fix64Vec2` 与 `float` 的转换。

### 1.2 一切皆 Mod

- Mod 唯一入口是 `IMod.OnLoad(IModContext)`。
- System 通过 `SystemFactoryRegistry` 或正式注册链路接入。
- 配置通过 `ConfigPipeline` 合并，不自建加载器。
- 业务能力优先放在 `mods/` 和可复用基础设施中，不塞进 host loop。

### 1.3 四个禁止

- 禁止 fallback
- 禁止向后兼容
- 禁止重复造轮子
- 禁止跨越职责

## 2 ECS 硬性约束

- 组件必须是 blittable struct。
- 热路径零 GC。
- `QueryDescription` 缓存为字段，不在循环里新建。
- 结构变更通过 `CommandBuffer` 或正式回放链路执行。
- gameplay 数值使用 `Fix64` / `Fix64Vec2`，不直接使用 `float`。

## 3 命名与挂靠

- 组件使用 `Cm`、`Tag`、`Event` 等既定后缀。
- 新增 System 必须明确归属一个 `SystemGroup` phase。
- 命名不耦合具体业务，业务差异优先由配置和 Mod 决定。

## 4 提交要求

- Commit 格式使用 `<type>(<scope>): <description>`
- 行为变更必须同步更新正式文档
- 变更前后都应能解释挂靠点、复用清单和新增清单

## 5 深度材料

- 仓库深度版：`docs/conventions/00_coding_standards.md`
- 相关架构：`docs/architecture/adapter_pattern.md`
- 相关架构：`docs/architecture/gas_layered_architecture.md`
