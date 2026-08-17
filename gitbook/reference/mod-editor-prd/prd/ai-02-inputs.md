# ai-04 · 效用输入

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-02-inputs.md)；编辑器需求见 [UXD](../uxd/ai-02-inputs.md)；引擎实现见 [runtime spec](../spec-runtime/ai-02-inputs.md)；editor spec 见 [editor spec](../spec-editor/ai-02-inputs.md)；现状见 [reference](../reference/ai-02-inputs.md)。

## 1. 定位

input 是效用 AI 的感知原语：决策考量采样世界时的八种读法（常量、距离、目标桶、就绪度、图分数、双方 tag、技能就绪）。每个 input 是可复用的一次采样定义。

## 2. 产品承诺

- **八种 Kind 一套结构**：一个 id + 一个 Kind + Kind 专属参数；被任意多个考量引用。
- **图只读**：GraphScore 只允许指向 Score 图，写操作被编译期黑名单拒绝——感知不能改世界。
- **引用即校验**：Tag 与 AbilityKey 在编译期对注册表核验；GraphKey/GraphId 二选一。
- **采样失败即 0**：运行期组件缺失、图执行异常，返回 0 而非崩溃。

## 3. 运行行为

决策评估时逐考量调用 SampleInput：Constant 返回 Value；DistanceToTarget 返回 actor→target 距离；TargetPriorityBucket 读目标 UtilityAiTargetPriority.Bucket；ActuatorReadiness01 读执行器就绪；GraphScore 执行 Score 图取输出；两个 HasTag 判 tag 容器；AbilityReady 查技能就绪。

## 4. 异常承诺

未知 Kind、ActuatorId 非正、GraphScore 指非 Score 图或写 op 图、AbilityKey 未注册、Tag 未注册——启动失败并给 路径.字段。

**相关文档**：[配置说明](../config/ai-02-inputs.md) · [ai-04](ai-03-norm-curves.md) · [ai-05](ai-04-decisions.md)
