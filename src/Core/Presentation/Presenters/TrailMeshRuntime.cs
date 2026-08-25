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
    /// </summary>
    public sealed class TrailMeshRuntime
    {
        private sealed class TrailSampler
        {
            internal Entity Entity = Entity.Null;
            internal int StableId;
            internal int PoolIndex;
            internal TrailMeshConfig Config;
            internal readonly TrailMeshSample[] Samples = new TrailMeshSample[TrailMeshBuffer.MaxSamplesPerTrail];
            internal readonly float[] SampleTimes = new float[TrailMeshBuffer.MaxSamplesPerTrail];
            internal int Count;
            internal float LastSampleTime = float.MinValue;

            internal void Reset(Entity entity, int stableId)
            {
                Entity = entity;
                StableId = stableId;
                Count = 0;
                LastSampleTime = float.MinValue;
            }

            internal void Reset()
            {
                Entity = Entity.Null;
                StableId = 0;
                Count = 0;
                LastSampleTime = float.MinValue;
            }
        }

        private readonly TrailMeshBuffer _buffer;
        private readonly Dictionary<int, TrailSampler> _samplers;
        private readonly TrailSampler[] _samplerPool;
        private readonly int[] _freeSamplerIndices;
        private readonly List<int> _deadKeys;
        private readonly HashSet<int> _writtenThisAdvance;
        private readonly TrailMeshSample[] _emitScratch = new TrailMeshSample[TrailMeshBuffer.MaxSamplesPerTrail];

        public TrailMeshRuntime(TrailMeshBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            int samplerCapacity = Math.Max(buffer.Capacity, TrailMeshBuffer.MaxSamplesPerTrail * 2);
            _samplers = new Dictionary<int, TrailSampler>(samplerCapacity);
            _samplerPool = new TrailSampler[samplerCapacity];
            _freeSamplerIndices = new int[samplerCapacity];
            for (int i = 0; i < samplerCapacity; i++)
            {
                _samplerPool[i] = new TrailSampler { PoolIndex = i };
                _freeSamplerIndices[i] = samplerCapacity - 1 - i;
            }

            _deadKeys = new List<int>(samplerCapacity);
            _writtenThisAdvance = new HashSet<int>(samplerCapacity);
        }

        public int ActiveCount => _samplers.Count;

        public void Sample(Entity entity, int stableId, in TrailMeshConfig config, in Vector3 baseWorld, in Vector3 tipWorld, float now)
        {
            if (stableId <= 0)
            {
                throw new InvalidOperationException(
                    $"TrailMesh behavior requires a positive presenter stableId, got {stableId} for presenterEntityId={entity.Id}.");
            }

            if (!_samplers.TryGetValue(entity.Id, out TrailSampler? sampler))
            {
                if (_samplers.Count >= _freeSamplerIndices.Length)
                {
                    throw new InvalidOperationException(
                        $"TrailMeshRuntime sampler capacity exhausted while activating presenter entity {entity.Id} (capacity={_freeSamplerIndices.Length}).");
                }

                int samplerIndex = _freeSamplerIndices[_samplers.Count];
                sampler = _samplerPool[samplerIndex];
                sampler.Reset(entity, stableId);
                _samplers.Add(entity.Id, sampler);
            }

            sampler.Config = config;
            int maxSamples = Math.Clamp(config.MaxSamples, 2, TrailMeshBuffer.MaxSamplesPerTrail);
            if (sampler.Count > 0 && now - sampler.LastSampleTime < config.SampleIntervalSeconds)
            {
                sampler.Samples[0].Base = baseWorld;
                sampler.Samples[0].Tip = tipWorld;
                return;
            }

            if (sampler.Count >= maxSamples)
            {
                sampler.Count = maxSamples - 1;
            }

            if (sampler.Count > 0)
            {
                Array.Copy(sampler.Samples, 0, sampler.Samples, 1, sampler.Count);
                Array.Copy(sampler.SampleTimes, 0, sampler.SampleTimes, 1, sampler.Count);
            }

            sampler.Samples[0] = new TrailMeshSample { Base = baseWorld, Tip = tipWorld, Age01 = 0f };
            sampler.SampleTimes[0] = now;
            sampler.Count++;
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
                    while (sampler.Count > 0 && now - sampler.SampleTimes[sampler.Count - 1] > lifetime)
                    {
                        sampler.Count--;
                    }

                    if (sampler.Count == 0)
                    {
                        _deadKeys.Add(pair.Key);
                        continue;
                    }

                    for (int i = 0; i < sampler.Count; i++)
                    {
                        sampler.Samples[i].Age01 = Math.Clamp((now - sampler.SampleTimes[i]) / lifetime, 0f, 1f);
                        _emitScratch[i] = sampler.Samples[i];
                    }

                    if (!_buffer.Upsert(
                            sampler.StableId,
                            _emitScratch.AsSpan(0, sampler.Count),
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
    }
}
