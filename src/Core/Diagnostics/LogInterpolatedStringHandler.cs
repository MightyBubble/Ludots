using System.Runtime.CompilerServices;

namespace Ludots.Core.Diagnostics
{
    [InterpolatedStringHandler]
    public ref struct TraceLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool IsEnabled;

        public TraceLogInterpolatedStringHandler(int literalLength, int formattedCount,
            in LogChannel channel, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = Log.IsEnabled(in channel, LogLevel.Trace);
            if (shouldAppend)
                _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s) { if (IsEnabled) _inner.AppendLiteral(s); }
        public void AppendFormatted<T>(T value) { if (IsEnabled) _inner.AppendFormatted(value); }
        public void AppendFormatted<T>(T value, string? format) { if (IsEnabled) _inner.AppendFormatted(value, format); }
        public void AppendFormatted<T>(T value, int alignment) { if (IsEnabled) _inner.AppendFormatted(value, alignment); }
        public void AppendFormatted<T>(T value, int alignment, string? format) { if (IsEnabled) _inner.AppendFormatted(value, alignment, format); }
        public void AppendFormatted(string? value) { if (IsEnabled) _inner.AppendFormatted(value); }

        public string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
    }

    [InterpolatedStringHandler]
    public ref struct DebugLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool IsEnabled;

        public DebugLogInterpolatedStringHandler(int literalLength, int formattedCount,
            in LogChannel channel, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = Log.IsEnabled(in channel, LogLevel.Debug);
            if (shouldAppend)
                _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s) { if (IsEnabled) _inner.AppendLiteral(s); }
        public void AppendFormatted<T>(T value) { if (IsEnabled) _inner.AppendFormatted(value); }
        public void AppendFormatted<T>(T value, string? format) { if (IsEnabled) _inner.AppendFormatted(value, format); }
        public void AppendFormatted<T>(T value, int alignment) { if (IsEnabled) _inner.AppendFormatted(value, alignment); }
        public void AppendFormatted<T>(T value, int alignment, string? format) { if (IsEnabled) _inner.AppendFormatted(value, alignment, format); }
        public void AppendFormatted(string? value) { if (IsEnabled) _inner.AppendFormatted(value); }

        public string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
    }

    [InterpolatedStringHandler]
    public ref struct InfoLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool IsEnabled;

        public InfoLogInterpolatedStringHandler(int literalLength, int formattedCount,
            in LogChannel channel, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = Log.IsEnabled(in channel, LogLevel.Info);
            if (shouldAppend)
                _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s) { if (IsEnabled) _inner.AppendLiteral(s); }
        public void AppendFormatted<T>(T value) { if (IsEnabled) _inner.AppendFormatted(value); }
        public void AppendFormatted<T>(T value, string? format) { if (IsEnabled) _inner.AppendFormatted(value, format); }
        public void AppendFormatted<T>(T value, int alignment) { if (IsEnabled) _inner.AppendFormatted(value, alignment); }
        public void AppendFormatted<T>(T value, int alignment, string? format) { if (IsEnabled) _inner.AppendFormatted(value, alignment, format); }
        public void AppendFormatted(string? value) { if (IsEnabled) _inner.AppendFormatted(value); }

        public string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
    }

    [InterpolatedStringHandler]
    public ref struct WarnLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool IsEnabled;

        public WarnLogInterpolatedStringHandler(int literalLength, int formattedCount,
            in LogChannel channel, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = Log.IsEnabled(in channel, LogLevel.Warning);
            if (shouldAppend)
                _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s) { if (IsEnabled) _inner.AppendLiteral(s); }
        public void AppendFormatted<T>(T value) { if (IsEnabled) _inner.AppendFormatted(value); }
        public void AppendFormatted<T>(T value, string? format) { if (IsEnabled) _inner.AppendFormatted(value, format); }
        public void AppendFormatted<T>(T value, int alignment) { if (IsEnabled) _inner.AppendFormatted(value, alignment); }
        public void AppendFormatted<T>(T value, int alignment, string? format) { if (IsEnabled) _inner.AppendFormatted(value, alignment, format); }
        public void AppendFormatted(string? value) { if (IsEnabled) _inner.AppendFormatted(value); }

        public string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
    }

    [InterpolatedStringHandler]
    public ref struct ErrorLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool IsEnabled;

        public ErrorLogInterpolatedStringHandler(int literalLength, int formattedCount,
            in LogChannel channel, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = Log.IsEnabled(in channel, LogLevel.Error);
            if (shouldAppend)
                _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }

        public void AppendLiteral(string s) { if (IsEnabled) _inner.AppendLiteral(s); }
        public void AppendFormatted<T>(T value) { if (IsEnabled) _inner.AppendFormatted(value); }
        public void AppendFormatted<T>(T value, string? format) { if (IsEnabled) _inner.AppendFormatted(value, format); }
        public void AppendFormatted<T>(T value, int alignment) { if (IsEnabled) _inner.AppendFormatted(value, alignment); }
        public void AppendFormatted<T>(T value, int alignment, string? format) { if (IsEnabled) _inner.AppendFormatted(value, alignment, format); }
        public void AppendFormatted(string? value) { if (IsEnabled) _inner.AppendFormatted(value); }

        public string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
    }
}
