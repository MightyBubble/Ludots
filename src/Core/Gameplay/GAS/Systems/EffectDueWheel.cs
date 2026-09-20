using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    [Flags]
    internal enum EffectDueKind : byte
    {
        None = 0,
        PeriodDue = 1,
        ExpireDue = 2,
    }

    /// <summary>
    /// GAS 效果到期时间轮（A 档）：以 FixedFrame 域 tick 为唯一时间基，替代 EffectLifetimeSystem
    /// 每 tick 的全量效果快照扫描。三条车道：
    /// 快道——桶环 16384 桶按到期 tick 寻址，只访问途经桶；
    /// 溢出道——到期超出环窗口的注册项，摊销回扫迁回；
    /// 遗留道——时间轮无法忠实调度的效果（非 FixedFrame 时钟、有效 ExpireCondition、
    /// 待处理取消、Infinite 无周期锚点），保留原全量扫描语义作为零回归兜底，这不是 fallback，
    /// 而是 A 档的组成车道：任何进不了快道的效果行为与改造前逐 tick 扫描完全一致。
    /// 快道保真约束：到期判定为 now >= dueTick，AdvanceTo 以绝对 tick 推进并对 (watermark, tick]
    /// 逐桶出桶，与原扫描对同一时钟的判定逐 slice 等价；引擎取消路径通过 ForceVisit 保持
    /// next-slice 销毁语义；绕过服务的直接组件写（改 DueTick 字段）在下次自然出桶时按当前
    /// 字段值重新收敛，唯独"把到期 tick 提前"的外部写入最迟延迟到原注册 tick 才被观察。
    /// 一致性策略：注册表（(effect, kind) -> dueTick）是唯一权威，桶/溢出/强制道中的 Entry
    /// 出桶时与注册表不匹配即静默丢弃（惰性删除），同 (effect, kind) 重复注册为幂等替换。
    /// tick 回退或跨环跳变时置脏，由持有方 Rebuild 重建。
    /// </summary>
    internal sealed class EffectDueWheel
    {
        public const int RingSize = 16384;
        public const string SnapshotCapacityExceededError = "GAS.EFFECT_LIFETIME.ERR.SnapshotCapacityExceeded";

        private const int RingMask = RingSize - 1;
        private const int OverflowAmortizedScanPerTick = 256;

        private static readonly QueryDescription ActiveEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();

        private struct Entry
        {
            public Entity Effect;
            public int DueTick;
            public byte KindMask;
        }

        private readonly IClock _clock;
        private readonly int _trackedCapacity;
        private readonly Entry[][] _buckets = new Entry[RingSize][];
        private readonly int[] _bucketCounts = new int[RingSize];
        private readonly Dictionary<(Entity Effect, EffectDueKind Kind), int> _registeredDueTick = new(1024);
        private readonly List<Entry> _overflow = new(64);
        private readonly List<Entry> _forced = new(64);
        private readonly Entity[] _rebuildScratch;
        private readonly Dictionary<Entity, byte> _dueDedupe = new(64);
        private readonly List<Entity> _dueOrdered = new(64);
        private readonly List<Entity> _legacyEffects = new(64);
        // 快道 After 效果索引：RemainingTicks 是图算子逐帧读取的可观测字段（面板时序），
        // 原扫描逐 tick 刷新它；轮化后由本索引承载同等刷新，避免为它保留全量扫描。
        private readonly List<Entity> _afterEffects = new(64);
        private int _overflowCursor;
        private int _ticksSinceOverflowSweep;
        private int _watermark = -1;
        private bool _dirty = true;

        public EffectDueWheel(IClock clock, int trackedCapacity)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (trackedCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackedCapacity));
            }

            _trackedCapacity = trackedCapacity;
            _rebuildScratch = new Entity[trackedCapacity];
        }

        public int Watermark => _watermark;

        public bool IsDirty => _dirty;

        /// <summary>在册 (effect, kind) 注册数，供测试与诊断。</summary>
        public int Count => _registeredDueTick.Count;

        internal List<Entity> LegacyEffects => _legacyEffects;

        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>全量清空（含水位与脏位），供测试与诊断。</summary>
        public void Clear()
        {
            Array.Clear(_bucketCounts, 0, _bucketCounts.Length);
            _registeredDueTick.Clear();
            _overflow.Clear();
            _forced.Clear();
            _legacyEffects.Clear();
            _afterEffects.Clear();
            _dueDedupe.Clear();
            _dueOrdered.Clear();
            _overflowCursor = 0;
            _ticksSinceOverflowSweep = 0;
            _watermark = -1;
            _dirty = true;
        }

        /// <summary>注册一种到期；同 (effect, kind) 幂等替换 DueTick，旧 Entry 惰性失效。</summary>
        public void Register(Entity effect, int dueTick, EffectDueKind kind)
        {
            Debug.Assert(dueTick >= 0, "GAS.EFFECT_DUE_WHEEL.ERR.NegativeDueTick");
            Debug.Assert(
                kind == EffectDueKind.PeriodDue || kind == EffectDueKind.ExpireDue,
                "GAS.EFFECT_DUE_WHEEL.ERR.RegisterRequiresSingleKind");

            var key = (effect, kind);
            if (_registeredDueTick.TryGetValue(key, out int registered) && registered == dueTick)
            {
                return;
            }

            _registeredDueTick[key] = dueTick;
            byte mask = (byte)kind;
            if (dueTick <= _watermark)
            {
                _forced.Add(new Entry { Effect = effect, DueTick = dueTick, KindMask = mask });
                return;
            }

            if (dueTick > _watermark + RingSize)
            {
                _overflow.Add(new Entry { Effect = effect, DueTick = dueTick, KindMask = mask });
                return;
            }

            AddToBucket(dueTick, effect, mask);
        }

        /// <summary>注销一种到期；桶内旧 Entry 出桶时因注册表失配被惰性丢弃。</summary>
        public void Cancel(Entity effect, EffectDueKind kind)
        {
            _registeredDueTick.Remove((effect, kind));
        }

        public void CancelAll(Entity effect)
        {
            _registeredDueTick.Remove((effect, EffectDueKind.PeriodDue));
            _registeredDueTick.Remove((effect, EffectDueKind.ExpireDue));
        }

        /// <summary>
        /// 把在册效果的所有到期强制挪到下一 slice 出桶。引擎取消路径（事务、图 API、装备同步）
        /// 设置 CancelRequested 后调用，保持原"下一 slice 即销毁"语义；未注册（遗留道）为 no-op。
        /// </summary>
        public void ForceVisit(Entity effect)
        {
            byte mask = 0;
            if (_registeredDueTick.TryGetValue((effect, EffectDueKind.PeriodDue), out int periodDue))
            {
                mask |= (byte)EffectDueKind.PeriodDue;
                _registeredDueTick[(effect, EffectDueKind.PeriodDue)] = _watermark;
            }

            if (_registeredDueTick.TryGetValue((effect, EffectDueKind.ExpireDue), out int expireDue))
            {
                mask |= (byte)EffectDueKind.ExpireDue;
                _registeredDueTick[(effect, EffectDueKind.ExpireDue)] = _watermark;
            }

            if (mask != 0)
            {
                _forced.Add(new Entry { Effect = effect, DueTick = _watermark, KindMask = mask });
            }
        }

        /// <summary>
        /// 推进到 tick 并收集到期效果（去重）。tick 回退或跨环跳变时不排桶、置脏交由持有方 Rebuild。
        /// </summary>
        public void AdvanceTo(int tick, List<Entity> dueEffects)
        {
            dueEffects.Clear();
            if (_dirty)
            {
                return;
            }

            if (tick < _watermark || tick - _watermark > RingSize)
            {
                _dirty = true;
                return;
            }

            _dueDedupe.Clear();
            _dueOrdered.Clear();
            DrainForced();
            for (int t = _watermark + 1; t <= tick; t++)
            {
                DrainBucket(t);
            }

            _watermark = tick;
            dueEffects.AddRange(_dueOrdered);
            MaintainOverflow();
        }

        /// <summary>
        /// 效果提交（State == Committed）后的入轮入口：可忠实调度的进快道，其余 Committed 效果进遗留道；
        /// 未提交效果不入任何车道——原扫描对其本就是 no-op，提交入口会在其可见的第一 slice 注册。
        /// </summary>
        public void RegisterCommitted(Entity effect, World world)
        {
            if (!world.IsAlive(effect) || !world.TryGet<GameplayEffect>(effect, out GameplayEffect ge))
            {
                return;
            }

            RegisterClassified(effect, world, in ge);
        }

        /// <summary>
        /// 全量重建：从活跃效果查询拉全量并重新分类。容量契约与原 BeginSlice 快照一致
        /// （总活跃数超容量即抛 SnapshotCapacityExceeded）。重建后水位对齐当前 tick，
        /// 到期不晚于当前 tick 的注册进入强制道，由紧随的 AdvanceTo 出桶。
        /// </summary>
        public void Rebuild(World world)
        {
            int total = world.CountEntities(in ActiveEffectsQuery);
            if (total > _trackedCapacity)
            {
                throw new InvalidOperationException(
                    $"{SnapshotCapacityExceededError}: required={total}, capacity={_trackedCapacity}.");
            }

            Array.Clear(_bucketCounts, 0, _bucketCounts.Length);
            _registeredDueTick.Clear();
            _overflow.Clear();
            _forced.Clear();
            _legacyEffects.Clear();
            _afterEffects.Clear();
            _overflowCursor = 0;
            _ticksSinceOverflowSweep = 0;
            _watermark = _clock.Now(ClockDomainId.FixedFrame);

            world.GetEntities(in ActiveEffectsQuery, _rebuildScratch);
            for (int i = 0; i < total; i++)
            {
                Entity effect = _rebuildScratch[i];
                if (!world.IsAlive(effect) || !world.TryGet<GameplayEffect>(effect, out GameplayEffect ge))
                {
                    continue;
                }

                RegisterClassified(effect, world, in ge);
            }

            _dirty = false;
        }

        /// <summary>
        /// 快道可调度性判据（含到期锚点计算）。None 表示该效果必须走遗留道：
        /// 非 FixedFrame 时钟（Step 单帧可推进 0..N、EntityLocal 按 per-entity scale 变速，
        /// 单一时间基无法忠实映射）；有效 ExpireCondition（需逐 tick 条件评估）；
        /// CancelRequested / EffectCancelled（取消待处理，下一 slice 必须可见）；
        /// Infinite 无周期（无到期锚点）；未提交（提交入口负责入轮）。
        /// </summary>
        internal static EffectDueKind ComputeFastLaneKinds(World world, Entity effect, in GameplayEffect ge)
        {
            if (ge.State != EffectState.Committed) return EffectDueKind.None;
            if (ge.ClockId != GasClockId.FixedFrame) return EffectDueKind.None;
            if (ge.CancelRequested || world.Has<EffectCancelled>(effect)) return EffectDueKind.None;
            if (ge.ExpireCondition.IsValid) return EffectDueKind.None;

            EffectDueKind kinds = EffectDueKind.None;
            if (ge.PeriodTicks > 0 &&
                (ge.LifetimeKind == EffectLifetimeKind.After || ge.LifetimeKind == EffectLifetimeKind.Infinite) &&
                world.Has<EffectPeriodicTick>(effect))
            {
                kinds |= EffectDueKind.PeriodDue;
            }

            if (ge.LifetimeKind == EffectLifetimeKind.After && world.Has<EffectExpirationCheck>(effect))
            {
                kinds |= EffectDueKind.ExpireDue;
            }

            return kinds;
        }

        private void RegisterClassified(Entity effect, World world, in GameplayEffect ge)
        {
            EffectDueKind kinds = ComputeFastLaneKinds(world, effect, in ge);
            if (kinds == EffectDueKind.None)
            {
                if (ge.State == EffectState.Committed)
                {
                    _legacyEffects.Add(effect);
                }

                return;
            }

            // 提交钩子与 Rebuild 共用此入口：以时钟当前值为惰性初始化注册的到期 tick，
            // 与原扫描"首个可见 slice 完成初始化"逐 tick 对齐（水位可能落后于时钟）。
            int now = _clock.Now(ClockDomainId.FixedFrame);
            if ((kinds & EffectDueKind.PeriodDue) != 0)
            {
                Register(effect, ge.NextTickAtTick > 0 ? ge.NextTickAtTick : now, EffectDueKind.PeriodDue);
            }

            if ((kinds & EffectDueKind.ExpireDue) != 0)
            {
                Register(effect, ge.ExpiresAtTick > 0 ? ge.ExpiresAtTick : now, EffectDueKind.ExpireDue);
                _afterEffects.Add(effect);
            }
        }

        /// <summary>
        /// 快道 After 效果索引：RemainingTicks 是图算子逐帧读取的可观测字段（面板时序），
        /// 原扫描逐 tick 刷新；轮化后由持有方逐 slice 遍历本索引刷新（经事务暂存以保回滚语义），
        /// 出桶访问后的再注册不重复入索引（索引项在访问周期内持续有效）。
        /// </summary>
        internal List<Entity> AfterEffects => _afterEffects;

        private void AddToBucket(int dueTick, Entity effect, byte kindMask)
        {
            int index = dueTick & RingMask;
            Entry[] bucket = _buckets[index];
            int count = _bucketCounts[index];
            for (int i = 0; i < count; i++)
            {
                // 同效果同 DueTick 才合并 kind 位；跨轮次残留的同效果旧 Entry 保留独立条目，
                // 由注册表失配在出桶时惰性丢弃。
                if (bucket[i].Effect == effect && bucket[i].DueTick == dueTick)
                {
                    bucket[i].KindMask |= kindMask;
                    return;
                }
            }

            if (bucket == null)
            {
                bucket = new Entry[4];
                _buckets[index] = bucket;
            }
            else if (count == bucket.Length)
            {
                Array.Resize(ref bucket, bucket.Length * 2);
                _buckets[index] = bucket;
            }

            bucket[count] = new Entry { Effect = effect, DueTick = dueTick, KindMask = kindMask };
            _bucketCounts[index] = count + 1;
        }

        private void DrainForced()
        {
            for (int i = 0; i < _forced.Count; i++)
            {
                Entry entry = _forced[i];
                if (HasLiveRegistration(in entry, EffectDueKind.PeriodDue) |
                    HasLiveRegistration(in entry, EffectDueKind.ExpireDue))
                {
                    AddDue(entry.Effect);
                }
            }

            _forced.Clear();
        }

        private void DrainBucket(int tick)
        {
            int index = tick & RingMask;
            int count = _bucketCounts[index];
            if (count == 0)
            {
                return;
            }

            Entry[] bucket = _buckets[index];
            for (int i = 0; i < count; i++)
            {
                Entry entry = bucket[i];
                if (HasLiveRegistration(in entry, EffectDueKind.PeriodDue) |
                    HasLiveRegistration(in entry, EffectDueKind.ExpireDue))
                {
                    AddDue(entry.Effect);
                }
            }

            _bucketCounts[index] = 0;
        }

        private bool HasLiveRegistration(in Entry entry, EffectDueKind kind)
        {
            byte bit = (byte)kind;
            if ((entry.KindMask & bit) == 0)
            {
                return false;
            }

            if (_registeredDueTick.TryGetValue((entry.Effect, kind), out int dueTick) && dueTick == entry.DueTick)
            {
                _registeredDueTick.Remove((entry.Effect, kind));
                return true;
            }

            return false;
        }

        private void AddDue(Entity effect)
        {
            if (_dueDedupe.ContainsKey(effect))
            {
                return;
            }

            _dueDedupe.Add(effect, 0);
            _dueOrdered.Add(effect);
        }

        private void MaintainOverflow()
        {
            int count = _overflow.Count;
            if (count == 0)
            {
                _overflowCursor = 0;
                _ticksSinceOverflowSweep = 0;
                return;
            }

            _ticksSinceOverflowSweep++;
            bool fullSweep = _ticksSinceOverflowSweep >= RingSize;
            int budget = fullSweep ? _overflow.Count : Math.Min(_overflow.Count, OverflowAmortizedScanPerTick);
            int windowEnd = _watermark + RingSize;

            int examined = 0;
            while (examined < budget && _overflow.Count > 0)
            {
                Entry entry = _overflow[_overflowCursor];
                examined++;
                if (PromoteOverflowEntry(in entry, windowEnd))
                {
                    _overflow[_overflowCursor] = _overflow[^1];
                    _overflow.RemoveAt(_overflow.Count - 1);
                }
                else
                {
                    _overflowCursor++;
                }

                if (_overflow.Count == 0 || _overflowCursor >= _overflow.Count)
                {
                    _overflowCursor = 0;
                }
            }

            if (fullSweep)
            {
                _ticksSinceOverflowSweep = 0;
            }
        }

        private bool PromoteOverflowEntry(in Entry entry, int windowEnd)
        {
            if (entry.DueTick > windowEnd)
            {
                return false;
            }

            if (!_registeredDueTick.TryGetValue((entry.Effect, (EffectDueKind)entry.KindMask), out int dueTick) ||
                dueTick != entry.DueTick)
            {
                return true;
            }

            byte mask = entry.KindMask;
            if (entry.DueTick <= _watermark)
            {
                _forced.Add(new Entry { Effect = entry.Effect, DueTick = entry.DueTick, KindMask = mask });
            }
            else
            {
                AddToBucket(entry.DueTick, entry.Effect, mask);
            }

            return true;
        }
    }
}
