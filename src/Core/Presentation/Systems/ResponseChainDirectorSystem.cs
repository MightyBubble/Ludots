using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Primitives;
 
namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainDirectorSystem : BaseSystem<World, float>
    {
        private readonly OrderRequestQueue _orderRequests;
        private readonly ResponseChainTelemetryBuffer _telemetry;
        private readonly ResponseChainUiState _ui;
        private readonly PresentationCommandBuffer _commands;
 
        public ResponseChainDirectorSystem(World world, OrderRequestQueue orderRequests, ResponseChainTelemetryBuffer telemetry, ResponseChainUiState ui, PresentationCommandBuffer commands)
            : base(world)
        {
            _orderRequests = orderRequests;
            _telemetry = telemetry;
            _ui = ui;
            _commands = commands;
        }
 
        public override void Update(in float dt)
        {
            while (_orderRequests.TryDequeue(out var req))
            {
                _ui.ApplyRequest(req);
            }
 
            for (int i = 0; i < _telemetry.Count; i++)
            {
                var evt = _telemetry[i];
                if (evt.Kind == ResponseChainTelemetryKind.WindowClosed)
                {
                    _ui.Close(evt.RootId);
                }
 
                if (evt.Kind != ResponseChainTelemetryKind.ProposalAdded && evt.Kind != ResponseChainTelemetryKind.ProposalResolved)
                {
                    continue;
                }
 
                Vector3 pos = Vector3.Zero;
                if (World.IsAlive(evt.Target) && World.Has<VisualTransform>(evt.Target))
                {
                    pos = World.Get<VisualTransform>(evt.Target).Position;
                }
                else if (World.IsAlive(evt.Source) && World.Has<VisualTransform>(evt.Source))
                {
                    pos = World.Get<VisualTransform>(evt.Source).Position;
                }
 
                Vector4 color = new Vector4(0.7f, 0.7f, 0.7f, 1f);
                if (evt.Kind == ResponseChainTelemetryKind.ProposalAdded)
                {
                    color = new Vector4(0.2f, 0.6f, 1.0f, 1f);
                }
                else if (evt.Kind == ResponseChainTelemetryKind.ProposalResolved)
                {
                    color = evt.Outcome switch
                    {
                        ResponseChainResolveOutcome.AppliedInstant => new Vector4(0.2f, 1.0f, 0.2f, 1f),
                        ResponseChainResolveOutcome.CreatedEffect => new Vector4(0.2f, 1.0f, 0.2f, 1f),
                        ResponseChainResolveOutcome.Negated => new Vector4(1.0f, 0.9f, 0.2f, 1f),
                        ResponseChainResolveOutcome.Cancelled => new Vector4(0.4f, 0.4f, 0.4f, 1f),
                        _ => new Vector4(1.0f, 0.3f, 0.3f, 1f)
                    };
                }
 
                _commands.TryAdd(new PresentationCommand
                {
                    LogicTickStamp = 0,
                    Kind = PresentationCommandKind.PlayOneShotPerformer,
                    IdA = PrimitivePrefabIds.CueMarker,
                    Source = evt.Source,
                    Target = evt.Target,
                    Position = pos,
                    Param0 = color
                });
            }
 
            _telemetry.Clear();
        }
    }
}
