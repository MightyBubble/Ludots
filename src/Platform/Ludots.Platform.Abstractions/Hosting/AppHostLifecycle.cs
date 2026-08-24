using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Phase state machine shared by <see cref="IAppHost"/> implementations:
    /// forward-only, except a Suspending host may resume to Running.
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

            bool forward = newPhase > Phase;
            bool resumeFromSuspend = Phase == AppLifecyclePhase.Suspending && newPhase == AppLifecyclePhase.Running;
            if (!forward && !resumeFromSuspend)
            {
                throw new InvalidOperationException(
                    $"App '{Descriptor.AppId}' cannot transition from {Phase} back to {newPhase}.");
            }

            AppLifecyclePhase previous = Phase;
            Phase = newPhase;
            PhaseChanged?.Invoke(new AppStateChangedEventArgs(Descriptor, previous, newPhase));
        }
    }
}
