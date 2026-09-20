using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// TrailSampleHistory 是含可变状态的 struct（_count、_samples、_times 均被 PushHead/
    /// EvictOlderThan/AgeTo 修改）。调用方必须以可写字段持有它——readonly 字段会让所有
    /// 修改落在防御性副本上，Count 永远为 0（SlashTrailScene 曾因此画不出任何东西）。
    /// 这些测试直接锁定“PushHead 真的改变 Count”这一契约。
    /// </summary>
    [TestFixture]
    public sealed class TrailSampleHistoryTests
    {
        [Test]
        public void TrailSampleHistory_PushHead_IncrementsCount()
        {
            TrailSampleHistory history = new(8);

            Assert.That(history.Count, Is.EqualTo(0));
            history.PushHead(Vector3.Zero, Vector3.UnitZ, now: 1f, maxSamples: 8);
            Assert.That(history.Count, Is.EqualTo(1), "PushHead 必须真实改变 Count（readonly struct 字段的防御性副本会让它保持 0）");
            history.PushHead(Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 1.1f, maxSamples: 8);
            Assert.That(history.Count, Is.EqualTo(2));
        }

        [Test]
        public void TrailSampleHistory_PushHead_IndexZeroIsNewest()
        {
            TrailSampleHistory history = new(8);
            history.PushHead(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), now: 1f, maxSamples: 8);
            history.PushHead(new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f), now: 2f, maxSamples: 8);

            ReadOnlySpan<TrailMeshSample> samples = history.Samples;
            Assert.That(samples.Length, Is.EqualTo(2));
            Assert.That(samples[0].Base, Is.EqualTo(new Vector3(1f, 0f, 0f)), "index 0 恒为最新 head 样本");
            Assert.That(samples[1].Base, Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void TrailSampleHistory_PushHead_OverCapacityDropsOldest()
        {
            TrailSampleHistory history = new(3);
            history.PushHead(Vector3.Zero, Vector3.UnitZ, now: 1f, maxSamples: 3);
            history.PushHead(Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 2f, maxSamples: 3);
            history.PushHead(Vector3.UnitX * 2f, (Vector3.UnitX * 2f) + Vector3.UnitZ, now: 3f, maxSamples: 3);
            history.PushHead(Vector3.UnitX * 3f, (Vector3.UnitX * 3f) + Vector3.UnitZ, now: 4f, maxSamples: 3);

            Assert.That(history.Count, Is.EqualTo(3));
            ReadOnlySpan<TrailMeshSample> samples = history.Samples;
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX * 3f));
            Assert.That(samples[2].Base, Is.EqualTo(Vector3.UnitX), "最旧样本被丢弃");
        }

        [Test]
        public void TrailSampleHistory_EvictOlderThan_RemovesExpiredTail_KeepsHead()
        {
            TrailSampleHistory history = new(8);
            history.PushHead(Vector3.Zero, Vector3.UnitZ, now: 1f, maxSamples: 8);
            history.PushHead(Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 2f, maxSamples: 8);

            history.EvictOlderThan(now: 2.5f, lifetimeSeconds: 0.5f);

            Assert.That(history.Count, Is.EqualTo(1), "now - t > lifetime 的尾部样本被淘汰");
            Assert.That(history.Samples[0].Base, Is.EqualTo(Vector3.UnitX), "head 恒定保留");
        }
    }
}
