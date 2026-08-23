namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 渲染程序集诊断出口；宿主启动时接线到正式日志，未接线时静默（画廊可独立运行）。
    /// 文件诊断是低频调查工具，经 LUDOTS_RAYLIB_DIAGNOSTIC_PATH 开启，默认关闭。
    /// </summary>
    public static class RenderDiagnostics
    {
        public static System.Action<string>? InfoSink;
        public static System.Action<string>? WarnSink;

        private static readonly System.Lazy<string?> s_filePath =
            new(() => System.Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH"));
        private static readonly System.Collections.Generic.HashSet<(string Category, int Key)> s_reportedFileDetails = new();

        public static bool FileSinkEnabled => !string.IsNullOrWhiteSpace(s_filePath.Value);

        public static void Info(string message) => InfoSink?.Invoke(message);

        public static void Warn(string message) => WarnSink?.Invoke(message);

        /// <summary>
        /// 按 (category, key) 去重的文件细节诊断。热路径调用方必须先用 <see cref="FileSinkEnabled"/>
        /// 拦截再构造消息字符串，否则关闭诊断时仍按帧分配。
        /// </summary>
        public static void Detail(string category, int key, string message)
        {
            if (!FileSinkEnabled)
            {
                return;
            }

            if (!s_reportedFileDetails.Add((category, key)))
            {
                return;
            }

            string fullPath = System.IO.Path.GetFullPath(s_filePath.Value!);
            string? directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.AppendAllText(fullPath, $"[{System.DateTime.UtcNow:O}] {category} key={key} {message}{System.Environment.NewLine}");
        }
    }
}
