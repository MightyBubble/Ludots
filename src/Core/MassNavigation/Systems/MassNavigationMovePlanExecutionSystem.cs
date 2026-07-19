using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Systems;

public sealed class MassNavigationMovePlanExecutionSystem : ISystem<float>, IMovePlanCommandGroupExecutionSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, MovePlanExecutionIntent, MovePlanExecutionResult>()
        .WithNone<SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly int _commandGroupCapacity;
    private readonly int _memberCapacity;
    private readonly Dictionary<int, int> _bucketIndexByToken;
    private readonly List<CommandGroupBucket> _buckets;
    private readonly HashSet<int> _activeTokens;
    private readonly List<int> _completedTokens;
    private readonly Entity[] _memberEntities;
    private readonly Vector2[] _preparedMemberTargets;
    private MassNavigationRouteExecutionSink? _routeSink;
    private MassNavigationSimulationRuntime? _lastSimulation;
    private int _usedBucketCount;

    public MassNavigationMovePlanExecutionSystem(GameEngine engine, MassNavigationConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        MassNavigationRuntimeCapacityConfig capacity = config.ScenarioRuntime.RuntimeCapacity;
        _commandGroupCapacity = capacity.MovePlanExecutionGroupCapacity;
        _memberCapacity = capacity.MovePlanExecutionMemberCapacity;
        _bucketIndexByToken = new Dictionary<int, int>(_commandGroupCapacity);
        _buckets = new List<CommandGroupBucket>(_commandGroupCapacity);
        _activeTokens = new HashSet<int>(_commandGroupCapacity);
        _completedTokens = new List<int>(_commandGroupCapacity);
        _memberEntities = new Entity[_memberCapacity];
        _preparedMemberTargets = new Vector2[_memberCapacity];
        for (int i = 0; i < _commandGroupCapacity; i++)
        {
            _buckets.Add(new CommandGroupBucket(_memberCapacity));
        }
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (!ReferenceEquals(_lastSimulation, simulation))
        {
            _lastSimulation = simulation;
            _routeSink = null;
        }

        _bucketIndexByToken.Clear();
        _activeTokens.Clear();
        _completedTokens.Clear();
        for (int i = 0; i < _usedBucketCount; i++)
        {
            _buckets[i].Reset();
        }

        _usedBucketCount = 0;
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            Span<MovePlanExecutionIntent> intents = chunk.GetSpan<MovePlanExecutionIntent>();

            foreach (int index in chunk)
            {
                ref readonly MovePlanExecutionIntent intent = ref intents[index];
                if (intent.HasTarget == 0 || intent.Mode != MovePlanExecutionMode.CommandGroup)
                {
                    continue;
                }

                int token = intent.CommandGroupToken;
                if (token <= 0)
                {
                    throw new InvalidOperationException(
                        "MassNavigation execution intent requires a positive command-group token.");
                }

                int bucketIndex = GetOrCreateBucket(token);
                _activeTokens.Add(token);
                CommandGroupBucket bucket = _buckets[bucketIndex];
                bucket.AssignOrValidatePayload(
                    simulation.MassNavigationFlow.GetTeam(agentIndices[index].Value),
                    intent.TargetWorldCm);
                if (bucket.Members.Count >= _memberCapacity)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation command group {token} required more than configured scenarioRuntime.runtimeCapacity.movePlanExecutionMemberCapacity {_memberCapacity} members.");
                }

                bucket.AddMember(agentIndices[index].Value);
            }
        }

        MassNavigationRouteExecutionSink? routeSink = ResolveRouteSink(simulation);
        PrepareCommandGroups(simulation, routeSink);
        try
        {
            CommitCommandFocus(simulation);
            simulation.NavGroupRuntime.PruneInactiveOrderGroups(simulation.MassNavigationFlow, _activeTokens);
            CommitCommandGroups(simulation, requiresNewGroup: false);
            CommitCommandGroups(simulation, requiresNewGroup: true);

            routeSink?.EndSync();

            for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
            {
                CommandGroupBucket bucket = _buckets[bucketIndex];
                if (bucket.Members.Count <= 0 || bucket.Failed ||
                    !simulation.NavGroupRuntime.TryGetOrderGroup(bucket.Token, out bool arrived) ||
                    !arrived)
                {
                    continue;
                }

                _completedTokens.Add(bucket.Token);
                for (int i = 0; i < bucket.Members.Count; i++)
                {
                    int memberIndex = bucket.Members[i];
                    Entity member = ResolveBoundAgent(simulation, memberIndex, bucket.Token);
                    ref MovePlanExecutionResult result = ref _engine.World.Get<MovePlanExecutionResult>(member);
                    result.CommandGroupToken = bucket.Token;
                    result.Kind = MovePlanExecutionResultKind.Arrived;
                    result.FailureReason = MovePlanFailureReason.None;
                }
            }

            for (int i = 0; i < _completedTokens.Count; i++)
            {
                simulation.NavGroupRuntime.CompleteOrderGroup(simulation.MassNavigationFlow, _completedTokens[i]);
            }

            if (routeSink != null)
            {
                for (int i = 0; i < _completedTokens.Count; i++)
                {
                    routeSink.RemoveOrderToken(_completedTokens[i]);
                }
            }

            simulation.NavGroupRuntime.PruneInactiveOrderGroups(simulation.MassNavigationFlow, _activeTokens);
        }
        catch
        {
            routeSink?.CancelSync();
            throw;
        }

    }

    private void PrepareCommandGroups(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink? routeSink)
    {
        int preparedTargetCount = 0;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            if (preparedTargetCount + bucket.Members.Count > _preparedMemberTargets.Length)
            {
                throw new InvalidOperationException(
                    $"MassNavigation command preparation requires more than configured movePlanExecutionMemberCapacity {_preparedMemberTargets.Length} total members.");
            }

            bucket.PreparedTargetOffset = preparedTargetCount;
            bucket.CommandChanged = simulation.NavGroupRuntime.PrepareOrderMoveCommand(
                simulation.MassNavigationFlow,
                simulation.AgentState,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination,
                _preparedMemberTargets.AsSpan(preparedTargetCount, bucket.Members.Count),
                out Vector2 resolvedDestinationWorldCm);
            bucket.ResolvedDestination = resolvedDestinationWorldCm;
            bucket.RequiresNewGroup = simulation.NavGroupRuntime.RequiresNewOrderGroup(bucket.Token);
            preparedTargetCount += bucket.Members.Count;
        }

        if (routeSink != null)
        {
            ValidateRoutedCommandGroups(simulation, routeSink);
        }

        _activeTokens.Clear();
        int newCommandGroupCount = 0;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0 || bucket.Failed)
            {
                continue;
            }

            _activeTokens.Add(bucket.Token);
            if (simulation.NavGroupRuntime.RequiresNewOrderGroup(bucket.Token))
            {
                newCommandGroupCount++;
            }
        }

        simulation.NavGroupRuntime.EnsureCanAllocateNewOrderGroupsAfterPrune(newCommandGroupCount, _activeTokens);

        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0 || bucket.Failed || !bucket.CommandChanged)
            {
                continue;
            }

            int memberCount = ResolveMemberEntities(simulation, bucket);
            simulation.PreflightOrderTarget(
                bucket.Destination,
                _memberEntities.AsSpan(0, memberCount));
        }

        if (routeSink != null)
        {
            PrepareRouteSync(simulation, routeSink);
        }
    }

    private void CommitCommandGroups(
        MassNavigationSimulationRuntime simulation,
        bool requiresNewGroup)
    {
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0 || bucket.Failed || !bucket.CommandChanged ||
                bucket.RequiresNewGroup != requiresNewGroup)
            {
                continue;
            }

            simulation.NavGroupRuntime.CommitPreparedOrderMoveCommand(
                simulation.MassNavigationFlow,
                simulation.AgentState,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination,
                bucket.ResolvedDestination,
                _preparedMemberTargets.AsSpan(bucket.PreparedTargetOffset, bucket.Members.Count));
            simulation.MarkCommandApply();
        }
    }

    private void CommitCommandFocus(MassNavigationSimulationRuntime simulation)
    {
        CommandGroupBucket? focusBucket = null;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count > 0 && !bucket.Failed && bucket.CommandChanged)
            {
                focusBucket = bucket;
            }
        }

        if (focusBucket == null)
        {
            return;
        }

        int memberCount = ResolveMemberEntities(simulation, focusBucket);
        simulation.FocusOrderTarget(
            focusBucket.Destination,
            _memberEntities.AsSpan(0, memberCount));
    }

    private int ResolveMemberEntities(MassNavigationSimulationRuntime simulation, CommandGroupBucket bucket)
    {
        int count = bucket.Members.Count;
        for (int i = 0; i < count; i++)
        {
            _memberEntities[i] = ResolveBoundAgent(simulation, bucket.Members[i], bucket.Token);
        }

        return count;
    }

    private MassNavigationRouteExecutionSink? ResolveRouteSink(MassNavigationSimulationRuntime simulation)
    {
        IPathService? pathService = _engine.GetService(CoreServiceKeys.PathService);
        PathStore? pathStore = _engine.GetService(CoreServiceKeys.PathStore);
        PathingConfig? pathingConfig = _engine.GetService(CoreServiceKeys.PathingConfig);
        if (pathService == null && pathStore == null && pathingConfig == null)
        {
            _routeSink = null;
            _engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
            return null;
        }

        if (pathService == null || pathStore == null || pathingConfig == null)
        {
            throw new InvalidOperationException(
                "MassNavigation route execution requires PathService, PathStore, and PathingConfig to be registered together.");
        }

        if (_routeSink != null && _routeSink.IsBoundTo(pathService, pathStore, pathingConfig))
        {
            return _routeSink;
        }

        MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
        _routeSink = new MassNavigationRouteExecutionSink(
            pathService,
            pathStore,
            pathingConfig,
            capacity.RouteStateCapacity,
            capacity.RouteWaypointCapacityPerAgent);
        _engine.SetService(MassNavigationKeys.RouteExecutionSink, _routeSink);
        return _routeSink;
    }

    private void ValidateRoutedCommandGroups(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink routeSink)
    {
        MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            CommandGroupBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            for (int i = 0; i < bucket.Members.Count; i++)
            {
                int memberIndex = bucket.Members[i];
                Entity member = ResolveBoundAgent(simulation, memberIndex, bucket.Token);
                MassNavigationRouteSinkResult result = routeSink.ValidateRouteTarget(
                    simulation,
                    _engine.World,
                    member,
                    memberIndex,
                    _preparedMemberTargets[bucket.PreparedTargetOffset + i],
                    bucket.Token,
                    maxExpanded: capacity.RouteMaxExpandedPerRequest,
                    maxPoints: capacity.RouteWaypointCapacityPerAgent);
                if (result.Tracked)
                {
                    continue;
                }

                MarkCommandGroupFailed(
                    simulation,
                    bucket,
                    MovePlanFailureReason.ExecutionUnavailable);
                break;
            }
        }
    }

    private void PrepareRouteSync(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink routeSink)
    {
        routeSink.BeginSync();
        try
        {
            MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
            for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
            {
                CommandGroupBucket bucket = _buckets[bucketIndex];
                if (bucket.Members.Count <= 0 || bucket.Failed)
                {
                    continue;
                }

                for (int i = 0; i < bucket.Members.Count; i++)
                {
                    int memberIndex = bucket.Members[i];
                    Entity member = ResolveBoundAgent(simulation, memberIndex, bucket.Token);
                    MassNavigationRouteSinkResult result = routeSink.TrackRouteTarget(
                        simulation,
                        _engine.World,
                        member,
                        memberIndex,
                        _preparedMemberTargets[bucket.PreparedTargetOffset + i],
                        bucket.Token,
                        maxExpanded: capacity.RouteMaxExpandedPerRequest,
                        maxPoints: capacity.RouteWaypointCapacityPerAgent);
                    EnsureRouteTrackAccepted(result, bucket.Token, memberIndex);
                }
            }

            routeSink.PreflightSync();
        }
        catch
        {
            routeSink.CancelSync();
            throw;
        }
    }

    private void MarkCommandGroupFailed(
        MassNavigationSimulationRuntime simulation,
        CommandGroupBucket bucket,
        MovePlanFailureReason failureReason)
    {
        bucket.Failed = true;
        for (int i = 0; i < bucket.Members.Count; i++)
        {
            Entity member = ResolveBoundAgent(simulation, bucket.Members[i], bucket.Token);
            ref MovePlanExecutionResult result = ref _engine.World.Get<MovePlanExecutionResult>(member);
            result.CommandGroupToken = bucket.Token;
            result.Kind = MovePlanExecutionResultKind.Failed;
            result.FailureReason = failureReason;
        }
    }

    private static void EnsureRouteTrackAccepted(
        MassNavigationRouteSinkResult result,
        int commandGroupToken,
        int memberIndex)
    {
        if (!result.Tracked)
        {
            throw new InvalidOperationException(
                $"MassNavigation route execution failed for command group {commandGroupToken}, agent {memberIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
        }
    }

    private Entity ResolveBoundAgent(MassNavigationSimulationRuntime simulation, int agentIndex, int commandGroupToken)
    {
        if (!simulation.AgentState.TryGetAgentEntity(agentIndex, out Entity entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation command group {commandGroupToken} references unbound agent index {agentIndex}.");
        }

        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation command group {commandGroupToken} references dead agent index {agentIndex}.");
        }

        if (!_engine.World.Has<MovePlanExecutionResult>(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation command group {commandGroupToken} references agent index {agentIndex} without a MovePlanExecutionResult contract.");
        }

        return entity;
    }

    private int GetOrCreateBucket(int token)
    {
        if (_bucketIndexByToken.TryGetValue(token, out int index))
        {
            return index;
        }

        if (_usedBucketCount >= _commandGroupCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation MovePlan execution required more than configured scenarioRuntime.runtimeCapacity.movePlanExecutionGroupCapacity {_commandGroupCapacity} active command groups.");
        }

        index = _usedBucketCount++;
        _bucketIndexByToken[token] = index;
        _buckets[index].Reset(token);

        return index;
    }

    private sealed class CommandGroupBucket
    {
        public CommandGroupBucket(int memberCapacity)
        {
            Members = new List<int>(memberCapacity);
        }

        public int Token { get; private set; }
        public int TeamId { get; set; }
        public Vector2 Destination { get; set; }
        public Vector2 ResolvedDestination { get; set; }
        public List<int> Members { get; }
        public int PreparedTargetOffset { get; set; }
        public bool CommandChanged { get; set; }
        public bool RequiresNewGroup { get; set; }
        public bool Failed { get; set; }
        private bool HasPayload { get; set; }

        public void AssignOrValidatePayload(
            int teamId,
            Vector2 destination)
        {
            if (!HasPayload)
            {
                TeamId = teamId;
                Destination = destination;
                HasPayload = true;
                return;
            }

            if (TeamId != teamId ||
                Destination.X != destination.X ||
                Destination.Y != destination.Y)
            {
                throw new InvalidOperationException(
                    $"MassNavigation command group token {Token} has conflicting execution payloads. One token must carry one team and destination.");
            }
        }

        public void AddMember(int memberIndex)
        {
            Members.Add(memberIndex);
        }

        public void Reset()
        {
            Token = 0;
            TeamId = 0;
            Destination = default;
            ResolvedDestination = default;
            PreparedTargetOffset = 0;
            CommandChanged = false;
            RequiresNewGroup = false;
            Failed = false;
            HasPayload = false;
            Members.Clear();
        }

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            ResolvedDestination = default;
            PreparedTargetOffset = 0;
            CommandChanged = false;
            RequiresNewGroup = false;
            Failed = false;
            HasPayload = false;
            Members.Clear();
        }
    }
}
