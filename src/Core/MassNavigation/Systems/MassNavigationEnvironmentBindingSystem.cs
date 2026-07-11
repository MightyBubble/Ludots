using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationEnvironmentBindingSystem : ISystem<float>
{
    private static readonly QueryDescription BlockersQuery = new QueryDescription()
        .WithAll<MassNavigationFlowObstacleProjection, WorldPositionCm>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    private static readonly QueryDescription MarkersQuery = new QueryDescription()
        .WithAll<MassNavigationHotspotMarker, WorldPositionCm>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly MassNavigationRuntimeBinding _binding;
    private MassNavigationSimulationRuntime Simulation => _binding.RequireCurrent();
    private readonly List<MassNavigationObstacleSnapshot> _blockerObstacles = new();
    private readonly CommandBuffer _commandBuffer = new();
    private long _lastSignature;
    private int _observedRuntimeBindingRevision = -1;

    public MassNavigationEnvironmentBindingSystem(GameEngine engine, MassNavigationRuntimeBinding binding)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
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
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        if (_observedRuntimeBindingRevision != _binding.Revision)
        {
            _observedRuntimeBindingRevision = _binding.Revision;
            _lastSignature = 0L;
        }

        MassNavigationEnvironmentSignature environment = ComputeSignature();
        if (environment.Hash == _lastSignature &&
            Simulation.AgentState.BlockerCount == environment.BlockerCount &&
            Simulation.AgentState.WorldMarkerCount == environment.MarkerCount)
        {
            return;
        }

        Simulation.AgentState.ClearEnvironmentCounts();
        BindBlockers();
        BindMarkers();
        _lastSignature = environment.Hash;
        Simulation.MarkStructuralChange();
    }

    private MassNavigationEnvironmentSignature ComputeSignature()
    {
        long hash = 1469598103934665603L;
        int blockerCount = 0;
        int markerCount = 0;
        foreach (ref var chunk in _engine.World.Query(in BlockersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationFlowObstacleProjection> blockers = chunk.GetSpan<MassNavigationFlowObstacleProjection>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationFlowObstacleProjection blocker = blockers[index];
                WorldPositionCm position = positions[index];
                blockerCount += blocker.PieceCount;
                hash = Mix(hash, entity.Id);
                hash = Mix(hash, blocker.PieceCount);
                hash = Mix(hash, blocker.ShapeSignature);
                hash = Mix(hash, blocker.PoseSignature);
                hash = Mix(hash, position.Value.X.GetHashCode());
                hash = Mix(hash, position.Value.Y.GetHashCode());
                for (int pieceIndex = 0; pieceIndex < blocker.PieceCount; pieceIndex++)
                {
                    hash = Mix(hash, (int)blocker.GetShape(pieceIndex));
                    hash = Mix(hash, blocker.GetOffsetXCm(pieceIndex));
                    hash = Mix(hash, blocker.GetOffsetYCm(pieceIndex));
                    hash = Mix(hash, blocker.GetRadiusCm(pieceIndex));
                }
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

        return new MassNavigationEnvironmentSignature(hash, blockerCount, markerCount);
    }

    private void BindBlockers()
    {
        _blockerObstacles.Clear();
        foreach (ref var chunk in _engine.World.Query(in BlockersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationFlowObstacleProjection> blockers = chunk.GetSpan<MassNavigationFlowObstacleProjection>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationFlowObstacleProjection blocker = blockers[index];
                WorldPositionCm position = positions[index];
                if (blocker.PieceCount <= 0)
                {
                    throw new InvalidOperationException($"MassNavigationFlow obstacle projection entity {entity.Id} requires at least one obstacle piece.");
                }

                float maxRadiusCm = 0f;
                for (int pieceIndex = 0; pieceIndex < blocker.PieceCount; pieceIndex++)
                {
                    int radiusCm = blocker.GetRadiusCm(pieceIndex);
                    if (radiusCm <= 0)
                    {
                        throw new InvalidOperationException(
                            $"MassNavigationFlow obstacle projection entity {entity.Id} piece {pieceIndex} requires radiusCm > 0.");
                    }

                    maxRadiusCm = MathF.Max(maxRadiusCm, radiusCm);
                    Fix64 worldX = position.Value.X + Fix64.FromInt(blocker.GetOffsetXCm(pieceIndex));
                    Fix64 worldY = position.Value.Y + Fix64.FromInt(blocker.GetOffsetYCm(pieceIndex));
                    _blockerObstacles.Add(new MassNavigationObstacleSnapshot(
                        worldX.ToFloat(),
                        worldY.ToFloat(),
                        radiusCm));
                }

                var profile = new MassNavigationBlockerProfile { RadiusCm = maxRadiusCm };
                if (_engine.World.Has<MassNavigationBlockerProfile>(entity))
                {
                    _engine.World.Set(entity, profile);
                }
                else
                {
                    _commandBuffer.Add(entity, profile);
                }

                Simulation.AgentState.RegisterBlocker(entity);
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(_engine.World);
        }

        Simulation.RebuildRuntimeObstacles(CollectionsMarshal.AsSpan(_blockerObstacles));
    }

    private void BindMarkers()
    {
        foreach (ref var chunk in _engine.World.Query(in MarkersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Simulation.AgentState.RegisterWorldMarker(Unsafe.Add(ref entityFirst, index));
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

    private readonly record struct MassNavigationEnvironmentSignature(long Hash, int BlockerCount, int MarkerCount);
}
