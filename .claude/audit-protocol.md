## Codex → Claude Code 审计协议

当你完成一波任务后，写入信号文件触发自动审计。

### 触发审计

在 worktree 根目录执行：

```bash
echo "WAVE=2 TASKS=T4,T5,T6" > .claude/audit-request.txt
```

### 审计流程

1. Claude Code 主会话每 5 分钟轮询 `.claude/audit-request.txt`
2. 检测到文件后，按 `gitbook/architecture/performer-development-kanban.md` 的验收标准逐项审计
3. 审计结果写入 `.claude/audit-result.txt`
4. 删除 `audit-request.txt`
5. 更新看板状态

### 审计结果格式

```
WAVE=2
STATUS=PASS|FAIL
TIMESTAMP=2026-04-17T15:30:00

## T4: PASS
- PerformerInstance 字段全部匹配

## T5: FAIL
- 缺失 ReleaseScope 单元测试
- PerformerInstanceBuffer.Allocate 未设置 parentHandle

## 修复项
- T5-F1: ...
```

### 注意事项

- 此轮询仅在 Claude Code 主会话存活期间有效（session-only，最长 7 天）
- 如果主会话关闭，需要用户手动重新启动轮询
- 确保 `dotnet build` 通过后再触发审计
