# ai-11 · 行为树

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-09-behavior-trees.md)；编辑器需求见 [UXD](../uxd/ai-09-behavior-trees.md)；引擎实现见 [runtime spec](../spec-runtime/ai-09-behavior-trees.md)；editor spec 见 [editor spec](../spec-editor/ai-09-behavior-trees.md)；现状见 [reference](../reference/ai-09-behavior-trees.md)。

## 1. 定位

行为树是图驱动的反应式行为：Selector/Sequence 组合，Condition/Action 叶挂 ScriptSlice 图脚本。树描述"怎么想"，由调用方在 think wave 里驱动"何时想"。

## 2. 产品承诺

- **树即数据**：root+nodes 扁平声明，加载时 BFS 打包校验（id 去重、禁多父、禁不可达）；action 仅 ScriptSlice 合法且必须指向 ActionLib。
- **枚举严格**：kind/leaf 大小写敏感（与 utility 十表不同，I2）；非法值报错。
- **Condition 必须 halt**：脚本 ReturnInt≠0=Success，否则 Failure；Action 可 Yield 跨波续跑（cursor 恢复）。
- **预算与上限**：每树节点、栈深、思考周期、脚本步预算四常量封顶（见 facts）。

## 3. 运行行为

调用方先 RestartAllThinking 再 TickAll(scriptBudgetSteps) 驱动所有 agent：Sequence 依次子节点遇 Failure 回退，Selector 依次遇 Success 即成；叶节点按 binding 求值（AlwaysSuccess/AlwaysFailure/HoldRunning 直出，ScriptSlice 跑图）。状态跨波由 cursor 与脚本游标保持。

## 4. 异常承诺

root 缺失、nodes 空、id 重复、kind/leaf 非法（含大小写）、多父/不可达、action 挂非 ScriptSlice 叶、action 名不在 ActionLib——启动失败并带树 id 与节点 id。

**相关文档**：[配置说明](../config/ai-09-behavior-trees.md) · [ai-11](ai-10-hfsm.md) · [ai-02](ai-01-utility-overview.md)
