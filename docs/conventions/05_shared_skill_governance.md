# 共享 Skill 治理

本规范定义 Ludots 仓库内共享 agent skill 的正式治理规则。`skills/` 是共享 skill 的源码 SSOT；本地运行时目录只是同步产物，不是编辑入口。

## 1 目标与边界

共享 skill 用于承载跨 agent、跨会话、跨云端协作的稳定工作流，包括：

- 文档与规范治理
- 功能交付与验收证据
- 云端 handoff、Issue / PR 编排
- 截图、录屏、关键帧、视觉审计
- hook 契约、CI gate、运行时同步

**禁止行为：**

- 在本地运行时目录手工修改 skill，再反向同步回仓库
- 为不同 agent 维护两套逻辑不同的 skill 正文
- 通过“隐式 agent 调 agent”替代显式 hook 契约
- 为旧版路径或旧版 skill 结构保留兼容层
- 对长时运行 skill 进行无限等待、无限轮询或无上限重试

## 2 SSOT 结构

共享 skill 统一位于 `skills/`，按职责分层：

| 目录 | 职责 |
|------|------|
| `skills/governance/` | 文档、规则、对齐治理 |
| `skills/collaboration/` | 云端 handoff、Issue / PR 编排 |
| `skills/delivery/` | Feature 交付与验收 |
| `skills/evidence/` | 截图、录屏、关键帧与证据清单 |
| `skills/audit/` | 视觉审计、技术债熔断、特种审计 |
| `skills/tooling/` | hook 编排、CI gate、同步与校验 |
| `skills/contracts/` | 机器契约 |

`skills/README.md` 是共享 skill 目录入口，`skills/registry.json` 是机器可消费的唯一注册表。

## 3 Leaf Skill 结构

每个 leaf skill 必须具备：

- `SKILL.md`
- `agents/openai.yaml`
- `agents/claude.md`
- 按需 `references/`
- 按需 `scripts/`
- 按需 `assets/`

leaf skill 目录内不得新增 `README.md`、`CHANGELOG.md`、`INSTALL.md` 等辅助说明文件，避免重复入口和上下文污染。

## 4 Hook 与证据契约

跨 agent 协作必须通过文件契约完成。正式契约位于：

- `skills/contracts/hook.schema.json`
- `skills/contracts/evidence-manifest.schema.json`
- `skills/contracts/review-result.schema.json`

正式 hook 名称统一登记在 `skills/registry.json`，包括但不限于：

- `visual.capture.requested`
- `visual.capture.completed`
- `visual.capture.blocked`
- `visual.frames.ready`
- `visual.frames.blocked`
- `visual.review.completed`
- `visual.review.blocked`
- `handoff.ready`
- `pr.packet.ready`
- `ci.audit.completed`

禁止在 skill 正文或脚本里擅自引入未登记 hook 名称。

### 4.1 有界执行与阻塞退出

所有长时运行 skill 必须显式定义：

- 前置检查条件
- 启动超时
- 完成超时
- 轮询间隔
- 最大自动重试次数
- blocked 退出条件

规则如下：

- 超过启动预算仍未进入目标状态，必须停止并发出 blocked packet
- 连续一段时间无进展，必须停止并发出 blocked packet
- 前置依赖缺失时，不得进入等待循环，必须立即 blocked
- 默认最多允许 1 次自动重试；更高重试次数必须由用户明确授权
- blocked packet 必须记录 timeout / retry 预算、阶段、最后进展和 blocker 详情

## 5 视觉证据是一等公民

对 UI、渲染、交互、录屏审计相关工作，截图与录屏不是可选附件，而是正式交付物。

规则如下：

- 录屏与截图都必须进入 `artifacts/evidence/`
- 录屏进入审计前，应先抽取关键帧或联系图，避免审阅依赖“整段肉眼播放”
- 视觉审阅结果必须结构化落地到 `artifacts/reviews/`
- 视觉阻塞必须显式发出 `visual.capture.blocked`、`visual.frames.blocked` 或 `visual.review.blocked`

## 6 Git、校验与同步

共享 skill 通过 Git 管理，校验与同步入口如下：

- 校验脚本：`scripts/validate-skills.ps1`
- 同步脚本：`scripts/sync-skills.ps1`
- CI 工作流：`.github/workflows/skills-governance.yml`
- Owner 归属：`.github/CODEOWNERS`

`scripts/sync-skills.ps1` 将仓库内分层目录扁平同步到运行时目录：

- Codex：`$HOME/.codex/skills/`
- Claude：`$HOME/.claude/skills/`

同步结果不得作为手工编辑入口。所有变更必须先改仓库源文件，再重新同步。

## 7 变更规则

当共享 skill 的工作流、契约、hook、目录结构发生变化时，必须同一提交或同一 PR 内同步更新：

- `skills/README.md`
- `skills/registry.json`
- 对应 contract schema
- 对应 `SKILL.md` 与 agent 元数据
- 必要的脚本、CI、CODEOWNERS、入口文档

## 8 相关文档

- 开发规范总索引：见 [README.md](README.md)
- 文档治理规范：见 [04_documentation_governance.md](04_documentation_governance.md)
- 共享 Skill 入口：见 [`../../skills/README.md`](../../skills/README.md)
