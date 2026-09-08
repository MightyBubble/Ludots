using System.Numerics;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class HudDirtySerialFidelityTests
    {
        private static readonly Vector4 Background = new(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Vector4 Foreground = new(0.12f, 0.92f, 0.3f, 0.96f);

        [Test]
        public void BarDirtySerial_DistinguishesSubPixelHealthDrift()
        {
            int low = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, 0.30f, in Background, in Foreground);
            int high = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, 0.31f, in Background, in Foreground);

            Assert.That(high, Is.Not.EqualTo(low),
                "Bar health drift inside one rendered pixel must still change the dirty serial.");
        }

        [Test]
        public void BarDirtySerial_DistinguishesNonPositiveFromZero()
        {
            int negative = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, -0.5f, in Background, in Foreground);
            int zero = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, 0f, in Background, in Foreground);

            Assert.That(negative, Is.Not.EqualTo(zero),
                "Negative bar value must not collapse into the empty-bar serial.");
        }

        [Test]
        public void TextDirtySerial_DistinguishesSubUnitHealthDrift()
        {
            int low = HudItemIdentity.ComposeTextDirtySerial(
                fontSize: 11,
                stringTableId: 0,
                valueModeId: (int)WorldHudValueMode.AttributeCurrentOverBase,
                value0: 100.2f,
                value1: 260f,
                color: Foreground,
                packet: default);
            int high = HudItemIdentity.ComposeTextDirtySerial(
                fontSize: 11,
                stringTableId: 0,
                valueModeId: (int)WorldHudValueMode.AttributeCurrentOverBase,
                value0: 100.9f,
                value1: 260f,
                color: Foreground,
                packet: default);

            Assert.That(high, Is.Not.EqualTo(low),
                "Sub-unit health drift must still change the text dirty serial.");
        }

        [Test]
        public void TextDirtySerial_AttributeCurrentDistinguishesFraction()
        {
            int low = HudItemIdentity.ComposeTextDirtySerial(
                fontSize: 11,
                stringTableId: 0,
                valueModeId: (int)WorldHudValueMode.AttributeCurrent,
                value0: 10.1f,
                value1: 0f,
                color: Foreground,
                packet: default);
            int high = HudItemIdentity.ComposeTextDirtySerial(
                fontSize: 11,
                stringTableId: 0,
                valueModeId: (int)WorldHudValueMode.AttributeCurrent,
                value0: 10.6f,
                value1: 0f,
                color: Foreground,
                packet: default);

            Assert.That(high, Is.Not.EqualTo(low));
        }

        [Test]
        public void BarDirtySerial_StillCollapsesIdenticalContent()
        {
            int first = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, 0.5f, in Background, in Foreground);
            int second = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, 0.5f, in Background, in Foreground);

            Assert.That(second, Is.EqualTo(first),
                "Identical bar content must keep a stable dirty serial so retention can skip work.");
        }

        [Test]
        public void WorldHudBatchBuffer_DetectsContentChangeBehindEqualRoundedBarValue()
        {
            var buffer = new WorldHudBatchBuffer(16);
            var baseline = CreateBar(value: 0.30f);
            Assert.That(buffer.TryAdd(in baseline), Is.True);

            int projectionRevision = buffer.ProjectionRevision;
            var drifted = CreateBar(value: 0.31f);
            Assert.That(buffer.TryAdd(in drifted), Is.True);

            Assert.That(buffer.ProjectionRevision, Is.EqualTo(projectionRevision),
                "Sub-pixel bar drift must stay content-only; it must not invalidate projection.");
            Assert.That(buffer.GetDirtyContentSpan().Length, Is.EqualTo(1),
                "Sub-pixel bar drift must be published as a content delta.");
        }

        private static WorldHudItem CreateBar(float value)
        {
            return new WorldHudItem
            {
                StableId = 4242,
                DirtySerial = HudItemIdentity.ComposeBarDirtySerial(42f, 5f, value, in Background, in Foreground),
                Kind = WorldHudItemKind.Bar,
                WorldPosition = new Vector3(1f, 0f, 2f),
                Width = 42f,
                Height = 5f,
                Value0 = value,
                Color0 = Background,
                Color1 = Foreground,
            };
        }
    }
}
