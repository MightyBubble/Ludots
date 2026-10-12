using System;
using System.Reflection;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一次单位生成的实例输入。仿真参数在单位模板上，这里只带出生位置和体型半径（呈现环）。</summary>
public readonly record struct CrowdUnitSpawnRequest(
    Fix64 XCm,
    Fix64 YCm,
    int UnitType,
    int RadiusClass,
    int GroupId,
    int PlayerId,
    Fix64 RadiusCm,
    int ProfileIndex,
    int AgentTypeIndex);

/// <summary>
/// CrowdSimulation 单位存储(ECS 实体,不另造 SoA):每个单位是一个 Arch 实体,
/// 带 CrowdSimulationAgent、CrowdSimulationUnitState、WorldPositionCm、PlayerOwner。
/// 管理层只持稠密序、槽位代数组与空闲槽池。
/// 单位经 RuntimeEntitySpawnRequest 进入 RuntimeEntitySpawnSystem；本类只登记逐实例事实，
/// 并在指令边界调用同一次 Update。批内写入由挂在该系统实例上的初始化器完成。
/// </summary>
public sealed class CrowdSimUnits
{
    public const int SlotBits = 18, SlotMask = (1 << SlotBits) - 1, GenMask = (1 << 14) - 1;
    public const string ReceiptChannelName = "crowd_simulation.units";
    private const int MaxTemplateKeysPerDeploy = 64;

    private readonly ArchWorld _world;
    private readonly CrowdSimPresentationWiring _presentation;
    private readonly RuntimeEntitySpawnSystem _spawns;
    private readonly RuntimeEntitySpawnQueue _spawnQueue;
    private readonly RuntimeEntitySpawnReceiptQueue _receipts;
    private readonly int _receiptChannelId;
    private readonly ushort _initializerKind;
    private readonly List<Entity> _dense = new();
    private readonly List<int> _slotOfDense = new();
    private readonly int[] _sparse;
    private readonly ushort[] _gen;
    private readonly int[] _freeSlots;
    private int _freeTop;
    private readonly PendingUnit[] _pending;
    private int _pendingCount;
    private readonly RuntimeEntitySpawnRequest[] _requestScratch;
    private readonly RuntimeEntitySpawnReceipt[] _receiptScratch;
    private readonly CrowdUnitSpawnInitializer _initializer;

    static CrowdSimUnits()
    {
        TemplateBatchComponentAdmission.Register<CrowdSimulationAgent>("CrowdSimulationAgent");
    }

    public CrowdSimUnits(ArchWorld world, int capacity, CrowdSimPresentationWiring presentation)
    {
        if (capacity > SlotMask + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "单位容量超过句柄槽位上限。");
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Capacity = capacity;
        _sparse = new int[capacity];
        _gen = new ushort[capacity];
        _freeSlots = new int[capacity];
        _pending = new PendingUnit[capacity];
        int queueCapacity = Math.Max(capacity, 16);
        _requestScratch = new RuntimeEntitySpawnRequest[capacity];
        _receiptScratch = new RuntimeEntitySpawnReceipt[capacity];
        if (presentation.Spawns != null)
        {
            _spawns = presentation.Spawns;
            _spawnQueue = presentation.SpawnQueue ?? throw new InvalidOperationException(
                "CrowdSimulation 引擎接线给了 RuntimeEntitySpawnSystem，但没有 RuntimeEntitySpawnQueue。");
            _receipts = presentation.SpawnReceipts ?? throw new InvalidOperationException(
                "CrowdSimulation 引擎接线给了 RuntimeEntitySpawnSystem，但没有 RuntimeEntitySpawnReceiptQueue。");
            _receiptChannelId = presentation.SpawnReceiptChannelId;
            if (_receiptChannelId <= 0)
            {
                throw new InvalidOperationException("CrowdSimulation 引擎接线的生成回执通道 id 无效。");
            }
        }
        else
        {
            if (presentation.TemplateKeys == null)
            {
                throw new InvalidOperationException("CrowdSimulation 无头接线需要 EntityTemplateKeyRegistry。");
            }

            _spawnQueue = new RuntimeEntitySpawnQueue(queueCapacity);
            _receipts = new RuntimeEntitySpawnReceiptQueue(queueCapacity);
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            _receiptChannelId = channels.Register(ReceiptChannelName);
            _spawns = new RuntimeEntitySpawnSystem(
                world,
                _spawnQueue,
                presentation.TemplateRegistry,
                presentation.TemplateKeys,
                presentation.StableIds,
                receipts: _receipts);
        }

        _initializer = new CrowdUnitSpawnInitializer(this);
        _initializerKind = _spawns.RegisterInstanceInitializer(_initializer);
        Clear();
    }

    public int Capacity { get; }
    public int Count => _dense.Count;
    public Entity EntityAt(int dense) => _dense[dense];
    public int SlotAt(int dense) => _slotOfDense[dense];
    public ushort GenOf(int slot) => _gen[slot];
    public uint HandleAt(int dense) => (uint)(((_gen[_slotOfDense[dense]] & GenMask) << SlotBits) | _slotOfDense[dense]);
    public int FreeTop => _freeTop;
    public int LargestSpawnBatch => _spawns.LargestTemplateBatch;

    /// <summary>稠密下标 → 补全后的个人(避让/碰撞)半径。</summary>
    public Fix64 PersonalRadiusCmAt(int dense) => _world.Get<CrowdSimulationAgent>(_dense[dense]).PersonalRadiusCm;

    public int DenseOfSlot(int slot) => _sparse[slot];

    /// <summary>
    /// 占一个槽并记下逐实例事实。实体要等 <see cref="MaterializePending"/> 才绑定。
    /// 满容量返回 -1，不改槽池。
    /// </summary>
    public int Add(in CrowdUnitSpawnRequest unit)
    {
        if (_dense.Count >= Capacity) return -1;
        int slot = _freeSlots[--_freeTop];
        int dense = _dense.Count;
        _sparse[slot] = dense;
        (string templateId, int templateKeyId) = _presentation.TemplatesByUnitTypeRadius[unit.UnitType][unit.RadiusClass];
        _pending[dense] = new PendingUnit
        {
            Slot = slot,
            GroupId = unit.GroupId,
            PlayerId = unit.PlayerId,
            ProfileIndex = unit.ProfileIndex,
            AgentTypeIndex = unit.AgentTypeIndex,
            RadiusCm = unit.RadiusCm,
            TemplateKeyId = templateKeyId,
            TemplateId = templateId,
            XCm = unit.XCm,
            YCm = unit.YCm,
        };
        _dense.Add(Entity.Null);
        _slotOfDense.Add(slot);
        _pendingCount++;
        return dense;
    }

    /// <summary>
    /// 把尚未绑定的单位按模板分组送进生成队列，并立刻调用本世界的 RuntimeEntitySpawnSystem.Update。
    /// 没有待生成单位时直接返回。队列里已有别的请求，或空间不够整批，都抛错，不截断。
    /// </summary>
    public void MaterializePending()
    {
        if (_pendingCount == 0)
        {
            return;
        }

        int pending = _pendingCount;
        int first = _dense.Count - pending;
        if (first < 0)
        {
            throw new InvalidOperationException("SPAWN.RUNTIME.ERR.CrowdPendingRange");
        }

        Span<int> uniqueKeys = stackalloc int[MaxTemplateKeysPerDeploy];
        int uniqueCount = 0;
        for (int dense = first; dense < first + pending; dense++)
        {
            int key = _pending[dense].TemplateKeyId;
            bool seen = false;
            for (int u = 0; u < uniqueCount; u++)
            {
                if (uniqueKeys[u] == key)
                {
                    seen = true;
                    break;
                }
            }

            if (seen)
            {
                continue;
            }

            if (uniqueCount == uniqueKeys.Length)
            {
                throw new InvalidOperationException(
                    $"SPAWN.RUNTIME.ERR.CrowdTemplateKeyCap: more than {uniqueKeys.Length} unit templates in one deploy.");
            }

            uniqueKeys[uniqueCount++] = key;
        }

        if (_spawnQueue.Count != 0)
        {
            throw new InvalidOperationException(
                $"SPAWN.RUNTIME.ERR.QueueBusy: crowd materialize requires an empty spawn queue, count={_spawnQueue.Count}.");
        }

        for (int u = 0; u < uniqueCount; u++)
        {
            int key = uniqueKeys[u];
            int written = 0;
            for (int dense = first; dense < first + pending; dense++)
            {
                ref readonly PendingUnit item = ref _pending[dense];
                if (item.TemplateKeyId != key)
                {
                    continue;
                }

                _requestScratch[written++] = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = item.TemplateId,
                    HasWorldPosition = 1,
                    WorldPositionCm = new Fix64Vec2(item.XCm, item.YCm),
                    HasFacing = 1,
                    FacingAngleRad = 0f,
                    PlayerOwnerIdOverride = item.PlayerId,
                    EmitReceipt = 1,
                    ReceiptChannelId = _receiptChannelId,
                    ReceiptId = dense,
                    InstanceInitializerKind = _initializerKind,
                    InstanceInitializerIndex = dense,
                };
            }

            _spawnQueue.EnqueueAllOrThrow(_requestScratch.AsSpan(0, written));
        }

        _spawns.Update(0f);
        if (_spawnQueue.Count != 0)
        {
            throw new InvalidOperationException(
                $"SPAWN.RUNTIME.ERR.CrowdMaterializeIncomplete: remaining={_spawnQueue.Count}.");
        }

        int got = _receipts.DetachChannel(_receiptChannelId, _receiptScratch.AsSpan(0, pending));
        if (got != pending)
        {
            throw new InvalidOperationException(
                $"SPAWN.RUNTIME.ERR.ReceiptCount: expected {pending}, got {got}.");
        }

        for (int i = 0; i < got; i++)
        {
            RuntimeEntitySpawnReceipt receipt = _receiptScratch[i];
            int dense = receipt.ReceiptId;
            if ((uint)dense >= (uint)_dense.Count || _dense[dense] != Entity.Null)
            {
                throw new InvalidOperationException(
                    $"SPAWN.RUNTIME.ERR.ReceiptBind: receiptId={dense}, denseCount={_dense.Count}.");
            }

            if (!_world.IsAlive(receipt.Entity))
            {
                throw new InvalidOperationException(
                    $"SPAWN.RUNTIME.ERR.ReceiptEntity: receiptId={dense} is not alive.");
            }

            _dense[dense] = receipt.Entity;
        }

        for (int dense = first; dense < first + pending; dense++)
        {
            if (_dense[dense] == Entity.Null)
            {
                throw new InvalidOperationException($"SPAWN.RUNTIME.ERR.ReceiptMissing: dense={dense}.");
            }
        }

        _pendingCount = 0;
        OrderChunkRowsByDenseIndex();
    }

    /// <summary>
    /// 批车道按模板把同一签名的实体追加进同一个原型，chunk 序就不再是稠密下标。
    /// Gather/Scatter 仍按稠密下标写 SoA；序一乱，每行都打到不同缓存行，50k 的 tick 会多出数毫秒。
    /// 只交换这些实体已经占着的行，不新建实体，稠密表和实体 id 都不变。
    /// 原型里还有别的实体时不动：那会把别人的行卷进来。查询按下标回读，结果仍然对，只是少了这次顺序。
    /// </summary>
    private void OrderChunkRowsByDenseIndex()
    {
        int n = _dense.Count;
        if (n <= 1)
        {
            return;
        }

        Entity first = _dense[0];
        Archetype archetype = _world.GetArchetype(first);
        if (archetype.EntityCount != n)
        {
            return;
        }

        var packed = new Entity[n];
        int cursor = 0;
        int chunkCount = archetype.ChunkCount;
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            ref Chunk chunk = ref archetype.GetChunk(chunkIndex);
            Entity[] entities = chunk.Entities;
            int count = chunk.Count;
            for (int row = 0; row < count; row++)
            {
                packed[cursor++] = entities[row];
            }
        }

        if (cursor != n)
        {
            throw new InvalidOperationException(
                $"SPAWN.RUNTIME.ERR.CrowdChunkOrder: archetype has {cursor} rows, dense count is {n}.");
        }

        int maxId = 0;
        for (int i = 0; i < n; i++)
        {
            int id = packed[i].Id;
            if (id > maxId)
            {
                maxId = id;
            }
        }

        var at = new int[maxId + 1];
        Array.Fill(at, -1);
        for (int i = 0; i < n; i++)
        {
            at[packed[i].Id] = i;
        }

        ComponentType[] types = archetype.Signature.Components.ToArray();
        if (types.Length != archetype.GetChunk(0).Components.Length)
        {
            throw new InvalidOperationException("SPAWN.RUNTIME.ERR.CrowdChunkOrder: signature does not match chunk columns.");
        }

        var swaps = new ComponentRowSwap[types.Length];
        for (int c = 0; c < types.Length; c++)
        {
            swaps[c] = SwapFor(types[c].Type);
        }

        // 查询从最后一块的最后一行往回走，所以正向槽位 i 上要放稠密下标 n-1-i。
        for (int i = 0; i < n; i++)
        {
            int desired = n - 1 - i;
            int from = at[_dense[desired].Id];
            if ((uint)from >= (uint)n)
            {
                throw new InvalidOperationException(
                    $"SPAWN.RUNTIME.ERR.CrowdChunkOrder: dense {desired} is not in the unit archetype.");
            }

            if (from == i)
            {
                continue;
            }

            SwapChunkRows(archetype, swaps, packed[i], packed[from]);
            int displacedId = packed[i].Id;
            at[displacedId] = from;
            at[_dense[desired].Id] = i;
            (packed[i], packed[from]) = (packed[from], packed[i]);
        }
    }

    private delegate void ComponentRowSwap(Array left, int leftIndex, Array right, int rightIndex);

    private static readonly Dictionary<Type, ComponentRowSwap> RowSwaps = new();

    private static ComponentRowSwap SwapFor(Type elementType)
    {
        lock (RowSwaps)
        {
            if (RowSwaps.TryGetValue(elementType, out ComponentRowSwap? swap))
            {
                return swap;
            }

            MethodInfo method = typeof(CrowdSimUnits).GetMethod(nameof(SwapComponentRow), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(elementType);
            swap = (ComponentRowSwap)Delegate.CreateDelegate(typeof(ComponentRowSwap), method);
            RowSwaps.Add(elementType, swap);
            return swap;
        }
    }

    private static void SwapComponentRow<T>(Array left, int leftIndex, Array right, int rightIndex)
    {
        T[] a = (T[])left;
        T[] b = (T[])right;
        (a[leftIndex], b[rightIndex]) = (b[rightIndex], a[leftIndex]);
    }

    private void SwapChunkRows(Archetype archetype, ComponentRowSwap[] swaps, Entity a, Entity b)
    {
        Slot slotA = _world.GetSlot(a);
        Slot slotB = _world.GetSlot(b);
        ref Chunk chunkA = ref archetype.GetChunk(slotA.ChunkIndex);
        ref Chunk chunkB = ref archetype.GetChunk(slotB.ChunkIndex);
        (chunkA.Entities[slotA.Index], chunkB.Entities[slotB.Index]) =
            (chunkB.Entities[slotB.Index], chunkA.Entities[slotA.Index]);
        Array[] columnsA = chunkA.Components;
        Array[] columnsB = chunkB.Components;
        for (int c = 0; c < swaps.Length; c++)
        {
            swaps[c](columnsA[c], slotA.Index, columnsB[c], slotB.Index);
        }

        _world.SetSlot(a, slotB);
        _world.SetSlot(b, slotA);
    }

    /// <summary>从引擎的生成系统上摘掉本会话的初始化器。无头自建的系统随会话一起丢。</summary>
    public void ReleaseSpawnInitializer()
    {
        if (_initializerKind != 0)
        {
            _spawns.UnregisterInstanceInitializer(_initializerKind);
        }
    }

    /// <summary>
    /// 清空稠密表。已绑定且仍存活的单位走 ConsumeEntity，当帧从仿真里消失，
    /// 实体留到呈现销毁发布之后才 Destroy。Entity.Null 占位不销毁。
    /// 代 bump 与槽池 LIFO 序保持回放可复现。
    /// </summary>
    public void Clear()
    {
        if (_presentation.SelectionOwner != Entity.Null)
        {
            _presentation.Collections.Replace(
                _presentation.SelectionOwner,
                _presentation.SelectedCollectionKeyId,
                SelectionMirror.Descriptor,
                ReadOnlySpan<Entity>.Empty);
        }

        for (int i = 0; i < _dense.Count; i++)
        {
            Entity entity = _dense[i];
            if (entity != Entity.Null && _world.IsAlive(entity))
            {
                EntityLifecycleAtomicOps.ConsumeEntity(_world, entity, "CrowdSimulation.Clear");
            }

            _gen[_slotOfDense[i]] = (ushort)((_gen[_slotOfDense[i]] + 1) & GenMask);
        }

        _dense.Clear();
        _slotOfDense.Clear();
        _pendingCount = 0;
        Array.Fill(_sparse, -1);
        for (int k = 0; k < Capacity; k++) _freeSlots[k] = Capacity - 1 - k;
        _freeTop = Capacity;
    }

    /// <summary>逐单位读位置与身份的视图(校验 / 遍历用,不复制)。</summary>
    public readonly struct UnitView
    {
        public required Entity Entity { get; init; }
        public required int Dense { get; init; }
        public required uint Handle { get; init; }
    }

    private struct PendingUnit
    {
        public int Slot;
        public int GroupId;
        public int PlayerId;
        public int ProfileIndex;
        public int AgentTypeIndex;
        public Fix64 RadiusCm;
        public int TemplateKeyId;
        public string TemplateId;
        public Fix64 XCm;
        public Fix64 YCm;
    }

    private sealed class CrowdUnitSpawnInitializer : IRuntimeSpawnInstanceInitializer
    {
        private readonly CrowdSimUnits _units;

        public CrowdUnitSpawnInitializer(CrowdSimUnits units)
        {
            _units = units;
        }

        public Signature AppendSignature(Signature signature)
        {
            return signature
                + Component<CrowdSimulationUnitState>.Signature
                + Component<CrowdSimulationKinematics>.Signature
                + Component<PlayerOwner>.Signature
                + Component<ContinuousHeightmapSampleState>.Signature
                + Component<BlackboardFloatBuffer>.Signature;
        }

        public void WriteChunk(ArchWorld world, ReadOnlySpan<Entity> created, ReadOnlySpan<int> instanceIndices)
        {
            if (created.Length == 0)
            {
                return;
            }

            Entity first = created[0];
            Archetype archetype = world.GetEntityDataArray()[first.Id].Archetype;
            Slot slot = world.GetSlot(first);
            int keyId = _units._presentation.RadiusMetersBlackboardKeyId;
            int batchIndex = 0;
            int chunkIndex = slot.ChunkIndex;
            int row = slot.Index;
            while (batchIndex < created.Length)
            {
                ref Chunk chunk = ref archetype.GetChunk(chunkIndex);
                int run = Math.Min(created.Length - batchIndex, chunk.Count - row);
                Span<CrowdSimulationAgent> agents = chunk.GetSpan<CrowdSimulationAgent>();
                Span<CrowdSimulationUnitState> states = chunk.GetSpan<CrowdSimulationUnitState>();
                Span<CrowdSimulationKinematics> kinematics = chunk.GetSpan<CrowdSimulationKinematics>();
                Span<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
                Span<ContinuousHeightmapSampleState> heights = chunk.GetSpan<ContinuousHeightmapSampleState>();
                Span<BlackboardFloatBuffer> boards = chunk.GetSpan<BlackboardFloatBuffer>();
                for (int offset = 0; offset < run; offset++)
                {
                    int componentIndex = row + offset;
                    int dense = instanceIndices[batchIndex + offset];
                    ref readonly PendingUnit pending = ref _units._pending[dense];
                    agents[componentIndex].ProfileIndex = pending.ProfileIndex;
                    agents[componentIndex].AgentTypeIndex = pending.AgentTypeIndex;
                    states[componentIndex] = new CrowdSimulationUnitState
                    {
                        Slot = pending.Slot,
                        GroupId = pending.GroupId,
                    };
                    kinematics[componentIndex] = default;
                    owners[componentIndex] = new PlayerOwner { PlayerId = pending.PlayerId };
                    heights[componentIndex] = default;
                    var board = new BlackboardFloatBuffer();
                    board.Set(keyId, WorldUnits.CmToM((float)pending.RadiusCm.ToDouble()));
                    boards[componentIndex] = board;
                }

                batchIndex += run;
                chunkIndex++;
                row = 0;
            }
        }

        public void ApplyEntity(ArchWorld world, Entity entity, int instanceIndex)
        {
            ref readonly PendingUnit pending = ref _units._pending[instanceIndex];
            if (!world.Has<CrowdSimulationAgent>(entity))
            {
                throw new InvalidOperationException(
                    $"单位模板 \"{pending.TemplateId}\" 没有 CrowdSimulationAgent。");
            }

            ref CrowdSimulationAgent agent = ref world.Get<CrowdSimulationAgent>(entity);
            agent.ProfileIndex = pending.ProfileIndex;
            agent.AgentTypeIndex = pending.AgentTypeIndex;
            SetOrAdd(world, entity, new CrowdSimulationUnitState
            {
                Slot = pending.Slot,
                GroupId = pending.GroupId,
            });
            SetOrAdd(world, entity, new CrowdSimulationKinematics());
            SetOrAdd(world, entity, new PlayerOwner { PlayerId = pending.PlayerId });
            SetOrAdd(world, entity, new ContinuousHeightmapSampleState());
            var board = new BlackboardFloatBuffer();
            board.Set(_units._presentation.RadiusMetersBlackboardKeyId, WorldUnits.CmToM((float)pending.RadiusCm.ToDouble()));
            SetOrAdd(world, entity, board);
        }

        private static void SetOrAdd<T>(ArchWorld world, Entity entity, T component) where T : struct
        {
            if (world.Has<T>(entity)) world.Set(entity, component);
            else world.Add(entity, component);
        }
    }
}
