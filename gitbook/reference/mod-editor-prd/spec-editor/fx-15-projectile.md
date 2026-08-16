# fx-14 editor spec · 弹道

> 编辑器实现任务书。编辑器需求见 [fx-14 UXD](../uxd/fx-15-projectile.md)；引擎侧见 [runtime spec](../spec-runtime/fx-15-projectile.md)。

## 1. 概述

LaunchProjectile 效果表单的弹道子表单：四组参数、引用选择、Legacy 迁移提示。

## 2. 设计

- **表单模型**：飞行/命中/碰撞/子效果四组；impactPolicy 联动 maxHitCount；排源开关持久化为"有 true / 无字段"两态。
- **引用选择**：hitEffect/impactEffect/presentationEffect 走效果模板注册表，悬空引用阻保存。
- **迁移提示**：载入含 Legacy 的旧 JSON 时按 loader 报错文案引导改写。

## 3. 精确语义与不变量

- 表单校验集合与 loader 一一对应；`collisionExcludeSource: false` 永不落盘。
- maxHitCount 上界取命中历史容量常量（与事实页同源），不手抄。

## 4. 依赖接口与验收

- 消费：效果模板注册表、关系过滤枚举、保存管线、热通道分级。
- 验收：缺 hitEffect 保存被拒；impactPolicy 切 ContinueOnHit 后 maxHitCount 自动启用且限界。

**相关文档**：[fx-14 UXD](../uxd/fx-15-projectile.md) · [fx-14 runtime spec](../spec-runtime/fx-15-projectile.md)
