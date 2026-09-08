using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.NodeLibraries.GASGraph;

public sealed class GraphEntityQueryPlan
{
    private readonly GraphInstruction[] _program;
    private readonly int[] _filters;
    public int ResumePc { get; }
    public int InstructionCount { get; }
    public ComponentType[] Dependencies { get; }
    public QueryDescription Description { get; }

    private GraphEntityQueryPlan(GraphInstruction[] program, int[] filters, int resumePc, int instructionCount)
    {
        _program = program;
        _filters = filters;
        ResumePc = resumePc;
        InstructionCount = instructionCount;
        var dependencies = new HashSet<ComponentType> { Component<MapEntity>.ComponentType };
        var required = new HashSet<ComponentType> { Component<MapEntity>.ComponentType };
        foreach (int pc in filters)
        {
            ComponentType component = (GraphNodeOp)program[pc].Op switch
            {
                GraphNodeOp.QueryFilterTeam => Component<Team>.ComponentType,
                GraphNodeOp.QueryFilterTemplate => Component<EntityTemplateKeyRef>.ComponentType,
                GraphNodeOp.QueryFilterAttributeRange => Component<AttributeBuffer>.ComponentType,
                _ => Component<GameplayTagContainer>.ComponentType
            };
            dependencies.Add(component);
            if (program[pc].Op != (ushort)GraphNodeOp.QueryFilterTagNone) required.Add(component);
        }
        Dependencies = new ComponentType[dependencies.Count];
        dependencies.CopyTo(Dependencies);
        var signature = new ComponentType[required.Count];
        required.CopyTo(signature);
        Description = new QueryDescription(all: signature);
    }

    public static Dictionary<int, GraphEntityQueryPlan> Compile(GraphInstruction[] program)
    {
        var plans = new Dictionary<int, GraphEntityQueryPlan>();
        for (int source = 0; source < program.Length; source++)
        {
            if (program[source].Op != (ushort)GraphNodeOp.QueryAllMapEntities) continue;
            var filters = new List<int>();
            var visited = new HashSet<int> { source };
            int pc = source + 1;
            int steps = 0;
            while ((uint)pc < (uint)program.Length && visited.Add(pc))
            {
                GraphInstruction ins = program[pc];
                if (ins.Op == (ushort)GraphNodeOp.Jump)
                {
                    pc = checked(pc + 1 + ins.Imm);
                    steps++;
                    continue;
                }
                if ((GraphNodeOp)ins.Op is not (GraphNodeOp.QueryFilterTeam or GraphNodeOp.QueryFilterTemplate
                    or GraphNodeOp.QueryFilterAttributeRange or GraphNodeOp.QueryFilterTagAny or GraphNodeOp.QueryFilterTagNone)) break;
                filters.Add(pc++);
                steps++;
            }
            // A branch into a fused chain must still execute its original filter instructions.
            plans.Add(source, new GraphEntityQueryPlan(program, filters.ToArray(), pc, steps));
        }
        return plans;
    }

    public int ParameterCount => _filters.Length * 3;

    public void Bind(ReadOnlySpan<int> ints, ReadOnlySpan<float> floats, Span<int> values)
    {
        for (int i = 0; i < _filters.Length; i++)
        {
            GraphInstruction ins = _program[_filters[i]];
            int offset = i * 3;
            values[offset] = ins.Op == (ushort)GraphNodeOp.QueryFilterTeam && ins.Flags != 0 ? ints[ins.A] : ins.Imm;
            values[offset + 1] = ins.Op == (ushort)GraphNodeOp.QueryFilterAttributeRange ? BitConverter.SingleToInt32Bits(floats[ins.B]) : 0;
            values[offset + 2] = ins.Op == (ushort)GraphNodeOp.QueryFilterAttributeRange ? BitConverter.SingleToInt32Bits(floats[ins.C]) : 0;
        }
    }

    public EntityQueryPredicate CreatePredicate(EntitySetQueryRuntime runtime, int[] values)
    {
        return (world, entity) =>
        {
            Span<Entity> candidate = stackalloc Entity[1];
            for (int i = 0; i < _filters.Length; i++)
            {
                candidate[0] = entity;
                int offset = i * 3;
                int count = (GraphNodeOp)_program[_filters[i]].Op switch
                {
                    GraphNodeOp.QueryFilterTeam => runtime.FilterTeam(candidate, 1, values[offset]),
                    GraphNodeOp.QueryFilterTemplate => runtime.FilterTemplate(candidate, 1, values[offset]),
                    GraphNodeOp.QueryFilterAttributeRange => runtime.FilterAttributeRange(candidate, 1, values[offset],
                        BitConverter.Int32BitsToSingle(values[offset + 1]), BitConverter.Int32BitsToSingle(values[offset + 2])),
                    GraphNodeOp.QueryFilterTagAny => runtime.FilterTagAny(candidate, 1, values[offset]),
                    GraphNodeOp.QueryFilterTagNone => runtime.FilterTagNone(candidate, 1, values[offset]),
                    _ => throw new InvalidOperationException("ENTITY_QUERY.ERR.UnsupportedPredicate")
                };
                if (count == 0) return false;
            }
            return true;
        };
    }
}
