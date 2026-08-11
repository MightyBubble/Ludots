# ADR-0005 Task 进度唯一真相与 Quest 适配

## 1 背景

引擎已有 Quest 子系统（实例物化为 entity、阶段与信号推进），但缺少提议态、多目标 `ALL` / `ANY`、确定性任务链。若在 Quest 上并行再造一套 Task 进度表，或让叙事/界面各自缓存进度，玩家会在不同面板看到互相矛盾的数字。

## 2 决策

| 决策项 | 选择 | 被否决项 |
|---|---|---|
| 进度唯一真相 | `TaskInstance` entity | 平行 Quest 进度表、叙事层进度仓、界面本地进度 |
| Quest 对外接口 | 短期适配层，读写映射到 TaskInstance | 继续独立写 Quest 进度 |
| 叙事编排层 | 只读投影 | 第二份进度存储 |
| 内容作者入口 | 只写 `TaskDefinition` | 新内容仍写旧 Quest 定义作为真相 |
| 迁移策略 | 先切读路径，再下线旧写路径 | 长期双写再对账 |
| 与 Activity 关系 | Task 发状态事实；Activity 订阅或用 Effect 创建 Task | Task 内嵌选项 / Activity 扛跨周期进度 |

## 3 后果

- 内容作者只维护 Task 定义。
- 界面任务追踪、旧 Quest 查询适配、叙事投影三处读数必须同源。
- 命中双写时验收失败，原因码 `dual_progress_store`。
- Task 状态 Source 与创建 Task Effect 在 Provider 目录登记前不得自造键顶替（见 Provider gap catalog）。

## 4 迁移

1. 新定义只进 Task 结构。
2. 旧查询改为视图适配，底层读 TaskInstance。
3. 删除或封禁平行进度存储（守卫测试）。
4. 验收：同一件事只有一个进度实例。
5. 先停旧写路径，再删旧存储；禁止「两边都写再对账」。

## 5 禁令

- `dual_progress_store`
- `narrative_progress_store_forbidden`
- 空目标假任务
- 用 Task 顶替 Activity 当面抉择

## 6 状态

提案中 → 实现 Task 运行时并合并后标记为已接受。
