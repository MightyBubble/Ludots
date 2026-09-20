using System;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// 单条 trail 的头插样本列（index 0 = 最新）。TrailMeshRuntime（Core）与 Raylib
    /// 引擎画廊 SlashTrailScene 共用同一份头插/寿命淘汰/age01 折算实现，任何一侧
    /// 的采样语义变更都必须同步反映到另一侧——不存在第二套可分叉的 producer。
    /// </summary>
    public struct TrailSampleHistory
    {
        private readonly TrailMeshSample[] _samples;
        private readonly float[] _times;
        private int _count;

        public int Count => _count;

        public ReadOnlySpan<TrailMeshSample> Samples => _samples.AsSpan(0, _count);

        public TrailSampleHistory(int capacity)
        {
            if (capacity <= 0 || capacity > TrailMeshBuffer.MaxSamplesPerTrail)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    $"TrailSampleHistory capacity must be in [1, {TrailMeshBuffer.MaxSamplesPerTrail}], got {capacity}.");
            }

            _samples = new TrailMeshSample[capacity];
            _times = new float[capacity];
            _count = 0;
        }

        public void Reset()
        {
            _count = 0;
        }

        /// <summary>
        /// 头插一个新样本；超过 maxSamples 时先丢弃最旧样本再插入。
        /// maxSamples 调用方已校验在 [1, 容量] 内，此处仅防御性复核。
        /// </summary>
        public void PushHead(in Vector3 baseWorld, in Vector3 tipWorld, float now, int maxSamples)
        {
            if (maxSamples <= 0 || maxSamples > _samples.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSamples),
                    $"PushHead maxSamples must be in [1, {_samples.Length}], got {maxSamples}.");
            }

            if (_count >= maxSamples)
            {
                _count = maxSamples - 1;
            }

            if (_count > 0)
            {
                Array.Copy(_samples, 0, _samples, 1, _count);
                Array.Copy(_times, 0, _times, 1, _count);
            }

            _samples[0] = new TrailMeshSample { Base = baseWorld, Tip = tipWorld, Age01 = 0f };
            _times[0] = now;
            _count++;
        }

        /// <summary>
        /// 丢弃已超过寿命的尾部样本（head 恒定保留；尾部逐帧老化由 AgeTo 折算）。
        /// </summary>
        public void EvictOlderThan(float now, float lifetimeSeconds)
        {
            while (_count > 0 && now - _times[_count - 1] > lifetimeSeconds)
            {
                _count--;
            }
        }

        public void AgeTo(float now, float lifetimeSeconds)
        {
            for (int i = 0; i < _count; i++)
            {
                _samples[i].Age01 = Math.Clamp((now - _times[i]) / lifetimeSeconds, 0f, 1f);
            }
        }

        /// <summary>
        /// 采样间隔内的头钉：原地刷新最新样本的世界坐标，不追加样本、不更新时间戳。
        /// </summary>
        public void PinHead(in Vector3 baseWorld, in Vector3 tipWorld)
        {
            _samples[0] = new TrailMeshSample { Base = baseWorld, Tip = tipWorld, Age01 = _samples[0].Age01 };
        }
    }
}
