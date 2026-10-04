# GAS Composition Gate · 属性层来源回滚（#1535 revert，refs #1460/#1720）

## Step 0 适用范围判定

本任务为整体回滚（`git revert -m 1 0cc88ce375`），不新增 BuiltinHandlerId、EffectPresetType、profile schema、graph op、spawn/morph 编排或声明式开关——按 skill Step 0 可跳过完整 gate，本文件记录判定依据。

## 组合判断

新变体是新增 graph 节点 / effect 步骤，还是新增 profile enum / preset 开关？

答：两者皆无。回滚移除一条被否决的取数通道（属性组件内联来源槽 + 写通道 source 穿参），不引入任何替代变体；写入者供给的方向性工作归 effect 域（#1720 另票）。

## 复用 / 新增清单

| 类型 | 项 |
|------|-----|
| 复用 | 既有 DirtyFlags 脏位/清位合同、DeferredTriggerQueue、AttributeMutationOps 原签名、GasEventTriggerBridgeSystem 的 SourceEntity |
| 新增 Layer 0-2 | 无 |
| 禁止项核查 | 无新 profile DSL、无平行加载器、无 inherit/mode 枚举；DirtyFlags 恢复 ≤48B 热路径合同 |

## 验证

- `git grep AttributeSourceSlots|RecordAttributeSource|GetAttributeSource` 零命中。
- HotPath 布局测试预期由红转绿；GAS 延迟触发 / 效果事务测试集在回滚树上运行（见 PR 描述）。
