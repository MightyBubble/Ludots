using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// TrailMesh 行为的采样器集合：行为激活期间每个 presenter 一个头插环形样本列
    /// （index 0 恒为最新，长度上限 TrailMeshConfig.MaxSamples），每帧把存活样本
    /// 折算成 age01 快照 upsert 进 TrailMeshBuffer。行为停用后停止采样、存量样本
    /// 按 SampleLifetimeSeconds 自然老化（淡出收尾）；presenter 死亡立即整条移除。
    /// 头插/寿命淘汰/age01 折算全部走共享纯工具 TrailSampleHistory（与引擎画廊
    /// SlashTrailScene 同实现）。采样器容量与 buffer.Capacity 严格一致。
    /// </summary>
    public sealed class TrailMeshRuntime
    {
        private sealed class TrailSampler
        {
            internal Entity Entity = Entity.Null;
            internal int StableId;
            internal int PoolIndex;
            internal TrailMeshConfig Config;
            internal TrailSampleHistory History;
            internal float LastSampleTime = float.MinValue;

            internal TrailSampler(int historyCapacity)
            {
                History = new TrailSampleHistory(historyCapacity);
            }

            internal void Reset(Entity entity, int stableId)
            {
                Entity = entity;
                StableId = stableId;
                History.Reset();
                LastSampleTime = float.MinValue;
            }

            internal void Reset()
            {
                Entity = Entity.Null;
                StableId = 0;
                History.Reset();
                LastSampleTime = float.MinValue;
            }
        }

        private readonly TrailMeshBuffer _buffer;
        private readonly Dictionary<int, TrailSampler> _samplers;
        private readonly Dictionary<int, TrailSampler> _samplersByStableId;
        private readonly TrailSampler[] _samplerPool;
        private readonly int[] _freeSamplerIndices;
        private readonly List<int> _deadKeys;
        private readonly HashSet<int> _writtenThisAdvance;

        public TrailMeshRuntime(TrailMeshBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            int samplerCapacity = buffer.Capacity;
            _samplers = new Dictionary<int, TrailSampler>(samplerCapacity);
            _samplersByStableId = new Dictionary<int, TrailSampler>(samplerCapacity);
            _samplerPool = new TrailSampler[samplerCapacity];
            _freeSamplerIndices = new int[samplerCapacity];
            for (int i = 0; i < samplerCapacity; i++)
            {
                _samplerPool[i] = new TrailSampler(TrailMeshBuffer.MaxSamplesPerTrail) { PoolIndex = i };
                _freeSamplerIndices[i] = samplerCapacity - 1 - i;
            }

            _deadKeys = new List<int>(samplerCapacity);
            _writtenThisAdvance = new HashSet<int>(samplerCapacity);
        }

        public int ActiveCount => _samplers.Count;

        public void Sample(World world, Entity entity, int stableId, in TrailMeshConfig config, in Vector3 baseWorld, in Vector3 tipWorld, float now)
        {
            if (stableId <= 0)
            {
                throw new InvalidOperationException(
                    $"TrailMesh behavior requires a positive presenter stableId, got {stableId} for presenterEntityId={entity.Id}.");
            }

            ValidateConfig(config);

            if (!_samplers.TryGetValue(entity.Id, out TrailSampler? sampler))
            {
                if (_samplers.Count >= _samplerPool.Length)
                {
                    throw new InvalidOperationException(
                        $"TrailMeshRuntime sampler capacity exhausted while activating presenter entity {entity.Id} (capacity={_samplerPool.Length}).");
                }

                ClaimStableId(world, stableId, entity);
                int samplerIndex = _freeSamplerIndices[_samplers.Count];
                sampler = _samplerPool[samplerIndex];
                sampler.Reset(entity, stableId);
                _samplers.Add(entity.Id, sampler);
                _samplersByStableId.Add(stableId, sampler);
            }
            else if (sampler.Entity != entity || sampler.StableId != stableId)
            {
                // Arch may recycle an entity id. A recycled id must start a fresh trail;
                // the previous stableId claim is released and the new one verified unique.
                if (_samplersByStableId.TryGetValue(stableId, out TrailSampler? existing) &&
                    !ReferenceEquals(existing, sampler))
                {
                    if (world.IsAlive(existing.Entity))
                    {
                        throw new InvalidOperationException(
                            $"TrailMesh stableId {stableId} is already claimed by another active sampler " +
                            $"(owner entity {existing.Entity.Id}); presenter entity {entity.Id} would silently " +
                            "overwrite the same TrailMeshBuffer slot. Presenter stableIds must be unique among live presenters.");
                    }

                    // 旧属主同帧内已死亡、尚未被 Advance 清扫：法律上的 stableId 复用，释放旧声明。
                    _samplersByStableId.Remove(stableId);
                }

                int previousStableId = sampler.StableId;
                if (_samplersByStableId.TryGetValue(previousStableId, out TrailSampler? previousClaim) &&
                    ReferenceEquals(previousClaim, sampler))
                {
                    _samplersByStableId.Remove(previousStableId);
                }
                sampler.Reset(entity, stableId);
                _samplersByStableId.Add(stableId, sampler);
            }

            sampler.Config = config;
            if (sampler.History.Count > 0 && now - sampler.LastSampleTime < config.SampleIntervalSeconds)
            {
                sampler.History.PinHead(baseWorld, tipWorld);
                return;
            }

            sampler.History.PushHead(baseWorld, tipWorld, now, config.MaxSamples);
            sampler.LastSampleTime = now;
        }

        public void Advance(World world, float now)
        {
            _writtenThisAdvance.Clear();
            if (_samplers.Count != 0)
            {
                _deadKeys.Clear();
                foreach (KeyValuePair<int, TrailSampler> pair in _samplers)
                {
                    TrailSampler sampler = pair.Value;
                    if (!world.IsAlive(sampler.Entity))
                    {
                        _deadKeys.Add(pair.Key);
                        continue;
                    }

                    float lifetime = sampler.Config.SampleLifetimeSeconds;
                    sampler.History.EvictOlderThan(now, lifetime);
                    if (sampler.History.Count == 0)
                    {
                        _deadKeys.Add(pair.Key);
                        continue;
                    }

                    sampler.History.AgeTo(now, lifetime);
                    if (!_buffer.Upsert(
                            sampler.StableId,
                            sampler.History.Samples,
                            in sampler.Config.HeadColor,
                            in sampler.Config.TailColor))
                    {
                        throw new InvalidOperationException(
                            $"TrailMeshBuffer overflowed while emitting trail stableId={sampler.StableId} (capacity={_buffer.Capacity}).");
                    }

                    _writtenThisAdvance.Add(sampler.StableId);
                }

                for (int i = 0; i < _deadKeys.Count; i++)
                {
                    int deadKey = _deadKeys[i];
                    if (_samplers.Remove(deadKey, out TrailSampler? sampler))
                    {
                        if (_samplersByStableId.TryGetValue(sampler.StableId, out TrailSampler? claim) &&
                            ReferenceEquals(claim, sampler))
                        {
                            _samplersByStableId.Remove(sampler.StableId);
                        }
                        sampler.Reset();
                        _freeSamplerIndices[_samplers.Count] = sampler.PoolIndex;
                    }
                }
            }

            // 本 runtime 是 buffer 唯一写入方；上一轮世界遗留或本轮未刷新的条目都是残影，统一回收。
            for (int i = _buffer.Count - 1; i >= 0; i--)
            {
                int stableId = _buffer.GetStableId(i);
                if (!_writtenThisAdvance.Contains(stableId))
                {
                    _buffer.Remove(stableId);
                }
            }
        }

        /// <summary>
        /// buffer 槽位以 stableId 为身份；两个存活采样器撞同一 stableId 会在
        /// Upsert 时静默覆盖彼此——在此确定性 fail-fast，而不是等 buffer 层面掩盖。
        /// 例外：旧属主同帧内已死亡（死亡与声明发生在同一次 Advance 清扫之前）属于
        /// 合法的 stableId 回收，释放旧声明后允许新属主接管，不误报。
        /// </summary>
        private void ClaimStableId(World world, int stableId, Entity entity)
        {
            if (_samplersByStableId.TryGetValue(stableId, out TrailSampler? existing))
            {
                if (world.IsAlive(existing.Entity))
                {
                    throw new InvalidOperationException(
                        $"TrailMesh stableId {stableId} is already claimed by another active sampler " +
                        $"(owner entity {existing.Entity.Id}); presenter entity {entity.Id} would silently " +
                        "overwrite the same TrailMeshBuffer slot. Presenter stableIds must be unique among live presenters.");
                }

                _samplersByStableId.Remove(stableId);
            }
        }

        private static void ValidateConfig(in TrailMeshConfig config)
        {
            if (config.MaxSamples < 2 || config.MaxSamples > TrailMeshBuffer.MaxSamplesPerTrail)
            {
                throw new InvalidOperationException(
                    $"TrailMeshConfig.MaxSamples must be in [2, {TrailMeshBuffer.MaxSamplesPerTrail}], got {config.MaxSamples}.");
            }

            if (!float.IsFinite(config.SampleIntervalSeconds) || config.SampleIntervalSeconds < 0f)
            {
                throw new InvalidOperationException(
                    $"TrailMeshConfig.SampleIntervalSeconds must be a finite value >= 0, got {config.SampleIntervalSeconds}.");
            }

            if (!float.IsFinite(config.SampleLifetimeSeconds) || config.SampleLifetimeSeconds <= 0f)
            {
                throw new InvalidOperationException(
                    $"TrailMeshConfig.SampleLifetimeSeconds must be a finite value > 0, got {config.SampleLifetimeSeconds}.");
            }
        }
    }
}
