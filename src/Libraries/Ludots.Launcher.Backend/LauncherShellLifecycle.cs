using System.Diagnostics;
using System.IO;

namespace Ludots.Launcher.Backend;

/// <summary>
/// Adapter 进程的模式分发与中继重启合同：args[0] 非空 = GameMode（bootstrap 路径），
/// 无参 = ShellMode。跨会话回到 Shell 必须经中继重启（spawn 自身无参后退出），
/// 保证每进程最多一次引擎初始化与一次浏览器运行时会话。
/// </summary>
public static class LauncherShellLifecycle
{
    public static string? ResolveBootstrapConfigPath(string[] args)
    {
        return args is { Length: > 0 } && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : null;
    }

    public static ProcessStartInfo BuildRelayRestartStartInfo()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Relay restart requires Environment.ProcessPath.");
        }

        var processName = Path.GetFileName(processPath);
        var entryAssemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var runsUnderDotnetHost = processName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entryAssemblyPath)
            && entryAssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        if (runsUnderDotnetHost)
        {
            return new ProcessStartInfo(processPath, $"\"{entryAssemblyPath}\"")
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        return new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }
}
