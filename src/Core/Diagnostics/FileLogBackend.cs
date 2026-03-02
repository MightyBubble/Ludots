using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Ludots.Core.Diagnostics
{
    public sealed class FileLogBackend : ILogBackend
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly Thread _writerThread;
        private readonly ManualResetEventSlim _signal = new(false);
        private volatile bool _disposed;
        private readonly string _filePath;

        public FileLogBackend(string filePath)
        {
            _filePath = filePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "Ludots.FileLog"
            };
            _writerThread.Start();
        }

        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            if (_disposed) return;
            _queue.Enqueue($"[{DateTime.UtcNow:HH:mm:ss.fff}][{LevelTag(level)}][{channel.Name}] {message}");
            _signal.Set();
        }

        public void Flush()
        {
            _signal.Set();
            // Give writer thread a moment to drain
            Thread.Sleep(10);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _signal.Set();
            _writerThread.Join(2000);
            _signal.Dispose();
            DrainQueue();
        }

        private void WriterLoop()
        {
            while (!_disposed)
            {
                _signal.Wait(500);
                _signal.Reset();
                DrainQueue();
            }
        }

        private void DrainQueue()
        {
            try
            {
                using var writer = new StreamWriter(_filePath, append: true);
                while (_queue.TryDequeue(out var line))
                    writer.WriteLine(line);
            }
            catch
            {
                // Swallow I/O exceptions in log writer to avoid crashing the app
            }
        }

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
