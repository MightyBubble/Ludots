# fx-13 runtime spec · 参数化

> 引擎实现任务书。第一性需求见 [fx-13 PRD](../prd/fx-14-config-params.md)；现状见 [reference](../reference/fx-14-config-params.md)。

## 1. 概述
模板参数槽合同：七类型加载期解析、三条合并路径、caller 覆盖语义。

## 2. 设计
- 键经 ConfigKeyRegistry.Register 归一 int id；`_ep.*` 保留键在效果模板加载前统一注册（Initialize 先行）。
- 合并三路径保持：实体路径读创建时预合并的 EffectConfigParams 组件；请求路径 template+CallerParams 合并；Instant 内联每次现算。
- caller 覆盖语义保持：同键连 Types 一起改写，异键容量内追加；ApplyForce2D 力值 caller 优先、模板兜底。
- **治理项 E12**：caller 追加容量满时现状静默丢弃——改为可观测失败或计入预算指标（todo/effect.md E12）。

## 3. 精确语义与不变量
- 引用类型 value 在加载期锁定为注册 id；运行期不存在按名解析。
- 实体化效果的合并参数在实例存续期内不可变（预合并组件）。
- 同键覆盖后以 caller 的类型解释值；不存在"模板类型 + caller 值"的混合态。

## 4. 迁移与治理
现状即基线；E12 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-13 PRD](../prd/fx-14-config-params.md) · [reference](../reference/fx-14-config-params.md)
