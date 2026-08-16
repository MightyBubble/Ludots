# tag-02 · Tag 规则

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/tag-02-rules.md)；编辑器需求见 [UXD](../uxd/tag-02-rules.md)；引擎实现见 [runtime spec](../spec-runtime/tag-02-rules.md)；编辑器实现见 [editor spec](../spec-editor/tag-02-rules.md)；现状见 [reference](../reference/tag-02-rules.md)。

## 1. 定位

Tag 规则是状态间的相互作用律："隐身时不能格挡"、"被点燃附带 Visible.ByFire"、"驱散清掉所有 Debuff"。一张集中表，声明加某个 tag 时的连带后果。

## 2. 产品承诺

- **六类规则**：前置（requiredAll）、互斥（blockedAny）、连带授予（attached）、连带移除（removed）、条件禁用（disabledIfAny）、条件自清（removeIfAny）——覆盖状态机的常见相互作用。
- **事务性**：规则连带的级联在事务内闭环，带步数预算，环与超限即失败回滚。
- **集中表**：规则只在 `GAS/tag_rules.json` 声明（同 id 深合并），不散落各处。
- **可热替换**：既有规则的替换走工作台热通道（规则表替换），新增 tag 身份仍是重启级。

## 3. 运行行为

加 tag 时若命中规则即进事务：校验前置/互斥 → 写入 → 执行连带授予/移除 → 条件清理；减层与移除不走事务。

## 4. 异常承诺

前置不满足/互斥命中则拒绝添加；事务超预算即失败回滚并报错。

**相关文档**：[配置说明](../config/tag-02-rules.md) · [UXD](../uxd/tag-02-rules.md) · [tag-01](tag-01-basics.md)
