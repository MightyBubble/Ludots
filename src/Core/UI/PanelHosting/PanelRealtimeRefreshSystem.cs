using System;
using Arch.System;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Per-frame driver for opt-in realtime panel bindings. Only variables declared
    /// with realtime=true are re-evaluated here; everything else waits for an explicit
    /// Refresh(handle) call.
    /// </summary>
    public sealed class PanelRealtimeRefreshSystem : ISystem<float>
    {
        private readonly PanelHost _panelHost;

        public PanelRealtimeRefreshSystem(PanelHost panelHost)
        {
            _panelHost = panelHost ?? throw new ArgumentNullException(nameof(panelHost));
        }

        public int RefreshedInstancesLastUpdate { get; private set; }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            RefreshedInstancesLastUpdate = _panelHost.RefreshRealtime();
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
