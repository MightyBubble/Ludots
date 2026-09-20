# ab-04 · 冷却三件套

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ab-04-cooldown.md)；编辑器需求见 [UXD](../uxd/ab-04-cooldown.md)；引擎实现见 [runtime spec](../spec-runtime/ab-04-cooldown.md)；editor spec 见 [editor spec](../spec-editor/ab-04-cooldown.md)；现状见 [reference](../reference/ab-04-cooldown.md)。

## 1. 定位

冷却是"这段时间内不许再施放"的机制。它由三件组成：声明冷却的数据契约（cooldown 块）、真正挡住再次施放的 tag 闭环（时间轴加 tag + 激活门拒 tag）、AI 侧的就绪判定。三件各管一段，没有一件内建魔法。

## 2. 产品承诺

- **闭环可组合**：实战冷却 = 时间轴起点给自己挂冷却 tag（定时到期）+ 激活门把该 tag 列为禁止——两块普通积木拼出冷却，任何 mod 可拼。
- **到期自动回收**：定时 tag 到期由到期体系自动移除，冷却结束无需写移除条目。
- **数据契约独立**：cooldown 块声明"冷却的度量"（一个属性或一个 tag），供 AI 与界面查询就绪，不负责挡施放。
- **AI 与人同规则**：AI 判就绪读的同一份契约与同一批 tag，不允许 AI 走另一套冷却。
- **共享冷却**：多个技能声明同一冷却 tag 即共享冷却——一个转圈全部转圈。

## 3. 运行行为

起播时间轴的 TagClip 加冷却 tag 并预约到期；期间再次激活被激活门的 blockTags 拒绝；到期体系移除 tag 后恢复可施放。AI 决策前按契约查就绪，提交后记共享冷却窗口。

## 4. 异常承诺

cooldown 块引用未注册属性、或属性与 tag 皆空——启动失败；冷却期间再施放是激活拒绝（可观察原因），不是错误。

**相关文档**：[配置说明](../config/ab-04-cooldown.md) · [ab-02](ab-02-exec-timeline.md) · [ab-05](ab-05-activation-gates.md)
