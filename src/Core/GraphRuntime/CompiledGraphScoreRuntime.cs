using System;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.GraphRuntime
{
    public enum GraphScoreFailureReason : byte
    {
        None = 0,
        UnknownGraph = 1,
        WrongKind = 2,
        BudgetExhausted = 3
    }

    public struct GraphInstructionBudget
    {
        private readonly int _maxInstructions;
        private int _consumedInstructions;

        private GraphInstructionBudget(int maxInstructions)
        {
            _maxInstructions = maxInstructions;
            _consumedInstructions = 0;
        }

        public int MaxInstructions => _maxInstructions;
        public int ConsumedInstructions => _consumedInstructions;
        public int RemainingInstructions => _maxInstructions - _consumedInstructions;

        public static GraphInstructionBudget Create(int maxInstructions)
        {
            if (maxInstructions <= 0 || maxInstructions == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxInstructions),
                    maxInstructions,
                    "Graph instruction budget must be positive and finite.");
            }

            return new GraphInstructionBudget(maxInstructions);
        }

        internal bool TryConsumeInstruction()
        {
            if (_maxInstructions <= 0 || _consumedInstructions >= _maxInstructions)
            {
                return false;
            }

            _consumedInstructions++;
            return true;
        }
    }

    public interface IReadOnlyGraphScorer
    {
        void RequireScoreGraph(int graphId, string path);
        void RequireValidationGraph(int graphId, string path);

        bool TryEvaluateScore(
            Entity actor,
            Entity target,
            IntVector2 targetPosCm,
            int graphId,
            ref GraphInstructionBudget budget,
            out float score,
            out GraphScoreFailureReason failureReason);

        bool TryEvaluateValidation(
            Entity actor,
            Entity target,
            IntVector2 targetPosCm,
            int graphId,
            ref GraphInstructionBudget budget,
            out bool passed,
            out GraphScoreFailureReason failureReason);
    }

    public sealed class CompiledGraphScoreRuntime : IReadOnlyGraphScorer
    {
        private readonly World _world;
        private readonly IGraphRuntimeApi _api;
        private readonly GraphScorePlan[] _plans;

        private CompiledGraphScoreRuntime(World world, IGraphRuntimeApi api, GraphScorePlan[] plans)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _plans = plans ?? Array.Empty<GraphScorePlan>();
        }

        public static CompiledGraphScoreRuntime Compile(
            World world,
            IGraphRuntimeApi api,
            GraphProgramRegistry graphPrograms)
        {
            if (graphPrograms == null) throw new ArgumentNullException(nameof(graphPrograms));

            GraphProgramSnapshot[] snapshots = graphPrograms.CreateSnapshot();
            int maxGraphId = 0;
            for (int i = 0; i < snapshots.Length; i++)
            {
                GraphKind kind = snapshots[i].Kind;
                if (kind is GraphKind.Score or GraphKind.Validation && snapshots[i].GraphId > maxGraphId)
                {
                    maxGraphId = snapshots[i].GraphId;
                }
            }

            var plans = maxGraphId > 0
                ? new GraphScorePlan[maxGraphId + 1]
                : Array.Empty<GraphScorePlan>();

            for (int i = 0; i < snapshots.Length; i++)
            {
                GraphProgramSnapshot snapshot = snapshots[i];
                if (snapshot.Kind is not (GraphKind.Score or GraphKind.Validation))
                {
                    continue;
                }

                GraphKindOperationPolicy.RequireAllowed(
                    snapshot.Kind,
                    snapshot.Program,
                    GasGraphOpHandlerTable.Instance,
                    snapshot.GraphId,
                    nameof(CompiledGraphScoreRuntime));

                var program = new GraphInstruction[snapshot.Program.Length];
                Array.Copy(snapshot.Program, program, program.Length);
                plans[snapshot.GraphId] = new GraphScorePlan(snapshot.Kind, program);
            }

            return new CompiledGraphScoreRuntime(world, api, plans);
        }

        public void RequireScoreGraph(int graphId, string path)
        {
            RequireGraph(graphId, GraphKind.Score, path);
        }

        public void RequireValidationGraph(int graphId, string path)
        {
            RequireGraph(graphId, GraphKind.Validation, path);
        }

        public bool TryEvaluateScore(
            Entity actor,
            Entity target,
            IntVector2 targetPosCm,
            int graphId,
            ref GraphInstructionBudget budget,
            out float score,
            out GraphScoreFailureReason failureReason)
        {
            score = 0f;
            if (!TryGetPlan(graphId, GraphKind.Score, out GraphScorePlan plan, out failureReason))
            {
                return false;
            }

            if (!GraphExecutor.TryExecutePrevalidatedScore(
                    _world,
                    actor,
                    target,
                    targetPosCm,
                    plan.Program,
                    _api,
                    ref budget,
                    out score))
            {
                score = 0f;
                failureReason = GraphScoreFailureReason.BudgetExhausted;
                return false;
            }

            failureReason = GraphScoreFailureReason.None;
            return true;
        }

        public bool TryEvaluateValidation(
            Entity actor,
            Entity target,
            IntVector2 targetPosCm,
            int graphId,
            ref GraphInstructionBudget budget,
            out bool passed,
            out GraphScoreFailureReason failureReason)
        {
            passed = false;
            if (!TryGetPlan(graphId, GraphKind.Validation, out GraphScorePlan plan, out failureReason))
            {
                return false;
            }

            if (!GraphExecutor.TryExecutePrevalidatedValidation(
                    _world,
                    actor,
                    target,
                    targetPosCm,
                    plan.Program,
                    _api,
                    ref budget,
                    out passed))
            {
                passed = false;
                failureReason = GraphScoreFailureReason.BudgetExhausted;
                return false;
            }

            failureReason = GraphScoreFailureReason.None;
            return true;
        }

        private void RequireGraph(int graphId, GraphKind expected, string path)
        {
            if (!TryGetPlan(graphId, expected, out _, out GraphScoreFailureReason failure))
            {
                string reason = failure switch
                {
                    GraphScoreFailureReason.UnknownGraph => $"references unknown graph id {graphId}",
                    GraphScoreFailureReason.WrongKind => $"references graph id {graphId} with the wrong graph kind; expected {expected}",
                    _ => $"references invalid graph id {graphId}"
                };
                throw new InvalidOperationException($"{path}: {reason}.");
            }
        }

        private bool TryGetPlan(
            int graphId,
            GraphKind expected,
            out GraphScorePlan plan,
            out GraphScoreFailureReason failureReason)
        {
            plan = default;
            if (graphId <= 0 || (uint)graphId >= (uint)_plans.Length || !_plans[graphId].IsRegistered)
            {
                failureReason = GraphScoreFailureReason.UnknownGraph;
                return false;
            }

            plan = _plans[graphId];
            if (plan.Kind != expected)
            {
                failureReason = GraphScoreFailureReason.WrongKind;
                return false;
            }

            failureReason = GraphScoreFailureReason.None;
            return true;
        }

        private readonly struct GraphScorePlan
        {
            public GraphScorePlan(GraphKind kind, GraphInstruction[] program)
            {
                Kind = kind;
                Program = program ?? Array.Empty<GraphInstruction>();
            }

            public GraphKind Kind { get; }
            public GraphInstruction[] Program { get; }
            public bool IsRegistered => Kind != GraphKind.None && Program != null;
        }
    }
}
