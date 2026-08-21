using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace RngCoreTests
{
    [TestFixture]
    public class ProbeTableTests
    {
        [Test]
        public void Probe_GasGraphOpHandlerTable_Constructs()
        {
            var table = new GasGraphOpHandlerTable();
            Assert.That(table.Handlers[(int)GraphNodeOp.WeightedPick], Is.Not.Null);
        }
    }
}
