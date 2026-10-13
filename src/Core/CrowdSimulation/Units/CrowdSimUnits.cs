using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一次单位生成的实例输入。带单位类型、半径级、组、玩家、档案下标、代理类型下标和出生位置。速度等档案不在这条请求上。</summary>
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
/// 带 CrowdSimulationAgent(速度、半径、推挤优先级和下标)、CrowdSimulationUnitState(仿真身份)、
/// WorldPositionCm、PlayerOwner。管理层只持三件东西:稠密序(创建序)、
/// 槽位代数组与空闲槽池。句柄是 18 位槽位加 14 位代，回放可复现。
/// 单位经 RuntimeEntitySpawnRequest 进入 RuntimeEntitySpawnSystem.Materialize；本类只登记逐实例事实。
/// 批内写入由挂在该系统实例上的初始化器完成。不排空阶段队列。
/// 出生算法(盐流/搜索/散布)留在内核。
/// </summary>
public sealed class CrowdSimUnits
{
    public const int SlotBits = 18, SlotMask = (1 << SlotBits) - 1, GenMask = (1 << 14) - 1;
    public const string ReceiptChannelName = "crowd_simulation.units";
    private const string AgentBatchComponentName = "CrowdSimulationAgent";
    private const int MaxTemplateKeysPerDeploy = 48;

    private readonly ArchWorld _world;
    private readonly CrowdSimPresentationWiring _presentation;
    private readonly RuntimeEntitySpawnSystem _spawns;
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

            PresentationLifecycleConsumption.DeclareNoConsumer(world);
            _receipts = new RuntimeEntitySpawnReceiptQueue(queueCapacity);
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            _receiptChannelId = channels.Register(ReceiptChannelName);
            _spawns = new RuntimeEntitySpawnSystem(
                world,
                new RuntimeEntitySpawnQueue(16),
                presentation.TemplateRegistry,
                presentation.TemplateKeys,
                presentation.StableIds,
                receipts: _receipts);
        }

        _initializer = new CrowdUnitSpawnInitializer(this);
        _initializerKind = _spawns.RegisterInstanceInitializer(_initializer);
        _spawns.RegisterBatchComponent<CrowdSimulationAgent>(AgentBatchComponentName);
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

    public bool TryHandleOf(Entity entity, out uint handle)
    {
        if (!_world.IsAlive(entity) || !_world.Has<CrowdSimulationUnitState>(entity))
        {
            handle = 0;
            return false;
        }

        int slot = _world.Get<CrowdSimulationUnitState>(entity).Slot;
        if ((uint)slot >= (uint)Capacity || _sparse[slot] < 0 || _dense[_sparse[slot]] != entity)
        {
            handle = 0;
            return false;
        }

        handle = (uint)(((_gen[slot] & GenMask) << SlotBits) | slot);
        return true;
    }

    public bool TryResolveHandle(uint handle, out int dense)
    {
        int slot = (int)(handle & SlotMask);
        int gen = (int)(handle >> SlotBits);
        if ((uint)slot >= (uint)Capacity || _gen[slot] != gen)
        {
            dense = -1;
            return false;
        }

        dense = _sparse[slot];
        return dense >= 0;
    }

    /// <summary>本方单位句柄。接线给了隐藏模板标签时，带这个标签的单位不收。</summary>
    public int CopyOwnHandles(int playerId, Span<uint> destination)
    {
        int hiddenTag = _presentation.HiddenTemplateTagId;
        int n = 0;
        for (int i = 0; i < _dense.Count; i++)
        {
            var entity = _dense[i];
            if (_world.Get<PlayerOwner>(entity).PlayerId != playerId) continue;
            if (hiddenTag > 0 &&
                _world.Has<GameplayTagContainer>(entity) &&
                _world.Get<GameplayTagContainer>(entity).HasTag(hiddenTag))
            {
                continue;
            }

            if (n >= destination.Length)
            {
                throw new InvalidOperationException("order: 成员缓冲装不下本方单位。");
            }

            destination[n++] = HandleAt(i);
        }

        return n;
    }

    /// <summary>稠密下标 → 补全后的个人(避让/碰撞)半径。热路径读组件。</summary>
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
    /// 把尚未绑定的单位按模板分组，调用 RuntimeEntitySpawnSystem.Materialize。
    /// 没有待生成单位时直接返回。不入阶段队列，也不排空别人的请求。
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

        int written = 0;
        for (int u = 0; u < uniqueCount; u++)
        {
            int key = uniqueKeys[u];
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
        }

        _spawns.Materialize(_requestScratch.AsSpan(0, written));

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
            NoteOwnership(receipt.Entity, _pending[dense].PlayerId);
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
    /// Gather/Scatter 仍按稠密下标写 SoA；序一乱，每行都打到不同缓存行。
    /// 只交换这些实体已经占着的行，不新建实体，稠密表和实体 id 都不变。
    /// 同一签名各自成一个原型：带隐藏模板标签的单位多一个 GameplayTagContainer，不跟普通单位挤在同一原型。
    /// 每个原型必须正好装着这一签名的稠密单位。多出来的实体不能静默跳过，否则交换会卷进别人的行。
    /// </summary>
    private void OrderChunkRowsByDenseIndex()
    {
        int n = _dense.Count;
        if (n <= 1)
        {
            return;
        }

        Archetype primary = _world.GetArchetype(_dense[0]);
        bool uniform = true;
        for (int i = 1; i < n; i++)
        {
            if (!ReferenceEquals(_world.GetArchetype(_dense[i]), primary))
            {
                uniform = false;
                break;
            }
        }

        if (uniform)
        {
            var desired = new int[n];
            for (int i = 0; i < n; i++) desired[i] = n - 1 - i;
            OrderArchetypeRows(primary, desired);
            return;
        }

        var groups = new List<Archetype>(4);
        var members = new List<int[]>(4);
        var counts = new List<int>(4);
        for (int dense = 0; dense < n; dense++)
        {
            Archetype archetype = _world.GetArchetype(_dense[dense]);
            int group = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                if (ReferenceEquals(groups[g], archetype))
                {
                    group = g;
                    break;
                }
            }

            if (group < 0)
            {
                group = groups.Count;
                groups.Add(archetype);
                members.Add(new int[n]);
                counts.Add(0);
            }

            int written = counts[group];
            members[group][written] = dense;
            counts[group] = written + 1;
        }

        for (int g = 0; g < groups.Count; g++)
        {
            int count = counts[g];
            int[] bucket = members[g];
            Array.Sort(bucket, 0, count);
            for (int i = 0; i < count / 2; i++)
            {
                (bucket[i], bucket[count - 1 - i]) = (bucket[count - 1 - i], bucket[i]);
            }

            var desired = new int[count];
            Array.Copy(bucket, desired, count);
            OrderArchetypeRows(groups[g], desired);
        }
    }

    /// <summary>
    /// <paramref name="desiredDense"/> 的下标是正向槽位，值是要放进去的稠密下标。
    /// 查询从最后一块的最后一行往回走，所以这里按稠密下标降序填。
    /// </summary>
    private void OrderArchetypeRows(Archetype archetype, int[] desiredDense)
    {
        int n = desiredDense.Length;
        if (archetype.EntityCount != n)
        {
            throw new InvalidOperationException(
                $"SPAWN.RUNTIME.ERR.CrowdChunkOrder: unit archetype has {archetype.EntityCount} entities, dense count is {n}. The crowd unit archetype must contain exactly the dense set.");
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

        for (int i = 0; i < n; i++)
        {
            int desired = desiredDense[i];
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

    // Vendored Arch stores each column as T[] aligned with Signature.Components. The cast is the contract.
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

    /// <summary>从引擎的生成系统上摘掉本会话的初始化器和批组件准入。无头自建的系统随会话一起丢。</summary>
    public void ReleaseSpawnInitializer()
    {
        if (_initializerKind != 0)
        {
            _spawns.UnregisterInstanceInitializer(_initializerKind);
        }

        _spawns.UnregisterBatchComponent(AgentBatchComponentName);
    }

    /// <summary>
    /// 稠密表的唯一退场。ConsumeEntity 销毁实体，槽位代 +1，空闲槽 LIFO 压回。
    /// 与末行交换时调用方必须同时搬走避让行。生成尚未落地时拒绝。
    /// </summary>
    internal void ReleaseDense(int dense)
    {
        if (_pendingCount != 0)
        {
            throw new InvalidOperationException("单位生成尚未落地。");
        }

        if ((uint)dense >= (uint)_dense.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(dense));
        }

        Entity entity = _dense[dense];
        int slot = _slotOfDense[dense];
        if (entity != Entity.Null && _world.IsAlive(entity))
        {
            if (_world.Has<PlayerOwner>(entity))
            {
                SubtractSelected(entity, _world.Get<PlayerOwner>(entity).PlayerId);
            }

            ReleaseOwnership(entity);
            EntityLifecycleAtomicOps.ConsumeEntity(_world, entity, "CrowdSimulation.Release");
        }

        _gen[slot] = (ushort)((_gen[slot] + 1) & GenMask);
        _sparse[slot] = -1;
        _freeSlots[_freeTop++] = slot;
        int last = _dense.Count - 1;
        if (dense != last)
        {
            Entity moved = _dense[last];
            int movedSlot = _slotOfDense[last];
            _dense[dense] = moved;
            _slotOfDense[dense] = movedSlot;
            _sparse[movedSlot] = dense;
        }

        _dense.RemoveAt(last);
        _slotOfDense.RemoveAt(last);
    }

    private readonly Entity[] _selectedSubtract = new Entity[1];

    private void SubtractSelected(Entity entity, int playerId)
    {
        var applier = _presentation.CollectionApplier;
        var reps = _presentation.PlayerReps;
        if (applier == null || reps == null || (uint)playerId >= (uint)reps.Length) return;
        Entity rep = reps[playerId];
        if (rep == Entity.Null || !_world.IsAlive(rep)) return;
        _selectedSubtract[0] = entity;
        applier.Apply(rep, _presentation.SelectedCollectionKeyId, CollectionWriteOp.Subtract, _selectedSubtract);
    }

    /// <summary>
    /// 清空稠密表。已绑定且仍存活的单位走 ConsumeEntity，当帧从仿真里消失。
    /// 引擎世界有呈现消费者，实体留到销毁事件发布之后才 Destroy。
    /// 无头世界在构造时声明没有消费者，ConsumeEntity 当场收尾销毁。
    /// Entity.Null 占位不销毁。代 bump 与槽池 LIFO 序保持回放可复现。
    /// </summary>
    public void Clear()
    {
        // 活接线才清集合:回放派生接线没有 CollectionApplier,不能动正在显示的选中。
        ClearOwnedCollections();
        for (int i = 0; i < _dense.Count; i++)
        {
            Entity entity = _dense[i];
            if (entity != Entity.Null && _world.IsAlive(entity))
            {
                ReleaseOwnership(entity);
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

    private void NoteOwnership(Entity entity, int playerId)
    {
        OwnershipResolver? ownership = _presentation.Ownership;
        Entity[]? reps = _presentation.PlayerReps;
        if (ownership == null || reps == null || (uint)playerId >= (uint)reps.Length) return;
        Entity rep = reps[playerId];
        if (rep == Entity.Null || !_world.IsAlive(rep) || !_world.Has<PlayerIdentity>(rep))
        {
            throw new InvalidOperationException(
                $"crowd 单位的玩家 {playerId} 代表没有 PlayerIdentity,无法建立 owns 边。");
        }

        ownership.EnsureOwnership(rep, entity);
    }

    private void ReleaseOwnership(Entity entity)
    {
        if (_presentation.Ownership == null || entity == Entity.Null || !_world.IsAlive(entity)) return;
        _presentation.Ownership.ClearOwnership(entity);
    }

    private void ClearOwnedCollections()
    {
        var applier = _presentation.CollectionApplier;
        var reps = _presentation.PlayerReps;
        if (applier == null || reps == null) return;
        for (int player = 1; player < reps.Length; player++)
        {
            var rep = reps[player];
            if (rep == Entity.Null || !_world.IsAlive(rep)) continue;
            applier.Apply(rep, _presentation.SelectedCollectionKeyId, CollectionWriteOp.Replace, ReadOnlySpan<Entity>.Empty);
        }
    }

    /// <summary>实体、稠密下标、句柄。不含位置。</summary>
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
