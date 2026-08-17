namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 渲染程序集诊断出口；宿主启动时接线到正式日志，未接线时静默（画廊可独立运行）。
    /// </summary>
    public static class RenderDiagnostics
    {
        public static System.Action<string>? InfoSink;
        public static System.Action<string>? WarnSink;

        public static void Info(string message) => InfoSink?.Invoke(message);

        public static void Warn(string message) => WarnSink?.Invoke(message);
    }
}
