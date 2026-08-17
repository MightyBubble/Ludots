# fx-14 · 目标过滤

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-10-target-filter.md)；编辑器需求见 [UXD](../uxd/fx-10-target-filter.md)；引擎实现见 [runtime spec](../spec-runtime/fx-10-target-filter.md)；editor spec 见 [editor spec](../spec-editor/fx-10-target-filter.md)；现状见 [reference](../reference/fx-10-target-filter.md)。

## 1. 定位

目标过滤是候选集的裁决层：空间查询回答"范围内有谁"，过滤回答"其中谁算数"。

## 2. 产品承诺

- **过滤序固定**：排除施法者→环内径→层→敌我→数量→根预算，顺序不可配置、结果可预期。
- **敌我六值**：全部、敌对、友方、中立、非友、非敌；判定要求双方都有阵营，缺阵营一律滤除。
- **数量上限直白**：上限为零表示不限量；非零即截取前 N 个候选。
- **层掩码可选**：按层挑候选，缺省不过滤层。
- **必填不省心**：排除源与敌我关系必须显式声明——作者必须想清楚"会不会打自己人"。

## 3. 运行行为

过滤在派发前的链路上执行；环内径过滤只对环形查询生效。根预算是最后一道闸（fx-09）。

## 4. 异常承诺

敌我关系非六值、必填字段缺失、层未注册——启动失败并指明模板。

**相关文档**：[配置说明](../config/fx-10-target-filter.md) · [fx-11](fx-09-target-query.md) · [fx-14](fx-11-target-dispatch.md) · [tag-01](tag-01-basics.md)
