# Ludots Shared Skills

`skills/` 是 Ludots 共享 agent skill 的仓库内 SSOT。

这套结构服务于 Codex、Claude 以及后续接入的其他 agent，规则如下：

- 仓库内分层目录是唯一真相，运行时安装目录只允许由同步脚本生成。
- leaf skill 必须保持统一结构：`SKILL.md`、`agents/openai.yaml`、`agents/claude.md`、按需 `references/`、`scripts/`、`assets/`。
- hook 采用显式文件契约，不允许“偷偷调用另一个 agent”。跨 agent 协作只能通过 packet + artifact + registry 完成。
- 视觉证据是一等公民：截图、录屏、关键帧、联系图、审阅结果都必须可追溯。
- 所有长时运行 skill 都必须有边界：前置检查、启动超时、完成超时、最大重试次数、blocked 退出。
- 不做兼容层；旧的本地 skill 副本视为同步目标，不再是规范来源。

## 1 分层结构

| 目录 | 作用 |
|------|------|
| `skills/governance/` | 文档、规则、SSOT、设计-实现对齐治理 |
| `skills/collaboration/` | 云端接力、handoff、Issue/PR 编排 |
| `skills/delivery/` | Feature 交付与验收证据 |
| `skills/evidence/` | 截图、录屏、关键帧抽取、可视化证据产出 |
| `skills/audit/` | 技术债熔断、视觉审计、特种审计 |
| `skills/tooling/` | hook 编排、CI gate、同步与校验配套 |
| `skills/contracts/` | hook packet 与证据 JSON 契约 |

## 2 当前技能清单

| Skill | 层级 | 用途 |
|------|------|------|
| `ludots-doc-governance` | governance | 文档 SSOT、链接与证据治理 |
| `ludots-feature-delivery` | delivery | 基建优先的功能交付、showcase/UI 验收与证据 |
| `ludots-tech-debt-fuse` | audit | 跨层技术债升级与熔断 |
| `ludots-cloud-handoff` | collaboration | 云端开发接力与上下文交接 |
| `ludots-pr-issue-orchestration` | collaboration | Issue / PR 证据包编排 |
| `ludots-visual-capture` | evidence | 截图/录屏捕获与证据清单产出 |
| `ludots-video-frame-extract` | evidence | 录屏关键帧与联系图抽取 |
| `ludots-visual-review` | audit | 基于关键帧和截图的视觉审阅 |
| `ludots-hook-orchestrator` | tooling | hook packet 校验与后续 skill 路由 |
| `ludots-ci-audit-gate` | tooling | PR / CI 证据完整性 gate |

完整元数据见 `skills/registry.json`。

## 3 Hook 协作模型

### 3.1 核心事件

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

### 3.2 标准链路

1. Feature / PR 流程发出 `visual.capture.requested`
2. `ludots-visual-capture` 产出证据清单并发出 `visual.capture.completed`
3. `ludots-video-frame-extract` 消费录屏并发出 `visual.frames.ready`
4. `ludots-visual-review` 完成视觉审阅并发出 `visual.review.completed`
5. `ludots-cloud-handoff` 组织上下文并发出 `handoff.ready`
6. `ludots-pr-issue-orchestration` 产出 PR / Issue 包并发出 `pr.packet.ready`
7. `ludots-ci-audit-gate` 校验证据完整性并发出 `ci.audit.completed`

任一阶段若未在预算内启动、无进展、或前置条件不满足，必须发出对应 `*.blocked` packet，而不是继续等待。

`ludots-hook-orchestrator` 负责按 `skills/registry.json` 路由和校验，不直接替代其他 skill 的职责。

## 4 Git 与运行时管理

- 版本管理：所有共享 skill 一律随仓库 Git 管理。
- 注册表：`skills/registry.json` 是唯一安装索引。
- 契约：`skills/contracts/*.json` 是唯一机器契约。
- 校验：`scripts/validate-skills.ps1` 负责结构、frontmatter、hook 与引用校验。
- 同步：`scripts/sync-skills.ps1` 将分层目录扁平同步到本地运行时目录：
  - Codex：`$HOME/.codex/skills/`
  - Claude：`$HOME/.claude/skills/`

同步后的目录不是手工编辑入口。需要变更 skill 时，必须改仓库内源文件再重新同步。

## 5 产物约定

推荐把技能运行产物写到以下目录：

- `artifacts/agent-hooks/`：hook packet
- `artifacts/evidence/`：截图、录屏、关键帧、清单
- `artifacts/reviews/`：视觉审阅与审计结果
- `artifacts/handoffs/`：handoff 包
- `artifacts/pr/`：Issue / PR packet
- `artifacts/ci-audit/`：CI gate 结果

blocked 退出时，hook packet 至少应记录：

- timeout / retry 预算
- 当前阶段
- 最后一次观测到的进展
- blocker code / summary / retryable

具体 JSON 结构见：

- `skills/contracts/hook.schema.json`
- `skills/contracts/evidence-manifest.schema.json`
- `skills/contracts/review-result.schema.json`
