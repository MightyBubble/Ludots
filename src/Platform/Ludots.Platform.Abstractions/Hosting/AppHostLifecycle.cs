using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Phase state machine shared by <see cref="IAppHost"/> implementations:
    /// forward one step at a time, except a Suspending host may resume to Running,
    /// and Shutdown flow (ShuttingDown/Terminated) may be entered from any active phase.
    /// </summary>
    public sealed class AppHostLifecycle
    {
        public AppDescriptor Descriptor { get; }

        public AppLifecyclePhase Phase { get; private set; }

        public event Action<AppStateChangedEventArgs>? PhaseChanged;

        public AppHostLifecycle(AppDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Phase = AppLifecyclePhase.Created;
        }

        public void TransitionTo(AppLifecyclePhase newPhase)
        {
            if (newPhase == Phase)
            {
                throw new InvalidOperationException($"App '{Descriptor.AppId}' is already in phase {Phase}.");
            }

            bool forwardOneStep = newPhase == Phase + 1;
            bool resumeFromSuspend = Phase == AppLifecyclePhase.Suspending && newPhase == AppLifecyclePhase.Running;
            bool shutdownFromActive =
                (Phase is AppLifecyclePhase.Running or AppLifecyclePhase.Suspending) &&
                newPhase is AppLifecyclePhase.ShuttingDown or AppLifecyclePhase.Terminated;
            if (!forwardOneStep && !resumeFromSuspend && !shutdownFromActive)
            {
                throw new InvalidOperationException(
                    $"App '{Descriptor.AppId}' cannot transition from {Phase} to {newPhase}; " +
                    "forward transitions advance exactly one phase (shutdown may be entered from any active phase).");
            }

            AppLifecyclePhase previous = Phase;
            Phase = newPhase;
            PhaseChanged?.Invoke(new AppStateChangedEventArgs(Descriptor, previous, newPhase));
        }
    }
}
