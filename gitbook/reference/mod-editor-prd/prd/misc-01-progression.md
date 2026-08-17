# misc-01 · 进度域

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/misc-01-progression.md)；编辑器需求见 [UXD](../uxd/misc-01-progression.md)；引擎实现见 [runtime spec](../spec-runtime/misc-01-progression.md)；editor spec 见 [editor spec](../spec-editor/misc-01-progression.md)；现状见 [reference](../reference/misc-01-progression.md)。

## 1. 定位

进度域三张表把"长线目标"拆成三元组：范围声明谁的成绩算在一起（scope），进度条声明一条可推进的线（progression），需求声明一条用条件树表达的目标（requirement）。效果系统的 CompleteProgression 预设是它们的写入口。

## 2. 产品承诺

- **范围两种来源**：scope 的成员可来自 ScopeBinding（运行期绑定）或 EntityCollection（声明式实体集）；用集合时集合必须已配置，否则启动失败。
- **条件树是数据不是代码**：requirement 的 root 是组合条件（如 EntityCount + scope/entitySource/count/tags），由需求求值器在运行期求值。
- **效果即触发器**：GAS 的 CompleteProgression 预设要求 Instant 生命周期 + progression 块；progression.id 经 ProgressionIdRegistry 解析，未注册即启动失败——进度线与效果在装配期对齐，不留运行期悬空。
- **注册有上限可冻结**：进度 id 注册表有硬上限，可冻结；超限或冻结后注册即失败（上限见 reference 与事实纪律）。

## 3. 运行行为

ProgressionScopeBindingSystem 维护 scope 成员；RequirementEvaluator 在效果处理注入下求值需求；CompleteProgression 效果按解析好的 progression id 推进进度线。

## 4. 异常承诺

scope 引用未配置集合、progression 引用未声明 scope、条件树结构非法（未知 kind/缺参）、CompleteProgression 效果非 Instant 或缺 progression 块、progression.id 未注册——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/misc-01-progression.md) · [fx-23](fx-21-progression.md) · [attr-01](attr-01-definition.md)
