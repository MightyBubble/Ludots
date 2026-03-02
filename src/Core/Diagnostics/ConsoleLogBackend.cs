using System;

namespace Ludots.Core.Diagnostics
{
    public sealed class ConsoleLogBackend : ILogBackend
    {
        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            Console.WriteLine($"[{LevelTag(level)}][{channel.Name}] {message}");
        }

        public void Flush() { }
        public void Dispose() { }

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???"
        };
    }
}
