# fx-12 · 效果授予 Tag

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-13-granted-tags.md)；编辑器需求见 [UXD](../uxd/fx-13-granted-tags.md)；引擎实现见 [runtime spec](../spec-runtime/fx-13-granted-tags.md)；编辑器实现见 [editor spec](../spec-editor/fx-13-granted-tags.md)；现状见 [reference](../reference/fx-13-granted-tags.md)。

## 1. 定位

grantedTags 让效果把 tag 作为"有主状态"授给目标：效果在则 tag 计数在，效果走则按授予量收回；贡献量随效果层数按公式伸缩。

## 2. 产品承诺

- **公式三选一**：Fixed 与层数无关；Linear 随层数线性放大；LinearPlusBase 保底基线再加线性项。
- **差量合并**：堆叠刷新层数时只调"新旧层数贡献差"，不整体重算、不闪断。
- **失败原子**：授予或回收中途被 tag 规则拒绝、计数容量满，先完整恢复现场再上抛；不留半授予状态。
- **同生同灭**：过期、移除、打断都按移除时层数回收授予量。
- 单效果授予条数与计数上限见事实页。

## 3. 运行行为

授予发生在效果应用事务内，回收发生在过期与移除事务内，两者都是分阶段提交的副作用；amount 与 base 是非负计数，越界按计数上限钳制。

## 4. 异常承诺

tag 规则拒绝、计数容量满——事务失败并指明效果与 tag；GraphProgram 公式在加载期一律拒绝，理由写明"tag 贡献图评估器未接线"。

**相关文档**：[配置说明](../config/fx-13-granted-tags.md) · [tag-01](tag-01-basics.md) · 见 fx-02、fx-12 前后各篇（总目录见 README）
