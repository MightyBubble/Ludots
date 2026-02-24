using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ludots.Core.Engine
{
    public class GameSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            // Blocking Send is risky in a single-threaded game loop context.
            // For async/await continuations, Post is used.
            throw new NotSupportedException("Synchronous Send is not supported in Game Loop to prevent deadlocks.");
        }

        public void ProcessQueue()
        {
            // Process all pending actions for this frame
            // Limit loop count to prevent infinite loop if actions re-queue themselves immediately?
            // For now, simple drain.
            int count = _queue.Count;
            while (count > 0 && _queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameSyncContext] Error processing callback: {ex}");
                }
                count--;
            }
        }
    }
}
