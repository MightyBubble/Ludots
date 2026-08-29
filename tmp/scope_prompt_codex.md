你是架构研究员。读仓库内 C:\001_AI\LudotsProd\.epic-briefs\activity-scope-research-brief.md（研究简报）与 .epic-briefs/activity-v4-spec.md（v4 规格），完成简报中的研究任务。

你的侧重（另有一位研究员负责投票语义侧，你负责**作用域与引擎绑定侧**，勿越界写投票规则细节）：
1. 作用域分类学的落地：per-representative / per-team / per-faction / global 在引擎现有实体模型（Representative Entity、Players 绑定、mapSessions、seat）上的绑定方式；全局实例 vs 每人一份 fan-out 的 schema 区分；准入键（定义id, scopeKey）在全局语义下怎么变。
2. 面板与命令的作用域化：DataPlane topic 的 per-seat 订阅、activity.confirm 的 seat 维度、呈现 cue 的投递对象。
3. 触发轨（TriggerGraph OfferActivity）如何表达"给全部玩家/给某队/给全局"——需要新 op 还是 LoadPlacedEntity/查询组合够用。
4. 给出 v5 schema 增量草案（scope 相关部分）+ 作用域侧待裁定点 + 引擎缺口清单（标注已有可复用/需新增）。

只读仓库；输出中文 markdown，直接给最终文档。
