# 快速开始

本页给出当前 Ludots 的最短启动路径。完整命令契约以 [环境与构建](contributing/environment-setup.md) 和 [Launcher CLI Runbook](reference/cli-runbook.md) 为准。

## 1 环境要求

- .NET 9.0 SDK（`global.json` 固定 9.0.x，全仓 target `net9.0`）——**唯一硬前置**
- 仓库自带离线 NuGet（`external/nuget/` + 根 `nuget.config`）：规范路径 **不需要访问 nuget.org**（弱网可用）
- Node.js + npm：**仅** GUI 启动器 / Web adapter 需要；Raylib CLI 主路径不需要

缺少 .NET 9 SDK 时 `dotnet restore` / 构建会失败。弱网包契约见 [零环境/零网络契约](reference/zero-env-setup.md)。

## 2 弱网一键（推荐，跨平台）

Linux / macOS / agent 虚拟机：

```bash
chmod +x scripts/dev-up.sh   # 首次
./scripts/dev-up.sh          # 离线还原 + 构建 + 启动 ExampleMod（raylib）
./scripts/dev-up.sh resolve camera_acceptance --adapter raylib
./scripts/dev-up.sh build-only
```

Windows（PowerShell）：

```powershell
.\scripts\dev-up.ps1
.\scripts\dev-up.ps1 resolve camera_acceptance --adapter raylib
.\scripts\dev-up.ps1 build-only
```

## 3 其他常用入口

```powershell
# Windows：产品化 GUI launcher（会 npm ci，需要网络）
.\scripts\run-mod-launcher.cmd

# 任意平台：直接调已构建的 CLI
dotnet src/Tools/Ludots.Launcher.Cli/bin/Release/net9.0/Ludots.Launcher.Cli.dll launch camera_acceptance --adapter raylib
```

规则：

- 作者主路径优先 `scripts/dev-up.*`（离线包 + Raylib）
- `launch` 负责依赖解析、DLL 解析、runtime bootstrap 和 SDK ref 导出
- 直接运行 adapter app 只用于调试，不是产品使用入口

## 4 最常用测试命令

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

## 5 读代码前先看什么

如果你要改代码，先读这些页面：

- [编码标准](contributing/coding-standards.md)
- [Feature 开发工作流](contributing/feature-development-workflow.md)
- [AI 辅助开发规范](contributing/ai-assisted-development.md)
- [架构](architecture/README.md)
