using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class SurfaceSourceFlushSystem : BaseSystem<World, float>
    {
        private readonly PresentationRequestBuffer _requests;
        private readonly SurfaceSourcePayloadRegistry _payloads;
        private readonly SurfaceSourceRuntimeRegistry _runtime;

        public SurfaceSourceFlushSystem(
            World world,
            PresentationRequestBuffer requests,
            SurfaceSourcePayloadRegistry payloads,
            SurfaceSourceRuntimeRegistry runtime)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            int frame = _runtime.BeginFrame();
            ReadOnlySpan<PresentationRequestOp> ops = _requests.Ops;
            for (int i = 0; i < ops.Length; i++)
            {
                PresentationRequestOp op = ops[i];
                if (op.Channel == PresentationRequestChannel.Removal)
                {
                    ref readonly PresentationRemovalRequest removal = ref _requests.RemovalAt(op.Slot);
                    if (removal.Kind == PresentationRequestKind.RemoveSurfaceSource)
                    {
                        _runtime.MarkPendingRemoval(removal.StableId);
                    }

                    continue;
                }

                if (op.Channel != PresentationRequestChannel.SurfaceSource)
                {
                    continue;
                }

                ref readonly SurfaceSourceRequest surfaceSource = ref _requests.SurfaceSourceAt(op.Slot).Item;
                if (!_payloads.TryGet(surfaceSource.ScopeId, out SurfacePayloadSnapshot payload))
                {
                    throw new InvalidOperationException(
                        $"SurfaceSource presenter scopeId={surfaceSource.ScopeId} stableId={surfaceSource.StableId} is missing runtime payload registration.");
                }

                if (payload.Kind != surfaceSource.SurfaceKind)
                {
                    throw new InvalidOperationException(
                        $"SurfaceSource presenter scopeId={surfaceSource.ScopeId} stableId={surfaceSource.StableId} has payload kind '{payload.Kind}' but authoring kind '{surfaceSource.SurfaceKind}'.");
                }

                _runtime.Upsert(in surfaceSource, in payload, frame);
            }
        }
    }
}
