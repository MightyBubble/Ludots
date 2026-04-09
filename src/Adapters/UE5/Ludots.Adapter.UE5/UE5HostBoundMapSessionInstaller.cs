using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.UE5
{
    public static class UE5HostBoundMapSessionInstaller
    {
        public static IHostBoundMapSessionService Install(GameEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            engine.SetService(UE5AdapterServiceKeys.HostBoundMapSessionState, HostBoundMapSessionSnapshot.Empty);
            var service = new UE5HostBoundMapSessionService(
                () => engine.GetService(UE5AdapterServiceKeys.ExplicitHostMapBindingResolver),
                () => engine.GetService(UE5AdapterServiceKeys.HostLevelNavigator),
                () => engine.GetService(UE5AdapterServiceKeys.ExternalSessionTransitionHandler),
                snapshot => engine.SetService(UE5AdapterServiceKeys.HostBoundMapSessionState, snapshot));
            engine.SetService(UE5AdapterServiceKeys.HostBoundMapSessionService, service);
            engine.SetService(CoreServiceKeys.MapLoadCompletionGate, service);
            engine.SetService(CoreServiceKeys.FocusedMapLoadStateSink, service);
            return service;
        }
    }
}
