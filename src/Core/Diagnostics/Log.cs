using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ludots.Core.Diagnostics
{
    public static class Log
    {
        private static ILogBackend _backend = NullLogBackend.Instance;
        private static LogLevel _globalLevel = LogLevel.Info;
        private static LogLevel[] _channelLevels = new LogLevel[64];
        private static int _nextChannelId;
        private static readonly object _registrationLock = new();

        public static void Initialize(ILogBackend backend, LogLevel globalLevel = LogLevel.Info)
        {
            _backend = backend ?? NullLogBackend.Instance;
            _globalLevel = globalLevel;
            // Reset channel levels to global
            for (int i = 0; i < _channelLevels.Length; i++)
                _channelLevels[i] = globalLevel;
        }

        public static void Shutdown()
        {
            _backend.Flush();
            _backend.Dispose();
            _backend = NullLogBackend.Instance;
        }

        public static LogChannel RegisterChannel(string name)
        {
            lock (_registrationLock)
            {
                int id = _nextChannelId++;
                if (id >= _channelLevels.Length)
                {
                    var newLevels = new LogLevel[_channelLevels.Length * 2];
                    Array.Copy(_channelLevels, newLevels, _channelLevels.Length);
                    for (int i = _channelLevels.Length; i < newLevels.Length; i++)
                        newLevels[i] = _globalLevel;
                    _channelLevels = newLevels;
                }
                _channelLevels[id] = _globalLevel;
                return new LogChannel(id, name);
            }
        }

        public static LogChannel GetOrCreateModChannel(string modId)
        {
            return RegisterChannel($"Mod.{modId}");
        }

        public static void SetGlobalLevel(LogLevel level)
        {
            _globalLevel = level;
            for (int i = 0; i < _channelLevels.Length; i++)
                _channelLevels[i] = level;
        }

        public static void SetChannelLevel(in LogChannel channel, LogLevel level)
        {
            if (channel.Id >= 0 && channel.Id < _channelLevels.Length)
                _channelLevels[channel.Id] = level;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEnabled(in LogChannel channel, LogLevel level)
        {
            return level >= _channelLevels[channel.Id];
        }

        // --- Trace: compiled out unless LUDOTS_TRACE defined ---

        [Conditional("LUDOTS_TRACE")]
        public static void Trace(in LogChannel ch, string msg)
        {
            if (IsEnabled(in ch, LogLevel.Trace))
                _backend.Write(LogLevel.Trace, in ch, msg);
        }

        [Conditional("LUDOTS_TRACE")]
        public static void Trace(in LogChannel ch,
            [InterpolatedStringHandlerArgument("ch")] ref TraceLogInterpolatedStringHandler handler)
        {
            if (handler.IsEnabled)
                _backend.Write(LogLevel.Trace, in ch, handler.ToStringAndClear());
        }

        // --- Debug: compiled out unless LUDOTS_DEBUG defined ---

        [Conditional("LUDOTS_DEBUG")]
        public static void Dbg(in LogChannel ch, string msg)
        {
            if (IsEnabled(in ch, LogLevel.Debug))
                _backend.Write(LogLevel.Debug, in ch, msg);
        }

        [Conditional("LUDOTS_DEBUG")]
        public static void Dbg(in LogChannel ch,
            [InterpolatedStringHandlerArgument("ch")] ref DebugLogInterpolatedStringHandler handler)
        {
            if (handler.IsEnabled)
                _backend.Write(LogLevel.Debug, in ch, handler.ToStringAndClear());
        }

        // --- Info ---

        public static void Info(in LogChannel ch, string msg)
        {
            if (IsEnabled(in ch, LogLevel.Info))
                _backend.Write(LogLevel.Info, in ch, msg);
        }

        public static void Info(in LogChannel ch,
            [InterpolatedStringHandlerArgument("ch")] ref InfoLogInterpolatedStringHandler handler)
        {
            if (handler.IsEnabled)
                _backend.Write(LogLevel.Info, in ch, handler.ToStringAndClear());
        }

        // --- Warning ---

        public static void Warn(in LogChannel ch, string msg)
        {
            if (IsEnabled(in ch, LogLevel.Warning))
                _backend.Write(LogLevel.Warning, in ch, msg);
        }

        public static void Warn(in LogChannel ch,
            [InterpolatedStringHandlerArgument("ch")] ref WarnLogInterpolatedStringHandler handler)
        {
            if (handler.IsEnabled)
                _backend.Write(LogLevel.Warning, in ch, handler.ToStringAndClear());
        }

        // --- Error ---

        public static void Error(in LogChannel ch, string msg)
        {
            if (IsEnabled(in ch, LogLevel.Error))
                _backend.Write(LogLevel.Error, in ch, msg);
        }

        public static void Error(in LogChannel ch,
            [InterpolatedStringHandlerArgument("ch")] ref ErrorLogInterpolatedStringHandler handler)
        {
            if (handler.IsEnabled)
                _backend.Write(LogLevel.Error, in ch, handler.ToStringAndClear());
        }

        /// <summary>
        /// Reset all state. For testing only.
        /// </summary>
        internal static void Reset()
        {
            _backend = NullLogBackend.Instance;
            _globalLevel = LogLevel.Info;
            _channelLevels = new LogLevel[64];
            _nextChannelId = 0;
        }

        /// <summary>
        /// Get current backend. For testing only.
        /// </summary>
        public static ILogBackend Backend => _backend;
    }
}
