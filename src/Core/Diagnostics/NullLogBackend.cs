namespace Ludots.Core.Diagnostics
{
    public sealed class NullLogBackend : ILogBackend
    {
        public static readonly NullLogBackend Instance = new();

        public void Write(LogLevel level, in LogChannel channel, string message) { }
        public void Flush() { }
        public void Dispose() { }
    }
}
