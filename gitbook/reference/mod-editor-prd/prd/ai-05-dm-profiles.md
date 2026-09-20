# ai-05 · 决策者与档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-05-dm-profiles.md)；编辑器需求见 [UXD](../uxd/ai-05-dm-profiles.md)；引擎实现见 [runtime spec](../spec-runtime/ai-05-dm-profiles.md)；editor spec 见 [editor spec](../spec-editor/ai-05-dm-profiles.md)；现状见 [reference](../reference/ai-05-dm-profiles.md)。

## 1. 定位

decision maker 把一组决策排成竞技场，profile 把决策者打包成可挂到实体上的"性格"。实体带 UtilityAiAgent(ProfileId) 即获得该性格的自动施法行为——两层都是连续区间的引用链。

## 2. 产品承诺

- **两种择优模式**：UtilityScore 按分选（SwitchMargin 抑制抖动：超 best+margin 才换）；FixedPriority 按 Priority 定序。
- **margin 内有次级序**：分数差在 margin 内时先比优先桶再比距离——近者胜。
- **节奏参数**：DecisionIntervalSteps 控制思考步频，MaxCandidates 控制单次评估候选上限。
- **至少一个 profile**：效用十表非空则 profiles 必须非空；DefaultStance 用语义键（数字 id 显式拒绝）。

## 3. 运行行为

决策环每 interval 步跑一次：决策者内逐决策×逐候选评估，UtilityScore 模式下挑战者须超 best+margin 才替换（当前决策带 momentum 加分）；胜者提交任务。订单缓冲未清空（HasActive/Queued/Pending）时本轮跳过。

## 4. 异常承诺

Decisions 为空或不连续、DecisionMakers 为空或不连续、DecisionIntervalSteps/MaxCandidates 非正、DefaultStanceId 数字写法、十表非空而无 profile——启动失败并带路径。

**相关文档**：[配置说明](../config/ai-05-dm-profiles.md) · [ai-04](ai-04-decisions.md) · [ai-08](ai-08-stances-actuators.md)
