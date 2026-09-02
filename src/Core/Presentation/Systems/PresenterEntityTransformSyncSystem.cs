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
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresenterEntityTransformSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription OwnerPayloadTransformSyncQuery = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationStaticTransform>();

        private static readonly QueryDescription EntityAnchoredQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterWorldPosition, PresenterWorldPlanePosition, PresenterWorldRotation, PresenterWorldFacing, PresenterWorldScale, PresenterTransformSource, PresenterEmitCache, PerfTransformSyncTick>()
            .WithNone<PresenterBootstrapPending, PerfStaticStableVisual, PerfOwnerPayloadTransformSync>();

        private static readonly QueryDescription DebugSyncPathQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterTransformSource, PresenterWorldPosition, PerfTransformSyncTick>()
            .WithNone<PresenterBootstrapPending, PerfStaticStableVisual>();

        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly PresenterEntityRuntime _runtime;
        private readonly PresenterDefinitionRegistry? _definitions;

        public bool DebugSyncPathAssertionsEnabled { get; set; }

        public PresenterEntityTransformSyncSystem(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _timingDiagnostics = timingDiagnostics;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions;
            if (definitions != null)
            {
                _runtime.BindDefinitions(definitions);
            }
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            SyncSingleRootOwnerPayloads();

            foreach (ref var chunk in World.Query(in EntityAnchoredQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
                Span<PresenterWorldPlanePosition> planePositions = chunk.GetSpan<PresenterWorldPlanePosition>();
                Span<PresenterWorldRotation> rotations = chunk.GetSpan<PresenterWorldRotation>();
                Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
                Span<PresenterWorldScale> scales = chunk.GetSpan<PresenterWorldScale>();
                Span<PresenterTransformSource> sources = chunk.GetSpan<PresenterTransformSource>();
                Span<PresenterEmitCache> emitCaches = chunk.GetSpan<PresenterEmitCache>();

                foreach (int index in chunk)
                {
                    ref PresenterState state = ref states[index];
                    if (state.AnchorKind != PresentationAnchorKind.Entity ||
                        sources[index].Value != TransformSource.EntityTransform ||
                        !World.IsAlive(state.OwnerEntity) ||
                        !World.Has<VisualTransform>(state.OwnerEntity) ||
                        !_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                    {
                        continue;
                    }

                    VisualTransform ownerTransform = World.Get<VisualTransform>(state.OwnerEntity);
                    Vector3 newPosition = ownerTransform.Position + definition.PositionOffset;
                    Vector2 newPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in newPosition);
                    Quaternion newRotation = VisualMath.NormalizeOrIdentity(ownerTransform.Rotation);
                    PresenterWorldFacing newFacing = ResolveOwnerFacing(state.OwnerEntity);
                    Vector3 newScale = VisualMath.NormalizeScale(ownerTransform.Scale);

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
                    Entity presenter = Unsafe.Add(ref entityFirst, index);
                    MarkEmitDirty(presenter);
                    SyncFastAttachedChildren(presenter, in newPosition, in newRotation, in newFacing, in newScale);
                    PropagateInheritedChildTransforms(presenter);
                }
            }

            RunDebugSyncPathAssertions();

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresenterEntityTransformSync(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RunDebugSyncPathAssertions()
        {
            if (!DebugSyncPathAssertionsEnabled)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in DebugSyncPathQuery))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterTransformSource> sources = chunk.GetSpan<PresenterTransformSource>();
                Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    ref readonly PresenterState state = ref states[index];
                    if (state.AnchorKind != PresentationAnchorKind.Entity ||
                        sources[index].Value != TransformSource.EntityTransform ||
                        !World.IsAlive(state.OwnerEntity))
                    {
                        continue;
                    }

                    Entity presenter = Unsafe.Add(ref entityFirst, index);
                    bool ownerNamesPresenterAsSingleRoot = World.TryGet(
                        state.OwnerEntity,
                        out PresentationOwnerHasPresenterPayload payload) &&
                        payload.RootCount == 1 &&
                        payload.SingleRootPresenter == presenter &&
                        payload.SingleRootTransformSync != 0;
                    System.Diagnostics.Debug.Assert(
                        World.Has<PerfOwnerPayloadTransformSync>(presenter) == ownerNamesPresenterAsSingleRoot,
                        $"Presenter {presenter.Id} fast-path marker disagrees with owner {state.OwnerEntity.Id} payload; the owner payload write is the single sync-path decision point.");

                    if (!World.Has<VisualTransform>(state.OwnerEntity))
                    {
                        continue;
                    }

                    VisualTransform ownerTransform = World.Get<VisualTransform>(state.OwnerEntity);
                    if (!_definitions.TryGet(state.DefId, out PresenterDefinition assertionDefinition))
                    {
                        continue;
                    }

                    Vector3 ownerPosition = ownerTransform.Position + assertionDefinition.PositionOffset;
                    Vector3 presenterPosition = positions[index].Value;
                    System.Diagnostics.Debug.Assert(
                        Math.Abs(presenterPosition.X - ownerPosition.X) <= 0.001f &&
                        Math.Abs(presenterPosition.Y - ownerPosition.Y) <= 0.001f &&
                        Math.Abs(presenterPosition.Z - ownerPosition.Z) <= 0.001f,
                        $"Presenter {presenter.Id} at {presenterPosition} did not catch up to owner {state.OwnerEntity.Id} VisualTransform {ownerPosition}; neither the single-root fast path nor the per-presenter anchored path covered it.");
                }
            }
        }

        private void SyncSingleRootOwnerPayloads()
        {
            foreach (ref var chunk in World.Query(in OwnerPayloadTransformSyncQuery))
            {
                Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                Span<PresentationOwnerHasPresenterPayload> payloads = chunk.GetSpan<PresentationOwnerHasPresenterPayload>();
                bool hasFacings = chunk.Has<FacingDirection>();
                Span<FacingDirection> ownerFacings = hasFacings ? chunk.GetSpan<FacingDirection>() : default;

                foreach (int index in chunk)
                {
                    ref readonly PresentationOwnerHasPresenterPayload payload = ref payloads[index];
                    if (payload.RootCount != 1 ||
                        payload.SingleRootTransformSync == 0 ||
                        payload.SingleRootPresenter == Entity.Null ||
                        !World.IsAlive(payload.SingleRootPresenter) ||
                        !World.Has<PresenterWorldPosition>(payload.SingleRootPresenter) ||
                        !World.Has<PresenterWorldPlanePosition>(payload.SingleRootPresenter) ||
                        !World.Has<PresenterWorldRotation>(payload.SingleRootPresenter) ||
                        !World.Has<PresenterWorldFacing>(payload.SingleRootPresenter) ||
                        !World.Has<PresenterWorldScale>(payload.SingleRootPresenter) ||
                        !World.Has<PresenterEmitCache>(payload.SingleRootPresenter))
                    {
                        continue;
                    }

                    VisualTransform ownerTransform = visuals[index];
                    Vector3 newPosition = ownerTransform.Position;
                    Vector2 newPlanePosition = worldPositions[index].Value.ToVector2();
                    Quaternion newRotation = VisualMath.NormalizeOrIdentity(ownerTransform.Rotation);
                    PresenterWorldFacing newFacing = hasFacings
                        ? new PresenterWorldFacing
                        {
                            AngleRad = ownerFacings[index].AngleRad,
                            HasValue = 1,
                        }
                        : default;
                    Vector3 newScale = VisualMath.NormalizeScale(ownerTransform.Scale);
                    if (World.Has<PresenterState>(payload.SingleRootPresenter) &&
                        _definitions.TryGet(World.Get<PresenterState>(payload.SingleRootPresenter).DefId, out PresenterDefinition singleRootDefinition))
                    {
                        newPosition += singleRootDefinition.PositionOffset;
                        newPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in newPosition);
                    }

                    ref PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(payload.SingleRootPresenter);
                    ref PresenterWorldPlanePosition planePosition = ref World.Get<PresenterWorldPlanePosition>(payload.SingleRootPresenter);
                    ref PresenterWorldRotation rotation = ref World.Get<PresenterWorldRotation>(payload.SingleRootPresenter);
                    ref PresenterWorldFacing facing = ref World.Get<PresenterWorldFacing>(payload.SingleRootPresenter);
                    ref PresenterWorldScale scale = ref World.Get<PresenterWorldScale>(payload.SingleRootPresenter);
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
                    MarkEmitDirty(payload.SingleRootPresenter);
                    SyncFastAttachedChildren(payload.SingleRootPresenter, in newPosition, in newRotation, in newFacing, in newScale);
                    PropagateInheritedChildTransforms(payload.SingleRootPresenter);
                }
            }
        }

        private void SyncFastAttachedChildren(
            Entity parent,
            in Vector3 parentPosition,
            in Quaternion parentRotation,
            in PresenterWorldFacing parentFacing,
            in Vector3 parentScale)
        {
            if (_definitions == null ||
                parent == Entity.Null ||
                !World.IsAlive(parent) ||
                !World.Has<PresenterChildren>(parent))
            {
                return;
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(parent);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (!World.IsAlive(child) ||
                    (!World.Has<PerfOwnerPayloadAttachedTransformSync>(child) &&
                     !World.Has<PerfHasAttachmentTick>(child)) ||
                    !World.Has<PresenterState>(child) ||
                    !World.Has<PresenterParent>(child) ||
                    World.Get<PresenterParent>(child).Parent != parent)
                {
                    continue;
                }

                ref PresenterState state = ref World.Get<PresenterState>(child);
                if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition) ||
                    !definition.SupportsFastParentAttachmentTick ||
                    (uint)definition.FastParentAttachmentBehaviorIndex >= (uint)definition.Behaviors.Length)
                {
                    continue;
                }

                ref readonly BehaviorSlot slot = ref definition.Behaviors[definition.FastParentAttachmentBehaviorIndex];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ApplyFastParentAttachment(
                    child,
                    in slot.Attachment,
                    in parentPosition,
                    in parentRotation,
                    in parentFacing,
                    in parentScale);
            }
        }

        // 无 Attachment 行为、未挂快速标记的 InheritParent 子级只能经完整传播跟随父级；
        // 缺这一步时它们的稳定可视层会冻结在创建时的变换上。
        private void PropagateInheritedChildTransforms(Entity parent)
        {
            if (_definitions == null ||
                parent == Entity.Null ||
                !World.IsAlive(parent) ||
                !World.Has<PresenterChildren>(parent))
            {
                return;
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(parent);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (!World.IsAlive(child) ||
                    World.Has<PerfOwnerPayloadAttachedTransformSync>(child) ||
                    World.Has<PerfHasAttachmentTick>(child) ||
                    !World.Has<PresenterState>(child) ||
                    !World.Has<PresenterTransformSource>(child) ||
                    !World.Has<PresenterParent>(child) ||
                    World.Get<PresenterParent>(child).Parent != parent)
                {
                    continue;
                }

                TransformSource source = World.Get<PresenterTransformSource>(child).Value;
                if (source != TransformSource.InheritParent && source != TransformSource.AttachedToParent)
                {
                    continue;
                }

                _runtime.PropagateParentDrivenTransforms(parent);
                return;
            }
        }

        private void ApplyFastParentAttachment(
            Entity child,
            in AttachmentConfig config,
            in Vector3 parentPosition,
            in Quaternion parentRotation,
            in PresenterWorldFacing parentFacing,
            in Vector3 parentScale)
        {
            if (!World.Has<PresenterTransformSource>(child) ||
                !World.Has<PresenterWorldPosition>(child) ||
                !World.Has<PresenterWorldPlanePosition>(child) ||
                !World.Has<PresenterWorldRotation>(child) ||
                !World.Has<PresenterWorldFacing>(child) ||
                !World.Has<PresenterWorldScale>(child))
            {
                return;
            }

            Quaternion normalizedParentRotation = VisualMath.NormalizeOrIdentity(parentRotation);
            Vector3 normalizedParentScale = VisualMath.NormalizeScale(parentScale);
            Vector3 scaledOffset = config.InheritScale
                ? normalizedParentScale * config.Offset
                : config.Offset;
            Vector3 nextPosition = parentPosition + Vector3.Transform(scaledOffset, normalizedParentRotation);
            Vector2 nextPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in nextPosition);
            Quaternion nextRotation = VisualMath.NormalizeOrIdentity(
                normalizedParentRotation * VisualMath.NormalizeOrIdentity(config.RotationOffset));
            Vector3 nextScale = config.InheritScale ? normalizedParentScale : Vector3.One;

            ref PresenterTransformSource source = ref World.Get<PresenterTransformSource>(child);
            ref PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(child);
            ref PresenterWorldPlanePosition planePosition = ref World.Get<PresenterWorldPlanePosition>(child);
            ref PresenterWorldRotation rotation = ref World.Get<PresenterWorldRotation>(child);
            ref PresenterWorldFacing facing = ref World.Get<PresenterWorldFacing>(child);
            ref PresenterWorldScale scale = ref World.Get<PresenterWorldScale>(child);
            bool changed =
                source.Value != TransformSource.AttachedToParent ||
                position.Value != nextPosition ||
                planePosition.ValueCm != nextPlanePosition ||
                rotation.Value != nextRotation ||
                facing.AngleRad != parentFacing.AngleRad ||
                facing.HasValue != parentFacing.HasValue ||
                scale.Value != nextScale;
            if (!changed)
            {
                return;
            }

            source.Value = TransformSource.AttachedToParent;
            position.Value = nextPosition;
            planePosition.ValueCm = nextPlanePosition;
            rotation.Value = nextRotation;
            facing = parentFacing;
            scale.Value = nextScale;
            MarkEmitDirty(child);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PresenterWorldFacing ResolveOwnerFacing(Entity owner)
        {
            if (owner == Entity.Null ||
                !World.IsAlive(owner) ||
                !World.Has<FacingDirection>(owner))
            {
                return default;
            }

            return new PresenterWorldFacing
            {
                AngleRad = World.Get<FacingDirection>(owner).AngleRad,
                HasValue = 1,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkEmitDirty(Entity presenter)
        {
            _runtime.MarkTransformDrivenEmitDirty(presenter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

    }
}
