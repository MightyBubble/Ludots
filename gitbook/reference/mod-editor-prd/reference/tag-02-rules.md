# tag-02 reference · Tag 规则

> 现状参考。第一性需求见 [tag-02 PRD](../prd/tag-02-rules.md)；配置说明见 [tag-02 配置说明](../config/tag-02-rules.md)。

## 1. 现状快照

- 规则表 `GAS/tag_rules.json`：六类可选数组（requiredAll/blockedAny/attached/removed/disabledIfAny/removeIfAny），每类 ≤8；同 id 深合并。
- 编译为六组掩码 + 标志；添加期走事务（防环处理集、步数预算），连带目标各自过校验；失败回滚。
- 移除不走事务；热路径整表替换（不注册新名），冷路径新名首现即注册。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 规则形状与编译 | src/Core/Gameplay/GAS/Components/TagRuleSet.cs、TagRuleRegistry.cs |
| 加载器 | src/Core/Gameplay/GAS/Config/TagRuleSetLoader.cs |
| 添加事务（校验/级联/回滚） | src/Core/Gameplay/GAS/TagOps.cs、TagRuleTransaction.cs |
| 预算常量 | src/Core/Gameplay/GAS/GasConstants.cs（见事实页） |
| 真实规则实例 | mods/showcases/arpg_demo/ArpgDemoMod/assets/GAS/tag_rules.json |

**相关文档**：[tag-02 PRD](../prd/tag-02-rules.md) · [tag-01 reference](tag-01-basics.md)
