# fx-20 · 造单位

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-16-unit-creation.md)；编辑器需求见 [UXD](../uxd/fx-16-unit-creation.md)；引擎实现见 [runtime spec](../spec-runtime/fx-16-unit-creation.md)；editor spec 见 [editor spec](../spec-editor/fx-16-unit-creation.md)；现状见 [reference](../reference/fx-16-unit-creation.md)。

## 1. 定位

CreateUnit 效果一次生成若干单位：模板实体或 unitType 装配，摆放图案与朝向决定落位，出生效果链在落地时触发。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期加 unitCreation 块；unitType 与 templateId 恰选其一。
- **摆放两式**：Scatter 散布（禁朝向图案、环形半径与起始角）；Circle 环形（半径必填正数、起始角必填、禁散布半径）——两式的禁配字段写了即启动失败。
- **朝向四式**：PreserveTemplate（缺省）、RadialOutward、TangentClockwise、TangentCounterClockwise，仅 Circle 提供朝向。
- **归属开关**：copySourcePlayerOwner 与 linkSourceAsParent 只可 true 或省略；队伍固定继承源。
- onSpawnEffect 可选，引用出生时施加的效果。

## 3. 运行行为

逐个 count 循环计算摆放偏移与朝向后经实体生成队列入队；出生点由目标点保留参数解析；生成队列容量满抛错。

## 4. 异常承诺

来源二选一违例、图案字段违例、未知出生效果引用、count 非正——启动失败并指明字段；运行期队列容量满——执行失败抛错。

**相关文档**：[配置说明](../config/fx-16-unit-creation.md) · [ent-01](ent-01-templates.md) · @@fx21@@（出生后自动下单）
