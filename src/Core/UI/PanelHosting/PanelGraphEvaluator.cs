using System;
using Arch.Core;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Executes a panel's graph for one owning scope so its outputs materialize in
    /// the store. Implemented by the engine with <see cref="GraphReturnWriter"/>;
    /// test hosts may pass null to run read-only against whatever is already stored.
    /// </summary>
    public interface IPanelGraphEvaluator
    {
        void Evaluate(int graphId, Entity owner);
    }

    public sealed class GraphReturnWriterPanelEvaluator : IPanelGraphEvaluator
    {
        private readonly GraphReturnWriter _writer;
        private readonly IGraphRuntimeApi _api;

        public GraphReturnWriterPanelEvaluator(GraphReturnWriter writer, IGraphRuntimeApi api)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public void Evaluate(int graphId, Entity owner)
        {
            _writer.ExecuteAndWrite(
                graphId,
                owner,
                caster: owner,
                explicitTarget: Entity.Null,
                targetContext: Entity.Null,
                targetPosCm: default,
                randomSeed: 0u,
                _api);
        }
    }
}
