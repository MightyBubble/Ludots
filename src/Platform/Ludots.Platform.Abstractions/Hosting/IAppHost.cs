using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Shell contract for a process host. Frame loops (RaylibHostLoop, WebHostLoop) stay as-is;
    /// adapters expose them through this lifecycle surface.
    /// </summary>
    public interface IAppHost
    {
        AppDescriptor Descriptor { get; }

        AppLifecyclePhase Phase { get; }

        event Action<AppStateChangedEventArgs>? PhaseChanged;

        /// <summary>Config load, engine construction, service registration. Must run before <see cref="Run"/>.</summary>
        void Initialize(AppInitContext context);

        /// <summary>Enters the blocking frame loop. Returns only after the loop exits.</summary>
        void Run();

        /// <summary>Requests shutdown; the host observes the flag and winds the loop down.</summary>
        void RequestShutdown(string reason);
    }
}
