# 任务书：#1058 切片二（per-seat 输入路由 + 设备扇出 + 多 scheme 激活）——恢复运行版

工作树：C:\001_AI\LudotsProd\.worktrees\pi-1058-input-routing（分支 codex/1058-per-seat-input-routing）

## 现场状态（2026-08-28 16:55 快照）
**主体工作已完成并提交**（工作树干净）：
- `fbb77eb043` feat(client): #1058 输入侧切片——per-seat 输入通道、多 scheme 激活与设备扇出
- `dcdd1b2094` docs(contract): 座位合同 §3.3 补 per-seat 输入路由切片的行为边界
- 基点含 PR #1304 合入（054b172a69）

## 剩余工作：验证 → （必要时 rebase 最新 main）→ push → 建 PR
1. 先读改动：`git log -p -2` 通读两个提交，确认语义完整、没有半截子改动。
2. 对照验收：两路输入互不覆盖（WASD 只驱动自己 possessed rep）；设备重复绑定扇出 + BindDevice 一次性 warning（点名设备 id 与两个 seatId，不改映射不拒绝）；同 schemeId 多 seat 合法不报警；多 seat 各声明 controlSchemeId 各自激活（进图 fail-fast 保留）；sole seat 零回归（PR #1301 合同测试全绿，含偏好不被改写）。
3. 补充项（前次指令，确认已含）：ControlSchemeRuntime.InstallScheme 补 InputContexts[] 引用存在性校验（照 intent/dispatch 校验写法 throw 点名）+ 测试。若 fbb77eb043 未含则补上。
4. `dotnet build` 0 错误；跑受影响测试类（激活链 / 座位 / 输入路由）。全量 GasTests 谨慎跑（会污染 artifacts/、docs/benchmarks/：跑前 git status 留底、跑后恢复、只提交源文件）。
5. ArchitectureTests 有 5 个存量失败（SkiaSharp / 地图队伍绑定 / 关系反向索引 / 禁则② #1305 / BehaviorKind），不追不修；禁则④存档守卫必须绿。
6. push（网络不稳，隔 30-60 秒重试）；gh 建 PR（base main），中文正文：概要 / 选型决策（per-seat handler vs 单 handler 多 seat 视图的取舍）/ 改动 / 验证证据 / 推进 #1058 哪些验收项，Refs #1058。**禁止合并**。
7. 若与 main 有冲突（呈现侧 #1302 等已合入）：rebase 解决，RaylibHostLoop 输入区段与呈现区段可能小撞。
8. 开工前必读：gitbook/contributing/ai-assisted-development.md；gh issue view 1058。
