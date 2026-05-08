using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerEntityTransformSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription OwnerPayloadTransformSyncQuery = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationStaticTransform>();

        private static readonly QueryDescription EntityAnchoredQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerWorldPosition, PerformerWorldPlanePosition, PerformerWorldRotation, PerformerWorldFacing, PerformerWorldScale, PerformerTransformSource, PerformerEmitCache, PerfTransformSyncTick>()
            .WithNone<PerformerBootstrapPending, PerfStaticStableVisual, PerfOwnerPayloadTransformSync>();

        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly PerformerEntityRuntime _runtime;

        public PerformerEntityTransformSyncSystem(
            World world,
            PerformerEntityRuntime runtime,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _timingDiagnostics = timingDiagnostics;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            SyncSingleRootOwnerPayloads();

            foreach (ref var chunk in World.Query(in EntityAnchoredQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                Span<PerformerWorldPlanePosition> planePositions = chunk.GetSpan<PerformerWorldPlanePosition>();
                Span<PerformerWorldRotation> rotations = chunk.GetSpan<PerformerWorldRotation>();
                Span<PerformerWorldFacing> facings = chunk.GetSpan<PerformerWorldFacing>();
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
                    Vector2 newPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in newPosition);
                    Quaternion newRotation = WorldPlane2D.NormalizeOrIdentity(ownerTransform.Rotation);
                    PerformerWorldFacing newFacing = ResolveOwnerFacing(state.OwnerEntity);
                    Vector3 newScale = WorldPlane2D.NormalizeScale(ownerTransform.Scale);

                    bool changed =
                        positions[index].Value != newPosition ||
                        planePositions[index].ValueCm != newPlanePosition ||
                        rotations[index].Value != newRotation ||
                        facings[index].AngleRad != newFacing.AngleRad ||
                        facings[index].HasValue != newFacing.HasValue ||
                        scales[index].Value != newScale;
                    if (!changed)
                    {
                        continue;
                    }

                    positions[index].Value = newPosition;
                    planePositions[index].ValueCm = newPlanePosition;
                    rotations[index].Value = newRotation;
                    facings[index] = newFacing;
                    scales[index].Value = newScale;
                    MarkEmitDirty(Unsafe.Add(ref entityFirst, index));
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEntityTransformSync(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        private void SyncSingleRootOwnerPayloads()
        {
            foreach (ref var chunk in World.Query(in OwnerPayloadTransformSyncQuery))
            {
                Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                Span<PresentationOwnerHasPerformerPayload> payloads = chunk.GetSpan<PresentationOwnerHasPerformerPayload>();
                bool hasFacings = chunk.Has<FacingDirection>();
                Span<FacingDirection> ownerFacings = hasFacings ? chunk.GetSpan<FacingDirection>() : default;

                foreach (int index in chunk)
                {
                    ref readonly PresentationOwnerHasPerformerPayload payload = ref payloads[index];
                    if (payload.Count != 1 ||
                        payload.SingleRootTransformSync == 0 ||
                        payload.SingleRootPerformer == Entity.Null ||
                        !World.IsAlive(payload.SingleRootPerformer) ||
                        !World.Has<PerformerWorldPosition>(payload.SingleRootPerformer) ||
                        !World.Has<PerformerWorldPlanePosition>(payload.SingleRootPerformer) ||
                        !World.Has<PerformerWorldRotation>(payload.SingleRootPerformer) ||
                        !World.Has<PerformerWorldFacing>(payload.SingleRootPerformer) ||
                        !World.Has<PerformerWorldScale>(payload.SingleRootPerformer) ||
                        !World.Has<PerformerEmitCache>(payload.SingleRootPerformer))
                    {
                        continue;
                    }

                    VisualTransform ownerTransform = visuals[index];
                    Vector3 newPosition = ownerTransform.Position;
                    Vector2 newPlanePosition = worldPositions[index].Value.ToVector2();
                    Quaternion newRotation = WorldPlane2D.NormalizeOrIdentity(ownerTransform.Rotation);
                    PerformerWorldFacing newFacing = hasFacings
                        ? new PerformerWorldFacing
                        {
                            AngleRad = ownerFacings[index].AngleRad,
                            HasValue = 1,
                        }
                        : default;
                    Vector3 newScale = WorldPlane2D.NormalizeScale(ownerTransform.Scale);

                    ref PerformerWorldPosition position = ref World.Get<PerformerWorldPosition>(payload.SingleRootPerformer);
                    ref PerformerWorldPlanePosition planePosition = ref World.Get<PerformerWorldPlanePosition>(payload.SingleRootPerformer);
                    ref PerformerWorldRotation rotation = ref World.Get<PerformerWorldRotation>(payload.SingleRootPerformer);
                    ref PerformerWorldFacing facing = ref World.Get<PerformerWorldFacing>(payload.SingleRootPerformer);
                    ref PerformerWorldScale scale = ref World.Get<PerformerWorldScale>(payload.SingleRootPerformer);
                    bool changed =
                        position.Value != newPosition ||
                        planePosition.ValueCm != newPlanePosition ||
                        rotation.Value != newRotation ||
                        facing.AngleRad != newFacing.AngleRad ||
                        facing.HasValue != newFacing.HasValue ||
                        scale.Value != newScale;
                    if (!changed)
                    {
                        continue;
                    }

                    position.Value = newPosition;
                    planePosition.ValueCm = newPlanePosition;
                    rotation.Value = newRotation;
                    facing = newFacing;
                    scale.Value = newScale;
                    MarkEmitDirty(payload.SingleRootPerformer);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PerformerWorldFacing ResolveOwnerFacing(Entity owner)
        {
            if (owner == Entity.Null ||
                !World.IsAlive(owner) ||
                !World.Has<FacingDirection>(owner))
            {
                return default;
            }

            return new PerformerWorldFacing
            {
                AngleRad = World.Get<FacingDirection>(owner).AngleRad,
                HasValue = 1,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkEmitDirty(Entity performer)
        {
            _runtime.MarkTransformDrivenEmitDirty(performer);
        }

    }
}
