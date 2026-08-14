# PR #660 架构规范审计报告

- 审计日期：2026-07-25
- 审计工作区：`C:\001_AI\LudotsProd\.worktrees\audit-pr660`（干净 detached worktree）
- PR head：`832481ece355f7b94b5db5504d7d96542eca4677`（"test(gas): enforce ability exec hot path via il guard"）
- 合并基：`origin/main` @ `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`
- 规模：116 commits / 473 files / +51,300 / −26,851
- 审计依据：`gitbook/contributing/ai-assisted-development.md`、`gitbook/contributing/coding-standards.md`（四个禁止、ECS 硬性约束、六边形架构、一切皆 Mod）

## 一、独立验证结果（在干净 worktree 中实际执行）

| 验证项 | PR 证据声称 | 独立复跑结果 |
|---|---|---|
| ArchitectureTests 全量 | 188/188 PASS | **188/188 PASS（1m04s）**，含 IL 级热路径守卫 |
| GasTests 证据过滤器组合（8 个套件） | 分项 200/95/56/49/73/105 PASS | **合并 273/273 PASS（3m31s）** |
| `git diff --check` | PASS | 未复跑（低风险项） |

结论：PR 自带的 `artifacts/ci-audit/pr660/result.md` 证据链**诚实且可复现**。该产物还显式声明了三个与本次修复无关的脏文件并排除在证据外——这是规范的证据态度，加分。

## 二、架构合规逐项裁定

### 2.1 六边形架构 — PASS
`src/Core`（140 文件，+11,987/−3,240）未发现平台 API 引用；架构测试套件以 IL 检查强制 `World.Add/Remove<AbilityExecInstance>` 不得出现在热路径，比源码文本扫描更严格，属于治理升级。

### 2.2 四个禁止 — PASS
- **fallback**：新增代码扫描零命中；缺 effect/event/graph 服务时走 typed failure 而非静默跳过（证据与代码一致）。
- **向后兼容**：唯一 "Legacy" 命中是**删除**旧枚举值并对残留配置 fail-fast throw——正是"禁止向后兼容"的正确执行方式。
- **重复造轮子**：自审清单列出复用 `AbilityExecSystem` / `OrderSubmitter` / `OrderContinuationSystem` / `InputOrderMappingSystem` 等既有基建，未新建 Registry / loader / 平行管线；抽查与 diff 一致。
- **跨越职责**：showcase 侧 88 文件多为配置与适配既有接口的运行时装配，未见业务逻辑上漏进 Core。

### 2.3 ECS 硬性约束 — PASS（附注）
- 新增 `EffectPhaseSideEffectTransaction`（+1,605 行）为固定容量 staging 边界：所有副作用先入栈、相位全部成功后才提交世界，失败可整体回滚——符合"结构变更经 CommandBuffer/正式回放链路"。数组在构造期一次性分配，非热路径逐帧分配。
- 新增代码中 38 处 `float` 全部位于 GAS 属性系统，与既有 `AttributeBuffer`（`fixed float` 缓冲）的设计一致；Fix64 规则针对位置/物理数值，此处不构成违规。
- 新增代码无空 catch、无 TODO/FIXME/HACK 残留。

### 2.4 GAS 组合自审 — PASS（附卫生问题）
`artifacts/gas-composition-gate.md` 按 skill 要求完整填写（核心判断 / 层归属 / 复用清单 / 事务边界 / Config SSOT / 红旗扫描）。**问题**：文件头部出现编码损坏（BOM + `鈥?` 乱码替代 em-dash），应修复为干净 UTF-8。

### 2.5 文档同步 — PASS
新增《GAS、订单与输入运行时合同》架构文档并注册进 `gitbook/SUMMARY.md`；57 张架构图同步更新；行为变更伴随文档，符合提交要求。

### 2.6 提交规范 — MINOR VIOLATION
116 个提交中 4 个不符合 `<type>(<scope>): <description>` 格式（如 `Fix order admission transaction boundaries`、`fix pr660 final order admission...`）。若 squash merge 影响有限，但规范层面违规成立。

## 三、扣分项

1. **PR 体量失控（−6）**：116 commits / 473 文件 / 5.1 万行新增，混合了 ordering 主线修复、spawn 重构、graph runtime、road、showcase 配置等多个逻辑单元。单 PR 可审查性极低，违反"沿现有管线做增量"的精神，应拆分为 4–6 个独立 PR。
2. **提交信息格式（−2）**：4/116 不合规。
3. **自审产物编码损坏（−2）**：`artifacts/gas-composition-gate.md` 头部乱码。
4. **全量 GasTests 未经独立验证（−2）**：PR 证据与本次审计均只覆盖定向过滤器组合；全量套件（含 showcase 验收测试）未在本环境复跑，存在未知回归窗口。

## 四、亮点（超出合规基线）

- 用编译后 IL 检查替代源码文本扫描做热路径治理，是仓库治理工具的实质性进步。
- 证据产物明确区分"当前完成声称"与"历史上下文"，不夸大推送前的远端状态。
- 事务化 staging（EffectPhaseSideEffectTransaction）把"原子性"从口头约束落为类型边界。

## 五、评分

**88 / 100（A−，架构合规，建议修复扣分项后合并）**

| 维度 | 得分 |
|---|---|
| 六边形架构 / 分层 | 20/20 |
| 四个禁止 | 20/20 |
| ECS 硬性约束 | 18/20 |
| 测试与证据可复现性 | 18/20 |
| 文档与自审流程 | 9/10 |
| 变更规模与提交卫生 | 3/10 |
