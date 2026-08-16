# ai-00 · AI 行为层总论

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-01-utility-overview.md)；编辑器需求见 [UXD](../uxd/ai-01-utility-overview.md)；引擎实现见 [runtime spec](../spec-runtime/ai-01-utility-overview.md)；编辑器实现见 [editor spec](../spec-editor/ai-01-utility-overview.md)；现状见 [reference](../reference/ai-01-utility-overview.md)。

## 1. 定位

AI 行为层让 mod 以纯配置定义"单位自己会做什么"：效用 AI 十表（输入→归一化→曲线→决策→决策者→档案）与三套行为引擎（行为树、HFSM、GOAP/HTN），全部经 18 张配置表编译进一份 AiCompiledRuntime。

## 2. 产品承诺

- **一切皆表**：18 张 `AI/*.json` 全部 ArrayById 合并（htn_domain 例外，DeepObject）；mod 与主仓同表叠加，id 即全局命名。
- **三接缝即边界**：效用 AI 只能经 GraphScore 读图、经 SubmitOrder 写订单、经 AbilityKey 引技能——写世界被结构性禁止。
- **两套决策节奏**：效用 AI 按 profile 间隔自动思考；BT/HFSM 由调用方驱动 think wave——配置只描述行为，不描述调度。
- **至少一个 profile**：效用十表任一非空，则 profiles 必须非空，否则整包报错。

## 3. 运行行为

加载序：atoms→projection→utility goals→goap_actions→goap_goals→htn_domain→效用十表→BT+HFSM，产出 AiCompiledRuntime 九字段。效用 AI 运行环：ThinkSchedule 唤醒→Decision 评估（过滤器→考量→择优→提交任务）→订单入 OrderQueue；订单缓冲未清空前不再思考。

## 4. 异常承诺

引用未定义的 input/normalization/curve/task/decision/decision maker/profile、Tasks/Decisions/DecisionMakers 非连续区间、无 profile 的效用配置、utility 配置缺校验上下文——启动失败并给出表名+条目+字段路径。

**相关文档**：[配置说明](../config/ai-01-utility-overview.md) · [ai-01](ai-02-inputs.md) · [ai-08](ai-09-behavior-trees.md) · [ai-10](ai-11-goap-htn.md)
