using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.System;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerEntityTransformSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription EntityAnchoredQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldScale, PerformerTransformSource, PerformerEmitCache>()
            .WithAny<PerfHasEmitWork, PerfRetainedPresentationRequest>()
            .WithNone<PerformerBootstrapPending, PerfStaticStableVisual>();

        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        public PerformerEntityTransformSyncSystem(World world, PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            foreach (ref var chunk in World.Query(in EntityAnchoredQuery))
            {
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                Span<PerformerWorldRotation> rotations = chunk.GetSpan<PerformerWorldRotation>();
                Span<PerformerWorldScale> scales = chunk.GetSpan<PerformerWorldScale>();
                Span<PerformerTransformSource> sources = chunk.GetSpan<PerformerTransformSource>();
                Span<PerformerEmitCache> emitCaches = chunk.GetSpan<PerformerEmitCache>();

                foreach (int index in chunk)
                {
                    ref PerformerState state = ref states[index];
                    if (state.AnchorKind != PresentationAnchorKind.Entity ||
                        sources[index].Value != TransformSource.EntityTransform ||
                        !World.IsAlive(state.OwnerEntity) ||
                        !World.Has<VisualTransform>(state.OwnerEntity))
                    {
                        continue;
                    }

                    VisualTransform ownerTransform = World.Get<VisualTransform>(state.OwnerEntity);
                    Vector3 newPosition = ownerTransform.Position;
                    Quaternion newRotation = NormalizeOrIdentity(ownerTransform.Rotation);
                    Vector3 newScale = NormalizeScale(ownerTransform.Scale);

                    bool changed =
                        positions[index].Value != newPosition ||
                        rotations[index].Value != newRotation ||
                        scales[index].Value != newScale;
                    if (!changed)
                    {
                        continue;
                    }

                    positions[index].Value = newPosition;
                    rotations[index].Value = newRotation;
                    scales[index].Value = newScale;
                    MarkEmitDirty(ref emitCaches[index]);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEntityTransformSync(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MarkEmitDirty(ref PerformerEmitCache emitCache)
        {
            if (emitCache.StaticDirty == 0)
            {
                emitCache.StaticDirty = 1;
            }

            if (emitCache.RetainedDirty == 0)
            {
                emitCache.RetainedDirty = 1;
            }
        }

    }
}
