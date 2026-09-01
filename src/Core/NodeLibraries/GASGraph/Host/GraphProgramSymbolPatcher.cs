using System;
using System.Runtime.CompilerServices;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public static class GraphProgramSymbolPatcher
    {
        private static readonly ConditionalWeakTable<GraphInstruction[], object> PatchedPrograms = new();

        public static void Patch(
            string[] symbols,
            GraphInstruction[] program,
            IGraphSymbolResolver symbolResolver,
            EntityCollectionStore? entityCollections = null,
            BuiltinHandlerRegistry? builtinHandlers = null)
        {
            if (symbols == null || symbols.Length == 0) return;
            if (program == null || program.Length == 0) return;
            if (symbolResolver == null) throw new ArgumentNullException(nameof(symbolResolver));
            if (PatchedPrograms.TryGetValue(program, out _))
            {
                return;
            }

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
                    case GraphNodeOp.LoadTextKey:
                        ins.Imm = symbolResolver.ResolveTextToken(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.StartDialogue:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.OfferActivity:
                    case GraphNodeOp.OfferTask:
                        _ = ResolveSymbol(symbols, ins.Imm);
                        break;
                    case GraphNodeOp.LoadAttribute:
                    case GraphNodeOp.ModifyAttributeAdd:
                    case GraphNodeOp.ModifyAttributeSet:
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
                    case GraphNodeOp.SpawnTemplate:
                        ins.Imm = symbolResolver.ResolveEntityTemplate(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.QueryCollectAbilityHolders:
                        ins.Imm = symbolResolver.ResolveAbility(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ResolveTableRow:
                        ins.Imm = symbolResolver.ResolveGraphLookupTable(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.WeightedPick:
                        ins.Imm = symbolResolver.ResolveRngDistribution(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.TableReadInt:
                    case GraphNodeOp.TableReadFloat:
                        ins.Imm = symbolResolver.ResolveGraphLookupField(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ShowPanel:
                    case GraphNodeOp.HidePanel:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ReadMapVarInt:
                    case GraphNodeOp.ReadMapVarFloat:
                    case GraphNodeOp.WriteMapVarInt:
                    case GraphNodeOp.WriteMapVarFloat:
                    case GraphNodeOp.SetInteractionMode:
                    case GraphNodeOp.LoadEntryPayloadEntity:
                    case GraphNodeOp.LoadEntryPayloadInt:
                    case GraphNodeOp.LoadEntryPayloadFloat:
                    case GraphNodeOp.LoadPlacedEntity:
                    case GraphNodeOp.LoadPlacedRegion:
                    case GraphNodeOp.LoadPlacedAnchor:
                    case GraphNodeOp.StoreArgInt:
                    case GraphNodeOp.StoreArgFloat:
                    case GraphNodeOp.StoreArgEntity:
                    case GraphNodeOp.DispatchMapEvent:
                    case GraphNodeOp.AwaitCallback:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.CreatePanel:
                        ins.Imm = UI.PanelHosting.PanelOpEncoding.Pack(
                            ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm)),
                            ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Dst)));
                        ins.Dst = 0;
                        bool skinAuthored = (ins.Flags & 1) != 0 && ins.B != byte.MaxValue;
                        ins.B = skinAuthored
                            ? UI.PanelHosting.PanelSkinIds.ToId(ResolveSymbol(symbols, ins.B))
                            : UI.PanelHosting.PanelSkinIds.Unspecified;
                        ins.Flags = 0;
                        if (ins.ImmF == 0f)
                        {
                            ins.ImmF = 100f;
                        }
                        break;
                    case GraphNodeOp.DestroyPanel:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ActivateContext:
                        ins.Imm = ContextOpEncoding.Pack(
                            ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm)),
                            ins.Dst == byte.MaxValue
                                ? 0
                                : ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Dst)));
                        ins.Dst = 0;
                        break;
                    case GraphNodeOp.DeactivateContext:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.SetPanelAudience:
                        ins.Imm = UI.PanelHosting.PanelOpEncoding.PackAudience(
                            ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm)),
                            ins.Dst == byte.MaxValue
                                ? 0
                                : ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Dst)));
                        ins.Dst = 0;
                        break;
                    case GraphNodeOp.QueryFromCollection:
                        ins.Imm = ResolveEntityCollectionKey(entityCollections, ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ScreenPointToEntity:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        }
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
                    {
                        string handlerKey = ResolveSymbol(symbols, ins.Imm);
                        if (builtinHandlers == null)
                        {
                            throw new InvalidOperationException(
                                $"Graph InvokeBuiltin symbol '{handlerKey}' requires a builtin handler registry.");
                        }

                        int handlerId = builtinHandlers.GetId(handlerKey);
                        if (handlerId <= 0)
                        {
                            throw new InvalidOperationException(
                                $"Graph InvokeBuiltin references unknown builtin handler '{handlerKey}'.");
                        }

                        ins.Imm = handlerId;
                        break;
                    }
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
                    case GraphNodeOp.RelationshipHasLink:
                        if (ins.Flags != byte.MaxValue)
                        {
                            ins.Flags = checked((byte)symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Flags)));
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

            PatchedPrograms.Add(program, PatchedPrograms);
        }

        /// <summary>
        /// Resolves InvokeScript instructions that carry Func Lib names (Flags=<see cref="GraphInstructionFlags.FuncLibName"/>)
        /// and InvokeGraph instructions that carry graph keys (same flag): the script name goes
        /// through the catalog, the TriggerGraph key resolves via <see cref="GraphIdRegistry"/>.
        /// </summary>
        public static void PatchFuncLib(string[] symbols, GraphInstruction[] program, GraphFunctionCatalog catalog)
        {
            if (program == null || program.Length == 0) return;
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            for (int i = 0; i < program.Length; i++)
            {
                ref var ins = ref program[i];
                if ((ins.Op == (ushort)GraphNodeOp.InvokeScript || ins.Op == (ushort)GraphNodeOp.InvokeGraph) &&
                    (ins.Flags & GraphInstructionFlags.FuncLibName) != 0)
                {
                    string symbol = ResolveSymbol(symbols, ins.Imm);
                    if (ins.Op == (ushort)GraphNodeOp.InvokeGraph)
                    {
                        int targetGraphId = GraphIdRegistry.GetId(symbol);
                        if (targetGraphId <= 0)
                        {
                            throw new InvalidOperationException(
                                $"InvokeGraph.functionName '{symbol}' is not a registered graph key.");
                        }

                        ins.Imm = targetGraphId;
                    }
                    else
                    {
                        GraphFunctionEntry entry = catalog.Require(symbol);
                        ins.Imm = entry.GraphId;
                    }

                    ins.Flags = (byte)(ins.Flags & ~GraphInstructionFlags.FuncLibName);
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
