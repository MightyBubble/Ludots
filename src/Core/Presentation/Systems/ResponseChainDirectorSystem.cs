using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainDirectorSystem : BaseSystem<World, float>
    {
        private readonly OrderRequestQueue _orderRequests;
        private readonly ResponseChainTelemetryBuffer _telemetry;
        private readonly ResponseChainUiState _ui;

        public ResponseChainDirectorSystem(
            World world,
            OrderRequestQueue orderRequests,
            ResponseChainTelemetryBuffer telemetry,
            ResponseChainUiState ui)
            : base(world)
        {
            _orderRequests = orderRequests;
            _telemetry = telemetry;
            _ui = ui;
        }

        public override void Update(in float dt)
        {
            for (int i = 0; i < _telemetry.Count; i++)
            {
                var evt = _telemetry[i];
                if (evt.Kind == ResponseChainTelemetryKind.WindowClosed)
                {
                    _ui.Close(evt.RootId);
                }
            }

            while (_orderRequests.TryDequeue(out var request))
            {
                if (_ui.Visible && _ui.RootId != request.RequestId)
                {
                    throw new InvalidOperationException(
                        $"ResponseChainDirectorSystem: cannot replace active root {_ui.RootId} with queued root {request.RequestId} before the previous prompt is closed.");
                }

                _ui.ApplyRequest(request);
            }

            _telemetry.Clear();
        }
    }
}
