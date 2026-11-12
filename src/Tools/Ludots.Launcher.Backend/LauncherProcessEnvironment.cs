using System.Diagnostics;

namespace Ludots.Launcher.Backend;

/// <summary>
/// Git Bash 会把 Windows 进程的 TEMP/HOME 换成 POSIX 路径,并清掉 PATHEXT。
/// NuGet 用 TEMP 拼缓存路径,PATHEXT 为空时 cmd 也找不到 .exe。这里只在 Windows 上修,
/// 而且不用当前进程的 GetTempPath——它读的就是被毒化的 TEMP。
/// </summary>
internal static class LauncherProcessEnvironment
{
    internal const string WindowsPathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";

    internal static void Apply(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Sanitize(
            startInfo.Environment,
            isWindows: true,
            windowsUserProfile: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            windowsTemp: ResolveWindowsTempDirectory());
    }

    internal static void Sanitize(
        IDictionary<string, string?> environment,
        bool isWindows,
        string? windowsUserProfile,
        string? windowsTemp)
    {
        if (!isWindows)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Read(environment, "PATHEXT")))
        {
            environment["PATHEXT"] = WindowsPathExt;
        }

        if (IsPosixPath(Read(environment, "TEMP")) || IsPosixPath(Read(environment, "TMP")))
        {
            if (string.IsNullOrWhiteSpace(windowsTemp) || IsPosixPath(windowsTemp))
            {
                throw new InvalidOperationException(
                    "Windows TEMP/TMP are POSIX paths and no Windows temp directory is available.");
            }

            environment["TEMP"] = windowsTemp;
            environment["TMP"] = windowsTemp;
        }

        if (IsPosixPath(Read(environment, "HOME")))
        {
            if (string.IsNullOrWhiteSpace(windowsUserProfile) || IsPosixPath(windowsUserProfile))
            {
                throw new InvalidOperationException(
                    "Windows HOME is a POSIX path and UserProfile is unavailable.");
            }

            environment["HOME"] = windowsUserProfile;
        }
    }

    internal static string ResolveWindowsTempDirectory()
    {
        string? user = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.User);
        if (IsWindowsPath(user))
        {
            return user!;
        }

        string? machine = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine);
        if (IsWindowsPath(machine))
        {
            return machine!;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            return Path.Combine(local, "Temp");
        }

        throw new InvalidOperationException(
            "Cannot resolve a Windows temp directory; process TEMP/TMP are POSIX and User/Machine TEMP are unset.");
    }

    private static string? Read(IDictionary<string, string?> environment, string key)
    {
        return environment.TryGetValue(key, out string? value) ? value : null;
    }

    private static bool IsPosixPath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && (value[0] == '/' || value[0] == '~');
    }

    private static bool IsWindowsPath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !IsPosixPath(value);
    }
}
