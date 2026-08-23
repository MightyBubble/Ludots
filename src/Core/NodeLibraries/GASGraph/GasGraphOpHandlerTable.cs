using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Concrete delegate for GAS Graph opcode handlers.
    /// Uses a concrete ref struct type (not generic) because .NET 8 does not support
    /// 'allows ref struct' generic constraints.
    /// </summary>
    public delegate void GasGraphOpHandler(ref GraphExecutionState state, in GraphInstruction ins, ref int pc);

    /// <summary>
    /// Opcode handler table for the GAS Graph VM.
    /// Provides a handler array consumable by <see cref="Execute"/>.
    /// </summary>
    public sealed class GasGraphOpHandlerTable
    {
        public static readonly GasGraphOpHandlerTable Instance = new();

        public GasGraphOpHandler[] Handlers { get; }

        private readonly string?[] _descriptions;
        private readonly EffectOperationMetadata[] _operationMetadata;

        private GasGraphOpHandlerTable()
        {
            Handlers = new GasGraphOpHandler[GraphVmLimits.HandlerTableSize];
            _descriptions = new string?[GraphVmLimits.HandlerTableSize];
            _operationMetadata = new EffectOperationMetadata[GraphVmLimits.HandlerTableSize];
            RegisterBuiltins();
            EnsureRegistrationComplete();
        }

        /// <summary>
        /// Builds a table with built-in opcodes and optionally installs mod-registered
        /// extension graph ops into the free opcode slots.
        /// </summary>
        public GasGraphOpHandlerTable(GasGraphOpRegistry? extensions = null)
            : this()
        {
            if (extensions != null)
            {
                InstallExtensions(extensions);
            }
        }

        /// <summary>
        /// Installs mod-registered extension graph ops into this table's free opcode slots
        /// (ids at or above <see cref="GasGraphOpRegistry.FirstModOpCode"/>). Built-in opcodes
        /// must already be registered; extension installs reject occupied slots.
        /// </summary>
        public void InstallExtensions(GasGraphOpRegistry extensions)
        {
            if (extensions == null) throw new ArgumentNullException(nameof(extensions));
            extensions.InstallHandlers(Handlers);
        }

        /// <summary>
        /// Registers an executable opcode. Requires a non-empty description and rejects duplicates.
        /// </summary>
        public void Register(GraphNodeOp op, GasGraphOpHandler handler, string description)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException(
                    $"Graph opcode '{op}' requires a non-empty description.",
                    nameof(description));
            }

            ushort code = (ushort)op;
            if (op == GraphNodeOp.None || code >= Handlers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(op), $"Graph opcode '{op}' ({code}) is not registerable.");
            }

            if (Handlers[code] != null)
            {
                throw new InvalidOperationException(
                    $"Graph opcode '{op}' ({code}) is already registered; duplicate registration is not allowed.");
            }

            Handlers[code] = handler;
            _descriptions[code] = description.Trim();
            _operationMetadata[code] = CreateOperationMetadata(op, description.Trim());
        }

        public bool TryGetDescription(GraphNodeOp op, out string description)
        {
            ushort code = (ushort)op;
            if (code < _descriptions.Length && !string.IsNullOrEmpty(_descriptions[code]))
            {
                description = _descriptions[code]!;
                return true;
            }

            description = string.Empty;
            return false;
        }

        public string GetDescription(GraphNodeOp op)
        {
            if (!TryGetDescription(op, out string description))
            {
                throw new InvalidOperationException($"Graph opcode '{op}' has no registered description.");
            }

            return description;
        }

        public bool TryGetOperationMetadata(GraphNodeOp op, out EffectOperationMetadata operationMetadata)
        {
            ushort code = (ushort)op;
            if (code < _operationMetadata.Length &&
                _operationMetadata[code].Kind != EffectOperationKind.None)
            {
                operationMetadata = _operationMetadata[code];
                return true;
            }

            operationMetadata = default;
            return false;
        }

        private void EnsureRegistrationComplete()
        {
            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (op == GraphNodeOp.None)
                {
                    continue;
                }

                ushort code = (ushort)op;
                if (Handlers[code] == null ||
                    string.IsNullOrWhiteSpace(_descriptions[code]) ||
                    _operationMetadata[code].Kind == EffectOperationKind.None)
                {
                    throw new InvalidOperationException(
                        $"Executable graph opcode '{op}' ({code}) is missing required handler/description/operation metadata registration.");
                }
            }
        }

        private static EffectOperationMetadata CreateOperationMetadata(GraphNodeOp op, string description)
        {
            return op switch
            {
                GraphNodeOp.ApplyEffectTemplate or
                GraphNodeOp.FanOutApplyEffect or
                GraphNodeOp.ApplyEffectDynamic or
                GraphNodeOp.FanOutApplyEffectDynamic or
                GraphNodeOp.RemoveEffectTemplate or
                GraphNodeOp.FanOutDispatchEffect or
                GraphNodeOp.FanOutDispatchEffectDynamic or
                GraphNodeOp.ModifyAttributeAdd or
                GraphNodeOp.SendEvent or
                GraphNodeOp.WriteBlackboardFloat or
                GraphNodeOp.WriteBlackboardInt or
                GraphNodeOp.WriteBlackboardEntity or
                GraphNodeOp.WriteSelfAttribute
                    => EffectOperationMetadata.GasTransactional(description),

                GraphNodeOp.InvokeBuiltin
                    => EffectOperationMetadata.DelegatedBuiltin(description),

                GraphNodeOp.RelationshipEnsureLink or
                GraphNodeOp.RelationshipRemoveLink or
                GraphNodeOp.RelationshipSetMetric or
                GraphNodeOp.RelationshipAddMetric or
                GraphNodeOp.RelationshipSetFlag
                    => EffectOperationMetadata.Unsupported(EffectAtomicDomain.Relationship, description),

                GraphNodeOp.BeginLifecycleTransaction
                    => EffectOperationMetadata.Unsupported(EffectAtomicDomain.Lifecycle, description),

                GraphNodeOp.ConstBool or
                GraphNodeOp.ConstInt or
                GraphNodeOp.ConstFloat or
                GraphNodeOp.LoadCaster or
                GraphNodeOp.LoadExplicitTarget or
                GraphNodeOp.Jump or
                GraphNodeOp.JumpIfFalse or
                GraphNodeOp.LoadAttribute or
                GraphNodeOp.AddFloat or
                GraphNodeOp.MulFloat or
                GraphNodeOp.SubFloat or
                GraphNodeOp.DivFloat or
                GraphNodeOp.MinFloat or
                GraphNodeOp.MaxFloat or
                GraphNodeOp.ClampFloat or
                GraphNodeOp.AbsFloat or
                GraphNodeOp.NegFloat or
                GraphNodeOp.RandomFloat01 or
                GraphNodeOp.AddInt or
                GraphNodeOp.CompareGtFloat or
                GraphNodeOp.CompareLtInt or
                GraphNodeOp.CompareEqInt or
                GraphNodeOp.HasTag or
                GraphNodeOp.CompareEqEntity or
                GraphNodeOp.SelectEntity or
                GraphNodeOp.QueryRadius or
                GraphNodeOp.QuerySortStable or
                GraphNodeOp.QueryLimit or
                GraphNodeOp.QueryCone or
                GraphNodeOp.QueryRectangle or
                GraphNodeOp.QueryLine or
                GraphNodeOp.QueryFilterNotEntity or
                GraphNodeOp.QueryFilterLayer or
                GraphNodeOp.QueryFilterRelationship or
                GraphNodeOp.AggCount or
                GraphNodeOp.AggMinByDistance or
                GraphNodeOp.TargetListGet or
                GraphNodeOp.QueryHexRange or
                GraphNodeOp.QueryHexRing or
                GraphNodeOp.QueryHexNeighbors or
                GraphNodeOp.ReadBlackboardFloat or
                GraphNodeOp.ReadBlackboardInt or
                GraphNodeOp.ReadBlackboardEntity or
                GraphNodeOp.LoadConfigFloat or
                GraphNodeOp.LoadConfigInt or
                GraphNodeOp.LoadConfigEffectId or
                GraphNodeOp.LoadContextSource or
                GraphNodeOp.LoadContextTarget or
                GraphNodeOp.LoadContextTargetContext or
                GraphNodeOp.LoadSelfAttribute or
                GraphNodeOp.RelationshipGetMetric or
                GraphNodeOp.RelationshipHasFlag or
                GraphNodeOp.RelationshipQueryOutgoing or
                GraphNodeOp.RelationshipQueryIncoming or
                GraphNodeOp.RelationshipQueryMutual or
                GraphNodeOp.RelationshipQueryBetweenPair or
                GraphNodeOp.RelationshipFilterMetricRange or
                GraphNodeOp.RelationshipFilterFlag or
                GraphNodeOp.RelationshipSortByMetric or
                GraphNodeOp.RelationshipAggSumMetric or
                GraphNodeOp.RelationshipAggMaxMetric or
                GraphNodeOp.RelationshipAggAverageMetric or
                GraphNodeOp.QueryAllMapEntities or
                GraphNodeOp.QueryFromCollection or
                GraphNodeOp.QueryFilterTeam or
                GraphNodeOp.QueryFilterTemplate or
                GraphNodeOp.QueryFilterAttributeRange or
                GraphNodeOp.QueryFilterTagAny or
                GraphNodeOp.QueryFilterTagNone or
                GraphNodeOp.QuerySortByAttribute or
                GraphNodeOp.AggSumAttribute or
                GraphNodeOp.AggAverageAttribute or
                GraphNodeOp.AggMaxAttribute or
                GraphNodeOp.AggMinAttribute or
                GraphNodeOp.AggMaxEntityByAttribute or
                GraphNodeOp.AggMinEntityByAttribute or
                GraphNodeOp.RelationshipAggMinMetric or
                GraphNodeOp.RelationshipAggMaxEntityByMetric or
                GraphNodeOp.RelationshipAggMinEntityByMetric or
                GraphNodeOp.RelationshipHasLink or
                GraphNodeOp.LoadTargetPosX or
                GraphNodeOp.LoadTargetPosY or
                GraphNodeOp.ClampTargetToRange or
                GraphNodeOp.IsPointInCircle or
                GraphNodeOp.SnapToNearestInCollection or
                GraphNodeOp.SnapToNearestGraphEdge or
                GraphNodeOp.LoadViewer or
                GraphNodeOp.LoadEventPayloadInt or
                GraphNodeOp.LoadEventPayloadFloat or
                GraphNodeOp.ControlDomainResolve or
                GraphNodeOp.ControlDomainControls or
                GraphNodeOp.KnowledgeHasProjection or
                GraphNodeOp.Call or
                GraphNodeOp.Return or
                GraphNodeOp.Yield or
                GraphNodeOp.HaltReturnInt or
                GraphNodeOp.InvokeScript or
                GraphNodeOp.MoveInt or
                GraphNodeOp.ResolveTableRow or
                GraphNodeOp.TableReadInt or
                GraphNodeOp.TableReadFloat or
                GraphNodeOp.ShowPanel or
                GraphNodeOp.HidePanel or
                GraphNodeOp.CreatePanel or
                GraphNodeOp.DestroyPanel or
                GraphNodeOp.ReadMapVarInt or
                GraphNodeOp.ReadMapVarFloat or
                GraphNodeOp.WriteMapVarInt or
                GraphNodeOp.WriteMapVarFloat
                    => EffectOperationMetadata.Pure(description),

                _ => throw new InvalidOperationException(
                    $"Executable graph opcode '{op}' is missing explicit effect operation metadata."),
            };
        }

        /// <summary>
        /// Run-to-halt execution. Budget exhaustion throws. Yield is rejected.
        /// Falling off the program end is an error; programs must halt with HaltReturnInt.
        /// </summary>
        internal static void Execute(ref GraphExecutionState state, ReadOnlySpan<GraphInstruction> program, GasGraphOpHandlerTable handlers)
        {
            if (state.CallStack.Length < GraphVmLimits.MaxCallStackDepth)
            {
                throw new InvalidOperationException(
                    $"Execute requires a call stack span of at least {GraphVmLimits.MaxCallStackDepth} (caller-owned; heap allocation is forbidden on this path).");
            }

            if (state.TreeSteps >= GraphVmLimits.MaxInstructionsPerExecution)
            {
                throw new InvalidOperationException(
                    $"Graph VM exceeded MaxInstructionsPerExecution ({GraphVmLimits.MaxInstructionsPerExecution}). Possible infinite loop.");
            }

            var cursor = new GraphExecutionCursor
            {
                Pc = 0,
                Steps = state.TreeSteps,
                CallStackCount = state.CallStackCount,
                ReturnInt = state.ReturnInt,
                InvokeDepth = state.InvokeDepth,
                Status = GraphExecutionStatus.Running
            };

            GraphSliceResult result = ExecuteSliceCore(
                ref state,
                program,
                handlers,
                ref cursor,
                GraphVmLimits.MaxInstructionsPerExecution - state.TreeSteps);

            state.CallStackCount = cursor.CallStackCount;
            state.ReturnInt = cursor.ReturnInt;
            state.TreeSteps = cursor.Steps;
            state.Status = result.Status;

            if (result.Status is GraphExecutionStatus.Running or GraphExecutionStatus.BudgetSuspended)
            {
                throw new InvalidOperationException(
                    $"Graph VM exceeded MaxInstructionsPerExecution ({GraphVmLimits.MaxInstructionsPerExecution}). Possible infinite loop.");
            }

            if (result.Status == GraphExecutionStatus.Yielded)
            {
                throw new InvalidOperationException(
                    "Graph Yield is not allowed in RunToHalt Execute; use ExecuteSlice with GraphKind.Script.");
            }
        }

        /// <summary>
        /// Resumable execution. Caller must keep <see cref="GraphExecutionState.CallStack"/>
        /// alive across slices. Budget exhaustion returns <see cref="GraphExecutionStatus.BudgetSuspended"/>
        /// without throwing. Callers must resume from the cursor, not restart.
        /// </summary>
        internal static GraphSliceResult ExecuteSlice(
            ref GraphExecutionState state,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            ref GraphExecutionCursor cursor,
            int budgetSteps)
        {
            if (state.CallStack.Length < GraphVmLimits.MaxCallStackDepth)
            {
                throw new InvalidOperationException(
                    $"ExecuteSlice requires a call stack span of at least {GraphVmLimits.MaxCallStackDepth} that outlives the slice.");
            }

            return ExecuteSliceCore(ref state, program, handlers, ref cursor, budgetSteps);
        }

        private static GraphSliceResult ExecuteSliceCore(
            ref GraphExecutionState state,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            ref GraphExecutionCursor cursor,
            int budgetSteps)
        {
            ArgumentNullException.ThrowIfNull(handlers);
            if (budgetSteps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetSteps));
            }

            if (cursor.Status == GraphExecutionStatus.Halted)
            {
                return new GraphSliceResult(GraphExecutionStatus.Halted, cursor.ReturnInt, cursor.Steps);
            }

            if (cursor.CallStackCount < 0 || cursor.CallStackCount > state.CallStack.Length)
            {
                throw new InvalidOperationException($"Graph call stack count out of range: {cursor.CallStackCount}.");
            }

            cursor.Status = GraphExecutionStatus.Running;

            var table = handlers.Handlers;
            Span<int> ints = state.I;
            int pc = cursor.Pc;
            int callStackCount = cursor.CallStackCount;
            int returnInt = cursor.ReturnInt;
            int invokeDepth = cursor.InvokeDepth;
            int treeSteps = state.TreeSteps < cursor.Steps ? cursor.Steps : state.TreeSteps;
            int stepsThisSlice = 0;
            bool statePrepared = false;
            const ushort moveIntOp = (ushort)GraphNodeOp.MoveInt;
            const ushort constIntOp = (ushort)GraphNodeOp.ConstInt;
            const ushort haltReturnIntOp = (ushort)GraphNodeOp.HaltReturnInt;

            while (stepsThisSlice < budgetSteps)
            {
                if (treeSteps >= GraphVmLimits.MaxInstructionsPerExecution)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        pc,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.BudgetSuspended);
                    return new GraphSliceResult(GraphExecutionStatus.BudgetSuspended, returnInt, treeSteps);
                }

                if ((uint)pc >= (uint)program.Length)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        pc,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Running);
                    throw new InvalidOperationException(
                        $"{GraphKindOperationPolicy.PcOutOfRangeError}: pc={pc}, length={program.Length}. 程序计数器越界；掉出程序尾部不再算成功，必须用 HaltReturnInt 显式结束。");
                }

                ref readonly var ins = ref program[pc];
                int instructionIndex = pc;
                pc++;
                treeSteps++;
                stepsThisSlice++;

                ushort op = ins.Op;
                if (op == moveIntOp)
                {
                    if (ins.Dst == ins.A)
                    {
                        if ((uint)ins.Dst >= (uint)ints.Length)
                        {
                            PersistSliceState(
                                ref state,
                                ref cursor,
                                pc,
                                callStackCount,
                                returnInt,
                                treeSteps,
                                program.Length,
                                invokeDepth,
                                GraphExecutionStatus.Running);
                            throw new InvalidOperationException(
                                $"Graph MoveInt int register {ins.Dst} exceeds int register capacity ({ints.Length}).");
                        }

                        while (stepsThisSlice < budgetSteps && treeSteps < GraphVmLimits.MaxInstructionsPerExecution)
                        {
                            if ((uint)pc >= (uint)program.Length)
                            {
                                break;
                            }

                            ref readonly var next = ref program[pc];
                            if (next.Op != moveIntOp || next.Dst != next.A)
                            {
                                break;
                            }

                            pc++;
                            treeSteps++;
                            stepsThisSlice++;
                            if ((uint)next.Dst >= (uint)ints.Length)
                            {
                                PersistSliceState(
                                    ref state,
                                    ref cursor,
                                    pc,
                                    callStackCount,
                                    returnInt,
                                    treeSteps,
                                    program.Length,
                                    invokeDepth,
                                    GraphExecutionStatus.Running);
                                throw new InvalidOperationException(
                                    $"Graph MoveInt int register {next.Dst} exceeds int register capacity ({ints.Length}).");
                            }
                        }

                        continue;
                    }
                    else
                    {
                        ints[ins.Dst] = ints[ins.A];
                    }

                    continue;
                }

                if (op == constIntOp)
                {
                    ints[ins.Dst] = ins.Imm;
                    continue;
                }

                if (op == haltReturnIntOp)
                {
                    returnInt = ints[ins.A];
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        instructionIndex + 1,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Halted);
                    return new GraphSliceResult(GraphExecutionStatus.Halted, returnInt, cursor.Steps);
                }

                if (op == 0)
                {
                    continue;
                }

                if (op >= table.Length)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        pc,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Running);
                    throw new InvalidOperationException(
                        $"Graph op {op} exceeds handler table capacity ({table.Length}).");
                }

                var handler = table[op];
                if (handler == null)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        pc,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Running);
                    throw new InvalidOperationException(
                        $"No handler registered for graph op {op}.");
                }

                if (!statePrepared)
                {
                    state.CallStackCount = callStackCount;
                    state.ReturnInt = returnInt;
                    state.Status = GraphExecutionStatus.Running;
                    state.ProgramLength = program.Length;
                    state.InvokeDepth = invokeDepth;
                    statePrepared = true;
                }

                state.TreeSteps = treeSteps;
                handler(ref state, in ins, ref pc);
                callStackCount = state.CallStackCount;
                returnInt = state.ReturnInt;
                if (state.TreeSteps > treeSteps)
                {
                    treeSteps = state.TreeSteps;
                }

                if (state.Status == GraphExecutionStatus.Yielded)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        pc,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Yielded);
                    return new GraphSliceResult(GraphExecutionStatus.Yielded, returnInt, cursor.Steps);
                }

                if (state.Status == GraphExecutionStatus.Halted)
                {
                    PersistSliceState(
                        ref state,
                        ref cursor,
                        instructionIndex + 1,
                        callStackCount,
                        returnInt,
                        treeSteps,
                        program.Length,
                        invokeDepth,
                        GraphExecutionStatus.Halted);
                    return new GraphSliceResult(GraphExecutionStatus.Halted, returnInt, cursor.Steps);
                }
            }

            PersistSliceState(
                ref state,
                ref cursor,
                pc,
                callStackCount,
                returnInt,
                treeSteps,
                program.Length,
                invokeDepth,
                GraphExecutionStatus.BudgetSuspended);
            return new GraphSliceResult(GraphExecutionStatus.BudgetSuspended, returnInt, cursor.Steps);
        }

        private static void PersistSliceState(
            ref GraphExecutionState state,
            ref GraphExecutionCursor cursor,
            int pc,
            int callStackCount,
            int returnInt,
            int treeSteps,
            int programLength,
            int invokeDepth,
            GraphExecutionStatus status)
        {
            state.CallStackCount = callStackCount;
            state.ReturnInt = returnInt;
            state.ProgramLength = programLength;
            state.InvokeDepth = invokeDepth;
            state.TreeSteps = treeSteps;
            state.Status = status;
            cursor.Pc = pc;
            cursor.CallStackCount = callStackCount;
            cursor.ReturnInt = returnInt;
            cursor.Steps = treeSteps;
            cursor.InvokeDepth = invokeDepth;
            cursor.Status = status;
        }

        private void RegisterBuiltins()
        {
            Register(GraphNodeOp.ConstBool, HandleConstBool, "ConstBool graph opcode.");
            Register(GraphNodeOp.ConstInt, HandleConstInt, "ConstInt graph opcode.");
            Register(GraphNodeOp.ConstFloat, HandleConstFloat, "ConstFloat graph opcode.");
            Register(GraphNodeOp.LoadCaster, HandleLoadCaster, "LoadCaster graph opcode.");
            Register(GraphNodeOp.LoadExplicitTarget, HandleLoadExplicitTarget, "LoadExplicitTarget graph opcode.");
            Register(GraphNodeOp.Jump, HandleJump, "Jump graph opcode.");
            Register(GraphNodeOp.JumpIfFalse, HandleJumpIfFalse, "JumpIfFalse graph opcode.");
            Register(GraphNodeOp.LoadAttribute, HandleLoadAttribute, "LoadAttribute graph opcode.");
            Register(GraphNodeOp.AddFloat, HandleAddFloat, "AddFloat graph opcode.");
            Register(GraphNodeOp.MulFloat, HandleMulFloat, "MulFloat graph opcode.");
            Register(GraphNodeOp.CompareGtFloat, HandleCompareGtFloat, "CompareGtFloat graph opcode.");
            Register(GraphNodeOp.SelectEntity, HandleSelectEntity, "SelectEntity graph opcode.");
            Register(GraphNodeOp.QueryRadius, HandleQueryRadius, "QueryRadius graph opcode.");
            Register(GraphNodeOp.QuerySortStable, HandleQuerySortStable, "QuerySortStable graph opcode.");
            Register(GraphNodeOp.QueryLimit, HandleQueryLimit, "QueryLimit graph opcode.");
            Register(GraphNodeOp.QueryCone, HandleQueryCone, "QueryCone graph opcode.");
            Register(GraphNodeOp.QueryRectangle, HandleQueryRectangle, "QueryRectangle graph opcode.");
            Register(GraphNodeOp.QueryLine, HandleQueryLine, "QueryLine graph opcode.");
            Register(GraphNodeOp.QueryFilterNotEntity, HandleQueryFilterNotEntity, "QueryFilterNotEntity graph opcode.");
            Register(GraphNodeOp.QueryFilterLayer, HandleQueryFilterLayer, "QueryFilterLayer graph opcode.");
            Register(GraphNodeOp.QueryFilterRelationship, HandleQueryFilterRelationship, "QueryFilterRelationship graph opcode.");
            Register(GraphNodeOp.AggCount, HandleAggCount, "AggCount graph opcode.");
            Register(GraphNodeOp.AggMinByDistance, HandleAggMinByDistance, "AggMinByDistance graph opcode.");
            Register(GraphNodeOp.TargetListGet, HandleTargetListGet, "TargetListGet graph opcode.");
            Register(GraphNodeOp.ApplyEffectTemplate, HandleApplyEffectTemplate, "ApplyEffectTemplate graph opcode.");
            Register(GraphNodeOp.FanOutApplyEffect, HandleFanOutApplyEffect, "FanOutApplyEffect graph opcode.");
            Register(GraphNodeOp.RemoveEffectTemplate, HandleRemoveEffectTemplate, "RemoveEffectTemplate graph opcode.");
            Register(GraphNodeOp.ModifyAttributeAdd, HandleModifyAttributeAdd, "ModifyAttributeAdd graph opcode.");
            Register(GraphNodeOp.SendEvent, HandleSendEvent, "SendEvent graph opcode.");
            Register(GraphNodeOp.RelationshipEnsureLink, HandleRelationshipEnsureLink, "RelationshipEnsureLink graph opcode.");
            Register(GraphNodeOp.RelationshipRemoveLink, HandleRelationshipRemoveLink, "RelationshipRemoveLink graph opcode.");
            Register(GraphNodeOp.RelationshipSetMetric, HandleRelationshipSetMetric, "RelationshipSetMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAddMetric, HandleRelationshipAddMetric, "RelationshipAddMetric graph opcode.");
            Register(GraphNodeOp.RelationshipGetMetric, HandleRelationshipGetMetric, "RelationshipGetMetric graph opcode.");
            Register(GraphNodeOp.RelationshipHasFlag, HandleRelationshipHasFlag, "RelationshipHasFlag graph opcode.");
            Register(GraphNodeOp.RelationshipSetFlag, HandleRelationshipSetFlag, "RelationshipSetFlag graph opcode.");
            Register(GraphNodeOp.RelationshipQueryOutgoing, HandleRelationshipQueryOutgoing, "RelationshipQueryOutgoing graph opcode.");
            Register(GraphNodeOp.RelationshipQueryIncoming, HandleRelationshipQueryIncoming, "RelationshipQueryIncoming graph opcode.");
            Register(GraphNodeOp.RelationshipQueryMutual, HandleRelationshipQueryMutual, "RelationshipQueryMutual graph opcode.");
            Register(GraphNodeOp.RelationshipQueryBetweenPair, HandleRelationshipQueryBetweenPair, "RelationshipQueryBetweenPair graph opcode.");
            Register(GraphNodeOp.RelationshipFilterMetricRange, HandleRelationshipFilterMetricRange, "RelationshipFilterMetricRange graph opcode.");
            Register(GraphNodeOp.RelationshipFilterFlag, HandleRelationshipFilterFlag, "RelationshipFilterFlag graph opcode.");
            Register(GraphNodeOp.RelationshipSortByMetric, HandleRelationshipSortByMetric, "RelationshipSortByMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggSumMetric, HandleRelationshipAggSumMetric, "RelationshipAggSumMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMaxMetric, HandleRelationshipAggMaxMetric, "RelationshipAggMaxMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggAverageMetric, HandleRelationshipAggAverageMetric, "RelationshipAggAverageMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMinMetric, HandleRelationshipAggMinMetric, "RelationshipAggMinMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMaxEntityByMetric, HandleRelationshipAggMaxEntityByMetric, "RelationshipAggMaxEntityByMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMinEntityByMetric, HandleRelationshipAggMinEntityByMetric, "RelationshipAggMinEntityByMetric graph opcode.");
            Register(GraphNodeOp.QueryAllMapEntities, HandleQueryAllMapEntities, "QueryAllMapEntities graph opcode.");
            Register(GraphNodeOp.QueryFromCollection, HandleQueryFromCollection, "QueryFromCollection graph opcode.");
            Register(GraphNodeOp.QueryFilterTeam, HandleQueryFilterTeam, "QueryFilterTeam graph opcode.");
            Register(GraphNodeOp.QueryFilterTemplate, HandleQueryFilterTemplate, "QueryFilterTemplate graph opcode.");
            Register(GraphNodeOp.QueryFilterAttributeRange, HandleQueryFilterAttributeRange, "QueryFilterAttributeRange graph opcode.");
            Register(GraphNodeOp.QueryFilterTagAny, HandleQueryFilterTagAny, "QueryFilterTagAny graph opcode.");
            Register(GraphNodeOp.QueryFilterTagNone, HandleQueryFilterTagNone, "QueryFilterTagNone graph opcode.");
            Register(GraphNodeOp.QuerySortByAttribute, HandleQuerySortByAttribute, "QuerySortByAttribute graph opcode.");
            Register(GraphNodeOp.AggSumAttribute, HandleAggSumAttribute, "AggSumAttribute graph opcode.");
            Register(GraphNodeOp.AggAverageAttribute, HandleAggAverageAttribute, "AggAverageAttribute graph opcode.");
            Register(GraphNodeOp.AggMaxAttribute, HandleAggMaxAttribute, "AggMaxAttribute graph opcode.");
            Register(GraphNodeOp.AggMinAttribute, HandleAggMinAttribute, "AggMinAttribute graph opcode.");
            Register(GraphNodeOp.AggMaxEntityByAttribute, HandleAggMaxEntityByAttribute, "AggMaxEntityByAttribute graph opcode.");
            Register(GraphNodeOp.AggMinEntityByAttribute, HandleAggMinEntityByAttribute, "AggMinEntityByAttribute graph opcode.");
            Register(GraphNodeOp.BeginLifecycleTransaction, HandleBeginLifecycleTransaction, "BeginLifecycleTransaction graph opcode.");
            Register(GraphNodeOp.InvokeBuiltin, HandleInvokeBuiltin, "InvokeBuiltin graph opcode.");
            Register(GraphNodeOp.AddInt, HandleAddInt, "AddInt graph opcode.");
            Register(GraphNodeOp.CompareLtInt, HandleCompareLtInt, "CompareLtInt graph opcode.");
            Register(GraphNodeOp.CompareEqInt, HandleCompareEqInt, "CompareEqInt graph opcode.");
            Register(GraphNodeOp.HasTag, HandleHasTag, "HasTag graph opcode.");
            Register(GraphNodeOp.CompareEqEntity, HandleCompareEqEntity, "CompareEqEntity graph opcode.");
            Register(GraphNodeOp.RandomFloat01, HandleRandomFloat01, "RandomFloat01 graph opcode.");
            Register(GraphNodeOp.QueryHexRange, HandleQueryHexRange, "QueryHexRange graph opcode.");
            Register(GraphNodeOp.QueryHexRing, HandleQueryHexRing, "QueryHexRing graph opcode.");
            Register(GraphNodeOp.QueryHexNeighbors, HandleQueryHexNeighbors, "QueryHexNeighbors graph opcode.");
            Register(GraphNodeOp.SubFloat, HandleSubFloat, "SubFloat graph opcode.");
            Register(GraphNodeOp.DivFloat, HandleDivFloat, "DivFloat graph opcode.");
            Register(GraphNodeOp.MinFloat, HandleMinFloat, "MinFloat graph opcode.");
            Register(GraphNodeOp.MaxFloat, HandleMaxFloat, "MaxFloat graph opcode.");
            Register(GraphNodeOp.ClampFloat, HandleClampFloat, "ClampFloat graph opcode.");
            Register(GraphNodeOp.AbsFloat, HandleAbsFloat, "AbsFloat graph opcode.");
            Register(GraphNodeOp.NegFloat, HandleNegFloat, "NegFloat graph opcode.");
            Register(GraphNodeOp.ReadBlackboardFloat, HandleReadBlackboardFloat, "ReadBlackboardFloat graph opcode.");
            Register(GraphNodeOp.ReadBlackboardInt, HandleReadBlackboardInt, "ReadBlackboardInt graph opcode.");
            Register(GraphNodeOp.ReadBlackboardEntity, HandleReadBlackboardEntity, "ReadBlackboardEntity graph opcode.");
            Register(GraphNodeOp.WriteBlackboardFloat, HandleWriteBlackboardFloat, "WriteBlackboardFloat graph opcode.");
            Register(GraphNodeOp.WriteBlackboardInt, HandleWriteBlackboardInt, "WriteBlackboardInt graph opcode.");
            Register(GraphNodeOp.WriteBlackboardEntity, HandleWriteBlackboardEntity, "WriteBlackboardEntity graph opcode.");
            Register(GraphNodeOp.LoadConfigFloat, HandleLoadConfigFloat, "LoadConfigFloat graph opcode.");
            Register(GraphNodeOp.LoadConfigInt, HandleLoadConfigInt, "LoadConfigInt graph opcode.");
            Register(GraphNodeOp.LoadConfigEffectId, HandleLoadConfigEffectId, "LoadConfigEffectId graph opcode.");
            Register(GraphNodeOp.LoadContextSource, HandleLoadContextSource, "LoadContextSource graph opcode.");
            Register(GraphNodeOp.LoadContextTarget, HandleLoadContextTarget, "LoadContextTarget graph opcode.");
            Register(GraphNodeOp.LoadContextTargetContext, HandleLoadContextTargetContext, "LoadContextTargetContext graph opcode.");
            Register(GraphNodeOp.ApplyEffectDynamic, HandleApplyEffectDynamic, "ApplyEffectDynamic graph opcode.");
            Register(GraphNodeOp.FanOutApplyEffectDynamic, HandleFanOutApplyEffectDynamic, "FanOutApplyEffectDynamic graph opcode.");
            Register(GraphNodeOp.FanOutDispatchEffect, HandleFanOutDispatchEffect, "FanOutDispatchEffect graph opcode.");
            Register(GraphNodeOp.FanOutDispatchEffectDynamic, HandleFanOutDispatchEffectDynamic, "FanOutDispatchEffectDynamic graph opcode.");
            Register(GraphNodeOp.LoadSelfAttribute, HandleLoadSelfAttribute, "LoadSelfAttribute graph opcode.");
            Register(GraphNodeOp.WriteSelfAttribute, HandleWriteSelfAttribute, "WriteSelfAttribute graph opcode.");
            Register(GraphNodeOp.LoadTargetPosX, HandleLoadTargetPosX, "LoadTargetPosX graph opcode.");
            Register(GraphNodeOp.LoadTargetPosY, HandleLoadTargetPosY, "LoadTargetPosY graph opcode.");
            Register(GraphNodeOp.ClampTargetToRange, HandleClampTargetToRange, "ClampTargetToRange graph opcode.");
            Register(GraphNodeOp.IsPointInCircle, HandleIsPointInCircle, "IsPointInCircle graph opcode.");
            Register(GraphNodeOp.SnapToNearestInCollection, HandleSnapToNearestInCollection, "SnapToNearestInCollection graph opcode.");
            Register(GraphNodeOp.SnapToNearestGraphEdge, HandleSnapToNearestGraphEdge, "SnapToNearestGraphEdge graph opcode.");
            Register(GraphNodeOp.LoadViewer, HandleLoadViewer, "LoadViewer graph opcode.");
            Register(GraphNodeOp.LoadEventPayloadInt, HandleLoadEventPayloadInt, "LoadEventPayloadInt graph opcode.");
            Register(GraphNodeOp.LoadEventPayloadFloat, HandleLoadEventPayloadFloat, "LoadEventPayloadFloat graph opcode.");
            Register(GraphNodeOp.RelationshipHasLink, HandleRelationshipHasLink, "RelationshipHasLink graph opcode.");
            Register(GraphNodeOp.ControlDomainResolve, HandleControlDomainResolve, "ControlDomainResolve graph opcode.");
            Register(GraphNodeOp.ControlDomainControls, HandleControlDomainControls, "ControlDomainControls graph opcode.");
            Register(GraphNodeOp.KnowledgeHasProjection, HandleKnowledgeHasProjection, "KnowledgeHasProjection graph opcode.");
            Register(GraphNodeOp.Call, HandleCall, "Call graph opcode.");
            Register(GraphNodeOp.Return, HandleReturn, "Return graph opcode.");
            Register(GraphNodeOp.Yield, HandleYield, "Yield graph opcode.");
            Register(GraphNodeOp.HaltReturnInt, HandleHaltReturnInt, "HaltReturnInt graph opcode.");
            Register(GraphNodeOp.InvokeScript, HandleInvokeScript, "InvokeScript graph opcode.");
            Register(GraphNodeOp.MoveInt, HandleMoveInt, "MoveInt graph opcode.");
            Register(GraphNodeOp.ResolveTableRow, HandleResolveTableRow, "ResolveTableRow graph opcode.");
            Register(GraphNodeOp.TableReadInt, HandleTableReadInt, "TableReadInt graph opcode.");
            Register(GraphNodeOp.ShowPanel, HandleShowPanel, "ShowPanel graph opcode.");
            Register(GraphNodeOp.HidePanel, HandleHidePanel, "HidePanel graph opcode.");
            Register(GraphNodeOp.CreatePanel, HandleCreatePanel, "CreatePanel graph opcode.");
            Register(GraphNodeOp.DestroyPanel, HandleDestroyPanel, "DestroyPanel graph opcode.");
            Register(GraphNodeOp.TableReadFloat, HandleTableReadFloat, "TableReadFloat graph opcode.");
            Register(GraphNodeOp.ReadMapVarInt, HandleReadMapVarInt, "ReadMapVarInt graph opcode.");
            Register(GraphNodeOp.ReadMapVarFloat, HandleReadMapVarFloat, "ReadMapVarFloat graph opcode.");
            Register(GraphNodeOp.WriteMapVarInt, HandleWriteMapVarInt, "WriteMapVarInt graph opcode.");
            Register(GraphNodeOp.WriteMapVarFloat, HandleWriteMapVarFloat, "WriteMapVarFloat graph opcode.");
        }

        // ── Value Ops ──

        private static void HandleConstBool(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(ins.Imm != 0 ? 1 : 0);
        }

        private static void HandleConstInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = ins.Imm;
        }

        private static void HandleConstFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = ins.ImmF;
        }

        // ── Entity Loading ──

        private static void HandleLoadCaster(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Caster;
        }

        private static void HandleLoadExplicitTarget(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.ExplicitTarget;
        }

        // ── Control Flow ──

        private static void HandleJump(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            pc += ins.Imm;
        }

        private static void HandleJumpIfFalse(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (s.B[ins.A] == 0)
            {
                pc += ins.Imm;
            }
        }

        private static void HandleCall(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (s.CallStackCount >= GraphVmLimits.MaxCallStackDepth)
            {
                throw new InvalidOperationException(
                    $"Graph call stack exceeded MaxCallStackDepth ({GraphVmLimits.MaxCallStackDepth}).");
            }

            int target = ins.Imm;
            if ((uint)target >= (uint)s.ProgramLength)
            {
                throw new InvalidOperationException($"Graph Call target out of range: {target}.");
            }

            s.CallStack[s.CallStackCount++] = pc;
            pc = target;
        }

        private static void HandleReturn(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (s.CallStackCount <= 0)
            {
                throw new InvalidOperationException("Graph Return executed with an empty call stack.");
            }

            pc = s.CallStack[--s.CallStackCount];
        }

        private static void HandleYield(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Status = GraphExecutionStatus.Yielded;
        }

        private static void HandleHaltReturnInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.ReturnInt = s.I[ins.A];
            s.Status = GraphExecutionStatus.Halted;
        }

        private static void HandleInvokeScript(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (s.Programs == null)
            {
                throw new InvalidOperationException("InvokeScript requires GraphExecutionState.Programs.");
            }

            if (s.InvokeDepth >= GraphVmLimits.MaxInvokeDepth)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvokeDepthExceeded: invoke depth {s.InvokeDepth + 1} exceeds MaxInvokeDepth ({GraphVmLimits.MaxInvokeDepth}).");
            }

            int graphId = ins.Imm;
            if (graphId <= 0)
            {
                throw new InvalidOperationException("InvokeScript requires a positive Script graph id in Imm.");
            }

            s.Programs.RequireKind(graphId, GraphKind.Script);
            if (!s.Programs.TryGetRegistration(graphId, out GraphProgramRegistration childRegistration))
            {
                throw new InvalidOperationException($"InvokeScript target graph id {graphId} is not registered.");
            }

            if (childRegistration.ContainsYield)
            {
                throw new InvalidOperationException(
                    $"InvokeScript target graph id {graphId} contains Yield; nested Yield is not supported in this slice.");
            }

            ReadOnlySpan<GraphInstruction> childProgram = childRegistration.Program;

            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);
            e[0] = s.Caster;
            e[1] = s.ExplicitTarget;
            e[2] = s.E.Length > 2 ? s.E[2] : default;

            var child = new GraphExecutionState
            {
                World = s.World,
                Caster = s.Caster,
                ExplicitTarget = s.ExplicitTarget,
                TargetContext = s.TargetContext,
                Viewer = s.Viewer,
                EventPayload = s.EventPayload,
                TargetPosCm = s.TargetPosCm,
                RandomSeed = s.RandomSeed,
                Api = s.Api,
                Programs = s.Programs,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack,
                Status = GraphExecutionStatus.Running,
                InvokeDepth = s.InvokeDepth + 1,
                TreeSteps = s.TreeSteps
            };

            Execute(ref child, childProgram, Instance);
            s.TreeSteps = child.TreeSteps;
            s.I[ins.Dst] = child.ReturnInt;
        }

        private static void HandleMoveInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.I[ins.A];
        }

        // ── Generic lookup tables (#881) ──

        private static void HandleResolveTableRow(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[Dst] = ResolveTableRow(Imm=tableId, I[A]=key)
            s.I[ins.Dst] = s.Api.ResolveTableRow(ins.Imm, s.I[ins.A]);
        }

        private static void HandleTableReadInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[Dst] = TableReadInt(Imm=fieldId, I[A]=rowHandle)
            s.I[ins.Dst] = s.Api.TableReadInt(ins.Imm, s.I[ins.A]);
        }

        private static void HandleShowPanel(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Panel show request; the UI records the decision without orchestrating (#1014).
            s.Api.ShowPanel(ins.Imm);
        }

        private static void HandleHidePanel(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.HidePanel(ins.Imm);
        }

        private static void HandleCreatePanel(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity scope = ins.A == byte.MaxValue ? s.Caster : s.E[ins.A];
            s.Api.CreatePanel(
                UI.PanelHosting.PanelOpEncoding.UnpackTemplate(ins.Imm),
                UI.PanelHosting.PanelOpEncoding.UnpackAnchor(ins.Imm),
                scope,
                ins.B,
                ins.ImmF);
        }

        private static void HandleDestroyPanel(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity scope = ins.A == byte.MaxValue ? Entity.Null : s.E[ins.A];
            s.Api.DestroyPanel(ins.Imm, scope);
        }

        // ── Map-scoped variables ──

        private static void HandleReadMapVarInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[Dst] = map variable (Imm=varName keyId); map resolved from E[A] (0xFF → caster).
            Entity scope = ins.A == byte.MaxValue ? s.Caster : s.E[ins.A];
            s.I[ins.Dst] = s.Api.ReadMapVarInt(
                ins.Imm,
                RequireMapVariableScopeMap(ref s, scope, nameof(GraphNodeOp.ReadMapVarInt)));
        }

        private static void HandleReadMapVarFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity scope = ins.A == byte.MaxValue ? s.Caster : s.E[ins.A];
            s.F[ins.Dst] = s.Api.ReadMapVarFloat(
                ins.Imm,
                RequireMapVariableScopeMap(ref s, scope, nameof(GraphNodeOp.ReadMapVarFloat)));
        }

        private static void HandleWriteMapVarInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // store.Write(Imm=varName keyId, I[A]); map resolved from E[B] (0xFF → caster).
            Entity scope = ins.B == byte.MaxValue ? s.Caster : s.E[ins.B];
            s.Api.WriteMapVarInt(
                ins.Imm,
                RequireMapVariableScopeMap(ref s, scope, nameof(GraphNodeOp.WriteMapVarInt)),
                s.I[ins.A]);
        }

        private static void HandleWriteMapVarFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity scope = ins.B == byte.MaxValue ? s.Caster : s.E[ins.B];
            s.Api.WriteMapVarFloat(
                ins.Imm,
                RequireMapVariableScopeMap(ref s, scope, nameof(GraphNodeOp.WriteMapVarFloat)),
                s.F[ins.A]);
        }

        private static MapId RequireMapVariableScopeMap(ref GraphExecutionState s, Entity scope, string opName)
        {
            if (s.World == null ||
                !s.World.IsAlive(scope) ||
                !s.World.TryGet<MapEntity>(scope, out MapEntity mapEntity))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.MapVariableScopeEntity: {opName} requires a scope entity with a MapEntity component (caster or explicit register).");
            }

            return mapEntity.MapId;
        }

        private static void HandleTableReadFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[Dst] = TableReadFloat(Imm=fieldId, I[A]=rowHandle)
            s.F[ins.Dst] = s.Api.TableReadFloat(ins.Imm, s.I[ins.A]);
        }

        // ── Attribute ──

        private static void HandleLoadAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var src = s.E[ins.A];
            if (s.World.IsAlive(src) && s.Api.TryGetAttributeCurrent(src, ins.Imm, out float value))
            {
                s.F[ins.Dst] = value;
            }
            else
            {
                s.F[ins.Dst] = 0f;
            }
        }

        // ── Arithmetic ──

        private static void HandleAddFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] + s.F[ins.B];
        }

        private static void HandleMulFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] * s.F[ins.B];
        }

        private static void HandleCompareGtFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.F[ins.A] > s.F[ins.B] ? 1 : 0);
        }

        private static void HandleSelectEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.B[ins.A] != 0 ? s.E[ins.B] : s.E[ins.C];
        }

        // ── Spatial Queries ──

        private static void HandleQueryRadius(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryRadius(s.TargetPosCm, ins.ImmF, s.Targets));
        }

        private static void HandleQueryCone(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryCone(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.ImmF, s.Targets));
        }

        private static void HandleQueryRectangle(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryRectangle(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        private static void HandleQueryLine(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryLine(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        private static void ApplySpatialQueryResult(
            ref GraphExecutionState s,
            in GraphInstruction ins,
            Ludots.Core.Spatial.SpatialQueryResult result)
        {
            if (ins.Flags > 1)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidSpatialQueryCapacityPolicy: flags={ins.Flags}.");
            }

            if ((uint)result.Count > (uint)s.Targets.Length || result.Dropped < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidSpatialQueryResult: count={result.Count}, dropped={result.Dropped}, capacity={s.Targets.Length}.");
            }

            if (result.Dropped > 0 && ins.Flags == 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SpatialQueryIncomplete: count={result.Count}, dropped={result.Dropped}.");
            }

            s.TargetList.SetCount(result.Count);
            if (ins.Flags == 1)
            {
                s.I[ins.Dst] = result.Dropped;
            }
        }

        // ── Query Filters ──

        private static void HandleQuerySortStable(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.SortStableDedup(s.Targets, s.TargetList.Count));
        }

        private static void HandleQueryLimit(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.Limit(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterNotEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterNotEntity(s.Targets, s.TargetList.Count, s.E[ins.A]));
        }

        private static void HandleQueryFilterLayer(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterLayer(s.Targets, s.TargetList.Count, unchecked((uint)ins.Imm)));
        }

        private static void HandleQueryFilterRelationship(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTeamRelationship(
                s.Targets,
                s.TargetList.Count,
                s.E[ins.A],
                ParseRelationshipFilterMode(ins.Imm)));
        }

        // ── Aggregation ──

        private static void HandleAggCount(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetList.Count;
        }

        private static void HandleAggMinByDistance(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var centerCm = new WorldCmInt2(s.TargetPosCm.X, s.TargetPosCm.Y);
            s.E[ins.Dst] = s.Api.TryMinEntityByWorldDistanceCm(s.TargetList.Span, centerCm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        // ── Effect / Event Actions ──

        private static void HandleApplyEffectTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                byte floatCount = ins.Flags;
                if (floatCount == 0)
                {
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm);
                    return;
                }

                float f0 = s.F[ins.B];
                float f1 = floatCount > 1 ? s.F[ins.C] : 0f;
                var args = new EffectArgs(floatCount, f0, f1);
                s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm, in args);
            }
        }

        private static void HandleModifyAttributeAdd(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.ModifyAttributeAdd(s.Caster, target, ins.Imm, s.F[ins.B]);
            }
        }

        private static void HandleRemoveEffectTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.RemoveEffectTemplate(target, ins.Imm);
            }
        }

        private static void HandleSendEvent(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.SendEvent(s.Caster, target, ins.Imm, s.F[ins.B]);
            }
        }

        // ── TargetList Iteration (123) ──

        private static void HandleTargetListGet(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[Dst] = TargetList[I[A]]; B[Flags] = valid (0/1)
            int idx = s.I[ins.A];
            if (idx >= 0 && idx < s.TargetList.Count)
            {
                s.E[ins.Dst] = s.TargetList.Span[idx];
                s.B[ins.Flags] = 1;
            }
            else
            {
                s.E[ins.Dst] = default;
                s.B[ins.Flags] = 0;
            }
        }

        // ── Batch Effect Application (201) ──

        private static void HandleFanOutApplyEffect(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Apply Effect(Imm=templateId) to ALL entities in TargetList
            var span = s.TargetList.Span;
            byte floatCount = ins.Flags;
            for (int i = 0; i < span.Length; i++)
            {
                var target = span[i];
                if (!s.World.IsAlive(target)) continue;
                if (floatCount == 0)
                {
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm);
                }
                else
                {
                    float f0 = s.F[ins.A];
                    float f1 = floatCount > 1 ? s.F[ins.B] : 0f;
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm, new EffectArgs(floatCount, f0, f1));
                }
            }
        }

        // ── Int Math / Bool (29, 31-33) ──

        private static void HandleRelationshipEnsureLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.EnsureRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Dst));
        }

        private static void HandleRelationshipRemoveLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.RemoveRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Dst));
        }

        private static void HandleRelationshipSetMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.SetRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, s.I[ins.C], reasonId, typeId);
        }

        private static void HandleRelationshipAddMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.AddRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, s.I[ins.C], reasonId, typeId);
        }

        private static void HandleRelationshipGetMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.Api.GetRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, RequireExplicitRelationshipTypeId(ins.Flags));
        }

        private static void HandleRelationshipHasFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasRelationshipFlag(s.E[ins.A], s.E[ins.B], ins.Imm, RequireExplicitRelationshipTypeId(ins.Flags)) ? 1 : 0);
        }

        private static void HandleRelationshipSetFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.SetRelationshipFlag(s.E[ins.A], s.E[ins.B], ins.Imm, s.B[ins.C] != 0, reasonId, typeId);
        }

        private static void HandleRelationshipQueryOutgoing(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplyRelationshipQueryResult(ref s, in ins, s.Api.CollectOutgoing(s.E[ins.A], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryIncoming(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplyRelationshipQueryResult(ref s, in ins, s.Api.CollectIncoming(s.E[ins.A], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryMutual(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplyRelationshipQueryResult(ref s, in ins, s.Api.CollectMutual(s.E[ins.A], s.E[ins.B], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryBetweenPair(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplyRelationshipQueryResult(ref s, in ins, s.Api.CollectBetweenPair(s.E[ins.A], s.E[ins.B], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void ApplyRelationshipQueryResult(
            ref GraphExecutionState s,
            in GraphInstruction ins,
            RelationshipQueryResult result)
        {
            if (ins.Flags > 1)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidRelationshipQueryCapacityPolicy: flags={ins.Flags}.");
            }

            if ((uint)result.Count > (uint)s.Targets.Length || result.Dropped < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidRelationshipQueryResult: count={result.Count}, dropped={result.Dropped}, capacity={s.Targets.Length}.");
            }

            if (result.Dropped > 0 && ins.Flags == 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.RelationshipQueryIncomplete: count={result.Count}, dropped={result.Dropped}.");
            }

            s.TargetList.SetCount(result.Count);
            if (ins.Flags == 1)
            {
                s.I[ins.C] = result.Dropped;
            }
        }

        private static void HandleRelationshipFilterMetricRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            short min = (short)s.F[ins.B];
            short max = (short)s.F[ins.C];
            s.TargetList.SetCount(s.Api.FilterRelationshipMetricRange(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, min, max));
        }

        private static void HandleRelationshipFilterFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            bool expected = ins.Flags != 0 || s.B[ins.B] != 0;
            s.TargetList.SetCount(s.Api.FilterRelationshipFlag(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, expected));
        }

        private static void HandleRelationshipSortByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            bool descending = ins.Flags != 0;
            s.Api.SortByRelationshipMetric(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, descending);
        }

        private static void HandleRelationshipAggSumMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.SumRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMaxMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.MaxRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggAverageMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.AverageRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMinMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.MinRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMaxEntityByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.E[ins.Dst] = s.Api.TryMaxEntityByRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleRelationshipAggMinEntityByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.E[ins.Dst] = s.Api.TryMinEntityByRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleQueryAllMapEntities(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectMapEntities(s.Targets));
        }

        private static void HandleQueryFromCollection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CopyEntityCollection(s.E[ins.A], ins.Imm, s.Targets));
        }

        private static void HandleQueryFilterTeam(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int teamId = ins.Flags != 0 ? s.I[ins.A] : ins.Imm;
            s.TargetList.SetCount(s.Api.FilterTeam(s.Targets, s.TargetList.Count, teamId));
        }

        private static void HandleQueryFilterTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTemplate(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterAttributeRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterAttributeRange(s.Targets, s.TargetList.Count, ins.Imm, s.F[ins.B], s.F[ins.C]));
        }

        private static void HandleQueryFilterTagAny(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTagAny(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterTagNone(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTagNone(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQuerySortByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.SortByAttribute(s.Targets, s.TargetList.Count, ins.Imm, ins.Flags != 0);
        }

        private static void HandleAggSumAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.SumAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggAverageAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.AverageAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMaxAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.MaxAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMinAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.MinAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMaxEntityByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.TryMaxEntityByAttribute(s.TargetList.Span, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleAggMinEntityByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.TryMinEntityByAttribute(s.TargetList.Span, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static int RequireExplicitRelationshipTypeId(byte encoded)
        {
            if (encoded == byte.MaxValue)
            {
                throw new InvalidOperationException("Graph relationship op requires an explicit relationshipType symbol.");
            }

            return encoded;
        }

        private static int ResolveQueryTypeId(byte encoded)
        {
            return encoded == byte.MaxValue ? RelationshipTypeRegistry.AnyTypeId : encoded;
        }

        private static RelationshipFilter ParseRelationshipFilterMode(int mode)
        {
            return mode switch
            {
                1 => RelationshipFilter.Hostile,
                2 => RelationshipFilter.Friendly,
                3 => RelationshipFilter.Neutral,
                4 => RelationshipFilter.NotFriendly,
                5 => RelationshipFilter.NotHostile,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported graph relationship filter mode.")
            };
        }

        private static void HandleAddInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.I[ins.A] + s.I[ins.B];
        }

        private static void HandleCompareLtInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.I[ins.A] < s.I[ins.B] ? 1 : 0);
        }

        private static void HandleCompareEqInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.I[ins.A] == s.I[ins.B] ? 1 : 0);
        }

        private static void HandleHasTag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // B[Dst] = E[A].HasTag(Imm) ? 1 : 0
            var entity = s.E[ins.A];
            s.B[ins.Dst] = (byte)(s.Api.HasTag(entity, ins.Imm) ? 1 : 0);
        }

        private static void HandleCompareEqEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.E[ins.A] == s.E[ins.B] ? 1 : 0);
        }

        // ── Event evaluation context (410-412) ──

        private static void HandleLoadViewer(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Viewer;
        }

        private static void HandleLoadEventPayloadInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = ins.Imm switch
            {
                0 => s.EventPayload.PayloadA,
                1 => s.EventPayload.PayloadB,
                _ => throw new InvalidOperationException($"LoadEventPayloadInt slot {ins.Imm} is out of range (0=PayloadA, 1=PayloadB)."),
            };
        }

        private static void HandleLoadEventPayloadFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = ins.Imm switch
            {
                0 => s.EventPayload.FloatA,
                1 => s.EventPayload.FloatB,
                2 => s.EventPayload.FloatC,
                3 => s.EventPayload.FloatD,
                _ => throw new InvalidOperationException($"LoadEventPayloadFloat slot {ins.Imm} is out of range (0..3 = FloatA..FloatD)."),
            };
        }

        // ── Topology predicates (397, 420-422) ──

        private static void HandleRelationshipHasLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Flags)) ? 1 : 0);
        }

        private static void HandleControlDomainResolve(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.ResolveControlDomain(s.E[ins.A]);
        }

        private static void HandleControlDomainControls(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.IsControllableBy(s.E[ins.A], s.E[ins.B]) ? 1 : 0);
        }

        private static void HandleKnowledgeHasProjection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasKnowledgeProjection(s.E[ins.A], s.E[ins.B]) ? 1 : 0);
        }

        private static void HandleRandomFloat01(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            uint x = s.RandomSeed;
            if (x == 0u)
            {
                x = 2463534242u;
            }

            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s.RandomSeed = x;
            s.F[ins.Dst] = (x & 0x00FFFFFFu) / 16777215f;
        }

        // ── Hex Spatial Queries (130-132) ──

        private static void HandleQueryHexRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexRange(s.TargetPosCm, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexRing(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexRing(s.TargetPosCm, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexNeighbors(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexNeighbors(s.TargetPosCm, s.Targets));
        }

        // ── Additional Math Ops (22-28) ──

        private static void HandleSubFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] - s.F[ins.B];
        }

        private static void HandleDivFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            float divisor = s.F[ins.B];
            s.F[ins.Dst] = divisor == 0f ? 0f : s.F[ins.A] / divisor;
        }

        private static void HandleMinFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] < s.F[ins.B] ? s.F[ins.A] : s.F[ins.B];
        }

        private static void HandleMaxFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] > s.F[ins.B] ? s.F[ins.A] : s.F[ins.B];
        }

        private static void HandleClampFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = clamp(F[A], min=F[B], max=F[C])
            float val = s.F[ins.A];
            float min = s.F[ins.B];
            float max = s.F[ins.C];
            s.F[ins.Dst] = val < min ? min : (val > max ? max : val);
        }

        private static void HandleAbsFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            float v = s.F[ins.A];
            s.F[ins.Dst] = v < 0f ? -v : v;
        }

        private static void HandleNegFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = -s.F[ins.A];
        }

        // ── Blackboard immediate read/write (300-305) ──
        // Encoding: A = entity register index, Imm = blackboard keyId, Dst/B = value register

        private static void HandleReadBlackboardFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = E[A].BB_Float[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardFloat(entity, ins.Imm, out float value))
                s.F[ins.Dst] = value;
            else
                s.F[ins.Dst] = 0f;
        }

        private static void HandleReadBlackboardInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = E[A].BB_Int[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardInt(entity, ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        private static void HandleReadBlackboardEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = E[A].BB_Entity[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardEntity(entity, ins.Imm, out Entity value))
                s.E[ins.Dst] = value;
            else
                s.E[ins.Dst] = default;
        }

        private static void HandleWriteBlackboardFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Float[Imm] = F[B]   (immediate write)
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardFloat(entity, ins.Imm, s.F[ins.B]);
        }

        private static void HandleWriteBlackboardInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Int[Imm] = I[B]
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardInt(entity, ins.Imm, s.I[ins.B]);
        }

        private static void HandleWriteBlackboardEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Entity[Imm] = E[B]
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardEntity(entity, ins.Imm, s.E[ins.B]);
        }

        // ── Config parameter reading (310-312) ──
        // Encoding: Imm = config keyId, Dst = destination register

        private static void HandleLoadConfigFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = EffectTemplate.ConfigParams[Imm]
            if (s.Api.TryLoadConfigFloat(ins.Imm, out float value))
                s.F[ins.Dst] = value;
            else
                s.F[ins.Dst] = 0f;
        }

        private static void HandleLoadConfigInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = EffectTemplate.ConfigParams[Imm]
            if (s.Api.TryLoadConfigInt(ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        private static void HandleLoadConfigEffectId(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = EffectTemplate.ConfigParams[Imm] (effectTemplateId, stored as int)
            if (s.Api.TryLoadConfigInt(ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        // ── Context entity loading (320-322) ──

        private static void HandleLoadContextSource(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.Source (same entity as Caster in current model)
            s.E[ins.Dst] = s.Caster;
        }

        private static void HandleLoadContextTarget(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.Target (same entity as ExplicitTarget in current model)
            s.E[ins.Dst] = s.ExplicitTarget;
        }

        private static void HandleLoadContextTargetContext(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.TargetContext (additional context entity)
            s.E[ins.Dst] = s.TargetContext;
        }

        // ── Dynamic dispatch (202-203) ──

        private static void HandleApplyEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // source=Caster, target=E[A], templateId=I[B]
            var target = s.E[ins.A];
            int templateId = s.I[ins.B];
            if (s.World.IsAlive(target) && templateId > 0)
            {
                s.Api.ApplyEffectTemplate(s.Caster, target, templateId);
            }
        }

        private static void HandleFanOutApplyEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // source=Caster, TargetList ALL, templateId=I[A]
            int templateId = s.I[ins.A];
            if (templateId <= 0) return;
            var span = s.TargetList.Span;
            for (int i = 0; i < span.Length; i++)
            {
                var target = span[i];
                if (!s.World.IsAlive(target)) continue;
                s.Api.ApplyEffectTemplate(s.Caster, target, templateId);
            }
        }

        private static void HandleFanOutDispatchEffect(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (ins.Imm <= 0 || ins.Dst == 0)
            {
                return;
            }

            s.Api.FanOutDispatchEffect(s.Caster, s.ExplicitTarget, s.TargetContext, s.TargetList.Span, ins.Imm, ins.Dst);
        }

        private static void HandleFanOutDispatchEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int templateId = s.I[ins.A];
            if (templateId <= 0 || ins.Dst == 0)
            {
                return;
            }

            s.Api.FanOutDispatchEffect(s.Caster, s.ExplicitTarget, s.TargetContext, s.TargetList.Span, templateId, ins.Dst);
        }

        // ── Self attribute access for derived graphs (330-331) ──

        private static void HandleLoadSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.TryGetAttributeCurrent(s.Caster, ins.Imm, out float value)
                ? value
                : 0f;
        }

        private static void HandleWriteSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Caster.Attribute[Imm] = F[A] — direct SetCurrent bypassing modifier pipeline
            var self = s.Caster;
            if (s.World.IsAlive(self) && s.World.Has<AttributeBuffer>(self))
            {
                s.Api.ModifyAttributeSet(s.Caster, self, ins.Imm, s.F[ins.A]);
            }
        }

        private static void HandleLoadTargetPosX(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetPosCm.X;
        }

        private static void HandleLoadTargetPosY(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetPosCm.Y;
        }

        private static void HandleClampTargetToRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (!PlacementValidation.TryGetEntityWorldPositionCm(s.World, s.E[ins.A], out Fix64Vec2 originCm))
            {
                s.B[ins.Dst] = 0;
                return;
            }

            Fix64Vec2 targetCm = Fix64Vec2.FromInt(s.TargetPosCm.X, s.TargetPosCm.Y);
            PlacementValidation.ClampToRange(
                in originCm,
                ref targetCm,
                Fix64.FromFloat(s.F[ins.B]),
                out bool inRange);
            var rounded = targetCm.RoundToInt();
            s.TargetPosCm = new IntVector2(rounded.x, rounded.y);
            s.B[ins.Dst] = (byte)(inRange ? 1 : 0);
        }

        private static void HandleIsPointInCircle(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (!PlacementValidation.TryGetEntityWorldPositionCm(s.World, s.E[ins.A], out Fix64Vec2 centerCm))
            {
                s.B[ins.Dst] = 0;
                return;
            }

            Fix64Vec2 pointCm = Fix64Vec2.FromInt(s.TargetPosCm.X, s.TargetPosCm.Y);
            bool inside = PlacementValidation.IsPointInCircle(
                in pointCm,
                in centerCm,
                Fix64.FromFloat(s.F[ins.B]));
            s.B[ins.Dst] = (byte)(inside ? 1 : 0);
        }

        private static void HandleSnapToNearestInCollection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            bool found = s.Api.TrySnapTargetToNearestInCollection(
                s.E[ins.A],
                ins.Imm,
                ref s.TargetPosCm,
                s.F[ins.B],
                out Entity snappedEntity);
            s.E[ins.Dst] = snappedEntity;
            if (ins.Flags != byte.MaxValue)
            {
                s.B[ins.Flags] = (byte)(found ? 1 : 0);
            }
        }

        private static void HandleSnapToNearestGraphEdge(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            bool found = s.Api.TrySnapTargetToNearestGraphEdge(
                ref s.TargetPosCm,
                s.F[ins.A],
                out _);
            s.B[ins.Dst] = (byte)(found ? 1 : 0);
        }

        private static void HandleBeginLifecycleTransaction(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.BeginLifecycleTransaction();
        }

        private static void HandleInvokeBuiltin(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.InvokeBuiltin(ins.Imm);
        }
    }
}
