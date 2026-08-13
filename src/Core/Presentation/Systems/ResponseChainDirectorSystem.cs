using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainDirectorSystem : BaseSystem<World, float>
    {
        private static readonly Vector3 CueMarkerScale = new(0.2f, 0.2f, 0.2f);
        private static readonly Vector3 CueMarkerAnchorOffset = new(0f, 0.2f, 0f);

        private readonly OrderRequestQueue _orderRequests;
        private readonly ResponseChainTelemetryBuffer _telemetry;
        private readonly ResponseChainUiState _ui;
        private readonly TransientMarkerBuffer _markers;
        private readonly int _cueMarkerMeshId;

        public ResponseChainDirectorSystem(
            World world,
            OrderRequestQueue orderRequests,
            ResponseChainTelemetryBuffer telemetry,
            ResponseChainUiState ui,
            TransientMarkerBuffer markers,
            MeshAssetRegistry meshes)
            : base(world)
        {
            _orderRequests = orderRequests;
            _telemetry = telemetry;
            _ui = ui;
            _markers = markers;
            _cueMarkerMeshId = WellKnownMeshKeys.RequireCueMarkerId(meshes);
        }

        public override void Update(in float dt)
        {
            ConsumeUiStateTransitions();
            if (_telemetry.Count == 0) return;

            for (int i = 0; i < _telemetry.Count; i++)
            {
                var evt = _telemetry[i];
                if (evt.Kind != ResponseChainTelemetryKind.ProposalResolved)
                {
                    continue;
                }

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

                bool follow = evt.Target != Entity.Null && World.IsAlive(evt.Target) && World.Has<VisualTransform>(evt.Target);
                bool added = follow
                    ? _markers.TryAddAnchoredMesh(_cueMarkerMeshId, CueMarkerScale, color, 0.35f, evt.Target, CueMarkerAnchorOffset)
                    : _markers.TryAddMesh(_cueMarkerMeshId, pos, CueMarkerScale, color, 0.35f);
                if (!added)
                {
                    throw new InvalidOperationException("TransientMarkerBuffer is full while emitting response-chain cue marker.");
                }
            }

            _telemetry.Clear();
        }

        private void ConsumeUiStateTransitions()
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
        }
    }
}
