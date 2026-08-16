# input-03 UXD · 交互上下文档案的编辑器需求

> input-03 的编辑器需求（高保真规格）。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；配置写法见 [input-03 配置说明](../config/input-03-interaction-context.md)；编辑器实现见 [editor spec](../spec-editor/input-03-interaction-context.md)。

## 1. 界面定位

交互上下文编辑器：五个拼装位的档案卡 + 运行期栈预览——哪个能力正压着哪层环境一目了然。

## 2. 布局线框

```text
┌─ 交互上下文编辑器 ────────────────────────────────────────────────────┐
├─ 左：档案清单 ────────┬─ 中：档案卡：ctx.guided ─────────────────────┤
│ ▸（空——引导新建）    │ 集合键 [collection.guided.targets ▾]          │
│ ▸ ctx.guided  ●     │ 视图键 [view.enemies.visible ▾]              │
│ ＋新建档案           │ 过滤 [filter…default ▾ | 直通] 输入上下文 [—] │
│                      │ 命令意图 [intent.command.default ▾ | —]       │
├─ 右：栈预览 ─────────┴───────────────────────────────────────────────┤
│ #2 ctx.guided @ Ability.GuidedMissile #e17（第 4 帧）                │
│ #1 …（基础帧）   ▶回收时机：exec 结束                                │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 档案清单 | 档案表（现状空表引导态） | 空态文案 + 新建引导 |
| 集合/视图下拉 | 实体集合键与视图键注册视图 | 支持留空 |
| 过滤下拉 | filter_profiles（input-05） | "直通"选项 = 不写 filterProfileId |
| 输入上下文/意图下拉 | default_input contexts / 意图档案表 | "—" = 不写 |
| 栈预览 | 会话期交互上下文栈快照 | 帧来源能力与已持续时间 |

## 4. 关键交互流：给引导技能挂上下文

1. ＋新建档案 `ctx.guided`。
2. 集合键选 `collection.guided.targets`，过滤选直通。
3. 输入上下文选 `GuidedAim`，命令意图选默认意图。
4. 能力编辑器把 `interactionContextProfile: "ctx.guided"` 写入技能（跳转完成）。
5. 试运行 → 栈预览出现 #2 帧；技能结束帧消失。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 空表 | 根资产无档案 | 清单引导态（合法，非错误） |
| 引用悬空 | 档案五键引用不存在 | 红条 + 保存阻断（执行期才报错的项目体检项） |
| 帧滞留 | 栈预览帧持续超时 | 黄条 + 能力链接 |

## 6. 易用性验收口径

- 新建档案到被能力引用 ≤ 6 次交互。
- 栈预览与运行期实际栈一致（同源快照）。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [editor spec](../spec-editor/input-03-interaction-context.md)
