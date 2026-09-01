using System;
using System.Numerics;
using Ludots.Content.EngineGallery;
using Ludots.Content.EngineGallery.Scenes;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    /// <summary>
    /// 画廊刀光场景的 headless 回归：SlashTrailScene 的 TrailSampleHistory 是含可变状态的
    /// struct，字段一旦 readonly，PushHead/EvictOlderThan/AgeTo 全部落在防御性副本上，
    /// Count 恒为 0，帧末 TrailMeshBuffer 为空、画廊什么都不画。这些测试直接驱动
    /// SimulateTrailFrame（纯数据模拟，无渲染调用），证明 PushHead 真的改变 Count、
    /// 一帧结束缓冲里真的有条带、挥砍结束后条带真的老化离场。
    /// </summary>
    [TestFixture]
    [Category("raylib-field")]
    public sealed class SlashTrailSceneTests
    {
        [Test]
        public void SlashTrailScene_FirstFrame_EmitsTrailStripIntoBuffer()
        {
            using var scene = new SlashTrailScene();

            scene.SimulateTrailFrame(out Vector3 bladeBase, out Vector3 bladeTip);

            Assert.That(scene.TrailBuffer.Count, Is.EqualTo(1), "挥砍窗口内第一帧必须产出条带");
            Assert.That(scene.TrailBuffer.GetStableId(0), Is.EqualTo(1));
            ReadOnlySpan<TrailMeshSample> samples = scene.TrailBuffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(1));
            Assert.That(samples[0].Age01, Is.EqualTo(0f));
            Assert.That(samples[0].Base, Is.EqualTo(bladeBase));
            Assert.That(samples[0].Tip, Is.EqualTo(bladeTip));
        }

        [Test]
        public void SlashTrailScene_ConsecutiveFrames_GrowStripWhileSwinging()
        {
            using var scene = new SlashTrailScene();

            for (int i = 0; i < 10; i++)
            {
                scene.SimulateTrailFrame(out _, out _);
            }

            // 挥砍窗口 0.45s ≈ 27 帧；连续 10 帧的 PushHead 都必须真实落地，条带逐帧增长。
            ReadOnlySpan<TrailMeshSample> samples = scene.TrailBuffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(10), "连续帧采样必须真实增长（readonly struct 防御性副本会让它永远停在 1）");
            Assert.That(scene.TrailBuffer.Count, Is.EqualTo(1));

            // index 0 是最新 head：其 Base/Tip 就是最后一步的刀刃位置。
            scene.SimulateTrailFrame(out Vector3 bladeBase, out Vector3 bladeTip);
            ReadOnlySpan<TrailMeshSample> grown = scene.TrailBuffer.GetSamples(0);
            Assert.That(grown.Length, Is.EqualTo(11));
            Assert.That(grown[0].Base, Is.EqualTo(bladeBase));
            Assert.That(grown[0].Tip, Is.EqualTo(bladeTip));
            Assert.That(grown[0].Age01, Is.EqualTo(0f));
        }

        [Test]
        public void SlashTrailScene_AfterSwingEnds_AgesOutAndBufferEmpties()
        {
            using var scene = new SlashTrailScene();

            // 整个挥砍周期 + 余量：样本全部超寿命后被淘汰，缓冲回到空态。
            for (int i = 0; i < 150; i++)
            {
                scene.SimulateTrailFrame(out _, out _);
            }

            Assert.That(scene.TrailBuffer.Count, Is.EqualTo(0), "挥砍结束后存量样本必须老化离场，缓冲回到空态");
        }
    }
}
