using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class FixedDatagramSendQueueTests
    {
        [Test]
        public void PreparedBatch_IsHiddenUntilCommit_AndCancelPreservesVisibleQueue()
        {
            var queue = new FixedServerDatagramSendQueue(capacity: 3, maxPayloadBytes: 8);
            var firstConnection = new ConnectionId(1);
            var preparedConnection = new ConnectionId(2);
            var channel = new ChannelId(3);
            byte[] firstPayload = { 1, 2 };
            byte[] preparedPayload = { 3, 4, 5 };

            Assert.That(queue.TryEnqueue(firstConnection, channel, firstPayload), Is.True);
            Assert.That(queue.TryBeginPreparedBatch(), Is.True);
            Assert.That(queue.TryEnqueuePrepared(preparedConnection, channel, preparedPayload), Is.True);
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryPeek(out ConnectionId visibleConnection, out _, out ReadOnlySpan<byte> visiblePayload), Is.True);
            byte[] visiblePayloadCopy = visiblePayload.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(visibleConnection, Is.EqualTo(firstConnection));
                Assert.That(visiblePayloadCopy, Is.EqualTo(firstPayload));
            });

            queue.CancelPreparedBatch();
            Assert.That(queue.Count, Is.EqualTo(1));
            queue.RemoveHead();
            Assert.That(queue.TryPeek(out _, out _, out _), Is.False);

            Assert.That(queue.TryBeginPreparedBatch(), Is.True);
            Assert.That(queue.TryEnqueuePrepared(preparedConnection, channel, preparedPayload), Is.True);
            Assert.That(queue.TryPeek(out _, out _, out _), Is.False);
            queue.CommitPreparedBatch();

            Assert.That(queue.TryPeek(out visibleConnection, out ChannelId visibleChannel, out visiblePayload), Is.True);
            visiblePayloadCopy = visiblePayload.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(visibleConnection, Is.EqualTo(preparedConnection));
                Assert.That(visibleChannel, Is.EqualTo(channel));
                Assert.That(visiblePayloadCopy, Is.EqualTo(preparedPayload));
            });
        }
    }
}
