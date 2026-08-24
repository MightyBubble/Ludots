using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS.Scoring
{
    public struct GraphScoreEvaluationBudget
    {
        private readonly int _maxEvaluations;

        private GraphScoreEvaluationBudget(int maxEvaluations)
        {
            _maxEvaluations = maxEvaluations;
            Used = 0;
        }

        public int Used { get; private set; }

        public int MaxEvaluations => _maxEvaluations;

        public bool IsBounded => _maxEvaluations >= 0;

        public int Remaining => _maxEvaluations < 0
            ? int.MaxValue
            : Math.Max(0, _maxEvaluations - Used);

        public static GraphScoreEvaluationBudget Create(int maxEvaluations)
        {
            if (maxEvaluations < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvaluations), maxEvaluations, "Score graph budget must be non-negative.");
            }

            return new GraphScoreEvaluationBudget(maxEvaluations);
        }

        public static GraphScoreEvaluationBudget CreateUnbounded()
            => new(-1);

        public bool TryConsume()
        {
            if (_maxEvaluations >= 0 && Used >= _maxEvaluations)
            {
                return false;
            }

            Used++;
            return true;
        }
    }

    public static class GraphScoreEvaluator
    {
        public static bool TryEvaluate(
            World world,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi,
            int graphId,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ref GraphScoreEvaluationBudget budget,
            out float score)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (graphPrograms == null)
            {
                throw new ArgumentNullException(nameof(graphPrograms));
            }

            if (graphApi == null)
            {
                throw new ArgumentNullException(nameof(graphApi));
            }

            if (graphId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(graphId), graphId, "Score graph id must be positive.");
            }

            if (!budget.TryConsume())
            {
                score = 0f;
                return false;
            }

            ReadOnlySpan<GraphInstruction> program = RequireScoreProgram(graphPrograms, graphId, "GraphScoreEvaluator");
            GraphKind kind = graphPrograms.RequireKind(graphId, GraphKind.Score);
            score = GraphExecutor.ExecuteScore(world, caster, explicitTarget, targetPosCm, program, graphApi, kind, programs: graphPrograms);
            return true;
        }

        public static ReadOnlySpan<GraphInstruction> RequireScoreProgram(
            GraphProgramRegistry graphPrograms,
            int graphId,
            string source)
        {
            if (graphPrograms == null)
            {
                throw new ArgumentNullException(nameof(graphPrograms));
            }

            if (graphId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(graphId), graphId, "Score graph id must be positive.");
            }

            if (!graphPrograms.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException($"{source}: score graph id {graphId} is not registered.");
            }

            GraphKind kind = graphPrograms.RequireKind(graphId, GraphKind.Score);
            GraphKindOperationPolicy.RequireAllowed(
                kind,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                entrypoint: source);
            return program;
        }
    }
}
