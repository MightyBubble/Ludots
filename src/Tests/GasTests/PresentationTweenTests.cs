using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Tween;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public class PresentationTweenTests
    {
        private World _world;
        private Entity _frameStateEntity;
        private TweenCommandBuffer _commands;
        private TweenInstanceBuffer _instances;
        private TweenSinkRegistry _sinks;
        private TweenService _service;
        private TweenedScreenOverlayRegistry _overlayItems;
        private TweenRuntimeSystem _runtime;
        private TweenedScreenOverlayEmitSystem _emit;
        private ScreenOverlayBuffer _overlayBuffer;
        private int _overlaySinkId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _frameStateEntity = _world.Create(PresentationFrameState.Default, new PresentationFrameStateTag());
            _commands = new TweenCommandBuffer();
            _instances = new TweenInstanceBuffer();
            _sinks = new TweenSinkRegistry();
            _service = new TweenService(_commands, _sinks);
            _overlayItems = new TweenedScreenOverlayRegistry();
            _overlayBuffer = new ScreenOverlayBuffer();
            _overlaySinkId = _sinks.Register(TweenBuiltInSinkKeys.ScreenOverlayItem, new ScreenOverlayTweenSink(_overlayItems));
            _runtime = new TweenRuntimeSystem(_world, _commands, _instances, _sinks);
            _emit = new TweenedScreenOverlayEmitSystem(_world, _overlayItems, _overlayBuffer);
        }

        [TearDown]
        public void TearDown()
        {
            _emit?.Dispose();
            _runtime?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void TweenRuntime_InterpolatesOverlayX_OverRenderFrames()
        {
            Assert.That(_overlayItems.TryAllocateRect(10, 20, 100, 40, new Vector4(1f, 1f, 1f, 1f), Vector4.Zero, out int overlayId), Is.True);
            Assert.That(_service.TryStart(_overlaySinkId, overlayId, (int)ScreenOverlayItemTweenProperty.X, 10f, 110f, 1f), Is.True);

            SetRenderDelta(0.25f);
            _runtime.Update(0.25f);
            Assert.That(_overlayItems.Get(overlayId).X, Is.EqualTo(35f).Within(0.001f));
            Assert.That(_instances.ActiveCount, Is.EqualTo(1));

            SetRenderDelta(0.75f);
            _runtime.Update(0.75f);
            Assert.That(_overlayItems.Get(overlayId).X, Is.EqualTo(110f).Within(0.001f));
            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void TweenRuntime_StopScope_FreezesOverlayAtLastAppliedValue()
        {
            Assert.That(_overlayItems.TryAllocateRect(0, 0, 40, 20, new Vector4(1f, 1f, 1f, 1f), Vector4.Zero, out int overlayId), Is.True);
            Assert.That(_service.TryStart(_overlaySinkId, overlayId, (int)ScreenOverlayItemTweenProperty.X, 0f, 100f, 1f, scopeId: 7), Is.True);

            SetRenderDelta(0.2f);
            _runtime.Update(0.2f);
            float stoppedAt = _overlayItems.Get(overlayId).X;

            Assert.That(_service.TryStopScope(7), Is.True);
            SetRenderDelta(0.4f);
            _runtime.Update(0.4f);

            Assert.That(_overlayItems.Get(overlayId).X, Is.EqualTo(stoppedAt).Within(0.001f));
            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void TweenRuntime_Delay_HoldsFromValueUntilDelayExpires()
        {
            Assert.That(_overlayItems.TryAllocateRect(0, 0, 40, 20, new Vector4(1f, 1f, 1f, 0f), Vector4.Zero, out int overlayId), Is.True);
            Assert.That(
                _service.TryStart(_overlaySinkId, overlayId, (int)ScreenOverlayItemTweenProperty.FillAlpha, 0f, 1f, 0.5f, delay: 0.25f),
                Is.True);

            SetRenderDelta(0.1f);
            _runtime.Update(0.1f);
            Assert.That(_overlayItems.Get(overlayId).FillColor.W, Is.EqualTo(0f).Within(0.001f));

            SetRenderDelta(0.15f);
            _runtime.Update(0.15f);
            Assert.That(_overlayItems.Get(overlayId).FillColor.W, Is.EqualTo(0f).Within(0.001f));

            SetRenderDelta(0.25f);
            _runtime.Update(0.25f);
            Assert.That(_overlayItems.Get(overlayId).FillColor.W, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void TweenRuntime_CompleteScope_SnapsToTargetValue()
        {
            Assert.That(_overlayItems.TryAllocateRect(0, 0, 40, 20, new Vector4(1f, 1f, 1f, 0f), Vector4.Zero, out int overlayId), Is.True);
            Assert.That(_service.TryStart(_overlaySinkId, overlayId, (int)ScreenOverlayItemTweenProperty.FillAlpha, 0f, 1f, 1f, scopeId: 9), Is.True);

            SetRenderDelta(0.25f);
            _runtime.Update(0.25f);
            Assert.That(_overlayItems.Get(overlayId).FillColor.W, Is.EqualTo(0.25f).Within(0.001f));

            Assert.That(_service.TryCompleteScope(9), Is.True);
            SetRenderDelta(0.01f);
            _runtime.Update(0.01f);

            Assert.That(_overlayItems.Get(overlayId).FillColor.W, Is.EqualTo(1f).Within(0.001f));
            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void TweenedScreenOverlayEmitSystem_EmitsPersistentItemsIntoFrameBuffer()
        {
            Assert.That(_overlayItems.TryAllocateRect(10, 12, 80, 24, new Vector4(0f, 1f, 0f, 0.75f), new Vector4(1f, 1f, 1f, 1f), out _), Is.True);
            Assert.That(_overlayItems.TryAllocateText(40, 50, "Tween", 18, new Vector4(1f, 1f, 1f, 0.5f), out _), Is.True);

            _emit.Update(0.016f);

            var span = _overlayBuffer.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2));
            Assert.That(span[0].Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
            Assert.That(span[0].BackgroundColor.W, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(span[1].Kind, Is.EqualTo(ScreenOverlayItemKind.Text));
            Assert.That(_overlayBuffer.GetString(span[1].StringId), Is.EqualTo("Tween"));
        }

        private void SetRenderDelta(float dt)
        {
            ref var frameState = ref _world.Get<PresentationFrameState>(_frameStateEntity);
            frameState.RenderDeltaTime = dt;
        }
    }
}
