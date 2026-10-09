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

    private const long FnvOffsetBasis = 1469598103934665603L;
    private const long FnvPrime = 1099511628211L;

    private readonly GameEngine _engine;
    private readonly List<MassNavigationObstacleSnapshot> _blockerObstacles = new();
    private readonly CommandBuffer _commandBuffer = new();
    private MassNavigationSimulationRuntime? _lastSimulation;
    private long _lastSignature;

    public MassNavigationEnvironmentBindingSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
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
        if (!MassNavigationIds.TryGetActiveNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (!ReferenceEquals(_lastSimulation, simulation))
        {
            _lastSimulation = simulation;
            _lastSignature = 0L;
        }

        MassNavigationEnvironmentSignature environment = ComputeSignature(_engine.World);
        if (environment.Hash == _lastSignature &&
            simulation.AgentState.BlockerCount == environment.BlockerCount &&
            simulation.AgentState.WorldMarkerCount == environment.MarkerCount)
        {
            CompleteEnvironmentBindingPass(simulation);
            return;
        }

        simulation.AgentState.ClearEnvironmentCounts();
        BindBlockers(simulation);
        BindMarkers(simulation);
        _lastSignature = environment.Hash;
        simulation.MarkStructuralChange();
        CompleteEnvironmentBindingPass(simulation);
    }

    private void CompleteEnvironmentBindingPass(MassNavigationSimulationRuntime simulation)
    {
        simulation.MarkEnvironmentBindingPassComplete();
        MassNavigationIds.PublishPreparedWhenBindingComplete(_engine, simulation);
    }

    /// <summary>
    /// 每个实体单独算指纹再求和：chunk 迁移、实体增删带来的遍历顺序变化不会改变签名，
    /// 只有障碍/标记集合或其几何真的变了才触发重绑。计数口径与 RegisterBlocker 一致，按实体计。
    /// </summary>
    internal static MassNavigationEnvironmentSignature ComputeSignature(World world)
    {
        long blockerSetHash = 0L;
        long markerSetHash = 0L;
        int blockerCount = 0;
        int markerCount = 0;
        foreach (ref var chunk in world.Query(in BlockersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassNavigationFlowObstacleProjection> blockers = chunk.GetSpan<MassNavigationFlowObstacleProjection>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                MassNavigationFlowObstacleProjection blocker = blockers[index];
                WorldPositionCm position = positions[index];
                blockerCount++;
                long hash = FnvOffsetBasis;
                hash = Mix(hash, entity.Id);
                hash = Mix(hash, entity.Version);
                hash = Mix(hash, blocker.PieceCount);
                hash = Mix(hash, blocker.ShapeSignature);
                hash = Mix(hash, blocker.PoseSignature);
                hash = Mix(hash, position.Value.X.RawValue);
                hash = Mix(hash, position.Value.Y.RawValue);
                for (int pieceIndex = 0; pieceIndex < blocker.PieceCount; pieceIndex++)
                {
                    hash = Mix(hash, (int)blocker.GetShape(pieceIndex));
                    hash = Mix(hash, blocker.GetOffsetXCm(pieceIndex));
                    hash = Mix(hash, blocker.GetOffsetYCm(pieceIndex));
                    hash = Mix(hash, blocker.GetRadiusCm(pieceIndex));
                }

                blockerSetHash = unchecked(blockerSetHash + hash);
            }
        }

        foreach (ref var chunk in world.Query(in MarkersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                WorldPositionCm position = positions[index];
                markerCount++;
                long hash = FnvOffsetBasis;
                hash = Mix(hash, entity.Id);
                hash = Mix(hash, entity.Version);
                hash = Mix(hash, position.Value.X.RawValue);
                hash = Mix(hash, position.Value.Y.RawValue);
                markerSetHash = unchecked(markerSetHash + hash);
            }
        }

        long signature = Mix(Mix(FnvOffsetBasis, blockerSetHash), markerSetHash);
        return new MassNavigationEnvironmentSignature(signature, blockerCount, markerCount);
    }

    private void BindBlockers(MassNavigationSimulationRuntime simulation)
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

                simulation.AgentState.RegisterBlocker(entity);
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(_engine.World);
        }

        simulation.RebuildRuntimeObstacles(CollectionsMarshal.AsSpan(_blockerObstacles));
    }

    private void BindMarkers(MassNavigationSimulationRuntime simulation)
    {
        foreach (ref var chunk in _engine.World.Query(in MarkersQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                simulation.AgentState.RegisterWorldMarker(Unsafe.Add(ref entityFirst, index));
            }
        }
    }

    private static long Mix(long hash, long value)
    {
        unchecked
        {
            hash ^= value;
            hash *= FnvPrime;
            return hash;
        }
    }

    internal readonly record struct MassNavigationEnvironmentSignature(long Hash, int BlockerCount, int MarkerCount);
}
