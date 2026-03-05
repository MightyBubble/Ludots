using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Presentation.Assets;
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
        private readonly int _cueMarkerPrefabId;
 
        public ResponseChainDirectorSystem(World world, OrderRequestQueue orderRequests, ResponseChainTelemetryBuffer telemetry, ResponseChainUiState ui, PresentationCommandBuffer commands, PrefabRegistry prefabs)
            : base(world)
        {
            _orderRequests = orderRequests;
            _telemetry = telemetry;
            _ui = ui;
            _commands = commands;
            _cueMarkerPrefabId = prefabs.GetId(WellKnownPrefabKeys.CueMarker);
        }
 
        public override void Update(in float dt)
        {
            if (_telemetry.Count == 0) return;
 
            for (int i = 0; i < _telemetry.Count; i++)
            {
                var evt = _telemetry[i];
 
                Vector3 pos = default;
                if (evt.Target != Entity.Null && World.IsAlive(evt.Target) && World.Has<VisualTransform>(evt.Target))
                {
                    pos = World.Get<VisualTransform>(evt.Target).Position;
                }
                else if (evt.Source != Entity.Null && World.IsAlive(evt.Source) && World.Has<VisualTransform>(evt.Source))
                {
                    pos = World.Get<VisualTransform>(evt.Source).Position;
                }
                else
                {
                    continue;
                }
 
                Vector4 color = new Vector4(1f, 1f, 1f, 1f);
                if (evt.Outcome != ResponseChainResolveOutcome.None)
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
                    IdA = _cueMarkerPrefabId,
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
