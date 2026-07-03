using System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public static class GraphProgramSymbolPatcher
    {
        public static void Patch(
            string[] symbols,
            GraphInstruction[] program,
            IGraphSymbolResolver symbolResolver,
            EntityCollectionStore? entityCollections = null)
        {
            if (symbols == null || symbols.Length == 0) return;
            if (program == null || program.Length == 0) return;
            if (symbolResolver == null) throw new ArgumentNullException(nameof(symbolResolver));

            for (int i = 0; i < program.Length; i++)
            {
                ref var ins = ref program[i];
                var op = (GraphNodeOp)ins.Op;
                switch (op)
                {
                    case GraphNodeOp.QueryFilterTagAny:
                    case GraphNodeOp.QueryFilterTagNone:
                    case GraphNodeOp.SendEvent:
                    case GraphNodeOp.HasTag:
                        ins.Imm = symbolResolver.ResolveTag(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.LoadAttribute:
                    case GraphNodeOp.ModifyAttributeAdd:
                    case GraphNodeOp.QueryFilterAttributeRange:
                    case GraphNodeOp.QuerySortByAttribute:
                    case GraphNodeOp.AggSumAttribute:
                    case GraphNodeOp.AggAverageAttribute:
                    case GraphNodeOp.AggMaxAttribute:
                    case GraphNodeOp.AggMinAttribute:
                    case GraphNodeOp.AggMaxEntityByAttribute:
                    case GraphNodeOp.AggMinEntityByAttribute:
                    case GraphNodeOp.LoadSelfAttribute:
                    case GraphNodeOp.WriteSelfAttribute:
                        ins.Imm = symbolResolver.ResolveAttribute(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.QueryFilterTemplate:
                        ins.Imm = symbolResolver.ResolveEntityTemplate(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.QueryFromCollection:
                        ins.Imm = ResolveEntityCollectionKey(entityCollections, ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.SnapToNearestInCollection:
                        ins.Imm = ResolveEntityCollectionKey(entityCollections, ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ApplyEffectTemplate:
                    case GraphNodeOp.FanOutApplyEffect:
                    case GraphNodeOp.RemoveEffectTemplate:
                        ins.Imm = symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.FanOutDispatchEffect:
                        ins.Imm = symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, ins.Imm));
                        ins.Dst = checked((byte)symbolResolver.ResolveTargetDispatchPreset(ResolveSymbol(symbols, ins.Dst)));
                        break;
                    case GraphNodeOp.FanOutDispatchEffectDynamic:
                        ins.Dst = checked((byte)symbolResolver.ResolveTargetDispatchPreset(ResolveSymbol(symbols, ins.Dst)));
                        break;
                    case GraphNodeOp.ReadBlackboardFloat:
                    case GraphNodeOp.ReadBlackboardInt:
                    case GraphNodeOp.ReadBlackboardEntity:
                    case GraphNodeOp.WriteBlackboardFloat:
                    case GraphNodeOp.WriteBlackboardInt:
                    case GraphNodeOp.WriteBlackboardEntity:
                    case GraphNodeOp.LoadConfigFloat:
                    case GraphNodeOp.LoadConfigInt:
                    case GraphNodeOp.LoadConfigEffectId:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.InvokeBuiltin:
                        ins.Imm = (int)GasEnumParser.ParseBuiltinHandlerId(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.RelationshipSetMetric:
                    case GraphNodeOp.RelationshipAddMetric:
                    case GraphNodeOp.RelationshipGetMetric:
                    case GraphNodeOp.RelationshipAggSumMetric:
                    case GraphNodeOp.RelationshipAggMaxMetric:
                    case GraphNodeOp.RelationshipAggAverageMetric:
                    case GraphNodeOp.RelationshipAggMinMetric:
                    case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                    case GraphNodeOp.RelationshipAggMinEntityByMetric:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = symbolResolver.ResolveRelationshipMetric(ResolveSymbol(symbols, ins.Imm));
                        }

                        if ((op == GraphNodeOp.RelationshipSetMetric || op == GraphNodeOp.RelationshipAddMetric) &&
                            ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)symbolResolver.ResolveRelationshipReason(ResolveSymbol(symbols, ins.Dst)));
                        }

                        if (ins.Flags != byte.MaxValue)
                        {
                            ins.Flags = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Flags)));
                        }
                        break;
                    case GraphNodeOp.RelationshipFilterMetricRange:
                    case GraphNodeOp.RelationshipSortByMetric:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = symbolResolver.ResolveRelationshipMetric(ResolveSymbol(symbols, ins.Imm));
                        }

                        if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                    case GraphNodeOp.RelationshipHasFlag:
                    case GraphNodeOp.RelationshipSetFlag:
                    case GraphNodeOp.RelationshipFilterFlag:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = symbolResolver.ResolveRelationshipFlag(ResolveSymbol(symbols, ins.Imm));
                        }

                        if (op == GraphNodeOp.RelationshipSetFlag && ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)symbolResolver.ResolveRelationshipReason(ResolveSymbol(symbols, ins.Dst)));
                        }

                        if (op == GraphNodeOp.RelationshipSetFlag || op == GraphNodeOp.RelationshipHasFlag)
                        {
                            if (ins.Flags != byte.MaxValue)
                            {
                                ins.Flags = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Flags)));
                            }
                        }
                        else if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                    case GraphNodeOp.RelationshipEnsureLink:
                    case GraphNodeOp.RelationshipRemoveLink:
                    case GraphNodeOp.RelationshipQueryOutgoing:
                    case GraphNodeOp.RelationshipQueryIncoming:
                    case GraphNodeOp.RelationshipQueryMutual:
                    case GraphNodeOp.RelationshipQueryBetweenPair:
                        if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                }
            }
        }

        private static string ResolveSymbol(string[] symbols, int symbolIndex)
        {
            if ((uint)symbolIndex >= (uint)symbols.Length)
            {
                throw new InvalidOperationException($"Graph symbol index out of range: {symbolIndex} (len={symbols.Length}).");
            }

            return symbols[symbolIndex] ?? string.Empty;
        }

        private static int ResolveEntityCollectionKey(EntityCollectionStore? entityCollections, string key)
        {
            if (entityCollections == null)
            {
                throw new InvalidOperationException(
                    $"Graph collection query key '{key}' requires an EntityCollectionStore.");
            }

            return entityCollections.KeyRegistry.Register(key);
        }
    }
}
