/*
'*******************************************************************
'Tank Framework
'*******************************************************************
*/
using System.Runtime.CompilerServices;
using Ludots.Core.Diagnostics;
using EngineLog = Ludots.Core.Diagnostics.Log;

namespace Sango
{
    /// <summary>
    /// 游戏日志管理器.
    /// 所有游戏日志需要从该处打印;内部转发平台 Ludots.Core.Diagnostics.Log
    /// (通道 Sango.Kernel),后端由宿主决定——headless 测试未装后端时静默。
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// 日志输出类型,用来格式化日志的标题,标题颜色
        /// </summary>
        public enum LogType : int
        {
            None,
            Assets,
            Network,
            Object,
            Script,
            UI,
            World,
            Sound,
            Game,
            Download,
            Other
        }

        // shim 层 UnityEngine.Debug 直调与 Sango.Log 共用同一通道。
        internal static readonly LogChannel Channel = EngineLog.RegisterChannel("Sango.Kernel");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Text(object message) => message?.ToString() ?? string.Empty;

        public static void Info(object message, LogType t) => EngineLog.Info(Channel, Text(message));
        public static void Info(object message) => Info(message, LogType.None);

        public static void Error(object message, LogType t) => EngineLog.Error(Channel, Text(message));
        public static void Error(object message) => Error(message, LogType.None);

        public static void Warning(object message, LogType t) => EngineLog.Warn(Channel, Text(message));
        public static void Warning(object message) => Warning(message, LogType.None);
    }
}
