using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassCrowdEnvironmentBindingSystem : ISystem<float>
{
    private static readonly QueryDescription BlockersQuery = new QueryDescription()
        .WithAll<MassCrowdBlocker, WorldPositionCm>()
        .WithNone<PresentationDestroyPending>();

    private static readonly QueryDescription MarkersQuery = new QueryDescription()
        .WithAll<MassCrowdHotspotMarker, WorldPositionCm>()
        .WithNone<PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly List<MassNavigationObstacleSnapshot> _blockerObstacles = new();
    private readonly CommandBuffer _commandBuffer = new();
    private long _lastSignature;

    public MassCrowdEnvironmentBindingSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose()
    {
        _commandBuffer.Dispose();
    }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        MassCrowdEnvironmentSignature environment = ComputeSignature();
        if (environment.Hash == _lastSignature &&
            _simulation.AgentState.BlockerCount == environment.BlockerCount &&
            _simulation.AgentState.WorldMarkerCount == environment.MarkerCount)
        {
            return;
        }

        _simulation.AgentState.ClearEnvironmentCounts();
        BindBlockers();
        BindMarkers();
        _lastSignature = environment.Hash;
        _simulation.MarkStructuralChange();
    }

    private MassCrowdEnvironmentSignature ComputeSignature()
    {
        long hash = 1469598103934665603L;
        int blockerCount = 0;
        int markerCount = 0;
        foreach (ref var chunk in _engine.World.Query(in BlockersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdBlocker> blockers = chunk.GetSpan<MassCrowdBlocker>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassCrowdBlocker blocker = blockers[index];
                WorldPositionCm position = positions[index];
                blockerCount++;
                hash = Mix(hash, entity.Id);
                hash = Mix(hash, blocker.RadiusCm.GetHashCode());
                hash = Mix(hash, position.Value.X.GetHashCode());
                hash = Mix(hash, position.Value.Y.GetHashCode());
            }
        }

        foreach (ref var chunk in _engine.World.Query(in MarkersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                WorldPositionCm position = positions[index];
                markerCount++;
                hash = Mix(hash, entity.Id);
                hash = Mix(hash, position.Value.X.GetHashCode());
                hash = Mix(hash, position.Value.Y.GetHashCode());
            }
        }

        return new MassCrowdEnvironmentSignature(hash, blockerCount, markerCount);
    }

    private void BindBlockers()
    {
        _blockerObstacles.Clear();
        foreach (ref var chunk in _engine.World.Query(in BlockersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdBlocker> blockers = chunk.GetSpan<MassCrowdBlocker>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassCrowdBlocker blocker = blockers[index];
                WorldPositionCm position = positions[index];
                if (!(blocker.RadiusCm > 0f))
                {
                    throw new InvalidOperationException($"MassCrowdBlocker entity {entity.Id} requires radiusCm > 0.");
                }

                var profile = new MassCrowdBlockerProfile { RadiusCm = blocker.RadiusCm };
                if (_engine.World.Has<MassCrowdBlockerProfile>(entity))
                {
                    _engine.World.Set(entity, profile);
                }
                else
                {
                    _commandBuffer.Add(entity, profile);
                }

                _simulation.AgentState.RegisterBlocker(entity);
                _blockerObstacles.Add(new MassNavigationObstacleSnapshot(
                    position.Value.X.ToFloat(),
                    position.Value.Y.ToFloat(),
                    blocker.RadiusCm));
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(_engine.World);
        }

        _simulation.RebuildRuntimeObstacles(CollectionsMarshal.AsSpan(_blockerObstacles));
    }

    private void BindMarkers()
    {
        foreach (ref var chunk in _engine.World.Query(in MarkersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                _simulation.AgentState.RegisterWorldMarker(Unsafe.Add(ref entityFirst, index));
            }
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

    private readonly record struct MassCrowdEnvironmentSignature(long Hash, int BlockerCount, int MarkerCount);
}
