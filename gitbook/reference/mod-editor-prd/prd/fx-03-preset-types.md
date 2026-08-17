# fx-06 · Preset 类型系统

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-03-preset-types.md)；编辑器需求见 [UXD](../uxd/fx-03-preset-types.md)；引擎实现见 [runtime spec](../spec-runtime/fx-03-preset-types.md)；editor spec 见 [editor spec](../spec-editor/fx-03-preset-types.md)；现状见 [reference](../reference/fx-03-preset-types.md)。

## 1. 定位

preset 类型是效果的行为原型库：每种原型声明活跃相位、允许寿命与默认相位处理器；效果模板选一个原型，再按原型填参数块。

## 2. 产品承诺

- **原型即合同**：十六种内建原型覆盖伤害、治疗、增益、持续伤害与回复、力、搜索、弹道、造兵、关系、兑换、进度、下单、部署消费。
- **可扩展**：mod 可在 preset_types 注册自定义原型，与内建同权；id 从独立段分配，注册表冻结后关闭。
- **声明归声明**：原型的组件清单只是提示作者"要带哪些块"的元数据；块合法性由模板侧联动规则裁决（fx-04）。
- **默认处理器兜底**：模板不提供主图且未显式跳过主槽时，回落到原型的默认相位处理器。

## 3. 运行行为

preset_types 在 graphs 之后、effects 之前加载；效果模板的 presetType 先查注册表再查内建枚举，两处皆无即失败。处理器只有内建与图两种形态。

## 4. 异常承诺

原型的任一字段缺失、处理器形态非法、冻结后注册、效果引用未注册原型——启动失败并指明条目。

**相关文档**：[配置说明](../config/fx-03-preset-types.md) · [fx-04](fx-02-template.md) · [fx-07](fx-05-phases.md)
