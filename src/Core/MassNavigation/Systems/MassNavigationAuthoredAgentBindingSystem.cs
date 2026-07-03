using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationAuthoredAgentBindingSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredAgentsQuery = new QueryDescription()
        .WithAll<MassNavigationAgent>()
        .WithNone<PresentationDestroyPending>();

    private static readonly QueryDescription UnboundAgentsQuery = new QueryDescription()
        .WithAll<MassNavigationAgent>()
        .WithNone<MassNavigationAgentIndex, PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly List<Entity> _entities;
    private readonly List<MassNavigationAgentSeed> _seeds;
    private readonly List<bool> _controllableFlags;
    private readonly int _agentCapacity;
    private long _lastAuthoringSignature;

    public MassNavigationAuthoredAgentBindingSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _agentCapacity = simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        _entities = new List<Entity>(_agentCapacity);
        _seeds = new List<MassNavigationAgentSeed>(_agentCapacity);
        _controllableFlags = new List<bool>(_agentCapacity);
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

        AuthoredAgentBindingScan scan = ScanAuthoredAgentBindingState();
        if (scan.AuthoredCount <= 0)
        {
            if (_simulation.AgentState.TotalAgents > 0)
            {
                _simulation.ClearAuthoredRuntimeBindings(_engine.World);
                _simulation.MarkStructuralChange();
                _lastAuthoringSignature = 0L;
            }

            return;
        }

        if (scan.AuthoredCount > _agentCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored binding observed {scan.AuthoredCount} agents, exceeding configured scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {_agentCapacity}.");
        }

        if (_simulation.AgentState.TotalAgents == scan.AuthoredCount &&
            _lastAuthoringSignature == scan.AuthoringSignature)
        {
            return;
        }

        if (TryAppendUnboundAuthoredAgents(in scan))
        {
            _lastAuthoringSignature = scan.AuthoringSignature;
            return;
        }

        RebuildAuthoredAgents();
        _lastAuthoringSignature = scan.AuthoringSignature;
    }

    private bool TryAppendUnboundAuthoredAgents(in AuthoredAgentBindingScan scan)
    {
        int boundCount = _simulation.AgentState.TotalAgents;
        int unboundCount = scan.UnboundCount;
        if (unboundCount <= 0 || boundCount <= 0 || scan.AuthoredCount <= boundCount)
        {
            return false;
        }

        if (!_simulation.AgentState.HasBoundAgents(boundCount))
        {
            return false;
        }

        if (boundCount + unboundCount != scan.AuthoredCount)
        {
            return false;
        }

        if (scan.BoundAuthoringSignature != _lastAuthoringSignature)
        {
            return false;
        }

        _entities.Clear();
        _seeds.Clear();
        _controllableFlags.Clear();
        foreach (ref var chunk in _engine.World.Query(in UnboundAgentsQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationAgent> agents = chunk.GetSpan<MassNavigationAgent>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationAgent agent = agents[index];
                _entities.Add(entity);
                _seeds.Add(CreateSeed(entity, in agent));
                _controllableFlags.Add(_engine.World.Has<OrderBuffer>(entity));
            }
        }

        if (_entities.Count != unboundCount)
        {
            return false;
        }

        _simulation.AppendAuthoredAgents(
            _engine.World,
            CollectionsMarshal.AsSpan(_entities),
            CollectionsMarshal.AsSpan(_seeds),
            CollectionsMarshal.AsSpan(_controllableFlags));
        return true;
    }

    private AuthoredAgentBindingScan ScanAuthoredAgentBindingState()
    {
        long xor = 0L;
        long sum = 0L;
        long rotatedSum = 0L;
        long boundXor = 0L;
        long boundSum = 0L;
        long boundRotatedSum = 0L;
        int authoredCount = 0;
        int unboundCount = 0;
        int boundCount = 0;
        foreach (ref var chunk in _engine.World.Query(in AuthoredAgentsQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationAgent> agents = chunk.GetSpan<MassNavigationAgent>();
            bool chunkIsBound = chunk.Has<MassNavigationAgentIndex>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationAgent agent = agents[index];
                long entityHash = ComputeEntityAuthoringHash(entity, in agent);
                xor ^= entityHash;
                sum += entityHash;
                rotatedSum += RotateLeft(entityHash, 17);
                authoredCount++;
                if (chunkIsBound)
                {
                    boundXor ^= entityHash;
                    boundSum += entityHash;
                    boundRotatedSum += RotateLeft(entityHash, 17);
                    boundCount++;
                }
                else
                {
                    unboundCount++;
                }
            }
        }

        return new AuthoredAgentBindingScan(
            authoredCount,
            unboundCount,
            boundCount,
            FinalizeAuthoringSignature(authoredCount, xor, sum, rotatedSum),
            FinalizeAuthoringSignature(boundCount, boundXor, boundSum, boundRotatedSum));
    }

    private long ComputeEntityAuthoringHash(Entity entity, in MassNavigationAgent agent)
    {
        long entityHash = 1469598103934665603L;
        entityHash = Mix(entityHash, entity.Id);
        entityHash = Mix(entityHash, agent.ProfileId);
        entityHash = Mix(entityHash, _engine.World.TryGet(entity, out Team team) ? team.Id : 0);
        if (_engine.World.TryGet(entity, out EntityLayer layer))
        {
            entityHash = Mix(entityHash, layer.Value.Category);
            entityHash = Mix(entityHash, layer.Value.Mask);
        }
        else
        {
            entityHash = Mix(entityHash, 0);
            entityHash = Mix(entityHash, 0);
        }

        entityHash = Mix(entityHash, _engine.World.Has<OrderBuffer>(entity) ? 1 : 0);
        return entityHash;
    }

    private static long FinalizeAuthoringSignature(int count, long xor, long sum, long rotatedSum)
    {
        long hash = 1469598103934665603L;
        hash = Mix(hash, count);
        hash = Mix(hash, xor);
        hash = Mix(hash, sum);
        hash = Mix(hash, rotatedSum);
        return hash;
    }

    private static long RotateLeft(long value, int offset)
    {
        unchecked
        {
            ulong raw = (ulong)value;
            return (long)((raw << offset) | (raw >> (64 - offset)));
        }
    }

    private static long Mix(long hash, int value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211L;
            return hash;
        }
    }

    private static long Mix(long hash, uint value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211L;
            return hash;
        }
    }

    private static long Mix(long hash, long value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211L;
            return hash;
        }
    }

    private void RebuildAuthoredAgents()
    {
        _entities.Clear();
        _seeds.Clear();
        _controllableFlags.Clear();

        foreach (ref var chunk in _engine.World.Query(in AuthoredAgentsQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationAgent> agents = chunk.GetSpan<MassNavigationAgent>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationAgent agent = agents[index];
                _entities.Add(entity);
                _seeds.Add(CreateSeed(entity, in agent));
                _controllableFlags.Add(_engine.World.Has<OrderBuffer>(entity));
            }
        }

        _simulation.RebuildFromAuthoredAgents(
            _engine.World,
            CollectionsMarshal.AsSpan(_entities),
            CollectionsMarshal.AsSpan(_seeds),
            CollectionsMarshal.AsSpan(_controllableFlags));
    }

    private MassNavigationAgentSeed CreateSeed(Entity entity, in MassNavigationAgent agent)
    {
        World world = _engine.World;
        if (!world.TryGet(entity, out Team team))
        {
            throw new InvalidOperationException($"MassNavigationAgent entity {entity.Id} requires Team.");
        }

        if (!world.TryGet(entity, out WorldPositionCm worldPosition))
        {
            throw new InvalidOperationException($"MassNavigationAgent entity {entity.Id} requires WorldPositionCm.");
        }

        if (!world.TryGet(entity, out EntityLayer layer))
        {
            throw new InvalidOperationException($"MassNavigationAgent entity {entity.Id} requires EntityLayer.");
        }

        if (agent.ProfileId <= 0)
        {
            throw new InvalidOperationException($"MassNavigationAgent entity {entity.Id} requires a resolved positive profileId.");
        }

        string profileKey = MassNavigationProfileRegistry.GetName(agent.ProfileId);
        MassNavigationAgentProfileConfig profile = _simulation.Config.AgentProfiles.Resolve(profileKey);
        AgentProfileConfig geometry = _simulation.Config.AgentProfiles.ResolveGeometry(profileKey);
        float worldXCm = worldPosition.Value.X.ToFloat();
        float worldYCm = worldPosition.Value.Y.ToFloat();
        return new MassNavigationAgentSeed(
            team.Id,
            _simulation.ToLocalXCm(worldXCm),
            _simulation.ToLocalYCm(worldYCm),
            profile.Heavy,
            geometry.Mass,
            profile.VisualScale,
            geometry.RadiusCm,
            profile.SpeedCmPerSecond,
            new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
    }

    private readonly struct AuthoredAgentBindingScan
    {
        public AuthoredAgentBindingScan(
            int authoredCount,
            int unboundCount,
            int boundCount,
            long authoringSignature,
            long boundAuthoringSignature)
        {
            AuthoredCount = authoredCount;
            UnboundCount = unboundCount;
            BoundCount = boundCount;
            AuthoringSignature = authoringSignature;
            BoundAuthoringSignature = boundAuthoringSignature;
        }

        public int AuthoredCount { get; }
        public int UnboundCount { get; }
        public int BoundCount { get; }
        public long AuthoringSignature { get; }
        public long BoundAuthoringSignature { get; }
    }
}
