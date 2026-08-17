using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class ScriptNodeDriver : IGraphOpsNodeDriver
{
    public const string ConstSevenCalleeFile = "_constSevenCallee.json";
    public const string ConstSevenCalleeGraphKey = "showcase.graph_op._constSevenCallee";
    private const int SliceBudget = 64;
    private const float TeaCupOffsetX = 2.2f;
    private const float TeaCupRadius = 1.1f;
    private const float TeaCellBottomOffset = 0.62f;
    private const float TeaCellSpacing = 0.55f;
    private const float TeaCellHalf = 0.21f;

    // P5 scroll geometry: world-space right side, rows go down from the anchor.
    private const float ScrollAnchorX = 7.5f;
    private const float ScrollAnchorY = 4.5f;
    private const float ScrollRowWidth = 3.2f;
    private const float ScrollRowHeight = 0.45f;
    private const float ScrollRowPitch = 0.55f;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private readonly GraphProgramRegistry _programs = new();
    private readonly List<ScrollRow> _scrollRows = new();
    private readonly Dictionary<string, int> _rowOfNodeId = new(StringComparer.Ordinal);
    private readonly List<ScrollArrow> _scrollArrows = new();
    private readonly HashSet<string> _visitedNodeIds = new(StringComparer.Ordinal);
    private GraphExecutionCursor _cursor;
    private bool _seeded;
    private bool _sawYield;
    private float _originX;
    private float _originY;
    private int _calleeGraphId;
    private byte _moveSrcReg;
    private byte _moveDstReg;

    private enum ScrollArrowKind
    {
        Next,
        BranchTrue,
        BranchFalse,
        Jump,
        Call,
        Return,
        Invoke
    }

    private sealed class ScrollRow
    {
        public required string NodeId;
        public required float Y;
        public required int FirstPc;
        public bool Featured;
        public bool Skipped;
    }

    private readonly record struct ScrollArrow(int From, int To, ScrollArrowKind Kind);

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        HideSoloOpHud(ctx);
        if (!_seeded)
        {
            int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
            _originX = ctx.Vignette.Actors[caster].X;
            _originY = ctx.Vignette.Actors[caster].Y;
            ResetSlice();
            if (IsInvokeScript(ctx))
            {
                RegisterConstSevenCallee(ctx);
                ctx.Programs = _programs;
            }

            BuildScroll(ctx);
            ctx.Metrics.AgentCount = ctx.Vignette.Actors.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
            _seeded = true;
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (_cursor.Status != GraphExecutionStatus.Halted)
        {
            GraphSliceResult result = ExecuteFeaturedSlice(ctx);
            if (result.Yielded)
            {
                _sawYield = true;
            }
            else if (result.Halted)
            {
                if (IsYieldOp(ctx) && !_sawYield)
                {
                    throw new InvalidOperationException(
                        "Yield gallery halted without GraphExecutionStatus.Yielded; the featured Yield never ran.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Script slice for {ctx.Vignette.Op} returned status {result.Status}; increase budget.");
            }

            TrackVisited(ctx);
        }

        ApplyBeat(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (IsDrinkOp(ctx))
        {
            DrawTeaCup(ctx, debugDraw);
        }

        DrawScroll(ctx, debugDraw);

        if (IsPatrolOp(ctx))
        {
            DrawPostStation(ctx, debugDraw);
        }

        if (IsInvokeScript(ctx))
        {
            DrawDualScroll(ctx, debugDraw);
        }

        if (IsMoveInt(ctx))
        {
            DrawRegisterPanel(ctx, debugDraw);
        }

        if (IsHaltReturnInt(ctx))
        {
            DrawAnswerTray(ctx, debugDraw);
        }

        if (IsYieldOp(ctx))
        {
            DrawYieldGhosts(ctx, debugDraw);
        }
    }

    private void DrawTeaCup(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0)
        {
            return;
        }

        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float cupX = actor.X + TeaCupOffsetX;
        float cupY = actor.Y;
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            cupX,
            cupY,
            TeaCupRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            GraphShowcaseStagePresenter.GhostColor);

        int limit = actor.HealthMax > 0f ? (int)actor.HealthMax : 0;
        int water = Math.Clamp(_ints[0], 0, limit);
        DebugDrawColor fill = _cursor.Status == GraphExecutionStatus.Halted
            ? DebugDrawColor.Green
            : DebugDrawColor.Cyan;
        for (int i = 0; i < water; i++)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(cupX, cupY - TeaCellBottomOffset + i * TeaCellSpacing),
                HalfWidth = TeaCellHalf,
                HalfHeight = TeaCellHalf,
                Thickness = TeaCellHalf * 2f,
                Color = fill
            });
        }
    }

    // ── P5 scroll ────────────────────────────────────────────────────────────

    private void BuildScroll(GraphOpsNodeDriverContext ctx)
    {
        _scrollRows.Clear();
        _rowOfNodeId.Clear();
        _scrollArrows.Clear();
        _visitedNodeIds.Clear();

        GraphInstruction[] program = ctx.Compiled.Program;
        GraphInstructionSourceMap map = ctx.Compiled.SourceMap;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int pc = 0; pc < program.Length; pc++)
        {
            if (!map.TryGetSource(pc, out GraphInstructionSource src) || string.IsNullOrWhiteSpace(src.NodeId))
            {
                continue;
            }

            if (!seen.Add(src.NodeId))
            {
                continue;
            }

            _rowOfNodeId[src.NodeId] = _scrollRows.Count;
            _scrollRows.Add(new ScrollRow
            {
                NodeId = src.NodeId,
                Y = _originY + ScrollAnchorY - _scrollRows.Count * ScrollRowPitch,
                FirstPc = pc,
                Featured = string.Equals(src.NodeId, ctx.Vignette.FeaturedNodeId, StringComparison.Ordinal)
            });
        }

        int callPc = -1;
        for (int pc = 0; pc < program.Length; pc++)
        {
            if (program[pc].Op == (ushort)GraphNodeOp.Call)
            {
                callPc = pc;
                break;
            }
        }

        for (int pc = 0; pc < program.Length; pc++)
        {
            if (!map.TryGetSource(pc, out GraphInstructionSource src) ||
                !_rowOfNodeId.TryGetValue(src.NodeId, out int from))
            {
                continue;
            }

            GraphInstruction ins = program[pc];
            switch ((GraphNodeOp)ins.Op)
            {
                case GraphNodeOp.Jump:
                {
                    int target = pc + 1 + ins.Imm;
                    if (target >= program.Length ||
                        !map.TryGetSource(target, out GraphInstructionSource ts) ||
                        !_rowOfNodeId.TryGetValue(ts.NodeId, out int to))
                    {
                        break;
                    }

                    ScrollArrowKind kind = src.ControlPort switch
                    {
                        GraphControlFlowPorts.Next => ScrollArrowKind.Next,
                        GraphControlFlowPorts.True => ScrollArrowKind.BranchTrue,
                        GraphControlFlowPorts.False => ScrollArrowKind.BranchFalse,
                        GraphControlFlowPorts.Target => ScrollArrowKind.Jump,
                        _ => ScrollArrowKind.Next
                    };
                    if (kind == ScrollArrowKind.Next && to == from)
                    {
                        break;
                    }

                    _scrollArrows.Add(new ScrollArrow(from, to, kind));
                    break;
                }

                case GraphNodeOp.JumpIfFalse:
                {
                    int falseTarget = pc + 1 + ins.Imm;
                    if (falseTarget < program.Length &&
                        map.TryGetSource(falseTarget, out GraphInstructionSource fs) &&
                        _rowOfNodeId.TryGetValue(fs.NodeId, out int fto))
                    {
                        _scrollArrows.Add(new ScrollArrow(from, fto, ScrollArrowKind.BranchFalse));
                    }

                    // The true arm is the emitted Jump right after JumpIfFalse (port "true");
                    // the Jump case above adds that arrow, so no duplicate here.
                    break;
                }

                case GraphNodeOp.Call:
                {
                    int target = ins.Imm;
                    if (target < program.Length &&
                        map.TryGetSource(target, out GraphInstructionSource cs) &&
                        _rowOfNodeId.TryGetValue(cs.NodeId, out int cto))
                    {
                        _scrollArrows.Add(new ScrollArrow(from, cto, ScrollArrowKind.Call));
                    }

                    break;
                }

                case GraphNodeOp.Return:
                {
                    if (callPc >= 0)
                    {
                        int nextJumpPc = callPc + 1;
                        if (nextJumpPc < program.Length && program[nextJumpPc].Op == (ushort)GraphNodeOp.Jump)
                        {
                            int home = nextJumpPc + 1 + program[nextJumpPc].Imm;
                            if (home < program.Length &&
                                map.TryGetSource(home, out GraphInstructionSource hs) &&
                                _rowOfNodeId.TryGetValue(hs.NodeId, out int hto))
                            {
                                _scrollArrows.Add(new ScrollArrow(from, hto, ScrollArrowKind.Return));
                            }
                        }
                    }

                    break;
                }

                case GraphNodeOp.InvokeScript:
                    _calleeGraphId = ins.Imm;
                    break;

                case GraphNodeOp.MoveInt:
                    if (string.Equals(src.NodeId, ctx.Vignette.FeaturedNodeId, StringComparison.Ordinal))
                    {
                        _moveSrcReg = ins.A;
                        _moveDstReg = ins.Dst;
                    }

                    break;
            }
        }

        foreach (ScrollArrow arrow in _scrollArrows)
        {
            if (arrow.Kind != ScrollArrowKind.Jump)
            {
                continue;
            }

            for (int r = arrow.From + 1; r < arrow.To; r++)
            {
                _scrollRows[r].Skipped = true;
            }
        }
    }

    private void TrackVisited(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Compiled.SourceMap.TryGetSource(_cursor.Pc, out GraphInstructionSource src) &&
            !string.IsNullOrWhiteSpace(src.NodeId))
        {
            _visitedNodeIds.Add(src.NodeId);
        }
    }

    private void DrawScroll(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (_scrollRows.Count == 0)
        {
            return;
        }

        float anchorX = _originX + ScrollAnchorX;
        bool halted = _cursor.Status == GraphExecutionStatus.Halted;
        string? current = null;
        if (!halted && ctx.Compiled.SourceMap.TryGetSource(_cursor.Pc, out GraphInstructionSource cur))
        {
            current = cur.NodeId;
        }

        for (int i = 0; i < _scrollRows.Count; i++)
        {
            ScrollRow row = _scrollRows[i];
            bool isCurrent = string.Equals(row.NodeId, current, StringComparison.Ordinal);
            bool visited = halted || row.FirstPc <= _cursor.Pc || _visitedNodeIds.Contains(row.NodeId);
            DrawScrollRow(debugDraw, anchorX, row.Y, isCurrent, visited, row.Skipped, row.Featured);
        }

        DrawScrollArrows(ctx, debugDraw, anchorX, halted);
        if (halted)
        {
            DrawClosingBar(ctx, debugDraw, anchorX);
        }
    }

    private static void DrawScrollRow(
        DebugDrawCommandBuffer debugDraw,
        float cx,
        float cy,
        bool isCurrent,
        bool visited,
        bool skipped,
        bool featured)
    {
        float hw = ScrollRowWidth * 0.5f;
        float hh = ScrollRowHeight * 0.5f;

        if (skipped)
        {
            DrawBoxFrame(debugDraw, cx, cy, hw, hh, 0.04f, GraphShowcaseStagePresenter.GhostColor);
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(cx - hw * 0.75f, cy),
                B = new Vector2(cx + hw * 0.75f, cy),
                Thickness = 0.03f,
                Color = GraphShowcaseStagePresenter.GhostColor
            });
        }
        else if (isCurrent)
        {
            DrawBoxFrame(debugDraw, cx, cy, hw, hh, 0.16f, GraphShowcaseStagePresenter.CasterColor);
        }
        else if (visited)
        {
            DrawBoxFrame(debugDraw, cx, cy, hw, hh, 0.08f, GraphShowcaseStagePresenter.CrowdColor);
        }
        else
        {
            DrawBoxFrame(debugDraw, cx, cy, hw, hh, 0.03f, GraphShowcaseStagePresenter.GhostColor);
        }

        if (featured)
        {
            DrawBoxFrame(debugDraw, cx, cy, hw + 0.12f, hh + 0.12f, 0.06f, GraphShowcaseStagePresenter.GateColor);
        }
    }

    private void DrawScrollArrows(
        GraphOpsNodeDriverContext ctx,
        DebugDrawCommandBuffer debugDraw,
        float anchorX,
        bool halted)
    {
        float rowRight = anchorX + ScrollRowWidth * 0.5f;
        float rowLeft = anchorX - ScrollRowWidth * 0.5f;
        int water = _ints[0];
        int limit = casterHealthMax(ctx);
        bool away = IsAway(ctx);

        foreach (ScrollArrow arrow in _scrollArrows)
        {
            if (arrow.From >= _scrollRows.Count || arrow.To >= _scrollRows.Count)
            {
                continue;
            }

            if (arrow.Kind == ScrollArrowKind.Next)
            {
                DrawSequentialTick(debugDraw, anchorX, _scrollRows[arrow.From].Y, _scrollRows[arrow.To].Y);
                continue;
            }

            if (!ShowsArrowKind(ctx, arrow.Kind))
            {
                continue;
            }

            (bool taken, DebugDrawColor color) = ArrowTaken(ctx, arrow.Kind, water, limit, away, halted);
            float fromY = _scrollRows[arrow.From].Y;
            float toY = _scrollRows[arrow.To].Y;
            float thickness = taken ? 0.14f : 0.04f;
            if (arrow.Kind == ScrollArrowKind.Return)
            {
                DrawLaneArrow(debugDraw, rowLeft, rowLeft - 0.55f, fromY, toY, thickness, color);
            }
            else
            {
                DrawLaneArrow(debugDraw, rowRight, rowRight + 0.55f, fromY, toY, thickness, color);
            }
        }
    }

    private static bool ShowsArrowKind(GraphOpsNodeDriverContext ctx, ScrollArrowKind kind)
    {
        return ctx.Vignette.Op switch
        {
            nameof(GraphNodeOp.Jump) => kind == ScrollArrowKind.Jump,
            nameof(GraphNodeOp.JumpIfFalse) or nameof(GraphNodeOp.Yield) =>
                kind is ScrollArrowKind.BranchTrue or ScrollArrowKind.BranchFalse,
            nameof(GraphNodeOp.Call) or nameof(GraphNodeOp.Return) =>
                kind is ScrollArrowKind.Call or ScrollArrowKind.Return,
            _ => false
        };
    }

    private static (bool Taken, DebugDrawColor Color) ArrowTaken(
        GraphOpsNodeDriverContext ctx,
        ScrollArrowKind kind,
        int water,
        int limit,
        bool away,
        bool halted)
    {
        switch (kind)
        {
            case ScrollArrowKind.Jump:
                return (true, GraphShowcaseStagePresenter.CasterColor);
            case ScrollArrowKind.BranchTrue:
                return water < limit
                    ? (true, GraphShowcaseStagePresenter.GuardColor)
                    : (false, GraphShowcaseStagePresenter.GhostColor);
            case ScrollArrowKind.BranchFalse:
                return water >= limit
                    ? (true, GraphShowcaseStagePresenter.CasterColor)
                    : (false, GraphShowcaseStagePresenter.GhostColor);
            case ScrollArrowKind.Call:
                return away
                    ? (true, GraphShowcaseStagePresenter.CasterColor)
                    : (false, GraphShowcaseStagePresenter.GhostColor);
            case ScrollArrowKind.Return:
                return halted
                    ? (true, GraphShowcaseStagePresenter.CasterColor)
                    : (false, GraphShowcaseStagePresenter.GhostColor);
            default:
                return (true, GraphShowcaseStagePresenter.CasterColor);
        }
    }

    private static void DrawSequentialTick(DebugDrawCommandBuffer debugDraw, float x, float fromY, float toY)
    {
        float gap = ScrollRowPitch - ScrollRowHeight;
        float dir = toY < fromY ? -1f : 1f;
        float start = fromY + dir * ScrollRowHeight * 0.5f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x, start),
            B = new Vector2(x, start + dir * gap),
            Thickness = 0.03f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
    }

    private static void DrawLaneArrow(
        DebugDrawCommandBuffer debugDraw,
        float rowEdgeX,
        float laneX,
        float fromY,
        float toY,
        float thickness,
        DebugDrawColor color)
    {
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, rowEdgeX, fromY, laneX, fromY, thickness, color, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, laneX, fromY, laneX, toY, thickness, color, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, laneX, toY, rowEdgeX, toY, thickness, color);
    }

    private void DrawClosingBar(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, float anchorX)
    {
        float y = _scrollRows[^1].Y - ScrollRowPitch * 0.6f;
        float half = ScrollRowWidth * 0.5f;
        float sweep = (ctx.Wave % 4) * (ScrollRowWidth / 4f);
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(anchorX - half, y),
            B = new Vector2(anchorX - half + sweep, y),
            Thickness = 0.18f,
            Color = GraphShowcaseStagePresenter.CrowdColor
        });
    }

    // ── Call / Return 驿站台 ──────────────────────────────────────────────────

    private void DrawPostStation(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "ally");
        if (ally < 0)
        {
            return;
        }

        GraphOpsNodeActor station = ctx.Vignette.Actors[ally];
        bool away = IsAway(ctx);

        DrawBidirectionalRoad(debugDraw, station, away);

        DrawSignpost(debugDraw, station.X + 1.1f, station.Y);

        float ghostX = away ? _originX : station.X;
        float ghostY = away ? _originY : station.Y;
        GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, ghostX, ghostY, 0.55f, GraphShowcaseStagePresenter.GhostColor);
    }

    private void DrawBidirectionalRoad(DebugDrawCommandBuffer debugDraw, GraphOpsNodeActor station, bool away)
    {
        float offset = 0.35f;
        DebugDrawColor outbound = away ? GraphShowcaseStagePresenter.CasterColor : GraphShowcaseStagePresenter.GhostColor;
        DebugDrawColor inbound = away ? GraphShowcaseStagePresenter.GhostColor : GraphShowcaseStagePresenter.CasterColor;
        float outThickness = away ? 0.16f : 0.05f;
        float inThickness = away ? 0.05f : 0.16f;

        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            _originX, _originY + offset,
            station.X, station.Y + offset,
            outThickness, outbound, arrowStart: false, arrowEnd: true);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            station.X, station.Y - offset,
            _originX, _originY - offset,
            inThickness, inbound, arrowStart: false, arrowEnd: true);
    }

    private static void DrawSignpost(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        const float poleTop = 1.05f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x, y - 0.45f),
            B = new Vector2(x, y + poleTop),
            Thickness = 0.09f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.5f, y + poleTop),
            B = new Vector2(x + 0.5f, y + poleTop),
            Thickness = 0.05f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.5f, y + poleTop),
            B = new Vector2(x - 0.5f, y + poleTop + 0.5f),
            Thickness = 0.05f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x + 0.5f, y + poleTop),
            B = new Vector2(x + 0.5f, y + poleTop + 0.5f),
            Thickness = 0.05f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.5f, y + poleTop + 0.5f),
            B = new Vector2(x + 0.5f, y + poleTop + 0.5f),
            Thickness = 0.05f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y + poleTop + 0.25f),
            Radius = 0.14f,
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GuardColor
        });
    }

    // ── InvokeScript 双卷轴 ──────────────────────────────────────────────────

    private void DrawDualScroll(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (_calleeGraphId <= 0 ||
            !_programs.TryGetSourceMap(_calleeGraphId, out GraphInstructionSourceMap calleeMap))
        {
            return;
        }

        float anchorX = _originX + ScrollAnchorX;
        var smallRows = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (GraphInstructionSource src in calleeMap.Sources)
        {
            if (string.IsNullOrWhiteSpace(src.NodeId) || !seen.Add(src.NodeId))
            {
                continue;
            }

            smallRows.Add(src.NodeId);
        }

        if (smallRows.Count == 0)
        {
            return;
        }

        const float smallPitch = 0.45f;
        const float smallWidth = 2.2f;
        float smallX = anchorX + ScrollRowWidth * 0.5f + 1.4f;
        float smallTopY = _originY + ScrollAnchorY - 1.2f;
        for (int i = 0; i < smallRows.Count; i++)
        {
            float y = smallTopY - i * smallPitch;
            DrawBoxFrame(
                debugDraw, smallX, y, smallWidth * 0.5f, ScrollRowHeight * 0.45f,
                0.06f, GraphShowcaseStagePresenter.CasterColor);
        }

        if (_rowOfNodeId.TryGetValue("invoke", out int invokeRow))
        {
            float fromY = _scrollRows[invokeRow].Y;
            DrawLaneArrow(
                debugDraw,
                anchorX + ScrollRowWidth * 0.5f,
                smallX - smallWidth * 0.5f,
                fromY,
                smallTopY,
                0.12f,
                GraphShowcaseStagePresenter.CasterColor);
        }

        if (_rowOfNodeId.TryGetValue("done", out int doneRow))
        {
            float doneY = _scrollRows[doneRow].Y;
            float smallBottom = smallTopY - (smallRows.Count - 1) * smallPitch;
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                smallX, smallBottom,
                anchorX + ScrollRowWidth * 0.5f + 0.9f, doneY,
                0.07f, DebugDrawColor.Cyan, arrowStart: false, arrowEnd: true);
            for (int i = 0; i < 7; i++)
            {
                debugDraw.Boxes.Add(new DebugDrawBox2D
                {
                    Center = new Vector2(anchorX + ScrollRowWidth * 0.5f + 0.45f + i * 0.3f, doneY),
                    HalfWidth = 0.11f,
                    HalfHeight = 0.11f,
                    Thickness = 0.05f,
                    Color = DebugDrawColor.Cyan
                });
            }
        }
    }

    // ── MoveInt 寄存器面板 ────────────────────────────────────────────────────

    private void DrawRegisterPanel(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        float boxW = 1.4f;
        float boxH = 1.0f;
        float leftX = _originX + 1.6f;
        float rightX = leftX + boxW + 0.5f;
        float cy = _originY + 0.5f;

        DrawBoxFrame(debugDraw, leftX, cy, boxW * 0.5f, boxH * 0.5f, 0.1f, GraphShowcaseStagePresenter.GateColor);
        DrawBoxFrame(debugDraw, rightX, cy, boxW * 0.5f, boxH * 0.5f, 0.1f, GraphShowcaseStagePresenter.GateColor);

        int srcPips = _moveSrcReg < _ints.Length ? _ints[_moveSrcReg] : 0;
        int dstPips = _moveDstReg < _ints.Length ? _ints[_moveDstReg] : 0;
        DrawPipRow(debugDraw, leftX, cy, srcPips);
        DrawPipRow(debugDraw, rightX, cy, dstPips);
    }

    private static void DrawPipRow(DebugDrawCommandBuffer debugDraw, float cx, float cy, int count)
    {
        for (int i = 0; i < count; i++)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(cx + (i - (count - 1) * 0.5f) * 0.32f, cy),
                HalfWidth = 0.12f,
                HalfHeight = 0.12f,
                Thickness = 0.06f,
                Color = DebugDrawColor.Cyan
            });
        }
    }

    // ── HaltReturnInt 答案托盘 ───────────────────────────────────────────────

    private void DrawAnswerTray(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int count = _cursor.ReturnInt;
        float cx = _originX + 1.4f;
        float cy = _originY + 0.4f;
        float w = Math.Max(1.2f, 0.35f * count);
        DrawBoxFrame(debugDraw, cx, cy, w * 0.5f, 0.28f, 0.1f, GraphShowcaseStagePresenter.GateColor);
        DebugDrawColor color = _cursor.Status == GraphExecutionStatus.Halted
            ? DebugDrawColor.Green
            : DebugDrawColor.Cyan;
        for (int i = 0; i < count; i++)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(cx + (i - (count - 1) * 0.5f) * 0.32f, cy),
                HalfWidth = 0.11f,
                HalfHeight = 0.11f,
                Thickness = 0.05f,
                Color = color
            });
        }
    }

    // ── Yield 残影连拍 ───────────────────────────────────────────────────────

    private void DrawYieldGhosts(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (_cursor.Status != GraphExecutionStatus.Yielded)
        {
            return;
        }

        float baseX = _originX + 0.4f;
        float baseY = _originY + 0.4f;
        byte[] alphas = [200, 140, 80];
        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            float x = baseX + MathF.Cos(MathF.PI * (0.15f + t * 0.7f)) * 1.1f;
            float y = baseY + MathF.Sin(MathF.PI * (0.15f + t * 0.7f)) * 0.5f;
            GraphShowcaseStagePresenter.DrawGhostCircle(
                debugDraw, x, y, 0.3f, new DebugDrawColor(150, 150, 150, alphas[i]));
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private int casterHealthMax(GraphOpsNodeDriverContext ctx)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        return caster >= 0 ? (int)ctx.Vignette.Actors[caster].HealthMax : 0;
    }

    private static void DrawBoxFrame(
        DebugDrawCommandBuffer debugDraw,
        float cx,
        float cy,
        float hw,
        float hh,
        float thickness,
        DebugDrawColor color)
    {
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(cx - hw, cy - hh),
            B = new Vector2(cx + hw, cy - hh),
            Thickness = thickness,
            Color = color
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(cx + hw, cy - hh),
            B = new Vector2(cx + hw, cy + hh),
            Thickness = thickness,
            Color = color
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(cx + hw, cy + hh),
            B = new Vector2(cx - hw, cy + hh),
            Thickness = thickness,
            Color = color
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(cx - hw, cy + hh),
            B = new Vector2(cx - hw, cy - hh),
            Thickness = thickness,
            Color = color
        });
    }

    private GraphSliceResult ExecuteFeaturedSlice(GraphOpsNodeDriverContext ctx)
    {
        var targetList = new GraphTargetList(_targets);
        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = ctx.Caster,
            ExplicitTarget = ctx.Target,
            Api = ctx.Api,
            Programs = _programs,
            F = _floats,
            I = _ints,
            B = _bools,
            E = _entities,
            Targets = _targets,
            TargetList = targetList,
            CallStack = _callStack,
            CallStackCount = _cursor.CallStackCount,
            ReturnInt = _cursor.ReturnInt,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)ctx.Wave),
            Status = GraphExecutionStatus.Running
        };

        return GasGraphOpHandlerTable.ExecuteSlice(
            ref state,
            ctx.Compiled.Program,
            GasGraphOpHandlerTable.Instance,
            ref _cursor,
            SliceBudget);
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "ally");
        int water = _ints[0];
        int limit = caster >= 0 ? (int)ctx.Vignette.Actors[caster].HealthMax : 0;
        int result = _cursor.ReturnInt;

        if (IsDrinkOp(ctx) && caster >= 0)
        {
            ctx.Vignette.Actors[caster].X = _originX;
            ctx.Vignette.Actors[caster].Y = _originY;
        }
        else if (IsPatrolOp(ctx) && caster >= 0)
        {
            if (_cursor.Status == GraphExecutionStatus.Yielded && ally >= 0)
            {
                ctx.Vignette.Actors[caster].X = ctx.Vignette.Actors[ally].X;
                ctx.Vignette.Actors[caster].Y = ctx.Vignette.Actors[ally].Y;
            }
            else
            {
                ctx.Vignette.Actors[caster].X = _originX;
                ctx.Vignette.Actors[caster].Y = _originY;
            }
        }
        else if (IsHaltReturnInt(ctx) && caster >= 0 && _cursor.Status == GraphExecutionStatus.Halted)
        {
            ctx.Vignette.Actors[caster].X = _originX + 0.6f;
            ctx.Vignette.Actors[caster].Y = _originY;
        }

        ctx.CaptionValues["water"] = water.ToString();
        ctx.CaptionValues["limit"] = limit.ToString();
        ctx.CaptionValues["result"] = result.ToString();
        ctx.CaptionValues["tea"] = "茶水";
        ctx.CaptionValues["place"] = IsAway(ctx) ? "驿站" : "原点";
        ctx.CaptionValues["homeState"] = IsAway(ctx) ? "空着" : "有人";
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
    }

    private bool IsAway(GraphOpsNodeDriverContext ctx)
        => IsPatrolOp(ctx) && _cursor.Status == GraphExecutionStatus.Yielded;

    private void RegisterConstSevenCallee(GraphOpsNodeDriverContext ctx)
    {
        int calleeId = ResolveCalleeGraphId(ctx.Compiled);
        string path = Path.Combine(ctx.AssetsRoot, "GAS", "graphs", ConstSevenCalleeFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"InvokeScript gallery requires callee graph {ConstSevenCalleeFile}.",
                path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = GraphOpsNodeGraphCompiler.ParseSingleGraphShard(path);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            obj,
            ConstSevenCalleeGraphKey,
            options);
        if (!compiled.Succeeded)
        {
            string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"FrontDoor compile failed for InvokeScript callee: {message}");
        }

        GraphKindOperationPolicy.RequireAllowed(GraphKind.Script, compiled.Program, GasGraphOpHandlerTable.Instance);
        _programs.Register(calleeId, compiled.Program, GraphKind.Script, compiled.SourceMap);
        if (!_programs.TryGetProgram(calleeId, out _))
        {
            throw new InvalidOperationException($"InvokeScript callee graph id {calleeId} is not registered.");
        }
    }

    /// <summary>
    /// Resolves the callee graph ID for the gallery's InvokeScript demo.
    /// When the authored node uses functionName (FuncLib path), allocates a local callee ID
    /// and patches the instruction so the local registry can serve the call.
    /// </summary>
    private int ResolveCalleeGraphId(GraphControlFlowCompileResult compiled)
    {
        GraphInstruction[] program = compiled.Program;
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op != (ushort)GraphNodeOp.InvokeScript)
            {
                continue;
            }

            if ((program[i].Flags & GraphInstructionFlags.FuncLibName) != 0)
            {
                int calleeId = GraphIdRegistry.GetId(ConstSevenCalleeGraphKey);
                if (calleeId <= 0)
                {
                    calleeId = GraphIdRegistry.Register(ConstSevenCalleeGraphKey);
                }

                program[i].Imm = calleeId;
                program[i].Flags = (byte)(program[i].Flags & ~GraphInstructionFlags.FuncLibName);
                return calleeId;
            }

            if (program[i].Imm <= 0)
            {
                throw new InvalidOperationException("InvokeScript Imm must be a positive callee graph id.");
            }

            return program[i].Imm;
        }

        throw new InvalidOperationException("InvokeScript graph is missing the featured InvokeScript instruction.");
    }

    private void ResetSlice()
    {
        _cursor.Reset();
        Array.Clear(_floats, 0, _floats.Length);
        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_callStack, 0, _callStack.Length);
        _sawYield = false;
        _visitedNodeIds.Clear();
    }

    private static bool IsYieldOp(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.Yield), StringComparison.Ordinal);

    private static bool IsInvokeScript(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.InvokeScript), StringComparison.Ordinal);

    private static bool IsMoveInt(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.MoveInt), StringComparison.Ordinal);

    private static bool IsHaltReturnInt(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.HaltReturnInt), StringComparison.Ordinal);

    private static bool IsDrinkOp(GraphOpsNodeDriverContext ctx)
        => IsDrinkOp(ctx.Vignette.Op);

    private static bool IsDrinkOp(string op)
        => op is nameof(GraphNodeOp.Jump)
            or nameof(GraphNodeOp.JumpIfFalse)
            or nameof(GraphNodeOp.Yield);

    private static bool IsPatrolOp(GraphOpsNodeDriverContext ctx)
        => ctx.Vignette.Op is nameof(GraphNodeOp.Call) or nameof(GraphNodeOp.Return);

    private static void HideSoloOpHud(GraphOpsNodeDriverContext ctx)
    {
        if (!HidesCasterHud(ctx.Vignette.Op))
        {
            return;
        }

        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster >= 0)
        {
            GraphOpsNodeActorBinding.SetHudLit(ctx, caster, false);
        }
    }

    private static bool HidesCasterHud(string op)
        => IsDrinkOp(op)
            || op is nameof(GraphNodeOp.MoveInt)
                or nameof(GraphNodeOp.HaltReturnInt)
                or nameof(GraphNodeOp.InvokeScript);
}
