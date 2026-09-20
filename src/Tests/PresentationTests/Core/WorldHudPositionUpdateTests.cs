using System;
using System.Numerics;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class WorldHudPositionUpdateTests
    {
        [TestCase(WorldHudItemKind.Bar)]
        [TestCase(WorldHudItemKind.Text)]
        public void MovingRetainedItem_MatchesFullUpdateWithoutChangingContent(WorldHudItemKind kind)
        {
            var actual = new WorldHudBatchBuffer(1);
            var expected = new WorldHudBatchBuffer(1);
            var item = new WorldHudItem
            {
                StableId = 17,
                DirtySerial = 31,
                Kind = kind,
                WorldPosition = Vector3.One,
                Value0 = 0.5f,
                Width = 64f,
                Height = 8f,
                Color0 = Vector4.One,
                FontSize = 16,
                Text = PresentationTextPacket.FromWorldHudValueMode(7, WorldHudValueMode.AttributeCurrent, 42f, 0f),
            };
            Assert.That(actual.TryAdd(in item), Is.True);
            Assert.That(expected.TryAdd(in item), Is.True);
            item.WorldPosition = new Vector3(10f, 2f, 20f);
            Assert.That(expected.TryAdd(in item), Is.True);
            actual.UpdatePosition(item.StableId, in item.WorldPosition);

            Assert.That(actual.TryGetByStableId(item.StableId, out var moved), Is.True);
            Assert.That(moved, Is.EqualTo(item));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.ContentRevision, Is.EqualTo(expected.ContentRevision));
            Assert.That(actual.ProjectionRevision, Is.EqualTo(expected.ProjectionRevision));
            Assert.That(actual.ContentOnlyRevision, Is.EqualTo(expected.ContentOnlyRevision));
            Assert.That(actual.GetDirtyContentSpan().Length, Is.Zero);
            Assert.That(actual.DroppedTotal, Is.Zero);

            actual.UpdatePosition(item.StableId, in item.WorldPosition);
            Assert.That(actual.ContentRevision, Is.EqualTo(expected.ContentRevision));
            Assert.That(actual.ProjectionRevision, Is.EqualTo(expected.ProjectionRevision));
        }

        [Test]
        public void MovingMissingRetainedItem_ReportsBrokenContract()
        {
            var buffer = new WorldHudBatchBuffer(1);
            Assert.Throws<InvalidOperationException>(() => buffer.UpdatePosition(17, Vector3.One));
            Assert.That(buffer.Count, Is.Zero);
        }
    }
}
