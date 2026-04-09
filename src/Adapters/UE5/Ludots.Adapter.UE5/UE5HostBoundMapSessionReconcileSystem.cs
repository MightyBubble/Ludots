using System;
using Arch.System;
using Ludots.Core.Map;

namespace Ludots.Adapter.UE5
{
    public sealed class UE5HostBoundMapSessionReconcileSystem : ISystem<float>
    {
        private readonly Func<MapSession?> _focusedSessionAccessor;
        private readonly IHostBoundMapSessionService _sessionService;

        public UE5HostBoundMapSessionReconcileSystem(
            Func<MapSession?> focusedSessionAccessor,
            IHostBoundMapSessionService sessionService)
        {
            _focusedSessionAccessor = focusedSessionAccessor ?? throw new ArgumentNullException(nameof(focusedSessionAccessor));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            _sessionService.Reconcile(_focusedSessionAccessor());
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }
}
