using System;
using Ludots.Core.Diagnostics;

namespace Ludots.Client.Raylib.Diagnostics
{
    public sealed class RaylibConsoleLogBackend : ILogBackend
    {
        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = LevelColor(level);
            Console.WriteLine($"[{LevelTag(level)}][{channel.Name}] {message}");
            Console.ForegroundColor = prevColor;
        }

        public void Flush() { }
        public void Dispose() { }

        private static ConsoleColor LevelColor(LogLevel level) => level switch
        {
            LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.Cyan,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

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
