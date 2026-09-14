using System;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationRuntimeCapacityConfig
{
    public int NavigationGroupCapacity { get; set; }
    public int GroupMembershipAgentCapacity { get; set; }
    public int GroupMemberCapacity { get; set; }
    public int MovePlanExecutionGroupCapacity { get; set; }
    public int MovePlanExecutionMemberCapacity { get; set; }
    public int RouteStateCapacity { get; set; }
    public int RouteMaxExpandedPerRequest { get; set; }
    public int RouteWaypointCapacityPerAgent { get; set; }

    /// <summary>0 = 未作者，由板绑定按流送窗口 chunk 数推导。</summary>
    public int LoadedChunkCapacity { get; set; }

    public int RelationshipDomainCapacity { get; set; }
    public int DisplacedAgentCapacity { get; set; }

    public void ApplyEngineDefaults()
    {
        if (NavigationGroupCapacity <= 0)
        {
            NavigationGroupCapacity = MassNavigationEngineDefaults.NavigationGroupCapacity;
        }

        if (GroupMembershipAgentCapacity <= 0)
        {
            GroupMembershipAgentCapacity = MassNavigationEngineDefaults.GroupMembershipAgentCapacity;
        }

        if (GroupMemberCapacity <= 0)
        {
            GroupMemberCapacity = GroupMembershipAgentCapacity;
        }

        if (MovePlanExecutionGroupCapacity <= 0)
        {
            MovePlanExecutionGroupCapacity = MassNavigationEngineDefaults.MovePlanExecutionGroupCapacity;
        }

        if (MovePlanExecutionMemberCapacity <= 0)
        {
            MovePlanExecutionMemberCapacity = GroupMembershipAgentCapacity;
        }

        if (RouteStateCapacity <= 0)
        {
            RouteStateCapacity = MassNavigationEngineDefaults.RouteStateCapacity;
        }

        if (RouteMaxExpandedPerRequest <= 0)
        {
            RouteMaxExpandedPerRequest = MassNavigationEngineDefaults.RouteMaxExpandedPerRequest;
        }

        if (RouteWaypointCapacityPerAgent <= 0)
        {
            RouteWaypointCapacityPerAgent = MassNavigationEngineDefaults.RouteWaypointCapacityPerAgent;
        }

        if (RelationshipDomainCapacity <= 0)
        {
            RelationshipDomainCapacity = MassNavigationEngineDefaults.RelationshipDomainCapacity;
        }

        if (DisplacedAgentCapacity <= 0)
        {
            DisplacedAgentCapacity = MassNavigationEngineDefaults.DisplacedAgentCapacity;
        }
    }

    public void Validate()
    {
        RequirePositive(NavigationGroupCapacity, "navigationGroupCapacity");
        RequirePositive(GroupMembershipAgentCapacity, "groupMembershipAgentCapacity");
        RequirePositive(GroupMemberCapacity, "groupMemberCapacity");
        RequirePositive(MovePlanExecutionGroupCapacity, "movePlanExecutionGroupCapacity");
        RequirePositive(MovePlanExecutionMemberCapacity, "movePlanExecutionMemberCapacity");
        RequirePositive(RouteStateCapacity, "routeStateCapacity");
        RequirePositive(RouteMaxExpandedPerRequest, "routeMaxExpandedPerRequest");
        RequirePositive(RouteWaypointCapacityPerAgent, "routeWaypointCapacityPerAgent");
        RequirePositive(RelationshipDomainCapacity, "relationshipDomainCapacity");
        RequirePositive(DisplacedAgentCapacity, "displacedAgentCapacity");

        if (MovePlanExecutionGroupCapacity < NavigationGroupCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation runtimeCapacity.movePlanExecutionGroupCapacity must be >= runtimeCapacity.navigationGroupCapacity.");
        }

    }

    /// <summary>
    /// 板绑定推导：未作者的 loadedChunkCapacity 按流送窗口 chunk 数填充。
    /// </summary>
    public void ApplyBoardDerivedChunkCapacity(int streamingChunkSizeCm, int streamingRadiusCm)
    {
        if (LoadedChunkCapacity > 0)
        {
            return;
        }

        LoadedChunkCapacity = CountSquareChunksForRadius(streamingRadiusCm, streamingChunkSizeCm);
    }

    public void ValidateForStreaming(int streamingChunkSizeCm, int streamingRadiusCm)
    {
        int minimumWindowChunkCapacity = CountSquareChunksForRadius(streamingRadiusCm, streamingChunkSizeCm);
        if (LoadedChunkCapacity < minimumWindowChunkCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtimeCapacity.loadedChunkCapacity {LoadedChunkCapacity} is smaller than one streaming window chunk count {minimumWindowChunkCapacity}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MassNavigation runtimeCapacity.{fieldName} must be > 0.");
        }
    }

    private static int CountSquareChunksForRadius(int radiusCm, int chunkSizeCm)
    {
        if (radiusCm <= 0 || chunkSizeCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation streaming chunk capacity derivation requires positive radius and chunk size.");
        }

        int chunkRadius = (radiusCm + chunkSizeCm - 1) / chunkSizeCm;
        int span = checked((chunkRadius * 2) + 1);
        return checked(span * span);
    }
}
