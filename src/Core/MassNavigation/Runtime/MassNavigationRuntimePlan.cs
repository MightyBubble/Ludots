using System;
using System.Collections.Generic;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly record struct MassNavigationCadencePlan(
    int SimulationHz,
    int TargetUpdateHz,
    int FlowStepHz,
    int FlowCrowdStampHz,
    int FlowObstacleStampHz,
    int HardResolveHz,
    int EntitySyncHz,
    int MaxStepsPerFixedTick,
    int HardResolveCandidateThresholdAgents,
    int OrderIdleScanIntervalFrames);

public readonly record struct MassNavigationFlowPlan(
    bool CrowdCostEnabled,
    int CrowdStampBudgetAgentsPerRefresh);

public readonly record struct MassNavigationStreamingPlan(
    float RetainSeconds,
    int RadiusCm);

public readonly record struct MassNavigationRuntimeCapacityPlan(
    int InitialCommandActorScratchCapacity,
    int InitialCommandActorSnapshotCapacity,
    int NavigationGroupCapacity,
    int GroupMembershipAgentCapacity,
    int CommandActorScratchCapacity,
    int GroupMemberCapacity,
    int OrderIngestionTokenCapacity,
    int OrderIngestionMemberCapacity,
    int LoadedChunkCapacity,
    int MetadataTeamCapacity);

public readonly record struct MassNavigationHotZonePlan(
    string Id,
    string Label,
    int CenterXCm,
    int CenterYCm);

public sealed class MassNavigationWorldPlan
{
    private readonly MassNavigationHotZonePlan[] _hotZones;
    private readonly int _initialHotZoneIndex;

    internal MassNavigationWorldPlan(MassNavigationWorldConfig config)
    {
        StreamingChunkSizeCm = config.StreamingChunkSizeCm;
        CommandFocusHoldTicks = config.CommandFocusHoldTicks;
        WorkAreaPaddingCm = config.WorkAreaPaddingCm;
        WorkAreaMaxWidthCm = config.WorkAreaMaxWidthCm;
        WorkAreaMaxHeightCm = config.WorkAreaMaxHeightCm;
        _hotZones = new MassNavigationHotZonePlan[config.HotZones.Length];
        _initialHotZoneIndex = -1;
        for (int i = 0; i < config.HotZones.Length; i++)
        {
            MassNavigationHotZoneConfig source = config.HotZones[i];
            _hotZones[i] = new MassNavigationHotZonePlan(
                source.Id,
                source.Label,
                source.CenterXCm,
                source.CenterYCm);
            if (string.Equals(source.Id, config.ActiveHotZoneId, StringComparison.Ordinal))
            {
                _initialHotZoneIndex = i;
            }
        }

        if (_initialHotZoneIndex < 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation world plan cannot resolve active hot zone '{config.ActiveHotZoneId}'.");
        }
    }

    public int StreamingChunkSizeCm { get; }
    public int CommandFocusHoldTicks { get; }
    public int WorkAreaPaddingCm { get; }
    public int WorkAreaMaxWidthCm { get; }
    public int WorkAreaMaxHeightCm { get; }
    public MassNavigationHotZonePlan InitialHotZone => _hotZones[_initialHotZoneIndex];
    public ReadOnlySpan<MassNavigationHotZonePlan> HotZones => _hotZones;
}

public readonly record struct MassNavigationAgentProfilePlan(
    string Id,
    bool Heavy,
    float VisualScale,
    float SpeedCmPerSecond,
    int EveryNth,
    int NthOffset,
    float RadiusCm,
    float Mass);

public sealed class MassNavigationAgentProfilePlanSet
{
    private readonly MassNavigationAgentProfilePlan[] _profiles;
    private readonly Dictionary<string, int> _indexById;
    private readonly int _defaultProfileIndex;

    internal MassNavigationAgentProfilePlanSet(MassNavigationAgentProfileSetConfig config)
    {
        _profiles = new MassNavigationAgentProfilePlan[config.Profiles.Length];
        _indexById = new Dictionary<string, int>(config.Profiles.Length, StringComparer.Ordinal);
        _defaultProfileIndex = -1;
        for (int i = 0; i < config.Profiles.Length; i++)
        {
            MassNavigationAgentProfileConfig source = config.Profiles[i];
            var geometry = config.ResolveGeometry(source.Id);
            _profiles[i] = new MassNavigationAgentProfilePlan(
                source.Id,
                source.Heavy,
                source.VisualScale,
                source.SpeedCmPerSecond,
                source.EveryNth,
                source.NthOffset,
                geometry.RadiusCm,
                geometry.Mass);
            _indexById.Add(source.Id, i);
            if (string.Equals(source.Id, config.DefaultProfileId, StringComparison.Ordinal))
            {
                _defaultProfileIndex = i;
            }
        }

        if (_defaultProfileIndex < 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent profile plan cannot resolve default profile '{config.DefaultProfileId}'.");
        }
    }

    public ReadOnlySpan<MassNavigationAgentProfilePlan> Profiles => _profiles;

    public MassNavigationAgentProfilePlan ResolveForLocalIndex(int localIndex)
    {
        for (int i = 0; i < _profiles.Length; i++)
        {
            MassNavigationAgentProfilePlan profile = _profiles[i];
            if (profile.EveryNth > 0 && localIndex % profile.EveryNth == profile.NthOffset)
            {
                return profile;
            }
        }

        return _profiles[_defaultProfileIndex];
    }

    public MassNavigationAgentProfilePlan Resolve(string id)
    {
        if (!_indexById.TryGetValue(id, out int index))
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{id}' is not configured.");
        }

        return _profiles[index];
    }
}

public sealed class MassNavigationRuntimePlan
{
    private MassNavigationRuntimePlan(
        MassNavigationCadencePlan cadence,
        MassNavigationFlowPlan flow,
        MassNavigationStreamingPlan streaming,
        MassNavigationRuntimeCapacityPlan capacity,
        MassNavigationWorldPlan world,
        MassNavigationAgentProfilePlanSet agentProfiles)
    {
        Cadence = cadence;
        Flow = flow;
        Streaming = streaming;
        Capacity = capacity;
        World = world;
        AgentProfiles = agentProfiles;
    }

    public MassNavigationCadencePlan Cadence { get; }
    public MassNavigationFlowPlan Flow { get; }
    public MassNavigationStreamingPlan Streaming { get; }
    public MassNavigationRuntimeCapacityPlan Capacity { get; }
    public MassNavigationWorldPlan World { get; }
    public MassNavigationAgentProfilePlanSet AgentProfiles { get; }

    public static MassNavigationRuntimePlan Compile(MassNavigationConfig config)
    {
        System.ArgumentNullException.ThrowIfNull(config);
        MassNavigationCapacityConfig capacity = config.Capacity;
        return new MassNavigationRuntimePlan(
            new MassNavigationCadencePlan(
                config.Cadence.SimulationHz,
                config.Cadence.TargetUpdateHz,
                config.Cadence.FlowStepHz,
                config.Cadence.FlowCrowdStampHz,
                config.Cadence.FlowObstacleStampHz,
                config.Cadence.HardResolveHz,
                config.Cadence.EntitySyncHz,
                config.Cadence.MaxStepsPerFixedTick,
                config.Cadence.HardResolveCandidateThresholdAgents,
                config.Cadence.OrderIdleScanIntervalFrames),
            new MassNavigationFlowPlan(
                config.Flow.CrowdCostEnabled,
                config.Flow.CrowdStampBudgetAgentsPerRefresh),
            new MassNavigationStreamingPlan(
                config.Streaming.RetainSeconds,
                config.Streaming.RadiusCm),
            new MassNavigationRuntimeCapacityPlan(
                capacity.InitialCommandActorScratchCapacity,
                capacity.InitialCommandActorSnapshotCapacity,
                capacity.NavigationGroupCapacity,
                capacity.GroupMembershipAgentCapacity,
                capacity.CommandActorScratchCapacity,
                capacity.GroupMemberCapacity,
                capacity.OrderIngestionTokenCapacity,
                capacity.OrderIngestionMemberCapacity,
                capacity.LoadedChunkCapacity,
                capacity.MetadataTeamCapacity),
            new MassNavigationWorldPlan(config.World
                ?? throw new InvalidOperationException("MassNavigation runtime plan requires world config.")),
            new MassNavigationAgentProfilePlanSet(config.AgentProfiles));
    }
}
