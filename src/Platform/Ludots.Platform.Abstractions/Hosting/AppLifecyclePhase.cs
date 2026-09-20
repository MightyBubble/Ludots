namespace Ludots.Platform.Abstractions
{
    public enum AppLifecyclePhase : byte
    {
        Created = 0,
        Configuring = 1,
        Initialized = 2,
        Running = 3,
        Suspending = 4,
        ShuttingDown = 5,
        Terminated = 6
    }
}
