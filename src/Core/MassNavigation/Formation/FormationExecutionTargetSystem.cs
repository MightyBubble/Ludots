using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.MassNavigation.Formation;

public sealed class FormationExecutionTargetSystem : ISystem<float>
{
    private const ulong SignatureSeed = 14695981039346656037UL;
    private const ulong SignaturePrime = 1099511628211UL;

    private static readonly QueryDescription AnchorQuery = new QueryDescription()
        .WithAll<FormationAnchorState, FormationCommandState, FormationRuntimeState, MassNavigationAgentIndex, FacingDirection, WorldPositionCm>()
        .WithNone<SuspendedTag>();

    private static readonly QueryDescription MemberQuery = new QueryDescription()
        .WithAll<FormationMemberState, MassNavigationAgentIndex, FacingDirection>()
        .WithNone<SuspendedTag>();

    private static readonly QueryDescription SuspendedAnchorQuery = new QueryDescription()
        .WithAll<FormationAnchorState, SuspendedTag>();

    private static readonly QueryDescription SuspendedMemberQuery = new QueryDescription()
        .WithAll<FormationMemberState, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly IFormationRuntimeGate _runtimeGate;
    private readonly int _formationCapacity;
    private readonly int _maxSlotsPerFormation;
    private readonly int _slotCapacity;
    private readonly int[] _lastTargetCenterXByFormation;
    private readonly int[] _lastTargetCenterYByFormation;
    private readonly int[] _lastTargetFacingByFormation;
    private readonly ulong[] _targetIdentitySignatureByFormation;
    private readonly ulong[] _lastTargetIdentitySignatureByFormation;
    private readonly byte[] _targetSnapshotInitializedByFormation;
    private readonly Vector2[] _anchorCenterByFormation;
    private readonly FormationCommandState[] _commandByFormation;
    private readonly FormationAnchorState[] _anchorConfigByFormation;
    private readonly byte[] _anchorSeenByFormation;
    private readonly byte[] _memberSeenBySlot;
    private readonly int[] _aliveMemberCountByFormation;
    private readonly byte[] _targetChangedByFormation;
    private readonly MassNavigationPreparedMovePlanExecution[] _preparedAnchorExecutionByFormation;
    private readonly byte[] _anchorExecutionPreparedByFormation;
    private readonly MassNavigationPreparedMovePlanExecution[] _preparedMemberExecutionBySlot;
    private readonly byte[] _memberExecutionPreparedBySlot;
    private MassNavigationMovePlanExecutionSink? _movePlanExecutionSink;
    private MassNavigationSimulationRuntime? _movePlanExecutionSinkRuntime;

    public FormationExecutionTargetSystem(
        GameEngine engine,
        IFormationRuntimeGate runtimeGate,
        int formationCapacity,
        int maxSlotsPerFormation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtimeGate = runtimeGate ?? throw new ArgumentNullException(nameof(runtimeGate));
        if (formationCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formationCapacity));
        }

        if (maxSlotsPerFormation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSlotsPerFormation));
        }

        _formationCapacity = formationCapacity;
        _maxSlotsPerFormation = maxSlotsPerFormation;
        _slotCapacity = checked(formationCapacity * maxSlotsPerFormation);
        _lastTargetCenterXByFormation = new int[formationCapacity];
        _lastTargetCenterYByFormation = new int[formationCapacity];
        _lastTargetFacingByFormation = new int[formationCapacity];
        _targetIdentitySignatureByFormation = new ulong[formationCapacity];
        _lastTargetIdentitySignatureByFormation = new ulong[formationCapacity];
        _targetSnapshotInitializedByFormation = new byte[formationCapacity];
        _anchorCenterByFormation = new Vector2[formationCapacity];
        _commandByFormation = new FormationCommandState[formationCapacity];
        _anchorConfigByFormation = new FormationAnchorState[formationCapacity];
        _anchorSeenByFormation = new byte[formationCapacity];
        _memberSeenBySlot = new byte[_slotCapacity];
        _aliveMemberCountByFormation = new int[formationCapacity];
        _targetChangedByFormation = new byte[formationCapacity];
        _preparedAnchorExecutionByFormation = new MassNavigationPreparedMovePlanExecution[formationCapacity];
        _anchorExecutionPreparedByFormation = new byte[formationCapacity];
        _preparedMemberExecutionBySlot = new MassNavigationPreparedMovePlanExecution[_slotCapacity];
        _memberExecutionPreparedBySlot = new byte[_slotCapacity];
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtimeGate.IsFormationRuntimeActive(_engine) ||
            !MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        MassNavigationMovePlanExecutionSink sink = EnsureMovePlanExecutionSink(simulation);
        ThrowIfSuspendedFormationEntitiesExist();
        Array.Clear(_anchorSeenByFormation);
        Array.Clear(_memberSeenBySlot);
        Array.Clear(_aliveMemberCountByFormation);
        Array.Clear(_targetChangedByFormation);
        Array.Clear(_targetIdentitySignatureByFormation);

        ScanAnchors(simulation, sink);
        ScanMembers(sink);
        DetectTargetChanges();
        PrepareAnchorTargets(simulation, sink);
        PrepareMemberTargets(simulation, sink);
        CommitPreparedTargets(sink);
        PublishFormationState(simulation);
    }

    private MassNavigationMovePlanExecutionSink EnsureMovePlanExecutionSink(MassNavigationSimulationRuntime simulation)
    {
        if (_movePlanExecutionSink != null &&
            ReferenceEquals(_movePlanExecutionSinkRuntime, simulation))
        {
            return _movePlanExecutionSink;
        }

        InvalidateTargetSnapshots();
        _movePlanExecutionSink = new MassNavigationMovePlanExecutionSink(simulation);
        _movePlanExecutionSinkRuntime = simulation;
        return _movePlanExecutionSink;
    }

    private void InvalidateTargetSnapshots()
    {
        Array.Clear(_lastTargetCenterXByFormation);
        Array.Clear(_lastTargetCenterYByFormation);
        Array.Clear(_lastTargetFacingByFormation);
        Array.Clear(_lastTargetIdentitySignatureByFormation);
        Array.Clear(_targetSnapshotInitializedByFormation);
    }

    private void ThrowIfSuspendedFormationEntitiesExist()
    {
        foreach (ref var chunk in _engine.World.Query(in SuspendedAnchorQuery))
        {
            Span<FormationAnchorState> anchors = chunk.GetSpan<FormationAnchorState>();
            foreach (int index in chunk)
            {
                throw new InvalidOperationException(
                    $"Formation execution anchor index {anchors[index].FormationIndex} is suspended outside the active map.");
            }
        }

        foreach (ref var chunk in _engine.World.Query(in SuspendedMemberQuery))
        {
            Span<FormationMemberState> members = chunk.GetSpan<FormationMemberState>();
            foreach (int index in chunk)
            {
                throw new InvalidOperationException(
                    $"Formation execution member formation {members[index].FormationIndex} slot {members[index].SlotIndex} is suspended outside the active map.");
            }
        }
    }

    private void ScanAnchors(
        MassNavigationSimulationRuntime simulation,
        MassNavigationMovePlanExecutionSink sink)
    {
        foreach (ref var chunk in _engine.World.Query(in AnchorQuery))
        {
            Span<FormationAnchorState> anchors = chunk.GetSpan<FormationAnchorState>();
            Span<FormationCommandState> commands = chunk.GetSpan<FormationCommandState>();
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                sink.ValidateBinding(_engine.World, entity);
                int formationIndex = anchors[index].FormationIndex;
                RequireFormationIndex(formationIndex);
                if (_anchorSeenByFormation[formationIndex] != 0)
                {
                    throw new InvalidOperationException(
                        $"Formation execution formation index {formationIndex} has more than one live anchor.");
                }

                if (anchors[index].SlotCount < 0 ||
                    anchors[index].SlotCount > _maxSlotsPerFormation ||
                    anchors[index].TargetChangeEpsilonCm <= 0 ||
                    anchors[index].FacingChangeEpsilonMicroRad <= 0 ||
                    commands[index].HasMoveTarget == 0)
                {
                    throw new InvalidOperationException(
                        $"Formation execution formation index {formationIndex} has invalid execution authoring.");
                }

                _anchorSeenByFormation[formationIndex] = 1;
                _commandByFormation[formationIndex] = commands[index];
                _anchorConfigByFormation[formationIndex] = anchors[index];
                _anchorCenterByFormation[formationIndex] = simulation.GetAgentWorldPositionCm(agentIndices[index].Value);
                _targetIdentitySignatureByFormation[formationIndex] = BuildAnchorIdentitySignature(entity, in anchors[index]);
            }
        }
    }

    private void ScanMembers(MassNavigationMovePlanExecutionSink sink)
    {
        foreach (ref var chunk in _engine.World.Query(in MemberQuery))
        {
            Span<FormationMemberState> members = chunk.GetSpan<FormationMemberState>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                sink.ValidateBinding(_engine.World, entity);
                ref readonly FormationMemberState member = ref members[index];
                int slot = ResolveSlotIndex(member.FormationIndex, member.SlotIndex);
                if (_memberSeenBySlot[slot] != 0)
                {
                    throw new InvalidOperationException(
                        $"Formation execution formation {member.FormationIndex} slot {member.SlotIndex} is bound more than once.");
                }

                _memberSeenBySlot[slot] = 1;
                _aliveMemberCountByFormation[member.FormationIndex]++;
                _targetIdentitySignatureByFormation[member.FormationIndex] = CombineSignature(
                    _targetIdentitySignatureByFormation[member.FormationIndex],
                    BuildMemberIdentitySignature(entity, in member));
            }
        }
    }

    private void DetectTargetChanges()
    {
        for (int formationIndex = 0; formationIndex < _formationCapacity; formationIndex++)
        {
            if (_anchorSeenByFormation[formationIndex] == 0)
            {
                continue;
            }

            Vector2 center = _anchorCenterByFormation[formationIndex];
            FormationCommandState command = _commandByFormation[formationIndex];
            FormationAnchorState anchor = _anchorConfigByFormation[formationIndex];
            float commandFacing = FormationNumericEncoding.DecodeRadians(command.TargetFacingMicroRad);
            var currentPose = new FormationPose(center, commandFacing);
            var previousPose = new FormationPose(
                new Vector2(
                    _lastTargetCenterXByFormation[formationIndex],
                    _lastTargetCenterYByFormation[formationIndex]),
                FormationNumericEncoding.DecodeRadians(_lastTargetFacingByFormation[formationIndex]));
            ulong currentIdentitySignature = BuildCurrentTargetIdentitySignature(formationIndex);
            if (currentIdentitySignature != _lastTargetIdentitySignatureByFormation[formationIndex] ||
                FormationTargetPlanner.HasTargetChanged(
                    in currentPose,
                    in previousPose,
                    anchor.TargetChangeEpsilonCm,
                    FormationNumericEncoding.DecodeRadians(anchor.FacingChangeEpsilonMicroRad),
                    _targetSnapshotInitializedByFormation[formationIndex] != 0))
            {
                _targetChangedByFormation[formationIndex] = 1;
            }
        }
    }

    private void PrepareAnchorTargets(
        MassNavigationSimulationRuntime simulation,
        MassNavigationMovePlanExecutionSink sink)
    {
        float anchorStopRadius = simulation.Config.Semantics.Group.UnitTargetStopThresholdCm;
        Array.Clear(_anchorExecutionPreparedByFormation);
        foreach (ref var chunk in _engine.World.Query(in AnchorQuery))
        {
            Span<FormationAnchorState> anchors = chunk.GetSpan<FormationAnchorState>();
            Span<FormationCommandState> commands = chunk.GetSpan<FormationCommandState>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                int formationIndex = anchors[index].FormationIndex;
                RequireFormationIndex(formationIndex);
                FormationCommandState command = commands[index];
                var intent = new MovePlanExecutionIntent
                {
                    TargetWorldCm = new Vector2(command.TargetCenterXCm, command.TargetCenterYCm),
                    StopRadiusCm = anchorStopRadius,
                    HasTarget = 1,
                };
                _preparedAnchorExecutionByFormation[formationIndex] = sink.PrepareApply(
                    _engine.World,
                    Unsafe.Add(ref entityFirst, index),
                    in intent);
                _anchorExecutionPreparedByFormation[formationIndex] = 1;
            }
        }

        for (int formationIndex = 0; formationIndex < _formationCapacity; formationIndex++)
        {
            if (_anchorSeenByFormation[formationIndex] != 0 &&
                _anchorExecutionPreparedByFormation[formationIndex] == 0)
            {
                throw new InvalidOperationException(
                    $"Formation execution formation index {formationIndex} did not prepare an anchor execution target.");
            }
        }
    }

    private void PrepareMemberTargets(
        MassNavigationSimulationRuntime simulation,
        MassNavigationMovePlanExecutionSink sink)
    {
        float memberStopRadius = simulation.Config.Semantics.Group.UnitTargetStopThresholdCm;
        float minimumClearance = simulation.Config.Semantics.TargetProjection.GroupSlotClearanceCm;
        Array.Clear(_memberExecutionPreparedBySlot);
        foreach (ref var chunk in _engine.World.Query(in MemberQuery))
        {
            Span<FormationMemberState> members = chunk.GetSpan<FormationMemberState>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                ref readonly FormationMemberState member = ref members[index];
                int formationIndex = member.FormationIndex;
                RequireFormationIndex(formationIndex);
                if (_anchorSeenByFormation[formationIndex] == 0 ||
                    _targetChangedByFormation[formationIndex] == 0)
                {
                    continue;
                }

                int slot = ResolveSlotIndex(formationIndex, member.SlotIndex);
                var pose = new FormationPose(
                    _anchorCenterByFormation[formationIndex],
                    FormationNumericEncoding.DecodeRadians(_commandByFormation[formationIndex].TargetFacingMicroRad));
                FormationTargetPlan target = FormationTargetPlanner.PlanMemberTarget(
                    in pose,
                    new FormationMember(
                        formationIndex,
                        member.SlotIndex,
                        new Vector2(member.LocalOffsetXCm, member.LocalOffsetYCm)));
                var intent = new MovePlanExecutionIntent
                {
                    TargetWorldCm = target.TargetWorldCm,
                    ProjectionHintWorldCm = target.ProjectionHintWorldCm,
                    StopRadiusCm = memberStopRadius,
                    MinimumClearanceCm = minimumClearance,
                    HasTarget = 1,
                    ResolveNavigableTarget = 1,
                };
                _preparedMemberExecutionBySlot[slot] = sink.PrepareApply(
                    _engine.World,
                    Unsafe.Add(ref entityFirst, index),
                    in intent);
                _memberExecutionPreparedBySlot[slot] = 1;
            }
        }
    }

    private void CommitPreparedTargets(MassNavigationMovePlanExecutionSink sink)
    {
        for (int formationIndex = 0; formationIndex < _formationCapacity; formationIndex++)
        {
            if (_anchorExecutionPreparedByFormation[formationIndex] != 0)
            {
                sink.ApplyPrepared(_engine.World, in _preparedAnchorExecutionByFormation[formationIndex]);
            }
        }

        for (int slot = 0; slot < _slotCapacity; slot++)
        {
            if (_memberExecutionPreparedBySlot[slot] != 0)
            {
                sink.ApplyPrepared(_engine.World, in _preparedMemberExecutionBySlot[slot]);
            }
        }
    }

    private void PublishFormationState(MassNavigationSimulationRuntime simulation)
    {
        foreach (ref var chunk in _engine.World.Query(in AnchorQuery))
        {
            Span<FormationAnchorState> anchors = chunk.GetSpan<FormationAnchorState>();
            Span<FormationCommandState> commands = chunk.GetSpan<FormationCommandState>();
            Span<FormationRuntimeState> states = chunk.GetSpan<FormationRuntimeState>();
            Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                int formationIndex = anchors[index].FormationIndex;
                FormationCommandState command = commands[index];
                float commandFacing = FormationNumericEncoding.DecodeRadians(command.TargetFacingMicroRad);
                facings[index].AngleRad = commandFacing;
                Vector2 center = _anchorCenterByFormation[formationIndex];
                ref FormationRuntimeState state = ref states[index];
                state.MemberCount = anchors[index].SlotCount;
                state.AliveMemberCount = _aliveMemberCountByFormation[formationIndex];
                state.CenterXCm = FormationNumericEncoding.RoundCm(center.X);
                state.CenterYCm = FormationNumericEncoding.RoundCm(center.Y);
                state.FacingMicroRad = command.TargetFacingMicroRad;
                worldPositions[index].Value = Fix64Vec2.FromInt(
                    state.CenterXCm,
                    state.CenterYCm);
            }
        }

        foreach (ref var chunk in _engine.World.Query(in MemberQuery))
        {
            Span<FormationMemberState> members = chunk.GetSpan<FormationMemberState>();
            Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
            foreach (int index in chunk)
            {
                int formationIndex = members[index].FormationIndex;
                if (_targetChangedByFormation[formationIndex] != 0)
                {
                    facings[index].AngleRad = FormationNumericEncoding.DecodeRadians(
                        _commandByFormation[formationIndex].TargetFacingMicroRad);
                }
            }
        }

        for (int formationIndex = 0; formationIndex < _formationCapacity; formationIndex++)
        {
            if (_targetChangedByFormation[formationIndex] == 0)
            {
                continue;
            }

            _lastTargetCenterXByFormation[formationIndex] = FormationNumericEncoding.RoundCm(
                _anchorCenterByFormation[formationIndex].X);
            _lastTargetCenterYByFormation[formationIndex] = FormationNumericEncoding.RoundCm(
                _anchorCenterByFormation[formationIndex].Y);
            _lastTargetFacingByFormation[formationIndex] = _commandByFormation[formationIndex].TargetFacingMicroRad;
            _lastTargetIdentitySignatureByFormation[formationIndex] = BuildCurrentTargetIdentitySignature(formationIndex);
            _targetSnapshotInitializedByFormation[formationIndex] = 1;
        }

        _ = simulation;
    }

    private ulong BuildCurrentTargetIdentitySignature(int formationIndex)
    {
        ulong signature = _targetIdentitySignatureByFormation[formationIndex];
        signature = MixSignature(signature, _aliveMemberCountByFormation[formationIndex]);
        return signature;
    }

    private static ulong BuildAnchorIdentitySignature(Entity entity, in FormationAnchorState anchor)
    {
        ulong signature = SignatureSeed;
        signature = MixEntity(signature, entity);
        signature = MixSignature(signature, anchor.FormationIndex);
        signature = MixSignature(signature, anchor.SlotCount);
        return signature;
    }

    private static ulong BuildMemberIdentitySignature(Entity entity, in FormationMemberState member)
    {
        ulong signature = SignatureSeed;
        signature = MixEntity(signature, entity);
        signature = MixSignature(signature, member.FormationIndex);
        signature = MixSignature(signature, member.SlotIndex);
        signature = MixSignature(signature, member.LocalOffsetXCm);
        signature = MixSignature(signature, member.LocalOffsetYCm);
        return signature;
    }

    private static ulong MixEntity(ulong signature, Entity entity)
    {
        signature = MixSignature(signature, entity.Id);
        signature = MixSignature(signature, entity.WorldId);
        signature = MixSignature(signature, entity.Version);
        return signature;
    }

    private static ulong MixSignature(ulong signature, int value)
    {
        unchecked
        {
            signature ^= (uint)value;
            signature *= SignaturePrime;
            return signature;
        }
    }

    private static ulong CombineSignature(ulong aggregate, ulong value)
    {
        unchecked
        {
            return aggregate + (value ^ RotateLeft(value, 32));
        }
    }

    private static ulong RotateLeft(ulong value, int bits)
    {
        return (value << bits) | (value >> (64 - bits));
    }

    private void RequireFormationIndex(int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationCapacity)
        {
            throw new InvalidOperationException(
                $"Formation execution references formation index {formationIndex}, exceeding configured formation capacity {_formationCapacity}.");
        }
    }

    private int ResolveSlotIndex(int formationIndex, int slotIndex)
    {
        RequireFormationIndex(formationIndex);
        if ((uint)slotIndex >= (uint)_maxSlotsPerFormation)
        {
            throw new InvalidOperationException(
                $"Formation execution slot index {slotIndex} exceeds configured per-formation slot capacity {_maxSlotsPerFormation}.");
        }

        return checked((formationIndex * _maxSlotsPerFormation) + slotIndex);
    }
}
