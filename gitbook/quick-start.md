# 快速开始

本页给出当前 Ludots 的最短启动路径。完整命令契约以 [环境与构建](contributing/environment-setup.md) 和 [Launcher CLI Runbook](reference/cli-runbook.md) 为准。

## 1 环境要求

- .NET 9.0 SDK（`global.json` 固定 9.0.x，全仓 target `net9.0`）
- Node.js + npm

缺少 .NET 9 SDK 可能导致 `dotnet restore` 或 launcher 构建失败。

## 2 最常用命令

```powershell
# 打开产品化 launcher
.\scripts\run-mod-launcher.cmd

# 查看启动计划
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib

# 在 raylib 上启动一个 root mod
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib

# 在 web 上启动多个 root mod
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
```

规则：

- 正式入口统一使用 `.\scripts\run-mod-launcher.cmd cli ...`
- `launch` 会负责依赖解析、DLL 解析、runtime bootstrap 和 SDK ref 导出
- 直接运行 adapter app 只用于调试，不是产品使用入口

## 3 最常用测试命令

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

## 4 读代码前先看什么

如果你要改代码，先读这些页面：

- [编码标准](contributing/coding-standards.md)
- [Feature 开发工作流](contributing/feature-development-workflow.md)
- [AI 辅助开发规范](contributing/ai-assisted-development.md)
- [架构](architecture/README.md)
