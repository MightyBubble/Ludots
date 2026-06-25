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
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassCrowdAuthoredAgentBindingSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredAgentsQuery = new QueryDescription()
        .WithAll<MassCrowdAgent>()
        .WithNone<PresentationDestroyPending>();

    private static readonly QueryDescription UnboundAgentsQuery = new QueryDescription()
        .WithAll<MassCrowdAgent>()
        .WithNone<MassCrowdAgentIndex, PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly List<Entity> _entities = new();
    private readonly List<MassNavigationAgentSeed> _seeds = new();
    private readonly List<bool> _controllableFlags = new();
    private long _lastAuthoringSignature;

    public MassCrowdAuthoredAgentBindingSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        int authoredCount = CountAuthoredAgents();
        if (authoredCount <= 0)
        {
            if (_simulation.AgentState.TotalAgents > 0)
            {
                _simulation.ClearAuthoredRuntimeBindings(_engine.World);
                _simulation.MarkStructuralChange();
                _lastAuthoringSignature = 0L;
            }

            return;
        }

        long authoringSignature = ComputeAuthoringSignature();
        if (!HasUnboundAgent() &&
            _simulation.AgentState.TotalAgents == authoredCount &&
            _lastAuthoringSignature == authoringSignature)
        {
            return;
        }

        RebuildAuthoredAgents();
        _lastAuthoringSignature = authoringSignature;
    }

    private int CountAuthoredAgents()
    {
        int count = 0;
        foreach (ref var chunk in _engine.World.Query(in AuthoredAgentsQuery))
        {
            count += chunk.Count;
        }

        return count;
    }

    private bool HasUnboundAgent()
    {
        foreach (ref var chunk in _engine.World.Query(in UnboundAgentsQuery))
        {
            if (chunk.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private long ComputeAuthoringSignature()
    {
        long xor = 0L;
        long sum = 0L;
        long rotatedSum = 0L;
        int count = 0;
        foreach (ref var chunk in _engine.World.Query(in AuthoredAgentsQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdAgent> agents = chunk.GetSpan<MassCrowdAgent>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassCrowdAgent agent = agents[index];
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
                xor ^= entityHash;
                sum += entityHash;
                rotatedSum += RotateLeft(entityHash, 17);
                count++;
            }
        }

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
            Span<MassCrowdAgent> agents = chunk.GetSpan<MassCrowdAgent>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassCrowdAgent agent = agents[index];
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

    private MassNavigationAgentSeed CreateSeed(Entity entity, in MassCrowdAgent agent)
    {
        World world = _engine.World;
        if (!world.TryGet(entity, out Team team))
        {
            throw new InvalidOperationException($"MassCrowdAgent entity {entity.Id} requires Team.");
        }

        if (!world.TryGet(entity, out WorldPositionCm worldPosition))
        {
            throw new InvalidOperationException($"MassCrowdAgent entity {entity.Id} requires WorldPositionCm.");
        }

        if (!world.TryGet(entity, out EntityLayer layer))
        {
            throw new InvalidOperationException($"MassCrowdAgent entity {entity.Id} requires EntityLayer.");
        }

        if (agent.ProfileId <= 0)
        {
            throw new InvalidOperationException($"MassCrowdAgent entity {entity.Id} requires a resolved positive profileId.");
        }

        string profileKey = MassCrowdProfileRegistry.GetName(agent.ProfileId);
        MassNavigationAgentProfileConfig profile = _simulation.Config.AgentProfiles.Resolve(profileKey);
        float worldXCm = worldPosition.Value.X.ToFloat();
        float worldYCm = worldPosition.Value.Y.ToFloat();
        return new MassNavigationAgentSeed(
            team.Id,
            _simulation.ToLocalXCm(worldXCm),
            _simulation.ToLocalYCm(worldYCm),
            profile.Heavy,
            profile.NavMass,
            profile.VisualScale,
            profile.BodyRadiusCm,
            profile.SpeedCmPerSecond,
            new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
    }
}
