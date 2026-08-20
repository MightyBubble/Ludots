# 共享 Skill 治理

本页定义 Ludots 共享 agent skill 的正式治理要求。

## 1 正式源

- `skills/` 是共享 skill 的源码真相
- `skills/README.md` 是人类入口
- `skills/registry.json` 是机器可消费的唯一注册表

## 2 结构规则

共享 skill 按职责分层：

- `skills/governance/`
- `skills/collaboration/`
- `skills/delivery/`
- `skills/evidence/`
- `skills/audit/`
- `skills/tooling/`
- `skills/contracts/`

leaf skill 必须具备：

- `SKILL.md`
- `agents/openai.yaml`
- `agents/claude.md`
- 按需 `references/`、`scripts/`、`assets/`

## 3 协作规则

- 跨 agent 协作必须通过显式 hook 契约完成
- 视觉证据是一等公民
- 长时运行 skill 必须有启动预算、完成预算和 blocked 退出条件
- 不为旧路径或旧结构保留兼容层

## 4 工具链

- 校验：`scripts/validate-skills.ps1`
- 同步：`scripts/sync-skills.ps1`（Codex / Claude / Pi）
- CI：`.github/workflows/skills-governance.yml`
- Owner：`.github/CODEOWNERS`

## 5 深度材料

- 仓库深度版：`docs/conventions/05_shared_skill_governance.md`
- 技能入口：`skills/README.md`
- 注册表：`skills/registry.json`
