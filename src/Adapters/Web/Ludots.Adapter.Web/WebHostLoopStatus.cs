using System;
using System.Threading;

namespace Ludots.Adapter.Web
{
    public sealed class WebHostLoopStatus
    {
        public const string RunningStatus = "ok";
        public const string FaultedStatus = "faulted";
        public const string StoppedStatus = "stopped";

        private Exception? _fault;
        private long _running;

        public bool IsRunning => Interlocked.Read(ref _running) != 0;
        public Exception? Fault => Volatile.Read(ref _fault);
        public bool IsFaulted => Fault != null;

        public WebHostLoopHealthSnapshot CaptureHealthSnapshot()
        {
            Exception? fault = Fault;
            bool running = IsRunning;
            bool healthy = fault == null && running;
            return new WebHostLoopHealthSnapshot(
                Status: fault != null ? FaultedStatus : running ? RunningStatus : StoppedStatus,
                Healthy: healthy,
                Running: running,
                Faulted: fault != null,
                FaultType: fault?.GetType().FullName,
                FaultMessage: fault?.Message);
        }

        public void MarkStarted()
        {
            Volatile.Write(ref _fault, null);
            Interlocked.Exchange(ref _running, 1);
        }

        public void MarkFaulted(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Volatile.Write(ref _fault, exception);
            Interlocked.Exchange(ref _running, 0);
        }

        public void MarkStopped()
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public readonly record struct WebHostLoopHealthSnapshot(
        string Status,
        bool Healthy,
        bool Running,
        bool Faulted,
        string? FaultType,
        string? FaultMessage);
}
