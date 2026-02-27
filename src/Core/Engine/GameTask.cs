using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Engine
{
    /// <summary>
    /// Provides game-time aware async/await functionality.
    /// Replaces Task.Delay to ensure timing respects TimeScale and Pause, 
    /// and continuations run on the main thread via GameSynchronizationContext.
    /// </summary>
    public static class GameTask
    {
        // Internal registries processed by GameEngine
        internal static readonly List<GameTimer> ActiveTimers = new List<GameTimer>();
        internal static readonly List<GameFrameWaiter> ActiveFrameWaiters = new List<GameFrameWaiter>();
        internal static readonly List<GameConditionWaiter> ActiveConditionWaiters = new List<GameConditionWaiter>();

        /// <summary>
        /// Waits for the specified amount of game time (in seconds).
        /// Respects Time.TimeScale.
        /// </summary>
        public static Task Delay(float seconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            ActiveTimers.Add(new GameTimer { RemainingTime = seconds, Tcs = tcs });
            return tcs.Task;
        }

        /// <summary>
        /// Waits until the next frame update.
        /// </summary>
        public static Task NextFrame()
        {
            var tcs = new TaskCompletionSource<bool>();
            ActiveFrameWaiters.Add(new GameFrameWaiter { Tcs = tcs });
            return tcs.Task;
        }

        /// <summary>
        /// Waits until the predicate returns true. Checked every frame.
        /// </summary>
        public static Task WaitUntil(Func<bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>();
            ActiveConditionWaiters.Add(new GameConditionWaiter { Predicate = predicate, Tcs = tcs });
            return tcs.Task;
        }

        /// <summary>
        /// Called by GameEngine to update timers and waiters.
        /// </summary>
        internal static void Update(float dt)
        {
            // 1. Update Timers
            for (int i = ActiveTimers.Count - 1; i >= 0; i--)
            {
                var timer = ActiveTimers[i];
                timer.RemainingTime -= dt;
                if (timer.RemainingTime <= 0)
                {
                    // Remove first, then set result (which might trigger immediate continuation if SyncContext allows)
                    ActiveTimers.RemoveAt(i);
                    timer.Tcs.TrySetResult(true);
                }
            }

            // 2. Process Frame Waiters (All current waiters complete this frame)
            if (ActiveFrameWaiters.Count > 0)
            {
                // Snapshot the list to allow new waiters to be added during execution of current ones
                var currentWaiters = activeFrameWaitersSnapshot;
                currentWaiters.Clear();
                currentWaiters.AddRange(ActiveFrameWaiters);
                ActiveFrameWaiters.Clear();

                foreach (var waiter in currentWaiters)
                {
                    waiter.Tcs.TrySetResult(true);
                }
            }

            // 3. Process Condition Waiters
            for (int i = ActiveConditionWaiters.Count - 1; i >= 0; i--)
            {
                var waiter = ActiveConditionWaiters[i];
                bool result = false;
                try
                {
                    result = waiter.Predicate();
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Engine, $"Error in WaitUntil predicate: {ex}");
                    ActiveConditionWaiters.RemoveAt(i);
                    waiter.Tcs.TrySetException(ex);
                    continue;
                }

                if (result)
                {
                    ActiveConditionWaiters.RemoveAt(i);
                    waiter.Tcs.TrySetResult(true);
                }
            }
        }

        // Snapshot buffer to avoid allocation
        private static readonly List<GameFrameWaiter> activeFrameWaitersSnapshot = new List<GameFrameWaiter>();
    }

    internal class GameTimer
    {
        public float RemainingTime;
        public TaskCompletionSource<bool> Tcs;
    }
    
    internal class GameFrameWaiter
    {
        public TaskCompletionSource<bool> Tcs;
    }

    internal class GameConditionWaiter
    {
        public Func<bool> Predicate;
        public TaskCompletionSource<bool> Tcs;
    }
}
