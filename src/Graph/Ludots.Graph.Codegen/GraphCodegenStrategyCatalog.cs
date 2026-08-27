using System;
using System.Collections.Generic;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Graph.Codegen
{
    public enum GraphCodegenEmitKind : byte
    {
        Exempt = 0,
        Specialize = 1,
        HandlerForward = 2,
    }

    public enum GraphCodegenFamily : byte
    {
        Exempt = 0,
        F0 = 1,
        F1 = 2,
        F2 = 3,
        F3 = 4,
        F4 = 5,
        F5 = 6,
        F6 = 7,
        F7 = 8,
        F8 = 9,
        F9 = 10,
        F10 = 11,
    }

    public readonly record struct GraphCodegenStrategy(
        GraphNodeOp Op,
        GraphCodegenFamily Family,
        GraphCodegenEmitKind Kind);

    /// <summary>
    /// SSOT: every executable <see cref="GraphNodeOp"/> has exactly one emit strategy.
    /// Specialize covers F0–F3 hot paths; everything else is HandlerForward (same handler table).
    /// </summary>
    public static class GraphCodegenStrategyCatalog
    {
        private static readonly Dictionary<GraphNodeOp, GraphCodegenStrategy> Strategies = Build();

        public static IReadOnlyDictionary<GraphNodeOp, GraphCodegenStrategy> All => Strategies;

        public static bool TryGet(GraphNodeOp op, out GraphCodegenStrategy strategy) =>
            Strategies.TryGetValue(op, out strategy);

        public static GraphCodegenStrategy Require(GraphNodeOp op)
        {
            if (!Strategies.TryGetValue(op, out GraphCodegenStrategy strategy))
            {
                throw new InvalidOperationException(
                    $"Graph codegen has no emit strategy for op '{op}'. Add it to GraphCodegenStrategyCatalog.");
            }

            return strategy;
        }

        public static bool IsSpecializeCapable(GraphNodeOp op) =>
            TryGet(op, out GraphCodegenStrategy strategy) &&
            strategy.Kind == GraphCodegenEmitKind.Specialize;

        private static Dictionary<GraphNodeOp, GraphCodegenStrategy> Build()
        {
            var map = new Dictionary<GraphNodeOp, GraphCodegenStrategy>();

            void Add(GraphNodeOp op, GraphCodegenFamily family, GraphCodegenEmitKind kind) =>
                map[op] = new GraphCodegenStrategy(op, family, kind);

            Add(GraphNodeOp.None, GraphCodegenFamily.Exempt, GraphCodegenEmitKind.Exempt);

            // F0
            Add(GraphNodeOp.ConstInt, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.AddInt, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.CompareLtInt, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.CompareEqInt, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.Jump, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.JumpIfFalse, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.HaltReturnInt, GraphCodegenFamily.F0, GraphCodegenEmitKind.Specialize);

            // F1
            Add(GraphNodeOp.ConstBool, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.ConstFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.MoveInt, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.AddFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.MulFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.SubFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.DivFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.MinFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.MaxFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.ClampFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.AbsFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.NegFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.CompareGtFloat, GraphCodegenFamily.F1, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.CompareEqEntity, GraphCodegenFamily.F1, GraphCodegenEmitKind.HandlerForward);
            Add(GraphNodeOp.SelectEntity, GraphCodegenFamily.F1, GraphCodegenEmitKind.HandlerForward);
            Add(GraphNodeOp.RandomFloat01, GraphCodegenFamily.F1, GraphCodegenEmitKind.HandlerForward);
            Add(GraphNodeOp.LoadCaster, GraphCodegenFamily.F1, GraphCodegenEmitKind.HandlerForward);
            Add(GraphNodeOp.LoadExplicitTarget, GraphCodegenFamily.F1, GraphCodegenEmitKind.HandlerForward);

            // F2 / F3
            Add(GraphNodeOp.ConstText, GraphCodegenFamily.F2, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.ConcatText, GraphCodegenFamily.F2, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.IntToText, GraphCodegenFamily.F2, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.FloatToText, GraphCodegenFamily.F2, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.SinkPresentationText, GraphCodegenFamily.F2, GraphCodegenEmitKind.Specialize);
            Add(GraphNodeOp.LoadTextKey, GraphCodegenFamily.F3, GraphCodegenEmitKind.Specialize);

            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (map.ContainsKey(op))
                {
                    continue;
                }

                GraphCodegenFamily family = ClassifyRemainder(op);
                Add(op, family, GraphCodegenEmitKind.HandlerForward);
            }

            return map;
        }

        private static GraphCodegenFamily ClassifyRemainder(GraphNodeOp op)
        {
            ushort code = (ushort)op;
            if (code is >= 300 and <= 331 or 10 or 33)
            {
                return GraphCodegenFamily.F4;
            }

            if (code is >= 430 and <= 435 or >= 450 and <= 455)
            {
                return GraphCodegenFamily.F9;
            }

            if (code is >= 443 and <= 446 or >= 416 and <= 429)
            {
                return GraphCodegenFamily.F5;
            }

            if (code is >= 437 and <= 442)
            {
                return GraphCodegenFamily.F6;
            }

            if (code is >= 100 and <= 132 or >= 360 and <= 397 or >= 380 and <= 396 or >= 402 and <= 412)
            {
                return GraphCodegenFamily.F7;
            }

            if (code is >= 200 and <= 220 or 401 or 447 or 448 or 449)
            {
                return GraphCodegenFamily.F8;
            }

            return GraphCodegenFamily.F10;
        }
    }
}
