# pi 自动化编排工作流

本文档说明本仓库的 pi 自动化流程：维护者在 GitHub issue 上标记，hahaboard conductor 自动拉起 pi agent 在隔离 worktree 里实现并提交 PR，人只负责最后的 review 和合并。

## 1 流程

```text
pi:auto 标签 → conductor 拾取 → 隔离 worktree → pi agent 实现 → PR + pi:done 标签 → 人工 review → 合并
```

## 2 五个步骤

1. **打标签**：维护者给要自动处理的 issue 打上 `pi:auto` 标签。
2. **拾取与隔离**：hahaboard conductor（跑在 codeseal 上）拾取该 issue，为它创建隔离的 git worktree，不在主检出上直接改动。
3. **自治实现**：pi agent 在 worktree 中实现 issue 描述的内容，遵循仓库内 AGENTS.md 与既有文档约定，提交到分支 `pi/issue-<编号>`。
4. **自动 PR**：conductor 基于该分支自动创建 PR，并给原 issue 回打 `pi:done` 标签。
5. **人工 review**：维护者 review 后合并。自动化到此为止，合并动作始终由人完成。

## 3 约定

* 分支命名固定为 `pi/issue-<编号>`，与 issue 一一对应。
* 标签 `pi:auto` 是自动处理的开关；去掉标签即取消自动处理。
* 标签 `pi:done` 表示 conductor 已完成实现并开出 PR，issue 进入人工 review。
