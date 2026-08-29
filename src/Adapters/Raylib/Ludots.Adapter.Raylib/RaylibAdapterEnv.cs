using System;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// 适配器公共工具：宿主环境变量读取与文本诊断追加的单一实现，
    /// 供 RaylibHostLoop、RaylibHostInputRouter、RaylibFrameRenderer 共用（#1324 复核收敛）。
    /// </summary>
    internal static class RaylibAdapterEnv
    {
        public static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
        {
            return bool.TryParse(Environment.GetEnvironmentVariable(key), out bool value)
                ? value
                : defaultValue;
        }

        public static int ReadEnvIntOrDefault(string key, int defaultValue)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(key), out int value)
                ? value
                : defaultValue;
        }

        public static float ReadEnvFloatOrDefault(string key, float defaultValue)
        {
            return float.TryParse(Environment.GetEnvironmentVariable(key), out float value)
                ? value
                : defaultValue;
        }

        public static void AppendDiagnostic(string? diagnosticPath, string message)
        {
            if (string.IsNullOrWhiteSpace(diagnosticPath))
            {
                return;
            }

            string fullPath = System.IO.Path.GetFullPath(diagnosticPath);
            string? directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
    }
}
