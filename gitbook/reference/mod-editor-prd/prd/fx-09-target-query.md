# fx-09 · 目标查询

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-09-target-query.md)；编辑器需求见 [UXD](../uxd/fx-09-target-query.md)；引擎实现见 [runtime spec](../spec-runtime/fx-09-target-query.md)；editor spec 见 [editor spec](../spec-editor/fx-09-target-query.md)；现状见 [reference](../reference/fx-09-target-query.md)。

## 1. 定位

目标查询把"打谁"从固定目标变成空间问题：五种形状、两种原点，或一张自绘查询图。

## 2. 产品承诺

- **形状字段互斥**：圆、锥、矩形、线、环各有一组必填边界字段，写错组合启动失败——不存在"缺半径的圆"。
- **原点二值**：默认原点或以施法者为原点；语义固定可预期。
- **动态查询等权**：挂查询图时空间字段全禁，候选集完全由图产出。
- **查询中心规则确定**：方向形状以施法者为参考中心，其余先取目标点、无目标再回退施法者。
- **查询归查询**：查询块只产出候选，敌我过滤与数量裁剪一律在过滤块（fx-10）。

## 3. 运行行为

查询在裁决相位执行（搜索类 preset），产出候选数；配合派发块形成扇出链（fx-11）。

## 4. 异常承诺

形状边界字段缺失或非正、环内径越界、挂图时残留空间字段——启动失败并指明模板。

**相关文档**：[配置说明](../config/fx-09-target-query.md) · [fx-10](fx-10-target-filter.md) · [fx-11](fx-11-target-dispatch.md) · [gr-op-06](gr-op-06-spatial.md)
