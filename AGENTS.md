# AGENTS.md

Ludots — 基于 Arch ECS 的高性能 C# 游戏框架。六边形架构，一切皆 Mod，禁止 fallback/向后兼容/重复造轮子/跨越职责。

**写任何代码前必须先读 `gitbook/contributing/ai-assisted-development.md` 的“任务执行决策规范”。**

Entity Association Core 的计划与 ADR SSOT 在 GitHub issue #239；ADR 正本在 #244，仓库 `docs/adr/` 不新增 AAC 平行 ADR 文件。

所有正式开发规范统一维护在 `gitbook/`：

| 文档 | 路径 |
|------|------|
| 文档首页 | `gitbook/README.md` |
| 规范总索引 | `gitbook/contributing/README.md` |
| 编码标准（含核心铁律） | `gitbook/contributing/coding-standards.md` |
| Feature 开发工作流 | `gitbook/contributing/feature-development-workflow.md` |
| **AI 辅助开发规范（必读）** | `gitbook/contributing/ai-assisted-development.md` |
| 开发环境与构建 | `gitbook/contributing/environment-setup.md` |
| 文档治理规范 | `gitbook/contributing/documentation-governance.md` |
| 共享 Skill 治理 | `gitbook/contributing/shared-skill-governance.md` |
| 架构文档索引 | `gitbook/architecture/README.md` |
| 共享 Skill 索引 | `skills/README.md` |
| 共享 Skill 注册表 | `skills/registry.json` |

## Cursor Cloud specific instructions

环境已预装 .NET 8 + .NET 9 SDK（位于 `~/.dotnet`，并已 symlink 到 `/usr/local/bin/dotnet`，所有 shell 直接可用，无需改 PATH）。Node 22 + npm 已预装。仓库无根 `.sln`、无 `global.json`，默认用 9.0.x SDK；按 `.csproj` 逐项目 build/test。完整命令见 `gitbook/contributing/environment-setup.md` 与 `docs/conventions/03_environment_setup.md`。

非显然要点（仅记不易发现的坑）：

- **产品入口是 launcher**。Linux/Cloud 上用 **web adapter**，不要用 Raylib（需 GPU/显示，云 VM 无法跑）。运行：`dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release -- launch <mod> --adapter web`。仓库脚本是 Windows `.cmd`/`.ps1`，Linux 直接用上面的 `dotnet run` 形式替代 `run-mod-launcher.cmd cli ...`。
- web adapter 监听 **http://localhost:5200**，C# 引擎 host loop ~30fps，浏览器经 websocket 连接（日志 `Clients=` 会从 0 变 1）。
- web 前端（`src/Client/Web`，Three.js）的 `dist/` 与 `node_modules/` 都被 gitignore；**launcher 在 web launch 时会自动跑 `npm ci`（仅当缺 node_modules）+ `npm run build`**，无需手动构建。若改了 `src/Client/Web/package.json`，需手动删 `node_modules` 让 launcher 重新 `npm ci`。
- CI（`.github/workflows/solution-verify.yml`）注明：origin/main 上 **完整 GasTests / PresentationTests 本就是红的**，测试请用 `--filter` 跑定向切片，不要拿整套失败当回归。
