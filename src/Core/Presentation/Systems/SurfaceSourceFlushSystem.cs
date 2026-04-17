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
            ReadOnlySpan<PresentationRequest> span = _requests.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationRequest request = ref span[i];
                if (request.Kind != PresentationRequestKind.SurfaceSource)
                {
                    continue;
                }

                if (!_payloads.TryGet(request.SurfaceSource.ScopeId, out SurfacePayloadSnapshot payload))
                {
                    throw new InvalidOperationException(
                        $"SurfaceSource performer scopeId={request.SurfaceSource.ScopeId} stableId={request.SurfaceSource.StableId} is missing runtime payload registration.");
                }

                if (payload.Kind != request.SurfaceSource.SurfaceKind)
                {
                    throw new InvalidOperationException(
                        $"SurfaceSource performer scopeId={request.SurfaceSource.ScopeId} stableId={request.SurfaceSource.StableId} has payload kind '{payload.Kind}' but authoring kind '{request.SurfaceSource.SurfaceKind}'.");
                }

                _runtime.Upsert(in request.SurfaceSource, in payload, frame);
            }
        }
    }
}
