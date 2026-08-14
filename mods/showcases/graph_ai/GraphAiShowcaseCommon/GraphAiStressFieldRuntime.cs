using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace GraphAiShowcaseCommon;

public sealed class GraphAiStressFieldRuntime
{
    public static readonly QueryDescription StressBrainQuery = new QueryDescription()
        .WithAll<GraphAiStressBrain, GraphAiStressIntent>();

    private Entity[] _entities = Array.Empty<Entity>();
    private GraphInstruction[] _fsmProgram = Array.Empty<GraphInstruction>();
    private GraphInstruction[] _btProgram = Array.Empty<GraphInstruction>();
    private int[] _fsmIntRegisters = Array.Empty<int>();
    private byte[] _fsmBoolRegisters = Array.Empty<byte>();
    private int[] _btIntRegisters = Array.Empty<int>();
    private byte[] _btBoolRegisters = Array.Empty<byte>();
    private ushort[] _btTaskRemaining = Array.Empty<ushort>();
    private PrimitiveDrawItem[] _primitiveItems = Array.Empty<PrimitiveDrawItem>();
    private int[] _primitiveRemovedStableIds = Array.Empty<int>();
    private int _entityCount;
    private int _primitiveStableIdBase;
    private int _columns;
    private int _baseXCm;
    private int _baseYCm;
    private int _spacingCm;
    private int _waveAmplitudeCm;
    private float _waveFrequency;
    private float _primitiveScaleMeters;
    private Vector4 _holdColor;
    private Vector4 _returnColor;
    private Vector4 _defendColor;
    private Vector4 _attackColor;
    private int _sphereMeshId;

    public GraphAiStressFieldSnapshot Snapshot { get; private set; } = GraphAiStressFieldSnapshot.Empty;

    public void Reset(
        GameEngine engine,
        GraphAiShowcaseConfig config,
        GraphInstruction[] fsmProgram,
        GraphInstruction[] btProgram)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (fsmProgram == null || fsmProgram.Length == 0)
        {
            throw new ArgumentException("Stress field requires a non-empty FSM graph program.", nameof(fsmProgram));
        }

        if (btProgram == null || btProgram.Length == 0)
        {
            throw new ArgumentException("Stress field requires a non-empty BT graph program.", nameof(btProgram));
        }

        Clear(engine);

        GraphAiStressFieldConfig stress = config.StressField;
        _entityCount = stress.EntityCount;
        _primitiveStableIdBase = stress.PrimitiveStableIdBase;
        _columns = stress.Columns;
        _baseXCm = stress.BaseXCm;
        _baseYCm = stress.BaseYCm;
        _spacingCm = stress.SpacingCm;
        _waveAmplitudeCm = stress.WaveAmplitudeCm;
        _waveFrequency = stress.WaveFrequency;
        _primitiveScaleMeters = stress.PrimitiveScaleMeters;
        _fsmProgram = fsmProgram;
        _btProgram = btProgram;
        BindStateColors(stress);

        _entities = new Entity[_entityCount];
        _fsmIntRegisters = new int[checked(_entityCount * GraphAiVmLimits.IntRegisters)];
        _fsmBoolRegisters = new byte[checked(_entityCount * GraphAiVmLimits.BoolRegisters)];
        _btIntRegisters = new int[checked(_entityCount * GraphAiVmLimits.IntRegisters)];
        _btBoolRegisters = new byte[checked(_entityCount * GraphAiVmLimits.BoolRegisters)];
        _btTaskRemaining = new ushort[_entityCount];
        _primitiveItems = new PrimitiveDrawItem[_entityCount];
        _primitiveRemovedStableIds = new int[_entityCount];
        for (int i = 0; i < _entityCount; i++)
        {
            _primitiveRemovedStableIds[i] = _primitiveStableIdBase + i + 1;
        }

        SeedRegisters();

        World world = engine.World ?? throw new InvalidOperationException("Graph stress field requires an active ECS world.");
        for (int i = 0; i < _entityCount; i++)
        {
            _entities[i] = world.Create(
                new GraphAiStressBrain { Index = i },
                new GraphAiStressIntent());
        }

        Tick(world, config.Outputs, 0);
    }

    public void Clear(GameEngine? engine)
    {
        RemoveStressPrimitives(engine);

        if (engine?.World != null && _entities.Length > 0)
        {
            World world = engine.World;
            for (int i = 0; i < _entities.Length; i++)
            {
                Entity entity = _entities[i];
                if (entity != Entity.Null && world.IsAlive(entity))
                {
                    world.Destroy(entity);
                }
            }
        }

        _entities = Array.Empty<Entity>();
        _fsmProgram = Array.Empty<GraphInstruction>();
        _btProgram = Array.Empty<GraphInstruction>();
        _fsmIntRegisters = Array.Empty<int>();
        _fsmBoolRegisters = Array.Empty<byte>();
        _btIntRegisters = Array.Empty<int>();
        _btBoolRegisters = Array.Empty<byte>();
        _btTaskRemaining = Array.Empty<ushort>();
        _primitiveItems = Array.Empty<PrimitiveDrawItem>();
        _primitiveRemovedStableIds = Array.Empty<int>();
        _entityCount = 0;
        _primitiveStableIdBase = 0;
        _sphereMeshId = 0;
        Snapshot = GraphAiStressFieldSnapshot.Empty;
    }

    public void Tick(World world, GraphAiOutputConfig outputs, int tick)
    {
        if (_entityCount == 0)
        {
            return;
        }

        int beforeGen0 = GC.CollectionCount(0);
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();

        var job = new StressTickJob(
            tick,
            _fsmProgram,
            _btProgram,
            _fsmIntRegisters,
            _fsmBoolRegisters,
            _btIntRegisters,
            _btBoolRegisters,
            _btTaskRemaining,
            outputs.StateRegister,
            outputs.IntentRegister,
            outputs.BtNodeRegister,
            outputs.TaskIdRegister,
            outputs.TaskDurationRegister);
        world.InlineEntityQuery<StressTickJob, GraphAiStressBrain, GraphAiStressIntent>(in StressBrainQuery, ref job);

        long stop = Stopwatch.GetTimestamp();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        int gen0Collections = GC.CollectionCount(0) - beforeGen0;
        if (job.EntityCount != _entityCount)
        {
            throw new InvalidOperationException(
                $"Graph stress field expected {_entityCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} ECS brains but queried {job.EntityCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        long elapsedMicros = (stop - start) * 1_000_000L / Stopwatch.Frequency;
        Snapshot = Snapshot with
        {
            EcsEntityCount = job.EntityCount,
            FsmGraphExecutionsLastTick = job.FsmGraphExecutions,
            BtGraphExecutionsLastTick = job.BtGraphExecutions,
            FsmGraphExecutionsTotal = Snapshot.FsmGraphExecutionsTotal + job.FsmGraphExecutions,
            BtGraphExecutionsTotal = Snapshot.BtGraphExecutionsTotal + job.BtGraphExecutions,
            LastElapsedMicroseconds = elapsedMicros,
            LastAllocatedBytes = allocatedBytes,
            LastGen0Collections = gen0Collections,
            CompletedTasks = Snapshot.CompletedTasks + job.CompletedTasks,
            HoldFireCount = job.HoldFireCount,
            ReturnFireCount = job.ReturnFireCount,
            DefendCount = job.DefendCount,
            AttackAnythingCount = job.AttackAnythingCount,
            FsmBranchMask = Snapshot.FsmBranchMask | job.FsmBranchMask,
            BtTaskMask = Snapshot.BtTaskMask | job.BtTaskMask,
            IntentChecksum = unchecked(Snapshot.IntentChecksum + job.IntentChecksum)
        };
    }

    public void RenderPrimitives(GameEngine engine, float elapsedSeconds)
    {
        if (_entityCount == 0)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer primitives)
        {
            throw new InvalidOperationException("Graph stress field requires PresentationPrimitiveDrawBuffer.");
        }

        if (engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer) is not PrimitiveDrawBuffer snapshotPrimitives)
        {
            throw new InvalidOperationException("Graph stress field requires PresentationVisualSnapshotBuffer.");
        }

        if (engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) is not MeshAssetRegistry meshes)
        {
            throw new InvalidOperationException("Graph stress field requires PresentationMeshAssetRegistry.");
        }

        if (primitives.Capacity < _entityCount || snapshotPrimitives.Capacity < _entityCount)
        {
            throw new InvalidOperationException(
                $"Graph stress field requires primitive buffers for {_entityCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} visible brains, but draw={primitives.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture)} snapshot={snapshotPrimitives.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        if (_sphereMeshId == 0)
        {
            _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
            if (_sphereMeshId == 0)
            {
                throw new InvalidOperationException("Graph stress field could not resolve the built-in sphere mesh.");
            }
        }

        var job = new StressRenderJob(
            _primitiveItems,
            _sphereMeshId,
            _primitiveStableIdBase,
            _columns,
            _baseXCm,
            _baseYCm,
            _spacingCm,
            _waveAmplitudeCm,
            _waveFrequency,
            _primitiveScaleMeters,
            elapsedSeconds,
            _holdColor,
            _returnColor,
            _defendColor,
            _attackColor);
        engine.World.InlineEntityQuery<StressRenderJob, GraphAiStressBrain, GraphAiStressIntent>(in StressBrainQuery, ref job);

        if (job.VisiblePrimitiveCount != _entityCount)
        {
            throw new InvalidOperationException(
                $"Graph stress field rendered {job.VisiblePrimitiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} primitive payloads for {_entityCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} ECS brains.");
        }

        ReadOnlySpan<PrimitiveDrawItem> changedItems = new(_primitiveItems, 0, job.VisiblePrimitiveCount);
        primitives.ApplyStaticMeshDelta(changedItems, ReadOnlySpan<int>.Empty, visibleOnly: true);
        snapshotPrimitives.ApplyStaticMeshDelta(changedItems, ReadOnlySpan<int>.Empty, visibleOnly: false);
        PublishStaticStressProjection(snapshotPrimitives, changedItems, ReadOnlySpan<int>.Empty);

        int visibleStressPrimitives = CountVisibleStressPrimitives(primitives);
        if (visibleStressPrimitives != _entityCount || primitives.DroppedSinceClear != 0)
        {
            throw new InvalidOperationException(
                $"Graph stress field must show {_entityCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} primitive dots without drops, but visible={visibleStressPrimitives.ToString(System.Globalization.CultureInfo.InvariantCulture)} dropped={primitives.DroppedSinceClear.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        Snapshot = Snapshot with
        {
            VisiblePrimitiveCount = visibleStressPrimitives,
            PrimitiveCapacity = primitives.Capacity,
            PrimitiveDroppedSinceClear = primitives.DroppedSinceClear
        };
    }

    private void SeedRegisters()
    {
        for (int index = 0; index < _entityCount; index++)
        {
            int fsmBase = index * GraphAiVmLimits.IntRegisters;
            int btBase = index * GraphAiVmLimits.IntRegisters;
            GraphAiHotPathProbe.SeedSenses(_fsmIntRegisters, fsmBase, index);
            GraphAiHotPathProbe.SeedSenses(_btIntRegisters, btBase, index);
            _fsmIntRegisters[fsmBase + 1] = index & 3;
            _btIntRegisters[btBase + 6] = index & 1;
        }
    }

    private void BindStateColors(GraphAiStressFieldConfig stress)
    {
        _holdColor = ResolveColor(stress, 0);
        _returnColor = ResolveColor(stress, 1);
        _defendColor = ResolveColor(stress, 2);
        _attackColor = ResolveColor(stress, 3);
    }

    private static Vector4 ResolveColor(GraphAiStressFieldConfig stress, int state)
    {
        for (int i = 0; i < stress.StateColors.Count; i++)
        {
            GraphAiStressStateColorConfig color = stress.StateColors[i];
            if (color.State == state)
            {
                return new Vector4(color.R, color.G, color.B, color.A);
            }
        }

        throw new InvalidOperationException(
            $"Graph stress field requires a state color for state {state.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
    }

    private void RemoveStressPrimitives(GameEngine? engine)
    {
        if (engine == null || _entityCount == 0)
        {
            return;
        }

        if (_primitiveRemovedStableIds.Length < _entityCount)
        {
            _primitiveRemovedStableIds = new int[_entityCount];
            for (int i = 0; i < _entityCount; i++)
            {
                _primitiveRemovedStableIds[i] = _primitiveStableIdBase + i + 1;
            }
        }

        ReadOnlySpan<int> removedStableIds = new(_primitiveRemovedStableIds, 0, _entityCount);
        if (engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is PrimitiveDrawBuffer primitives)
        {
            primitives.ApplyStaticMeshDelta(ReadOnlySpan<PrimitiveDrawItem>.Empty, removedStableIds, visibleOnly: true);
        }

        if (engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer) is PrimitiveDrawBuffer snapshotPrimitives)
        {
            snapshotPrimitives.ApplyStaticMeshDelta(ReadOnlySpan<PrimitiveDrawItem>.Empty, removedStableIds, visibleOnly: false);
            PublishStaticStressProjection(snapshotPrimitives, ReadOnlySpan<PrimitiveDrawItem>.Empty, removedStableIds);
        }
    }

    private void PublishStaticStressProjection(
        PrimitiveDrawBuffer snapshotPrimitives,
        ReadOnlySpan<PrimitiveDrawItem> changedItems,
        ReadOnlySpan<int> removedStableIds)
    {
        int previousRevision = snapshotPrimitives.Revision;
        int nextRevision = previousRevision == int.MaxValue ? 1 : previousRevision + 1;
        int nextGeometryRevision = snapshotPrimitives.StaticMeshGeometryRevision == int.MaxValue
            ? 1
            : snapshotPrimitives.StaticMeshGeometryRevision + 1;
        snapshotPrimitives.SetRevision(nextRevision);
        snapshotPrimitives.SetStaticMeshGeometryRevision(nextGeometryRevision);
        snapshotPrimitives.SetStaticMeshDeltas(previousRevision, changedItems, removedStableIds);
    }

    private int CountVisibleStressPrimitives(PrimitiveDrawBuffer primitives)
    {
        int minStableId = _primitiveStableIdBase + 1;
        int maxStableId = _primitiveStableIdBase + _entityCount;
        int count = 0;
        ReadOnlySpan<PrimitiveDrawItem> span = primitives.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly PrimitiveDrawItem item = ref span[i];
            if (item.StableId >= minStableId &&
                item.StableId <= maxStableId &&
                item.Visibility == VisualVisibility.Visible)
            {
                count++;
            }
        }

        return count;
    }

    private struct StressTickJob : IForEachWithEntity<GraphAiStressBrain, GraphAiStressIntent>
    {
        private readonly int _tick;
        private readonly GraphInstruction[] _fsmProgram;
        private readonly GraphInstruction[] _btProgram;
        private readonly int[] _fsmIntRegisters;
        private readonly byte[] _fsmBoolRegisters;
        private readonly int[] _btIntRegisters;
        private readonly byte[] _btBoolRegisters;
        private readonly ushort[] _btTaskRemaining;
        private readonly int _stateRegister;
        private readonly int _intentRegister;
        private readonly int _btNodeRegister;
        private readonly int _taskIdRegister;
        private readonly int _taskDurationRegister;

        public StressTickJob(
            int tick,
            GraphInstruction[] fsmProgram,
            GraphInstruction[] btProgram,
            int[] fsmIntRegisters,
            byte[] fsmBoolRegisters,
            int[] btIntRegisters,
            byte[] btBoolRegisters,
            ushort[] btTaskRemaining,
            int stateRegister,
            int intentRegister,
            int btNodeRegister,
            int taskIdRegister,
            int taskDurationRegister)
        {
            _tick = tick;
            _fsmProgram = fsmProgram;
            _btProgram = btProgram;
            _fsmIntRegisters = fsmIntRegisters;
            _fsmBoolRegisters = fsmBoolRegisters;
            _btIntRegisters = btIntRegisters;
            _btBoolRegisters = btBoolRegisters;
            _btTaskRemaining = btTaskRemaining;
            _stateRegister = stateRegister;
            _intentRegister = intentRegister;
            _btNodeRegister = btNodeRegister;
            _taskIdRegister = taskIdRegister;
            _taskDurationRegister = taskDurationRegister;
            EntityCount = 0;
            FsmGraphExecutions = 0;
            BtGraphExecutions = 0;
            CompletedTasks = 0;
            HoldFireCount = 0;
            ReturnFireCount = 0;
            DefendCount = 0;
            AttackAnythingCount = 0;
            FsmBranchMask = 0;
            BtTaskMask = 0;
            IntentChecksum = 0;
        }

        public int EntityCount { get; private set; }
        public int FsmGraphExecutions { get; private set; }
        public int BtGraphExecutions { get; private set; }
        public int CompletedTasks { get; private set; }
        public int HoldFireCount { get; private set; }
        public int ReturnFireCount { get; private set; }
        public int DefendCount { get; private set; }
        public int AttackAnythingCount { get; private set; }
        public int FsmBranchMask { get; private set; }
        public int BtTaskMask { get; private set; }
        public int IntentChecksum { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(Entity entity, ref GraphAiStressBrain brain, ref GraphAiStressIntent intent)
        {
            int index = brain.Index;
            int fsmBase = index * GraphAiVmLimits.IntRegisters;
            int boolBase = index * GraphAiVmLimits.BoolRegisters;
            GraphAiHotPathProbe.SeedSenses(_fsmIntRegisters, fsmBase, index);
            _fsmIntRegisters[fsmBase] = _tick;

            var fsmState = new GraphAiSoaVmState(_fsmIntRegisters, _fsmBoolRegisters, fsmBase, boolBase);
            GraphExecutor.Execute(ref fsmState, _fsmProgram, GraphAiSoaOpHandlerTable.Instance);
            FsmGraphExecutions++;

            int state = _fsmIntRegisters[fsmBase + _stateRegister];
            int fsmIntent = _fsmIntRegisters[fsmBase + _intentRegister];
            _fsmIntRegisters[fsmBase + 1] = state;
            FsmBranchMask |= 1 << state;
            CountState(state);

            int btBase = index * GraphAiVmLimits.IntRegisters;
            GraphAiHotPathProbe.SeedSenses(_btIntRegisters, btBase, index);
            _btIntRegisters[btBase] = _tick;
            _btIntRegisters[btBase + 1] = state;

            ushort remaining = _btTaskRemaining[index];
            if (remaining > 0)
            {
                remaining--;
                _btTaskRemaining[index] = remaining;
                _btIntRegisters[btBase + 7] = remaining;
                if (remaining == 0)
                {
                    CompletedTasks++;
                }
            }
            else
            {
                var btState = new GraphAiSoaVmState(_btIntRegisters, _btBoolRegisters, btBase, boolBase);
                GraphExecutor.Execute(ref btState, _btProgram, GraphAiSoaOpHandlerTable.Instance);
                BtGraphExecutions++;

                int taskId = _btIntRegisters[btBase + _taskIdRegister];
                int duration = _btIntRegisters[btBase + _taskDurationRegister];
                if (taskId <= 0 || duration <= 0 || duration > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Graph stress field BT produced an invalid task.");
                }

                remaining = (ushort)duration;
                _btTaskRemaining[index] = remaining;
                _btIntRegisters[btBase + 6] = _btIntRegisters[btBase + _btNodeRegister];
                _btIntRegisters[btBase + 7] = remaining;
                BtTaskMask |= 1 << taskId;
            }

            intent.State = (byte)state;
            intent.Code = (byte)((fsmIntent + _btIntRegisters[btBase + _intentRegister]) & 0xFF);
            intent.Task = (byte)_btIntRegisters[btBase + _taskIdRegister];
            intent.TaskRemaining = remaining;
            intent.Revision++;
            EntityCount++;
            IntentChecksum = unchecked(IntentChecksum + intent.State + intent.Code + intent.Task + intent.Revision);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CountState(int state)
        {
            switch (state)
            {
                case 0:
                    HoldFireCount++;
                    break;
                case 1:
                    ReturnFireCount++;
                    break;
                case 2:
                    DefendCount++;
                    break;
                case 3:
                    AttackAnythingCount++;
                    break;
            }
        }
    }

    private struct StressRenderJob : IForEachWithEntity<GraphAiStressBrain, GraphAiStressIntent>
    {
        private readonly PrimitiveDrawItem[] _primitiveItems;
        private readonly int _meshId;
        private readonly int _stableIdBase;
        private readonly int _columns;
        private readonly int _baseXCm;
        private readonly int _baseYCm;
        private readonly int _spacingCm;
        private readonly int _waveAmplitudeCm;
        private readonly float _waveFrequency;
        private readonly float _scaleMeters;
        private readonly float _elapsedSeconds;
        private readonly Vector4 _holdColor;
        private readonly Vector4 _returnColor;
        private readonly Vector4 _defendColor;
        private readonly Vector4 _attackColor;

        public StressRenderJob(
            PrimitiveDrawItem[] primitiveItems,
            int meshId,
            int stableIdBase,
            int columns,
            int baseXCm,
            int baseYCm,
            int spacingCm,
            int waveAmplitudeCm,
            float waveFrequency,
            float scaleMeters,
            float elapsedSeconds,
            Vector4 holdColor,
            Vector4 returnColor,
            Vector4 defendColor,
            Vector4 attackColor)
        {
            _primitiveItems = primitiveItems;
            _meshId = meshId;
            _stableIdBase = stableIdBase;
            _columns = columns;
            _baseXCm = baseXCm;
            _baseYCm = baseYCm;
            _spacingCm = spacingCm;
            _waveAmplitudeCm = waveAmplitudeCm;
            _waveFrequency = waveFrequency;
            _scaleMeters = scaleMeters;
            _elapsedSeconds = elapsedSeconds;
            _holdColor = holdColor;
            _returnColor = returnColor;
            _defendColor = defendColor;
            _attackColor = attackColor;
            VisiblePrimitiveCount = 0;
        }

        public int VisiblePrimitiveCount { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(Entity entity, ref GraphAiStressBrain brain, ref GraphAiStressIntent intent)
        {
            int index = brain.Index;
            int row = index / _columns;
            int column = index - (row * _columns);
            float phase = (_elapsedSeconds * _waveFrequency) + (row * 0.071f) + (column * 0.037f);
            float taskPhase = (intent.Task + 1) * 0.41f;
            float xCm = _baseXCm + (column * _spacingCm) + (((row & 1) == 0 ? 0f : _spacingCm * 0.5f));
            float yCm = _baseYCm + (row * _spacingCm);
            xCm += MathF.Sin(phase + taskPhase) * _waveAmplitudeCm;
            yCm += MathF.Cos((phase * 0.7f) + taskPhase) * (_waveAmplitudeCm * 0.55f);
            float height = 0.09f + (intent.State * 0.025f) + ((intent.TaskRemaining & 1) * 0.018f);

            _primitiveItems[index] = new PrimitiveDrawItem
            {
                MeshAssetId = _meshId,
                Position = WorldPlane2D.LogicCmToVisualMeters(xCm, yCm, height),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(_scaleMeters),
                Color = ResolveColor(intent.State, _holdColor, _returnColor, _defendColor, _attackColor),
                StableId = _stableIdBase + index + 1,
                TemplateId = _stableIdBase + index + 1,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
                AssetKind = AssetKind.Mesh,
            };
            VisiblePrimitiveCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector4 ResolveColor(byte state, Vector4 holdColor, Vector4 returnColor, Vector4 defendColor, Vector4 attackColor) =>
            state switch
            {
                1 => returnColor,
                2 => defendColor,
                3 => attackColor,
                _ => holdColor
            };
    }
}

public struct GraphAiStressBrain
{
    public int Index;
}

public struct GraphAiStressIntent
{
    public byte State;
    public byte Code;
    public byte Task;
    public ushort TaskRemaining;
    public int Revision;
}
