# fx-14 · 弹道

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-15-projectile.md)；编辑器需求见 [UXD](../uxd/fx-15-projectile.md)；引擎实现见 [runtime spec](../spec-runtime/fx-15-projectile.md)；编辑器实现见 [editor spec](../spec-editor/fx-15-projectile.md)；现状见 [reference](../reference/fx-15-projectile.md)。

## 1. 定位

LaunchProjectile 效果发射一条弹道实体：按直射或追踪飞行，命中按策略结算并转命中与落点子效果。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期加 projectile 块；speed/range/arcHeight 为整数。
- **飞行两式**：Direction 直射、TrackTarget 追踪；Legacy 一律显式报错。
- **命中两策**：DestroyOnFirstHit 首次命中即毁、ContinueOnHit 贯穿至 maxHitCount；Legacy 一律显式报错。
- **碰撞合同**：hitEffect 必填、collisionHalfWidth 必填正数、collisionRelationFilter 必填；collisionExcludeSource 只可 true 或省略。
- impactEffect 与 presentationEffect 可选；maxHitCount 限定在命中历史容量内（事实页）。

## 3. 运行行为

发射原点取源实体位置；Direction 模式无可解析方向直接抛错；弹道实体经生成队列入世界，效果事务内分阶段提交；生成队列容量满抛错。

## 4. 异常承诺

块与 preset 不匹配、缺必填字段、Legacy 取值、maxHitCount 越界——启动失败并指明字段；运行期方向不可解析、队列容量满——执行失败并抛错。

**相关文档**：[配置说明](../config/fx-15-projectile.md) · 见 fx-11（命中与落点子效果的派发）
