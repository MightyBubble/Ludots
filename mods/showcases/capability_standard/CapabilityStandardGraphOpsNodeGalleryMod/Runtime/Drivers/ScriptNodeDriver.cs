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

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private readonly GraphProgramRegistry _programs = new();
    private GraphExecutionCursor _cursor;
    private bool _seeded;
    private bool _sawYield;
    private float _originX;
    private float _originY;

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

        if (!IsPatrolOp(ctx))
        {
            return;
        }

        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "ally");
        if (ally < 0)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw,
            [
                new Vector2(_originX, _originY),
                new Vector2(ctx.Vignette.Actors[ally].X, ctx.Vignette.Actors[ally].Y)
            ],
            GraphShowcaseStagePresenter.PathColor);
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

        ctx.CaptionValues["water"] = water.ToString();
        ctx.CaptionValues["limit"] = limit.ToString();
        ctx.CaptionValues["result"] = result.ToString();
        ctx.CaptionValues["tea"] = "茶水";
        ctx.CaptionValues["place"] = IsAway(ctx) ? "驿站" : "原点";
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
    }

    private static bool IsYieldOp(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.Yield), StringComparison.Ordinal);

    private static bool IsInvokeScript(GraphOpsNodeDriverContext ctx)
        => string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.InvokeScript), StringComparison.Ordinal);

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
