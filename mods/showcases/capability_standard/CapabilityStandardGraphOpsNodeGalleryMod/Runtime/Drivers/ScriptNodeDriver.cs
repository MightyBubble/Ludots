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
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (!_seeded)
        {
            if (actors.Length == 0)
            {
                throw new InvalidOperationException($"Script vignette {ctx.Vignette.Op} requires a caster actor.");
            }

            ctx.SimActors = new Entity[actors.Length];
            ctx.ActorHealth = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                Entity entity = ctx.SimWorld.Create();
                ctx.SimActors[i] = entity;
                ctx.ActorHealth[i] = actors[i].Health;
                if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
                {
                    ctx.Caster = entity;
                    _originX = actors[i].X;
                    _originY = actors[i].Y;
                }
                else if (string.Equals(actors[i].Role, "target", StringComparison.Ordinal))
                {
                    ctx.Target = entity;
                }
            }

            if (ctx.Caster == Entity.Null)
            {
                throw new InvalidOperationException($"Script vignette {ctx.Vignette.Op} requires a caster actor.");
            }

            ResetSlice();
            if (IsInvokeScript(ctx))
            {
                RegisterConstSevenCallee(ctx);
            }

            ctx.Metrics.AgentCount = actors.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
            _seeded = true;
        }

        SpawnStage(ctx);
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
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (!IsPatrolOp(ctx))
        {
            return;
        }

        int ally = FindRole(ctx, "ally");
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
        int caster = FindRole(ctx, "caster");
        int ally = FindRole(ctx, "ally");
        int water = _ints[0];
        int limit = caster >= 0 ? (int)ctx.Vignette.Actors[caster].HealthMax : 0;
        int result = _cursor.ReturnInt;

        if (IsDrinkOp(ctx) && caster >= 0)
        {
            ctx.ActorHealth[caster] = Math.Clamp(water, 0, limit);
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
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
    }

    private bool IsAway(GraphOpsNodeDriverContext ctx)
        => IsPatrolOp(ctx) && _cursor.Status == GraphExecutionStatus.Yielded;

    private void RegisterConstSevenCallee(GraphOpsNodeDriverContext ctx)
    {
        int calleeId = RequireInvokeScriptGraphId(ctx.Compiled);
        string path = Path.Combine(ctx.AssetsRoot, "GAS", "graphs", ConstSevenCalleeFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"InvokeScript gallery requires callee graph {ConstSevenCalleeFile}.",
                path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
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

    private static int RequireInvokeScriptGraphId(GraphControlFlowCompileResult compiled)
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
                throw new InvalidOperationException(
                    "InvokeScript gallery must reference a registered graph id, not a function catalog name.");
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
        => ctx.Vignette.Op is nameof(GraphNodeOp.Jump)
            or nameof(GraphNodeOp.JumpIfFalse)
            or nameof(GraphNodeOp.Yield);

    private static bool IsPatrolOp(GraphOpsNodeDriverContext ctx)
        => ctx.Vignette.Op is nameof(GraphNodeOp.Call) or nameof(GraphNodeOp.Return);

    private static string FormatDetail(string template, Dictionary<string, string> values)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Detail template still has unsubstituted placeholders: {text}");
        }

        return text;
    }

    private static int FindRole(GraphOpsNodeDriverContext ctx, string role)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, role, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length > 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            ctx.StageProxies[i] = ctx.Stage.Spawn(
                actor.Template,
                actor.Name,
                actor.X,
                actor.Y,
                ctx.ActorHealth[i],
                actor.HealthMax);
        }
    }

    private static void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(ctx.StageProxies[i], actor.X, actor.Y);
            ctx.Stage.SetHealth(ctx.StageProxies[i], ctx.ActorHealth[i], actor.HealthMax);
        }
    }
}
