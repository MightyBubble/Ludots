# fx-02 · 效果模板骨架

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-02-template.md)；编辑器需求见 [UXD](../uxd/fx-02-template.md)；引擎实现见 [runtime spec](../spec-runtime/fx-02-template.md)；editor spec 见 [editor spec](../spec-editor/fx-02-template.md)；现状见 [reference](../reference/fx-02-template.md)。

## 1. 定位

效果模板是效果的唯一声明单位：身份、原型、寿命、响应参与，加至多一组按原型收窄的参数块。

## 2. 产品承诺

- **必填全显式**：presetType、lifetime、participatesInResponse 三项没有默认值——不写即失败，不存在隐式效果。
- **身份即一致**：条目 id 与模板 id 逐字相同；`categories` 至多一枚，作效果分类（非玩法 Tag）。
- **原型收窄参数面**：presetType 决定哪些参数块合法且必须携带；多写、少写、写错块一律启动失败。
- **热通道窄**：只有时长、周期、首个修改器数值、弹道效果引用、固定授予 tag 等有限字段可热替换；改身份与结构是重启级。
- **执行计划先编译后运行**：模板注册冻结后编译四窗口执行计划，运行期只消费编译产物。

## 3. 运行行为

效果表在 preset_types 之后加载注册；全部加载器跑完后注册表冻结；此后每模板的执行计划四窗口全部编译完成才允许进入运行。

## 4. 异常承诺

id 不一致、tags 超一枚、presetType 未注册、lifetime 非三值、标量时长、非法参数块组合、冻结后注册、重复 id——启动失败并指明条目。

**相关文档**：[配置说明](../config/fx-02-template.md) · [fx-01](fx-01-pipeline.md) · [fx-03](fx-03-preset-types.md) · [fx-04](fx-04-lifetime.md)
