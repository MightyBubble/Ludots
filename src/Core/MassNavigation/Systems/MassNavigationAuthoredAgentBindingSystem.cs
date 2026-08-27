using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationAuthoredAgentBindingSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredAgentsQuery = new QueryDescription()
        .WithAll<MassNavigationAgent>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    private static readonly QueryDescription UnboundAgentsQuery = new QueryDescription()
        .WithAll<MassNavigationAgent>()
        .WithNone<MassNavigationAgentIndex, PresentationDestroyPending, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly Ludots.Core.Movement.PoseAuthorityArbiter _poseAuthorityArbiter;
    private readonly List<Entity> _entities;
    private readonly List<MassNavigationAgentSeed> _seeds;
    private readonly List<bool> _controllableFlags;
    private readonly int _agentCapacity;
    private readonly ControlDomainQuery _controlDomains;
    private readonly DomainStanceQuery _stances;
    private readonly Entity[] _projectedEntitiesByAgentIndex;
    private readonly Entity[] _projectedDomainsByAgentIndex;
    private readonly byte[] _projectedDomainValidByAgentIndex;
    private MassNavigationSimulationRuntime? _lastSimulation;
    private long _lastAuthoringSignature;
    private uint _projectedRelationshipRevision = uint.MaxValue;

    internal int DomainResolutionCount { get; private set; }

    public MassNavigationAuthoredAgentBindingSystem(GameEngine engine, MassNavigationConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _agentCapacity = config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        _poseAuthorityArbiter = engine.GetService(CoreServiceKeys.PoseAuthorityArbiter)
            ?? throw new InvalidOperationException("MassNavigation authored binding requires the PoseAuthorityArbiter service.");
        _controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException("MassNavigation authored binding requires ControlDomainQuery.");
        _stances = engine.GetService(CoreServiceKeys.DomainStanceQuery)
            ?? throw new InvalidOperationException("MassNavigation authored binding requires DomainStanceQuery.");
        _entities = new List<Entity>(_agentCapacity);
        _seeds = new List<MassNavigationAgentSeed>(_agentCapacity);
        _controllableFlags = new List<bool>(_agentCapacity);
        _projectedEntitiesByAgentIndex = new Entity[_agentCapacity];
        _projectedDomainsByAgentIndex = new Entity[_agentCapacity];
        _projectedDomainValidByAgentIndex = new byte[_agentCapacity];
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetActiveNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (!ReferenceEquals(_lastSimulation, simulation))
        {
            _lastSimulation = simulation;
            _lastAuthoringSignature = 0L;
            _projectedRelationshipRevision = uint.MaxValue;
            ClearProjectedDomains();
        }

        uint relationshipRevision = ResolveRelationshipRevision();
        bool refreshRelationshipProjection = relationshipRevision != _projectedRelationshipRevision;
        AuthoredAgentBindingScan scan = ScanAuthoredAgentBindingState(refreshRelationshipProjection);
        if (scan.AuthoredCount <= 0)
        {
            if (simulation.AgentState.TotalAgents > 0)
            {
                CancelPoseWindowsBeforeStructuralReset();
                simulation.ClearAuthoredRuntimeBindings(_engine.World);
                simulation.MarkStructuralChange();
                _lastAuthoringSignature = 0L;
                ClearProjectedDomains();
            }

            _projectedRelationshipRevision = relationshipRevision;
            CompleteAgentBindingPass(simulation);
            return;
        }

        if (scan.AuthoredCount > _agentCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored binding observed {scan.AuthoredCount} agents, exceeding configured scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {_agentCapacity}.");
        }

        if (scan.UnboundCount == 0 &&
            !scan.ProjectedDomainChanged &&
            simulation.AgentState.TotalAgents == scan.AuthoredCount &&
            _lastAuthoringSignature == scan.AuthoringSignature)
        {
            _projectedRelationshipRevision = relationshipRevision;
            CompleteAgentBindingPass(simulation);
            return;
        }

        if (TryAppendUnboundAuthoredAgents(simulation, in scan))
        {
            _lastAuthoringSignature = scan.AuthoringSignature;
            _projectedRelationshipRevision = relationshipRevision;
            CompleteAgentBindingPass(simulation);
            return;
        }

        RebuildAuthoredAgents(simulation);
        _lastAuthoringSignature = scan.AuthoringSignature;
        _projectedRelationshipRevision = relationshipRevision;
        CompleteAgentBindingPass(simulation);
    }

    private void CompleteAgentBindingPass(MassNavigationSimulationRuntime simulation)
    {
        simulation.MarkAuthoredAgentBindingPassComplete();
        MassNavigationIds.PublishPreparedWhenBindingComplete(_engine, simulation);
    }

    private bool TryAppendUnboundAuthoredAgents(
        MassNavigationSimulationRuntime simulation,
        in AuthoredAgentBindingScan scan)
    {
        int boundCount = simulation.AgentState.TotalAgents;
        int unboundCount = scan.UnboundCount;
        if (unboundCount <= 0 || boundCount <= 0 || scan.AuthoredCount <= boundCount)
        {
            return false;
        }

        if (!simulation.AgentState.HasBoundAgents(boundCount))
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
                _seeds.Add(CreateSeed(simulation, entity, in agent));
                _controllableFlags.Add(true);
            }
        }

        if (_entities.Count != unboundCount)
        {
            return false;
        }

        simulation.AppendAuthoredAgents(
            _engine.World,
            CollectionsMarshal.AsSpan(_entities),
            CollectionsMarshal.AsSpan(_seeds),
            CollectionsMarshal.AsSpan(_controllableFlags));
        StoreProjectedDomainsForBoundEntities(_entities, _seeds);
        return true;
    }

    private AuthoredAgentBindingScan ScanAuthoredAgentBindingState(bool refreshRelationshipProjection)
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
        bool projectedDomainChanged = false;
        foreach (ref var chunk in _engine.World.Query(in AuthoredAgentsQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationAgent> agents = chunk.GetSpan<MassNavigationAgent>();
            bool chunkIsBound = chunk.Has<MassNavigationAgentIndex>();
            Span<MassNavigationAgentIndex> agentIndices = chunkIsBound
                ? chunk.GetSpan<MassNavigationAgentIndex>()
                : default;
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationAgent agent = agents[index];
                Entity domainRep;
                if (!chunkIsBound)
                {
                    domainRep = ResolveDomain(entity);
                }
                else
                {
                    int agentIndex = agentIndices[index].Value;
                    if (refreshRelationshipProjection)
                    {
                        domainRep = ResolveAndStoreProjectedDomain(entity, agentIndex, out bool domainChanged);
                        projectedDomainChanged |= domainChanged;
                    }
                    else
                    {
                        domainRep = RequireProjectedDomain(entity, agentIndex);
                    }
                }

                long entityHash = ComputeEntityAuthoringHash(entity, in agent, domainRep);
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
            projectedDomainChanged,
            FinalizeAuthoringSignature(authoredCount, xor, sum, rotatedSum),
            FinalizeAuthoringSignature(boundCount, boundXor, boundSum, boundRotatedSum));
    }

    private long ComputeEntityAuthoringHash(Entity entity, in MassNavigationAgent agent, Entity domainRep)
    {
        long entityHash = 1469598103934665603L;
        entityHash = Mix(entityHash, entity.Id);
        entityHash = Mix(entityHash, agent.ProfileId);
        entityHash = Mix(entityHash, domainRep.Id);
        entityHash = Mix(entityHash, domainRep.Version);
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

    /// <summary>
    /// 结构重建/清空会重排 agent 索引并清空求解器的 displaced 标记，
    /// 活跃的位姿写权窗口必须同步作废，否则仲裁器与求解器状态错位。
    /// 取消经桥做幂等清理；对应的位移效果会在下一 tick 识别窗口消失并合法终止。
    /// </summary>
    private void CancelPoseWindowsBeforeStructuralReset()
    {
        if (_poseAuthorityArbiter.ActiveWindowCount > 0 || _poseAuthorityArbiter.PendingTransitionCount > 0)
        {
            _poseAuthorityArbiter.CancelAllWindows(_engine.World);
        }
    }

    private void RebuildAuthoredAgents(MassNavigationSimulationRuntime simulation)
    {
        CancelPoseWindowsBeforeStructuralReset();
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
                _seeds.Add(CreateSeed(simulation, entity, in agent));
                _controllableFlags.Add(true);
            }
        }

        simulation.RebuildFromAuthoredAgents(
            _engine.World,
            CollectionsMarshal.AsSpan(_entities),
            CollectionsMarshal.AsSpan(_seeds),
            CollectionsMarshal.AsSpan(_controllableFlags));
        ClearProjectedDomains();
        StoreProjectedDomainsForBoundEntities(_entities, _seeds);
    }

    private MassNavigationAgentSeed CreateSeed(
        MassNavigationSimulationRuntime simulation,
        Entity entity,
        in MassNavigationAgent agent)
    {
        World world = _engine.World;
        Entity domainRep = world.TryGet(entity, out MassNavigationAgentIndex agentIndex) &&
                           (uint)agentIndex.Value < (uint)_projectedDomainValidByAgentIndex.Length &&
                           _projectedDomainValidByAgentIndex[agentIndex.Value] != 0 &&
                           _projectedEntitiesByAgentIndex[agentIndex.Value] == entity
            ? _projectedDomainsByAgentIndex[agentIndex.Value]
            : ResolveDomain(entity);

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
        MassNavigationAgentProfileConfig profile = simulation.Config.AgentProfiles.Resolve(profileKey);
        AgentProfileConfig geometry = simulation.Config.AgentProfiles.ResolveGeometry(profileKey);
        float worldXCm = worldPosition.Value.X.ToFloat();
        float worldYCm = worldPosition.Value.Y.ToFloat();
        return new MassNavigationAgentSeed(
            domainRep,
            simulation.ToLocalXCm(worldXCm),
            simulation.ToLocalYCm(worldYCm),
            profile.Heavy,
            geometry.Mass,
            geometry.RadiusCm,
            profile.SpeedCmPerSecond,
            new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
    }

    private Entity ResolveDomain(Entity entity)
    {
        DomainResolutionCount++;
        if (_controlDomains.TryResolveControlDomain(entity, out Entity controlDomain))
        {
            return controlDomain;
        }

        if (_stances.TryResolveStanceDomain(entity, out Entity stanceDomain))
        {
            return stanceDomain;
        }

        throw new InvalidOperationException(
            $"MassNavigationAgent entity {entity.Id} requires an authored control-domain or member-of relationship.");
    }

    private uint ResolveRelationshipRevision()
    {
        uint controlRevision = _controlDomains.Revision;
        uint stanceRevision = _stances.Revision;
        if (controlRevision != stanceRevision)
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship projection requires one committed relationship revision, but control-domain is {controlRevision} and stance-domain is {stanceRevision}.");
        }

        return controlRevision;
    }

    private Entity ResolveAndStoreProjectedDomain(Entity entity, int agentIndex, out bool changed)
    {
        Entity domain = ResolveDomain(entity);
        changed = (uint)agentIndex >= (uint)_projectedDomainValidByAgentIndex.Length ||
                  _projectedDomainValidByAgentIndex[agentIndex] == 0 ||
                  _projectedEntitiesByAgentIndex[agentIndex] != entity ||
                  _projectedDomainsByAgentIndex[agentIndex] != domain;
        StoreProjectedDomain(entity, agentIndex, domain);
        return domain;
    }

    private Entity RequireProjectedDomain(Entity entity, int agentIndex)
    {
        if ((uint)agentIndex >= (uint)_projectedDomainValidByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored binding references agent index {agentIndex}, exceeding configured capacity {_projectedDomainValidByAgentIndex.Length}.");
        }

        if (_projectedDomainValidByAgentIndex[agentIndex] == 0 ||
            _projectedEntitiesByAgentIndex[agentIndex] != entity)
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship projection has no committed domain for entity {entity.Id} at agent index {agentIndex}.");
        }

        return _projectedDomainsByAgentIndex[agentIndex];
    }

    private void StoreProjectedDomainsForBoundEntities(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<MassNavigationAgentSeed> seeds)
    {
        if (entities.Count != seeds.Count)
        {
            throw new InvalidOperationException("MassNavigation relationship projection requires one domain seed per bound entity.");
        }

        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            if (!_engine.World.TryGet(entity, out MassNavigationAgentIndex agentIndex))
            {
                throw new InvalidOperationException(
                    $"MassNavigation relationship projection could not find the committed agent index for entity {entity.Id}.");
            }

            StoreProjectedDomain(entity, agentIndex.Value, seeds[i].DomainRep);
        }
    }

    private void StoreProjectedDomain(Entity entity, int agentIndex, Entity domain)
    {
        if ((uint)agentIndex >= (uint)_projectedDomainValidByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship projection agent index {agentIndex} exceeds configured capacity {_projectedDomainValidByAgentIndex.Length}.");
        }

        if (domain == Entity.Null)
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship projection requires a non-null domain for entity {entity.Id}.");
        }

        _projectedEntitiesByAgentIndex[agentIndex] = entity;
        _projectedDomainsByAgentIndex[agentIndex] = domain;
        _projectedDomainValidByAgentIndex[agentIndex] = 1;
    }

    private void ClearProjectedDomains()
    {
        Array.Clear(_projectedEntitiesByAgentIndex);
        Array.Clear(_projectedDomainsByAgentIndex);
        Array.Clear(_projectedDomainValidByAgentIndex);
    }

    private readonly struct AuthoredAgentBindingScan
    {
        public AuthoredAgentBindingScan(
            int authoredCount,
            int unboundCount,
            int boundCount,
            bool projectedDomainChanged,
            long authoringSignature,
            long boundAuthoringSignature)
        {
            AuthoredCount = authoredCount;
            UnboundCount = unboundCount;
            BoundCount = boundCount;
            ProjectedDomainChanged = projectedDomainChanged;
            AuthoringSignature = authoringSignature;
            BoundAuthoringSignature = boundAuthoringSignature;
        }

        public int AuthoredCount { get; }
        public int UnboundCount { get; }
        public int BoundCount { get; }
        public bool ProjectedDomainChanged { get; }
        public long AuthoringSignature { get; }
        public long BoundAuthoringSignature { get; }
    }
}
