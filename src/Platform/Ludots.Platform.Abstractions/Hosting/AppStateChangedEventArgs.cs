using System;

namespace Ludots.Platform.Abstractions
{
    public sealed record AppStateChangedEventArgs(
        AppDescriptor App,
        AppLifecyclePhase PreviousPhase,
        AppLifecyclePhase NewPhase);
}
