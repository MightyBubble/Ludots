using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassCrowdFormationFollowerSystem : ISystem<float>
{
    private static readonly QueryDescription AnchorQuery = new QueryDescription()
        .WithAll<MassCrowdFormationAnchor, MassCrowdAgentIndex, FacingDirection>()
        .WithNone<PresentationDestroyPending>();

    private static readonly QueryDescription FollowerQuery = new QueryDescription()
        .WithAll<MassCrowdFormationFollower, MassCrowdAgentIndex>()
        .WithNone<PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly Dictionary<int, AnchorSnapshot> _anchorsByFormationId = new();
    private readonly Dictionary<int, FormationSyncState> _syncStateByFormationId = new();
    private readonly List<int> _memberAgentIndices = new();
    private readonly List<int> _staleFormationIds = new();
    private int _observedSceneResetCount;
    private int _observedAuthoredRuntimeBindingRevision;

    public MassCrowdFormationFollowerSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        InvalidateSyncStateForRuntimeLifecycle();
        if (!_simulation.AgentState.HasBoundAgents(_simulation.MassFlow.UnitCount))
        {
            _syncStateByFormationId.Clear();
            return;
        }

        _anchorsByFormationId.Clear();
        foreach (ref var chunk in _engine.World.Query(in AnchorQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdFormationAnchor> anchors = chunk.GetSpan<MassCrowdFormationAnchor>();
            Span<MassCrowdAgentIndex> agentIndices = chunk.GetSpan<MassCrowdAgentIndex>();
            Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
            foreach (int index in chunk)
            {
                MassCrowdFormationAnchor anchor = anchors[index];
                if (anchor.FormationId <= 0)
                {
                    throw new InvalidOperationException($"MassCrowdFormationAnchor entity {Unsafe.Add(ref entityFirst, index).Id} requires a resolved positive FormationId.");
                }

                if (anchor.SlotCount <= 0)
                {
                    throw new InvalidOperationException($"MassCrowdFormationAnchor '{MassCrowdFormationRegistry.GetName(anchor.FormationId)}' requires SlotCount > 0.");
                }

                if (!_anchorsByFormationId.TryAdd(
                        anchor.FormationId,
                        new AnchorSnapshot(
                            Unsafe.Add(ref entityFirst, index),
                            agentIndices[index].Value,
                            anchor.SlotCount,
                            facings[index].AngleRad,
                            ResolveLocomotion(anchor.FormationId, Unsafe.Add(ref entityFirst, index)))))
                {
                    throw new InvalidOperationException($"MassCrowdFormationAnchor '{MassCrowdFormationRegistry.GetName(anchor.FormationId)}' is authored more than once.");
                }
            }
        }

        if (_anchorsByFormationId.Count == 0)
        {
            _syncStateByFormationId.Clear();
            return;
        }

        PruneSyncStateWithoutAnchor();
        foreach ((int formationId, AnchorSnapshot anchor) in _anchorsByFormationId)
        {
            SyncFormationFollowers(formationId, in anchor);
        }
    }

    private void InvalidateSyncStateForRuntimeLifecycle()
    {
        if (_observedSceneResetCount == _simulation.SceneResetCount &&
            _observedAuthoredRuntimeBindingRevision == _simulation.AuthoredRuntimeBindingRevision)
        {
            return;
        }

        _observedSceneResetCount = _simulation.SceneResetCount;
        _observedAuthoredRuntimeBindingRevision = _simulation.AuthoredRuntimeBindingRevision;
        _syncStateByFormationId.Clear();
    }

    private void PruneSyncStateWithoutAnchor()
    {
        if (_syncStateByFormationId.Count == 0)
        {
            return;
        }

        _staleFormationIds.Clear();
        foreach (int formationId in _syncStateByFormationId.Keys)
        {
            if (!_anchorsByFormationId.ContainsKey(formationId))
            {
                _staleFormationIds.Add(formationId);
            }
        }

        for (int i = 0; i < _staleFormationIds.Count; i++)
        {
            _syncStateByFormationId.Remove(_staleFormationIds[i]);
        }
    }

    private void SyncFormationFollowers(int formationId, in AnchorSnapshot anchor)
    {
        _memberAgentIndices.Clear();
        foreach (ref var chunk in _engine.World.Query(in FollowerQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdFormationFollower> followers = chunk.GetSpan<MassCrowdFormationFollower>();
            Span<MassCrowdAgentIndex> agentIndices = chunk.GetSpan<MassCrowdAgentIndex>();
            foreach (int index in chunk)
            {
                int followerFormationId = followers[index].FormationId;
                if (followerFormationId <= 0)
                {
                    throw new InvalidOperationException($"MassCrowdFormationFollower entity {Unsafe.Add(ref entityFirst, index).Id} requires a resolved positive FormationId.");
                }

                if (followerFormationId == formationId)
                {
                    _memberAgentIndices.Add(agentIndices[index].Value);
                }
            }
        }

        if (_memberAgentIndices.Count == 0)
        {
            return;
        }

        _syncStateByFormationId.TryGetValue(formationId, out FormationSyncState state);
        MassNavigationCarriedRangeSyncResult carrierSync = _simulation.SyncCarriedAgentsToCarrier(
            anchor.AgentIndex,
            CollectionsMarshal.AsSpan(_memberAgentIndices),
            state.CarrierSnapshotInitialized,
            state.CarrierWorldXCm,
            state.CarrierWorldYCm);
        state.CarrierSnapshotInitialized = true;
        state.CarrierWorldXCm = carrierSync.CarrierWorldXCm;
        state.CarrierWorldYCm = carrierSync.CarrierWorldYCm;

        float targetChangeEpsilonSq = anchor.Locomotion.TargetChangeEpsilonCm * anchor.Locomotion.TargetChangeEpsilonCm;
        float deltaX = carrierSync.CarrierWorldXCm - state.TargetCenterWorldXCm;
        float deltaY = carrierSync.CarrierWorldYCm - state.TargetCenterWorldYCm;
        float facingDelta = MathF.Abs(NormalizeAngleRadians(anchor.FacingRad - state.TargetFacingRad));
        bool targetChanged =
            !state.TargetSnapshotInitialized ||
            (deltaX * deltaX) + (deltaY * deltaY) >= targetChangeEpsilonSq ||
            facingDelta >= anchor.Locomotion.FacingChangeEpsilonRadians;
        if (!targetChanged)
        {
            _syncStateByFormationId[formationId] = state;
            return;
        }

        float forwardX = MathF.Cos(anchor.FacingRad);
        float forwardY = MathF.Sin(anchor.FacingRad);
        float lateralX = -forwardY;
        float lateralY = forwardX;
        foreach (ref var chunk in _engine.World.Query(in FollowerQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdFormationFollower> followers = chunk.GetSpan<MassCrowdFormationFollower>();
            Span<MassCrowdAgentIndex> agentIndices = chunk.GetSpan<MassCrowdAgentIndex>();
            foreach (int index in chunk)
            {
                ref MassCrowdFormationFollower follower = ref followers[index];
                if (follower.FormationId != formationId)
                {
                    continue;
                }

                if ((uint)follower.SlotIndex >= (uint)anchor.SlotCount)
                {
                    Entity followerEntity = Unsafe.Add(ref entityFirst, index);
                    throw new InvalidOperationException(
                        $"MassCrowdFormationFollower entity {followerEntity.Id} slot {follower.SlotIndex} exceeds anchor '{MassCrowdFormationRegistry.GetName(formationId)}' slotCount {anchor.SlotCount}.");
                }

                if (follower.Anchor != anchor.Entity)
                {
                    follower.Anchor = anchor.Entity;
                }

                float slotOffsetX = (lateralX * follower.LocalOffsetXCm) + (forwardX * follower.LocalOffsetYCm);
                float slotOffsetY = (lateralY * follower.LocalOffsetXCm) + (forwardY * follower.LocalOffsetYCm);
                MassNavigationCarriedSlotTarget resolvedTarget = _simulation.ResolveCarriedAgentSlotTarget(
                    agentIndices[index].Value,
                    carrierSync.CarrierLocalXCm,
                    carrierSync.CarrierLocalYCm,
                    slotOffsetX,
                    slotOffsetY);
                _simulation.ApplyCarriedAgentSlotTarget(
                    agentIndices[index].Value,
                    in resolvedTarget,
                    resetRecovery: true);

                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (_engine.World.TryGet(entity, out FacingDirection facing) &&
                    facing.AngleRad != anchor.FacingRad)
                {
                    facing.AngleRad = anchor.FacingRad;
                    _engine.World.Set(entity, facing);
                }
            }
        }

        state.TargetSnapshotInitialized = true;
        state.TargetCenterWorldXCm = carrierSync.CarrierWorldXCm;
        state.TargetCenterWorldYCm = carrierSync.CarrierWorldYCm;
        state.TargetFacingRad = anchor.FacingRad;
        _syncStateByFormationId[formationId] = state;
    }

    private MassCrowdFollowerLocomotion ResolveLocomotion(int formationId, Entity anchor)
    {
        if (_engine.World.TryGet(anchor, out MassCrowdFollowerLocomotion locomotion))
        {
            if (!(locomotion.TargetChangeEpsilonCm > 0f))
            {
                throw new InvalidOperationException($"MassCrowdFormationAnchor '{MassCrowdFormationRegistry.GetName(formationId)}' requires MassCrowdFollowerLocomotion.TargetChangeEpsilonCm > 0.");
            }

            if (!(locomotion.FacingChangeEpsilonRadians > 0f))
            {
                throw new InvalidOperationException($"MassCrowdFormationAnchor '{MassCrowdFormationRegistry.GetName(formationId)}' requires MassCrowdFollowerLocomotion.FacingChangeEpsilonRadians > 0.");
            }

            return locomotion;
        }

        throw new InvalidOperationException(
            $"MassCrowdFormationAnchor '{MassCrowdFormationRegistry.GetName(formationId)}' requires explicit MassCrowdFollowerLocomotion.");
    }

    private static float NormalizeAngleRadians(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    private readonly record struct AnchorSnapshot(
        Entity Entity,
        int AgentIndex,
        int SlotCount,
        float FacingRad,
        MassCrowdFollowerLocomotion Locomotion);

    private struct FormationSyncState
    {
        public bool CarrierSnapshotInitialized;
        public float CarrierWorldXCm;
        public float CarrierWorldYCm;
        public bool TargetSnapshotInitialized;
        public float TargetCenterWorldXCm;
        public float TargetCenterWorldYCm;
        public float TargetFacingRad;
    }
}
