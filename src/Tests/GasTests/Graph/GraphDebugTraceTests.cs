using Arch.Core;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [NUnit.Framework.TestFixture]
    public sealed class GraphDebugTraceTests
    {
    [NUnit.Framework.Test]
    public void DisabledTraceDoesNotAdvanceSequence()
    {
        var trace = new GraphDebugTrace(2);
        trace.RecordNode(7, 1, 2, 1, GraphDebugTraceEvent.NodeEnter);

        NUnit.Framework.Assert.That(trace.LatestSequence, Is.EqualTo(0));
        NUnit.Framework.Assert.That(trace.DroppedCount, Is.EqualTo(0));
    }

    [NUnit.Framework.Test]
    public void RingReportsDroppedRecordsAndReadsIncrementally()
    {
        var trace = new GraphDebugTrace(2);
        trace.Configure(GraphDebugTraceMode.NodeAndPins);
        trace.RecordNode(7, 1, 2, 1, GraphDebugTraceEvent.NodeEnter);
        trace.RecordIntPin(7, 2, 0, 7, 2, 2);
        trace.RecordEntityPin(9, 3, 1, Entity.Null, 3, 3);

        var records = new GraphDebugTraceRecord[4];
        int count = trace.ReadSince(0, records, out long oldest);

        NUnit.Framework.Assert.That(count, Is.EqualTo(2));
        NUnit.Framework.Assert.That(oldest, Is.EqualTo(2));
        NUnit.Framework.Assert.That(trace.DroppedCount, Is.EqualTo(1));
        NUnit.Framework.Assert.That(records[0].EventKind, Is.EqualTo(GraphDebugTraceEvent.PinInt));
        NUnit.Framework.Assert.That(records[1].EventKind, Is.EqualTo(GraphDebugTraceEvent.PinEntity));
        NUnit.Framework.Assert.That(trace.ReadSince(records[0].Sequence, records, out _), Is.EqualTo(1));
    }
    }
}
