# Activity / Task / Engine Event / Narrative 术语对照

本页落实 y5k A1 合同，供实现与命名审计引用。

| 术语 | 玩家侧含义 | 运行时含义 | 禁止的说法 |
|---|---|---|---|
| Activity | 弹到面前的一次抉择或通报 | 内容运行时，实例是 entity，状态 `pending` / `active` / `resolved` | 不得把引擎总线键称为 Activity |
| Task | 目标追踪器里的持续目标 | 任务运行时，实例是 entity，支持 `ALL` / `ANY` | 不得用空目标 Task 假装「下一个活动」 |
| Engine Event | 玩家不应直接看到这个词 | 引擎事件总线上的订阅键 | 不得在玩法文案里叫「活动总线」 |
| Narrative | 演出与对话 | 脚本时间轴 | 不得用它承接活动选项结算 |

## 单层纪律

1. 选项结算完成，活动即 `resolved`。
2. 选项不得在结算时打开另一个活动。
3. 后续只许：结算 Effect 创建 Task，或发出新 Source 由另一活动定义订阅。

## 派发语义

| `dispatch_policy` | 玩家看到什么 |
|---|---|
| `forced` | 立刻弹出，必须处理完才能继续 |
| `pooled` | 进入候选池，被抽中后才弹出 |
| `automatic` | 不弹选项，直接结算并留通报 |

## 条件三分（仅 Activity）

- Trigger：活动出不出现
- Gate：选项显不显示
- Execution Condition：显示出来的选项能不能执行
